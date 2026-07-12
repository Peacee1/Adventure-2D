package packet

// ── Auth ──────────────────────────────────────────────────────────────────────

// LoginReqPacket: client gửi lên khi kết nối.
type LoginReqPacket struct {
	Username string // độ dài tối đa 32 byte (length-prefixed)
	Password string // mật khẩu đăng nhập (length-prefixed)
	Slot     uint8  // slot nhân vật (0, 1, 2)
}

// LoginAckPacket: server trả về sau khi xác thực.
// Chứa đầy đủ data để client spawn đúng nhân vật ở đúng map/vị trí.
type LoginAckPacket struct {
	Success  bool
	PlayerID uint32
	JobClass uint8   // 0=Warrior 1=Archer ...
	Level    uint16
	Exp      uint32
	HP       uint16
	MaxHP    uint16
	X        float32
	Y        float32
	MapName  string  // Tên scene Unity cần load, vd "Map1"
	Message  string  // Thông báo lỗi nếu Success=false
}

// RegisterReqPacket: client gửi khi đăng ký tài khoản.
type RegisterReqPacket struct {
	Username string
	Password string
}

// RegisterAckPacket: server phản hồi kết quả đăng ký.
type RegisterAckPacket struct {
	Success bool
	Message string
}

// GuestLoginReqPacket: client gửi deviceUniqueIdentifier để tự động login mà không cần đăng ký.
// Server tạo tài khoản guest nếu chưa có, luôn trả về LoginAck kể cả player mới.
type GuestLoginReqPacket struct {
	DeviceID string // SystemInfo.deviceUniqueIdentifier từ Unity
	Slot     uint8  // slot nhân vật (0, 1, 2)
}


// ── Room ──────────────────────────────────────────────────────────────────────

// JoinRoomReqPacket: client yêu cầu vào phòng.
type JoinRoomReqPacket struct {
	RoomID string // "" = matchmake tự động
}

// JoinRoomAckPacket: server xác nhận vào phòng.
type JoinRoomAckPacket struct {
	Success bool
	RoomID  string
	// Danh sách player đã có trong phòng
	ExistingPlayers []PlayerInfo
}

// PlayerInfo: thông tin cơ bản một player.
type PlayerInfo struct {
	PlayerID uint32
	Username string
	X, Y     float32
	HP       uint16
	MaxHP    uint16
	JobClass uint8 // 0=Warrior 1=Archer 2=Mage 3=Healer 4=Assassin 5=Tank
}

// PlayerJoinedPacket: broadcast khi có player mới vào phòng.
type PlayerJoinedPacket struct {
	Player PlayerInfo
}

// PlayerLeftPacket: broadcast khi player rời phòng.
type PlayerLeftPacket struct {
	PlayerID uint32
}

// ── Movement (UDP) ────────────────────────────────────────────────────────────

// MoveInputPacket: client gửi qua UDP mỗi frame khi có movement.
type MoveInputPacket struct {
	PlayerID  uint32
	DestX     float32 // điểm đến (right-click destination)
	DestY     float32
	DirX      float32 // hướng di chuyển normalize
	DirY      float32
	Timestamp uint32 // client tick count (để server order packets)
}

// PlayerSnapshot: trạng thái 1 player tại 1 tick.
type PlayerSnapshot struct {
	PlayerID uint32
	X, Y     float32
	DirX     float32
	DirY     float32
	HP       uint16
	State    uint8 // 0=Idle 1=Move 2=Dash 3=Attack 4=Dead
}

// WorldStatePacket: server broadcast qua UDP mỗi tick.
type WorldStatePacket struct {
	Tick    uint32
	Players []PlayerSnapshot
}

// ── Combat ────────────────────────────────────────────────────────────────────

// AttackReqPacket: client gửi khi nhấn attack.
type AttackReqPacket struct {
	PlayerID  uint32
	TargetID  uint32  // 0 = AOE / melee hit box
	DirX, DirY float32 // hướng tấn công
}

// DamageEventPacket: broadcast khi có ai bị dame.
type DamageEventPacket struct {
	AttackerID uint32
	TargetID   uint32
	Damage     uint32
	RemainingHP uint16
	IsCrit     bool
}

// DieEventPacket: broadcast khi có ai chết.
type DieEventPacket struct {
	PlayerID   uint32
	KillerID   uint32
}

// RespawnReqPacket: client yêu cầu hồi sinh.
type RespawnReqPacket struct {
	PlayerID uint32
}

// RespawnAckPacket: server xác nhận hồi sinh và vị trí spawn.
type RespawnAckPacket struct {
	PlayerID uint32
	X, Y     float32
	HP       uint16
}

// ── System ────────────────────────────────────────────────────────────────────

// PingPacket: đo latency.
type PingPacket struct {
	Timestamp uint32
}

// PongPacket: phản hồi ping, echo lại timestamp.
type PongPacket struct {
	Timestamp uint32
}
