package db

import (
	"database/sql"
	"errors"
	"fmt"
	"log"
	"regexp"
	"strings"

	"adventure2d-server/internal/player"

	_ "modernc.org/sqlite"
	"golang.org/x/crypto/bcrypt"
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

// migrate tạo bảng accounts và players (nhân vật) nếu chưa tồn tại.
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
		job_class INTEGER DEFAULT 1, -- Mặc định là Cung thủ (Archer - 1)
		x REAL DEFAULT 0.0,
		y REAL DEFAULT 0.0,
		hp INTEGER DEFAULT 800,      -- Archer MaxHP mặc định là 800
		level INTEGER DEFAULT 1,     -- Level nhân vật (bắt đầu từ 1)
		exp INTEGER DEFAULT 0,       -- EXP hiện tại
		map_name TEXT DEFAULT 'Map1', -- Scene cuối cùng player đã đứng
		FOREIGN KEY(account_id) REFERENCES accounts(id),
		UNIQUE(account_id, slot)
	);
	CREATE INDEX IF NOT EXISTS idx_accounts_username ON accounts(username);
	CREATE INDEX IF NOT EXISTS idx_players_account_slot ON players(account_id, slot);
	-- Migration: thêm cột level/exp nếu DB cũ chưa có
	`
	// Migrate cột level và exp cho DB đã tồn tại (ALTER TABLE IF NOT EXISTS cột)
	for _, alterSQL := range []string{
		`ALTER TABLE players ADD COLUMN level INTEGER DEFAULT 1`,
		`ALTER TABLE players ADD COLUMN exp INTEGER DEFAULT 0`,
		`ALTER TABLE players ADD COLUMN map_name TEXT DEFAULT 'Map1'`,
		`ALTER TABLE accounts ADD COLUMN device_id TEXT`,
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
		return fmt.Errorf("username quá ngắn (tối thiểu %d ký tự)", minUsernameLen)
	}
	if l > maxUsernameLen {
		return fmt.Errorf("username quá dài (tối đa %d ký tự)", maxUsernameLen)
	}
	if !usernameRegex.MatchString(username) {
		return errors.New("username chỉ được chứa chữ cái, số, _ và -")
	}
	return nil
}

// validatePassword kiểm tra password hợp lệ.
func validatePassword(password string) error {
	l := len(password)
	if l < minPasswordLen {
		return fmt.Errorf("password quá ngắn (tối thiểu %d ký tự)", minPasswordLen)
	}
	if l > maxPasswordLen {
		return fmt.Errorf("password quá dài (tối đa %d ký tự)", maxPasswordLen)
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
	var jobInt, hpInt int

	// Load đầy đủ cả level, exp, map_name
	query := `SELECT id, account_id, slot, username, job_class, x, y, hp, level, exp, map_name
	          FROM players WHERE account_id = ? AND slot = ?`
	err := d.db.QueryRow(query, accountID, slot).Scan(
		&rec.ID, &rec.AccountID, &rec.Slot, &rec.Username,
		&jobInt, &rec.X, &rec.Y, &hpInt,
		&rec.Level, &rec.Exp, &rec.MapName,
	)

	if err == sql.ErrNoRows {
		log.Printf("[DB] GetOrCreatePlayer: no character at slot=%d for accountID=%d — creating new...", slot, accountID)

		charName := fmt.Sprintf("%s_slot%d", username, slot+1)

		// Nhân vật mới: Archer, Level 1, EXP 0, Map1, pos (0,0)
		insertQuery := `INSERT INTO players (account_id, slot, username, job_class, x, y, hp, level, exp, map_name)
		                VALUES (?, ?, ?, 1, 0.0, 0.0, 800, 1, 0, 'Map1')`
		res, err := d.db.Exec(insertQuery, accountID, slot, charName)
		if err != nil {
			if strings.Contains(err.Error(), "UNIQUE constraint failed") {
				charName = fmt.Sprintf("%s_%d_slot%d", username, accountID, slot+1)
				log.Printf("[DB] GetOrCreatePlayer: charName conflict, retrying with name=%q", charName)
				res, err = d.db.Exec(insertQuery, accountID, slot, charName)
			}
			if err != nil {
				return nil, fmt.Errorf("create character query error: %w", err)
			}
		}

		lastID, err := res.LastInsertId()
		if err != nil {
			return nil, fmt.Errorf("get last insert id error: %w", err)
		}

		log.Printf("[DB] GetOrCreatePlayer: NEW character name=%q id=%d slot=%d job=Archer HP=800 level=1 map=Map1", charName, lastID, slot)
		return &player.PlayerRecord{
			ID:        uint32(lastID),
			AccountID: accountID,
			Slot:      slot,
			Username:  charName,
			JobClass:  player.JobArcher,
			X:         0.0,
			Y:         0.0,
			HP:        800,
			Level:     1,
			Exp:       0,
			MapName:   "Map1",
		}, nil

	} else if err != nil {
		log.Printf("[DB] GetOrCreatePlayer: select error for accountID=%d slot=%d: %v", accountID, slot, err)
		return nil, fmt.Errorf("select character error: %w", err)
	}

	if rec.MapName == "" { rec.MapName = "Map1" } // fallback an toàn
	rec.JobClass = player.JobClass(jobInt)
	rec.HP = uint16(hpInt)
	log.Printf("[DB] GetOrCreatePlayer: LOADED name=%q id=%d job=%d HP=%d level=%d exp=%d map=%q pos=(%.2f,%.2f)",
		rec.Username, rec.ID, rec.JobClass, rec.HP, rec.Level, rec.Exp, rec.MapName, rec.X, rec.Y)
	return &rec, nil
}

// Save cập nhật thông tin vị trí, HP, Job Class, Level, Exp, Map của nhân vật.
func (d *Database) Save(p *player.Player) error {
	id, pos, _, hp, _ := p.Snapshot()
	log.Printf("[DB] Save: name=%q id=%d pos=(%.2f,%.2f) HP=%d job=%d map=%q",
		p.Username, id, pos.X, pos.Y, hp, p.JobClass, p.MapName)

	query := `UPDATE players SET job_class=?, x=?, y=?, hp=?, level=?, exp=?, map_name=? WHERE id=?`
	result, err := d.db.Exec(query,
		int(p.JobClass), pos.X, pos.Y, int(hp),
		p.Level, p.Exp, p.MapName,
		id)
	if err != nil {
		log.Printf("[DB] Save ERROR: %v", err)
		return fmt.Errorf("save character error: %w", err)
	}

	rows, _ := result.RowsAffected()
	if rows == 0 {
		log.Printf("[DB] Save WARN: no rows updated for id=%d", id)
	} else {
		log.Printf("[DB] Save OK: id=%d saved (%d row)", id, rows)
	}
	return err
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
