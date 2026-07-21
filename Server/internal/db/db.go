package db

import (
	"database/sql"
	"errors"
	"fmt"
	"log"
	"regexp"
	"strings"

	"adventure2d-server/internal/player"

	"golang.org/x/crypto/bcrypt"
	_ "modernc.org/sqlite"
)

// Validation constants
const (
	minUsernameLen = 3
	maxUsernameLen = 32
	minPasswordLen = 6
	maxPasswordLen = 64
	bcryptCost     = 12
)

// usernameRegex: chỉ cho phép chữ cái, số, dấu gạch dưới, dấu gạch ngang.
var usernameRegex = regexp.MustCompile(`^[a-zA-Z0-9_\-]+$`)

// ErrUsernameTaken trả về khi username đã tồn tại.
var ErrUsernameTaken = errors.New("username already exists")

// Database triển khai giao diện player.Repository sử dụng SQLite.
// Tuân thủ nguyên tắc Single Responsibility (SOLID) trong việc xử lý lưu trữ.
type Database struct {
	db *sql.DB
}

// NewDatabase tạo kết nối SQLite và tự động migrate các bảng.
func NewDatabase(dbPath string) (*Database, error) {
	log.Printf("[DB] Opening SQLite database at: %s", dbPath)

	db, err := sql.Open("sqlite", dbPath)
	if err != nil {
		return nil, fmt.Errorf("open db error: %w", err)
	}

	if err := db.Ping(); err != nil {
		db.Close()
		return nil, fmt.Errorf("ping db error: %w", err)
	}

	// SQLite performance tuning
	pragmas := []string{
		"PRAGMA journal_mode=WAL;",
		"PRAGMA synchronous=NORMAL;",
		"PRAGMA foreign_keys=ON;",
		"PRAGMA cache_size=-8000;", // 8MB cache
	}
	for _, p := range pragmas {
		if _, err := db.Exec(p); err != nil {
			log.Printf("[DB] WARN: PRAGMA failed (%s): %v", p, err)
		}
	}

	d := &Database{db: db}
	if err := d.migrate(); err != nil {
		db.Close()
		return nil, fmt.Errorf("migration error: %w", err)
	}

	log.Printf("[DB] Database initialized OK (path=%s)", dbPath)
	return d, nil
}

// Close đóng kết nối cơ sở dữ liệu an toàn.
func (d *Database) Close() error {
	log.Println("[DB] Closing database connection...")
	return d.db.Close()
}

// migrate tạo bảng accounts, players và transactions nếu chưa tồn tại.
func (d *Database) migrate() error {
	log.Println("[DB] Running migrations...")
	query := `
	CREATE TABLE IF NOT EXISTS accounts (
		id INTEGER PRIMARY KEY AUTOINCREMENT,
		username TEXT UNIQUE NOT NULL,
		password_hash TEXT NOT NULL,
		device_id TEXT UNIQUE          -- NULL cho tài khoản thường, device UUID cho guest
	);
	CREATE TABLE IF NOT EXISTS players (
		id INTEGER PRIMARY KEY AUTOINCREMENT,
		account_id INTEGER NOT NULL,
		slot INTEGER NOT NULL,
		username TEXT UNIQUE NOT NULL,
		job_class INTEGER DEFAULT 1,          -- Mặc định là Cung thủ (Archer - 1)
		x REAL DEFAULT 0.0,
		y REAL DEFAULT 0.0,
		hp INTEGER DEFAULT 800,
		level INTEGER DEFAULT 1,
		exp INTEGER DEFAULT 0,
		map_name TEXT DEFAULT 'Map1',
		max_hp INTEGER DEFAULT 800,
		atk_physical INTEGER DEFAULT 80,
		atk_magic INTEGER DEFAULT 10,
		def_physical INTEGER DEFAULT 30,
		def_magic INTEGER DEFAULT 20,
		attack_range REAL DEFAULT 15.0,
		move_speed REAL DEFAULT 10.0,
		attack_speed REAL DEFAULT 1.0,
		inventory TEXT DEFAULT '',
		buffs TEXT DEFAULT '',
		skills TEXT DEFAULT '',
		created_at TEXT DEFAULT CURRENT_TIMESTAMP,
		FOREIGN KEY(account_id) REFERENCES accounts(id),
		UNIQUE(account_id, slot)
	);
	CREATE TABLE IF NOT EXISTS transactions (
		id INTEGER PRIMARY KEY AUTOINCREMENT,
		player_id INTEGER NOT NULL,
		amount INTEGER NOT NULL DEFAULT 0,
		description TEXT NOT NULL DEFAULT '',
		timestamp TEXT DEFAULT CURRENT_TIMESTAMP,
		FOREIGN KEY(player_id) REFERENCES players(id)
	);
	CREATE INDEX IF NOT EXISTS idx_accounts_username ON accounts(username);
	CREATE INDEX IF NOT EXISTS idx_players_account_slot ON players(account_id, slot);
	CREATE INDEX IF NOT EXISTS idx_transactions_player ON transactions(player_id);
	`
	// Migrate backward-compatible: thêm cột mới vào DB cũ nếu chưa có
	for _, alterSQL := range []string{
		`ALTER TABLE players ADD COLUMN level INTEGER DEFAULT 1`,
		`ALTER TABLE players ADD COLUMN exp INTEGER DEFAULT 0`,
		`ALTER TABLE players ADD COLUMN map_name TEXT DEFAULT 'Map1'`,
		`ALTER TABLE accounts ADD COLUMN device_id TEXT`,
		`ALTER TABLE players ADD COLUMN max_hp INTEGER DEFAULT 800`,
		`ALTER TABLE players ADD COLUMN atk_physical INTEGER DEFAULT 80`,
		`ALTER TABLE players ADD COLUMN atk_magic INTEGER DEFAULT 10`,
		`ALTER TABLE players ADD COLUMN def_physical INTEGER DEFAULT 30`,
		`ALTER TABLE players ADD COLUMN def_magic INTEGER DEFAULT 20`,
		`ALTER TABLE players ADD COLUMN attack_range REAL DEFAULT 15.0`,
		`ALTER TABLE players ADD COLUMN move_speed REAL DEFAULT 10.0`,
		`ALTER TABLE players ADD COLUMN attack_speed REAL DEFAULT 1.0`,
		`ALTER TABLE players ADD COLUMN inventory TEXT DEFAULT ''`,
		`ALTER TABLE players ADD COLUMN buffs TEXT DEFAULT ''`,
		`ALTER TABLE players ADD COLUMN skills TEXT DEFAULT ''`,
		`ALTER TABLE players ADD COLUMN created_at TEXT NOT NULL DEFAULT '2024-01-01T00:00:00Z'`, // SQLite ALTER TABLE không hỗ trợ CURRENT_TIMESTAMP (non-constant)
	} {
		_, alterErr := d.db.Exec(alterSQL)
		if alterErr != nil && !strings.Contains(alterErr.Error(), "duplicate column") {
			log.Printf("[DB] migrate ALTER (ignored): %v", alterErr)
		}
	}
	_, err := d.db.Exec(query)
	if err != nil {
		return fmt.Errorf("migration SQL error: %w", err)
	}
	log.Println("[DB] Migration OK")
	return nil
}

// validateUsername kiểm tra username hợp lệ.
func validateUsername(username string) error {
	username = strings.TrimSpace(username)
	l := len(username)
	if l < minUsernameLen {
		return fmt.Errorf("username too short (minimum %d characters)", minUsernameLen)
	}
	if l > maxUsernameLen {
		return fmt.Errorf("username too long (maximum %d characters)", maxUsernameLen)
	}
	if !usernameRegex.MatchString(username) {
		return errors.New("username can only contain alphanumeric characters, underscore, and hyphen")
	}
	return nil
}

// validatePassword kiểm tra password hợp lệ.
func validatePassword(password string) error {
	l := len(password)
	if l < minPasswordLen {
		return fmt.Errorf("password too short (minimum %d characters)", minPasswordLen)
	}
	if l > maxPasswordLen {
		return fmt.Errorf("password too long (maximum %d characters)", maxPasswordLen)
	}
	return nil
}

// RegisterAccount đăng ký một tài khoản người chơi mới.
// Password được hash bằng bcrypt trước khi lưu vào DB.
func (d *Database) RegisterAccount(username, password string) error {
	username = strings.TrimSpace(username)
	log.Printf("[DB] RegisterAccount: validating username=%q", username)

	// Validate input
	if err := validateUsername(username); err != nil {
		log.Printf("[DB] RegisterAccount: invalid username=%q: %v", username, err)
		return err
	}
	if err := validatePassword(password); err != nil {
		log.Printf("[DB] RegisterAccount: invalid password for username=%q: %v", username, err)
		return err
	}

	// Hash password bằng bcrypt
	log.Printf("[DB] RegisterAccount: hashing password for username=%q (cost=%d)", username, bcryptCost)
	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcryptCost)
	if err != nil {
		return fmt.Errorf("bcrypt hash error: %w", err)
	}

	// INSERT vào DB
	query := `INSERT INTO accounts (username, password_hash) VALUES (?, ?)`
	result, err := d.db.Exec(query, username, string(hash))
	if err != nil {
		// SQLite UNIQUE constraint → username đã tồn tại
		if strings.Contains(err.Error(), "UNIQUE constraint failed") {
			log.Printf("[DB] RegisterAccount: username=%q already exists", username)
			return ErrUsernameTaken
		}
		return fmt.Errorf("register account error: %w", err)
	}

	id, _ := result.LastInsertId()
	log.Printf("[DB] RegisterAccount: OK username=%q accountID=%d", username, id)
	return nil
}

// VerifyAccount xác thực thông tin đăng nhập và trả về Account ID nếu đúng.
// So sánh password với bcrypt hash lưu trong DB.
func (d *Database) VerifyAccount(username, password string) (uint32, error) {
	username = strings.TrimSpace(username)
	log.Printf("[DB] VerifyAccount: looking up username=%q", username)

	var id uint32
	var hash string
	query := `SELECT id, password_hash FROM accounts WHERE username = ?`
	err := d.db.QueryRow(query, username).Scan(&id, &hash)
	if err == sql.ErrNoRows {
		log.Printf("[DB] VerifyAccount: username=%q not found", username)
		// Trả về lỗi chung để tránh username enumeration
		return 0, errors.New("incorrect username or password")
	}
	if err != nil {
		log.Printf("[DB] VerifyAccount: DB error for username=%q: %v", username, err)
		return 0, fmt.Errorf("database error: %w", err)
	}

	// So sánh bcrypt hash
	if err := bcrypt.CompareHashAndPassword([]byte(hash), []byte(password)); err != nil {
		log.Printf("[DB] VerifyAccount: wrong password for username=%q", username)
		return 0, errors.New("incorrect username or password")
	}

	log.Printf("[DB] VerifyAccount: OK username=%q accountID=%d", username, id)
	return id, nil
}

// GetOrCreatePlayer tìm kiếm hoặc tạo nhân vật mới cho tài khoản tại slot chỉ định.
func (d *Database) GetOrCreatePlayer(accountID uint32, username string, slot uint8) (*player.PlayerRecord, error) {
	log.Printf("[DB] GetOrCreatePlayer: accountID=%d username=%q slot=%d", accountID, username, slot)

	var rec player.PlayerRecord
	var jobInt int
	var hpInt, maxHPInt, atkPhy, atkMag, defPhy, defMag int

	query := `SELECT id, account_id, slot, username, job_class, x, y, hp, level, exp, map_name,
	                 max_hp, atk_physical, atk_magic, def_physical, def_magic,
	                 attack_range, move_speed, attack_speed, inventory, buffs, skills,
	                 COALESCE(created_at, CURRENT_TIMESTAMP)
	          FROM players WHERE account_id = ? AND slot = ?`
	err := d.db.QueryRow(query, accountID, slot).Scan(
		&rec.ID, &rec.AccountID, &rec.Slot, &rec.Username,
		&jobInt, &rec.X, &rec.Y, &hpInt,
		&rec.Level, &rec.Exp, &rec.MapName,
		&maxHPInt, &atkPhy, &atkMag, &defPhy, &defMag,
		&rec.AttackRange, &rec.MoveSpeed, &rec.AttackSpeed,
		&rec.Inventory, &rec.Buffs, &rec.Skills,
		&rec.CreatedAt,
	)

	if err == sql.ErrNoRows {
		// Character name is simply the account username — no slot suffix needed.
		charName := username
		defStats := player.DefaultStats(player.JobArcher)

		insertQuery := `INSERT INTO players
		                (account_id, slot, username, job_class, x, y, hp, level, exp, map_name,
		                 max_hp, atk_physical, atk_magic, def_physical, def_magic,
		                 attack_range, move_speed, attack_speed, inventory, buffs, skills)
		                VALUES (?, ?, ?, 1, 0.0, 0.0, ?, 1, 0, 'Map0',
		                        ?, ?, ?, ?, ?, ?, ?, ?, '', '', '')`
		res, err := d.db.Exec(insertQuery, accountID, slot, charName,
			int(defStats.MaxHP),
			int(defStats.MaxHP), int(defStats.ATKPhysical), int(defStats.ATKMagic),
			int(defStats.DEFPhysical), int(defStats.DEFMagic),
			defStats.AttackRange, defStats.MoveSpeed, defStats.AttackSpeed,
		)
		if err != nil {
			if strings.Contains(err.Error(), "UNIQUE constraint failed") {
				// Fallback: username is already taken by another account — append accountID to disambiguate.
				charName = fmt.Sprintf("%s_%d", username, accountID)
				log.Printf("[DB] GetOrCreatePlayer: charName conflict, retrying with name=%q", charName)
				res, err = d.db.Exec(insertQuery, accountID, slot, charName,
					int(defStats.MaxHP),
					int(defStats.MaxHP), int(defStats.ATKPhysical), int(defStats.ATKMagic),
					int(defStats.DEFPhysical), int(defStats.DEFMagic),
					defStats.AttackRange, defStats.MoveSpeed, defStats.AttackSpeed,
				)
			}
			if err != nil {
				return nil, fmt.Errorf("create character query error: %w", err)
			}
		}

		lastID, err := res.LastInsertId()
		if err != nil {
			return nil, fmt.Errorf("get last insert id error: %w", err)
		}

		log.Printf("[DB] GetOrCreatePlayer: NEW character name=%q id=%d slot=%d job=Archer level=1 map=Map0", charName, lastID, slot)
		return &player.PlayerRecord{
			ID:          uint32(lastID),
			AccountID:   accountID,
			Slot:        slot,
			Username:    charName,
			JobClass:    player.JobArcher,
			X:           0.0,
			Y:           0.0,
			HP:          defStats.MaxHP,
			Level:       1,
			Exp:         0,
			MapName:     "Map0",
			MaxHP:       defStats.MaxHP,
			ATKPhysical: defStats.ATKPhysical,
			ATKMagic:    defStats.ATKMagic,
			DEFPhysical: defStats.DEFPhysical,
			DEFMagic:    defStats.DEFMagic,
			AttackRange: defStats.AttackRange,
			MoveSpeed:   defStats.MoveSpeed,
			AttackSpeed: defStats.AttackSpeed,
			Inventory:   "",
			Buffs:       "",
			Skills:      "",
		}, nil

	} else if err != nil {
		log.Printf("[DB] GetOrCreatePlayer: select error for accountID=%d slot=%d: %v", accountID, slot, err)
		return nil, fmt.Errorf("select character error: %w", err)
	}

	if rec.MapName == "" { rec.MapName = "Map0" } // safe fallback
	rec.JobClass    = player.JobClass(jobInt)
	rec.HP          = uint16(hpInt)
	rec.MaxHP       = uint16(maxHPInt)
	rec.ATKPhysical = uint16(atkPhy)
	rec.ATKMagic    = uint16(atkMag)
	rec.DEFPhysical = uint16(defPhy)
	rec.DEFMagic    = uint16(defMag)

	// If DB record has no stats (old DB, zeroed row), fall back to DefaultStats per job
	if rec.MaxHP == 0 {
		defStats := player.DefaultStats(rec.JobClass)
		rec.MaxHP       = defStats.MaxHP
		rec.ATKPhysical = defStats.ATKPhysical
		rec.ATKMagic    = defStats.ATKMagic
		rec.DEFPhysical = defStats.DEFPhysical
		rec.DEFMagic    = defStats.DEFMagic
		rec.AttackRange = defStats.AttackRange
		rec.MoveSpeed   = defStats.MoveSpeed
		rec.AttackSpeed = defStats.AttackSpeed
	}

	log.Printf("[DB] GetOrCreatePlayer: LOADED name=%q id=%d job=%d HP=%d/%d level=%d exp=%d map=%q pos=(%.2f,%.2f)",
		rec.Username, rec.ID, rec.JobClass, rec.HP, rec.MaxHP, rec.Level, rec.Exp, rec.MapName, rec.X, rec.Y)
	return &rec, nil
}

// GetAllCharacters trả về thông tin tóm tắt của cả 3 slot nhân vật cho một tài khoản.
// Slot nào chưa có nhân vật thì phần tử tương ứng sẽ là nil.
func (d *Database) GetAllCharacters(accountID uint32) ([3]*player.PlayerRecord, error) {
	log.Printf("[DB] GetAllCharacters: accountID=%d", accountID)

	query := `SELECT slot, username, job_class, level
	          FROM players
	          WHERE account_id = ? AND slot IN (0, 1, 2)
	          ORDER BY slot ASC`

	rows, err := d.db.Query(query, accountID)
	if err != nil {
		return [3]*player.PlayerRecord{}, fmt.Errorf("GetAllCharacters query error: %w", err)
	}
	defer rows.Close()

	var result [3]*player.PlayerRecord
	for rows.Next() {
		var rec player.PlayerRecord
		var jobInt int
		if err := rows.Scan(&rec.Slot, &rec.Username, &jobInt, &rec.Level); err != nil {
			return result, fmt.Errorf("GetAllCharacters scan error: %w", err)
		}
		rec.JobClass = player.JobClass(jobInt)
		if rec.Slot <= 2 {
			result[rec.Slot] = &rec
		}
	}
	if err := rows.Err(); err != nil {
		return result, fmt.Errorf("GetAllCharacters rows error: %w", err)
	}

	log.Printf("[DB] GetAllCharacters: accountID=%d slot0=%v slot1=%v slot2=%v",
		accountID,
		result[0] != nil, result[1] != nil, result[2] != nil)
	return result, nil
}

// Save cập nhật toàn bộ trạng thái nhân vật vào cơ sở dữ liệu.
func (d *Database) Save(p *player.Player) error {
	data := p.GetSaveData()
	log.Printf("[DB] Save: name=%q id=%d pos=(%.2f,%.2f) HP=%d job=%d map=%q level=%d exp=%d",
		p.Username, data.ID, data.Position.X, data.Position.Y,
		data.HP, data.JobClass, data.MapName, data.Level, data.Exp)

	query := `UPDATE players SET
			job_class=?, x=?, y=?, hp=?, level=?, exp=?, map_name=?,
			max_hp=?, atk_physical=?, atk_magic=?, def_physical=?, def_magic=?,
			attack_range=?, move_speed=?,
			inventory=?, buffs=?, skills=?
		WHERE id=?`
	result, err := d.db.Exec(query,
		int(data.JobClass), data.Position.X, data.Position.Y, int(data.HP),
		data.Level, data.Exp, data.MapName,
		int(data.Stats.MaxHP), int(data.Stats.ATKPhysical), int(data.Stats.ATKMagic),
		int(data.Stats.DEFPhysical), int(data.Stats.DEFMagic),
		data.Stats.AttackRange, data.Stats.MoveSpeed,
		data.Inventory, data.Buffs, data.Skills,
		data.ID)
	if err != nil {
		log.Printf("[DB] Save ERROR: %v", err)
		return fmt.Errorf("save character error: %w", err)
	}

	rows, _ := result.RowsAffected()
	if rows == 0 {
		log.Printf("[DB] Save WARN: no rows updated for id=%d", data.ID)
	} else {
		log.Printf("[DB] Save OK: id=%d saved (%d row)", data.ID, rows)
	}
	return nil
}

// AddTransaction ghi một bản ghi giao dịch mới cho nhân vật.
func (d *Database) AddTransaction(playerID uint32, amount int, description string) error {
	_, err := d.db.Exec(
		`INSERT INTO transactions (player_id, amount, description) VALUES (?, ?, ?)`,
		playerID, amount, description,
	)
	if err != nil {
		return fmt.Errorf("AddTransaction error: %w", err)
	}
	log.Printf("[DB] AddTransaction: playerID=%d amount=%d desc=%q", playerID, amount, description)
	return nil
}

// GetTransactions trả về toàn bộ lịch sử giao dịch của một nhân vật, sắp xếp theo thời gian mới nhất trước.
func (d *Database) GetTransactions(playerID uint32) ([]player.TransactionRecord, error) {
	rows, err := d.db.Query(
		`SELECT id, player_id, amount, description, timestamp
		 FROM transactions WHERE player_id = ? ORDER BY id DESC`,
		playerID,
	)
	if err != nil {
		return nil, fmt.Errorf("GetTransactions query error: %w", err)
	}
	defer rows.Close()

	var result []player.TransactionRecord
	for rows.Next() {
		var t player.TransactionRecord
		if err := rows.Scan(&t.ID, &t.PlayerID, &t.Amount, &t.Description, &t.Timestamp); err != nil {
			return nil, fmt.Errorf("GetTransactions scan error: %w", err)
		}
		result = append(result, t)
	}
	log.Printf("[DB] GetTransactions: playerID=%d count=%d", playerID, len(result))
	return result, rows.Err()
}

// GuestLogin tự động tạo tài khoản guest theo deviceID nếu chưa có.
// Luôn thành công — trả về accountID để load/tạo nhân vật.
func (d *Database) GuestLogin(deviceID string) (uint32, error) {
	if deviceID == "" {
		return 0, fmt.Errorf("deviceID không được để trống")
	}
	log.Printf("[DB] GuestLogin: deviceID=%q", deviceID[:min(8, len(deviceID))])

	// Kiểm tra account đã tồn tại chưa
	var id uint32
	err := d.db.QueryRow(`SELECT id FROM accounts WHERE device_id = ?`, deviceID).Scan(&id)
	if err == nil {
		log.Printf("[DB] GuestLogin: found existing guest account id=%d", id)
		return id, nil
	}
	if err != sql.ErrNoRows {
		return 0, fmt.Errorf("GuestLogin lookup error: %w", err)
	}

	// Tạo account mới với device_id
	// Username: guest_ + 8 ký tự đầu deviceID
	prefix := deviceID
	if len(prefix) > 8 { prefix = prefix[:8] }
	guestUsername := "guest_" + strings.ToLower(prefix)

	result, err := d.db.Exec(
		`INSERT INTO accounts (username, password_hash, device_id) VALUES (?, 'GUEST_NO_PASSWORD', ?)`,
		guestUsername, deviceID,
	)
	if err != nil {
		// Nếu username đã trùng, thêm suffix ngẫu nhiên
		if strings.Contains(err.Error(), "UNIQUE constraint failed") {
			guestUsername = fmt.Sprintf("g_%s", deviceID[:min(12, len(deviceID))])
			result, err = d.db.Exec(
				`INSERT INTO accounts (username, password_hash, device_id) VALUES (?, 'GUEST_NO_PASSWORD', ?)`,
				guestUsername, deviceID,
			)
		}
		if err != nil {
			return 0, fmt.Errorf("GuestLogin create account error: %w", err)
		}
	}

	newID, _ := result.LastInsertId()
	log.Printf("[DB] GuestLogin: created guest account id=%d username=%q", newID, guestUsername)
	return uint32(newID), nil
}

// min helper (Go 1.21+ có built-in, dùng này cho tương thích)
func min(a, b int) int {
	if a < b { return a }
	return b
}
