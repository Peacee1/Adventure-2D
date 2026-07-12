package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
	"adventure2d-server/pkg/mathutil"
)

// LoginHandler xử lý LoginReqPacket và xác thực thông tin tài khoản + slot nhân vật.
type LoginHandler struct {
	roomManager *room.Manager
	repo        player.Repository
}

func NewLoginHandler(rm *room.Manager, repo player.Repository) *LoginHandler {
	return &LoginHandler{
		roomManager: rm,
		repo:        repo,
	}
}

// Handle xác thực tài khoản từ database, lấy hoặc tạo nhân vật tại slot chỉ định.
func (h *LoginHandler) Handle(payload []byte, session *player.Session) {
	req, err := packet.DecodeLoginReq(payload)
	if err != nil {
		log.Printf("[LoginHandler] Decode error: %v", err)
		session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
			Success: false,
			Message: "invalid packet",
		}))
		return
	}

	if req.Username == "" || req.Password == "" {
		session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
			Success: false,
			Message: "username or password empty",
		}))
		return
	}

	// 1. Xác thực tài khoản qua username và password
	accountID, err := h.repo.VerifyAccount(req.Username, req.Password)
	if err != nil {
		log.Printf("[LoginHandler] Auth failed for username %s: %v", req.Username, err)
		session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
			Success: false,
			Message: "incorrect username or password",
		}))
		return
	}

	// 2. Kiểm tra slot nhân vật (Chỉ chấp nhận slot 0, 1, 2)
	if req.Slot > 2 {
		log.Printf("[LoginHandler] Invalid slot %d requested by %s", req.Slot, req.Username)
		session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
			Success: false,
			Message: "invalid character slot (max 3 characters)",
		}))
		return
	}

	// 3. Tải thông tin nhân vật hoặc tạo mới nhân vật mặc định (Cung thủ) tại slot được chỉ định
	record, err := h.repo.GetOrCreatePlayer(accountID, req.Username, req.Slot)
	if err != nil {
		log.Printf("[LoginHandler] Database error loading player slot %d for account %d: %v", req.Slot, accountID, err)
		session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
			Success: false,
			Message: "database error loading character",
		}))
		return
	}

	// 4. Đồng bộ dữ liệu nhân vật được tải vào session
	session.Player.ID = record.ID
	session.Player.Username = record.Username
	session.Player.JobClass = record.JobClass
	session.Player.Stats = player.DefaultStats(record.JobClass)

	// Hồi máu nếu nhân vật đang có HP = 0 (tránh spawn chết)
	hp := record.HP
	if hp == 0 {
		hp = session.Player.Stats.MaxHP
	}
	session.Player.InitState(mathutil.Vector2{X: record.X, Y: record.Y}, hp)

	log.Printf("[LoginHandler] Login OK: Account=%s (ID=%d) → Char=%s (ID=%d, Slot=%d) Job=%d, HP=%d, Pos=(%.2f, %.2f)",
		req.Username, accountID, record.Username, record.ID, req.Slot, record.JobClass, hp, record.X, record.Y)

	session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
		Success:  true,
		PlayerID: record.ID,
		Message:  "OK",
	}))
}
