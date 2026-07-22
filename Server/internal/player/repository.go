package player

// PlayerRecord holds the raw character data loaded from the database.
type PlayerRecord struct {
	ID        uint32
	AccountID uint32
	Slot      uint8
	Username  string
	JobClass  JobClass
	X, Y      float32
	HP        uint16
	Level       int
	Exp         int
	SkillPoints int
	MapName   string // Last scene the player was in, default "Map1"
	CreatedAt string // Character creation date (ISO 8601)

	// Combat stats persisted in the DB (supports upgrades/buffs)
	MaxHP       uint16
	MaxMP       uint16
	ATKPhysical uint16
	ATKMagic    uint16
	DEFPhysical uint16
	DEFMagic    uint16
	AttackRange float32
	MoveSpeed   float32
	AttackSpeed float32 // seconds per attack — fallback to DefaultStats if 0
	CritRate    float32 // 0.0–1.0 critical hit chance
	LifeSteal   float32 // 0.0–1.0 fraction of damage restored as HP

	// Text-based data (JSON or CSV depending on future game logic)
	Inventory string // Item storage
	Buffs     string // Currently active buff effects
	Skills    string // List of unlocked skills
}

// TransactionRecord holds one entry in a character's transaction history.
type TransactionRecord struct {
	ID          uint32
	PlayerID    uint32
	Amount      int    // Amount (positive = received, negative = spent)
	Description string // Transaction description (buy/sell/reward...)
	Timestamp   string // Transaction time (ISO 8601)
}

// Repository defines the interface for storing and retrieving account and character data.
// Follows the Dependency Inversion Principle (SOLID).
type Repository interface {
	// RegisterAccount creates a new player account.
	RegisterAccount(username, password string) error

	// VerifyAccount authenticates an account and returns its Account ID.
	VerifyAccount(username, password string) (uint32, error)

	// GuestLogin automatically creates a guest account by deviceID if one does not exist.
	// Always succeeds — returns accountID for loading or creating a character.
	GuestLogin(deviceID string) (accountID uint32, err error)

	// GetOrCreatePlayer fetches a character by account ID and slot.
	// If the character does not exist, creates a new default one (JobClass: Archer).
	GetOrCreatePlayer(accountID uint32, username string, slot uint8) (*PlayerRecord, error)

	// GetAllCharacters returns summary info for all 3 character slots of an account.
	// Elements for empty slots will be nil.
	GetAllCharacters(accountID uint32) ([3]*PlayerRecord, error)

	// Save updates the current state of a player in the database.
	Save(p *Player) error

	// AddTransaction records a new transaction entry for a character.
	AddTransaction(playerID uint32, amount int, description string) error

	// GetTransactions returns the full transaction history for a character.
	GetTransactions(playerID uint32) ([]TransactionRecord, error)
}
