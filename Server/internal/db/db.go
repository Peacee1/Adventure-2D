package db

import (
	"database/sql"
	"fmt"
	"log"

	"adventure2d-server/internal/player"

	_ "modernc.org/sqlite"
)

// Database triển khai giao diện player.Repository sử dụng SQLite.
// Tuân thủ nguyên tắc Single Responsibility (SOLID) trong việc xử lý lưu trữ.
type Database struct {
	db *sql.DB
}

// NewDatabase tạo kết nối SQLite và tự động migrate các bảng.
func NewDatabase(dbPath string) (*Database, error) {
	db, err := sql.Open("sqlite", dbPath)
	if err != nil {
		return nil, fmt.Errorf("open db error: %w", err)
	}

	if err := db.Ping(); err != nil {
		db.Close()
		return nil, fmt.Errorf("ping db error: %w", err)
	}

	d := &Database{db: db}
	if err := d.migrate(); err != nil {
		db.Close()
		return nil, fmt.Errorf("migration error: %w", err)
	}

	return d, nil
}

// Close đóng kết nối cơ sở dữ liệu an toàn.
func (d *Database) Close() error {
	return d.db.Close()
}

// migrate tạo bảng accounts và players (nhân vật) nếu chưa tồn tại.
func (d *Database) migrate() error {
	query := `
	CREATE TABLE IF NOT EXISTS accounts (
		id INTEGER PRIMARY KEY AUTOINCREMENT,
		username TEXT UNIQUE NOT NULL,
		password TEXT NOT NULL
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
		FOREIGN KEY(account_id) REFERENCES accounts(id),
		UNIQUE(account_id, slot)
	);
	CREATE INDEX IF NOT EXISTS idx_accounts_username ON accounts(username);
	CREATE INDEX IF NOT EXISTS idx_players_account_slot ON players(account_id, slot);
	`
	_, err := d.db.Exec(query)
	return err
}

// RegisterAccount đăng ký một tài khoản người chơi mới.
func (d *Database) RegisterAccount(username, password string) error {
	query := `INSERT INTO accounts (username, password) VALUES (?, ?)`
	_, err := d.db.Exec(query, username, password)
	if err != nil {
		return fmt.Errorf("register account error: %w", err)
	}
	return nil
}

// VerifyAccount xác thực thông tin đăng nhập và trả về Account ID nếu đúng.
func (d *Database) VerifyAccount(username, password string) (uint32, error) {
	var id uint32
	query := `SELECT id FROM accounts WHERE username = ? AND password = ?`
	err := d.db.QueryRow(query, username, password).Scan(&id)
	if err != nil {
		return 0, err
	}
	return id, nil
}

// GetOrCreatePlayer tìm kiếm hoặc tạo nhân vật mới cho tài khoản tại slot chỉ định.
func (d *Database) GetOrCreatePlayer(accountID uint32, username string, slot uint8) (*player.PlayerRecord, error) {
	var rec player.PlayerRecord
	var jobInt int
	var hpInt int

	// Tìm nhân vật của account tại slot chỉ định
	query := `SELECT id, account_id, slot, username, job_class, x, y, hp FROM players WHERE account_id = ? AND slot = ?`
	err := d.db.QueryRow(query, accountID, slot).Scan(&rec.ID, &rec.AccountID, &rec.Slot, &rec.Username, &jobInt, &rec.X, &rec.Y, &hpInt)
	if err == sql.ErrNoRows {
		// Đặt tên nhân vật theo dạng: TênTàiKhoản_Slot1, TênTàiKhoản_Slot2...
		charName := fmt.Sprintf("%s_slot%d", username, slot+1)

		// Tạo nhân vật mặc định: JobClass = 1 (Archer), HP = 800
		insertQuery := `INSERT INTO players (account_id, slot, username, job_class, x, y, hp) VALUES (?, ?, ?, 1, 0.0, 0.0, 800)`
		res, err := d.db.Exec(insertQuery, accountID, slot, charName)
		if err != nil {
			return nil, fmt.Errorf("create character query error: %w", err)
		}
		lastID, err := res.LastInsertId()
		if err != nil {
			return nil, fmt.Errorf("get last insert id error: %w", err)
		}

		return &player.PlayerRecord{
			ID:        uint32(lastID),
			AccountID: accountID,
			Slot:      slot,
			Username:  charName,
			JobClass:  player.JobArcher, // Archer
			X:         0.0,
			Y:         0.0,
			HP:        800,
		}, nil
	} else if err != nil {
		return nil, fmt.Errorf("select character error: %w", err)
	}

	rec.JobClass = player.JobClass(jobInt)
	rec.HP = uint16(hpInt)
	return &rec, nil
}

// Save cập nhật thông tin vị trí, HP, và Job Class hiện tại của nhân vật.
func (d *Database) Save(p *player.Player) error {
	id, pos, _, hp, _ := p.Snapshot()
	query := `UPDATE players SET job_class = ?, x = ?, y = ?, hp = ? WHERE id = ?`
	_, err := d.db.Exec(query, int(p.JobClass), pos.X, pos.Y, int(hp), id)
	if err != nil {
		return fmt.Errorf("save character error: %w", err)
	}
	log.Printf("[DB] Saved character %s (ID=%d): Pos=(%.2f, %.2f) HP=%d Job=%d", p.Username, id, pos.X, pos.Y, hp, p.JobClass)
	return nil
}
