package player

// PlayerRecord chứa dữ liệu thô của nhân vật được tải lên từ Database.
type PlayerRecord struct {
	ID        uint32
	AccountID uint32
	Slot      uint8
	Username  string
	JobClass  JobClass
	X, Y      float32
	HP        uint16
}

// Repository định nghĩa giao diện lưu trữ và truy xuất thông tin tài khoản & nhân vật.
// Tuân thủ nguyên tắc Dependency Inversion (SOLID).
type Repository interface {
	// RegisterAccount đăng ký tài khoản mới.
	RegisterAccount(username, password string) error

	// VerifyAccount xác thực tài khoản và trả về Account ID.
	VerifyAccount(username, password string) (uint32, error)

	// GetOrCreatePlayer truy vấn nhân vật theo account ID và slot.
	// Nếu chưa tồn tại, tạo mới nhân vật mặc định (JobClass: Archer).
	GetOrCreatePlayer(accountID uint32, username string, slot uint8) (*PlayerRecord, error)

	// Save cập nhật trạng thái hiện tại của player vào cơ sở dữ liệu.
	Save(p *Player) error
}
