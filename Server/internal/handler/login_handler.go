package handler

import (
	"log"
	"strings"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
	"adventure2d-server/pkg/mathutil"
)

func min(a, b int) int {
	if a < b {
		return a
	}
	return b
}

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
	clientAddr := session.RemoteAddr().String()
	log.Printf("[LoginHandler] Login request from %s", clientAddr)

	req, err := packet.DecodeLoginReq(payload)
	if err != nil {
		log.Printf("[LoginHandler] Decode error from %s: %v", clientAddr, err)
		session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
			Success: false,
			Message: "invalid packet",
		}))
		return
	}

	log.Printf("[LoginHandler] Decoded: username=%q slot=%d from %s", req.Username, req.Slot, clientAddr)

	// ── Guest login: username="guest", password=device_id ──────────────────
	// Mỗi device_id có account riêng → 2 thiết bị = 2 account khác nhau
	var accountID uint32
	if strings.TrimSpace(req.Username) == "guest" {
		deviceID := req.Password
		if deviceID == "" {
			session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
				Success: false, Message: "guest login requires device_id as password",
			}))
			return
		}
		var err error
		accountID, err = h.repo.GuestLogin(deviceID)
		if err != nil {
			log.Printf("[LoginHandler] GuestLogin FAILED deviceID=%q from %s: %v", deviceID, clientAddr, err)
			session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
				Success: false, Message: "guest login error",
			}))
			return
		}
		// Dùng device_id làm username prefix để hiển thị
		req.Username = "guest_" + deviceID[:min(8, len(deviceID))]
		log.Printf("[LoginHandler] Guest login OK: deviceID=%q → accountID=%d username=%q from %s",
			deviceID, accountID, req.Username, clientAddr)
	} else {
		// ── Normal login: username + password ───────────────────────────────
		if strings.TrimSpace(req.Username) == "" || req.Password == "" {
			log.Printf("[LoginHandler] Empty username/password from %s", clientAddr)
			session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
				Success: false, Message: "username or password cannot be empty",
			}))
			return
		}
		log.Printf("[LoginHandler] Verifying account username=%q from %s", req.Username, clientAddr)
		var err error
		accountID, err = h.repo.VerifyAccount(req.Username, req.Password)
		if err != nil {
			log.Printf("[LoginHandler] Auth FAILED for username=%q from %s: %v", req.Username, clientAddr, err)
			session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
				Success: false, Message: "incorrect username or password",
			}))
			return
		}
		log.Printf("[LoginHandler] Auth OK: username=%q accountID=%d from %s", req.Username, accountID, clientAddr)
	}

	// 2. Kiểm tra slot nhân vật (Chỉ chấp nhận slot 0, 1, 2)
	if req.Slot > 2 {
		log.Printf("[LoginHandler] Invalid slot=%d from username=%q (%s)", req.Slot, req.Username, clientAddr)
		session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
			Success: false,
			Message: "invalid character slot (max 3 characters, slot 0-2)",
		}))
		return
	}

	// 3. Tải thông tin nhân vật hoặc tạo mới nhân vật mặc định (Cung thủ) tại slot được chỉ định
	log.Printf("[LoginHandler] Loading character for accountID=%d slot=%d", accountID, req.Slot)
	record, err := h.repo.GetOrCreatePlayer(accountID, req.Username, req.Slot)
	if err != nil {
		log.Printf("[LoginHandler] DB error loading character for accountID=%d slot=%d: %v", accountID, req.Slot, err)
		session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
			Success: false,
			Message: "database error loading character",
		}))
		return
	}
	log.Printf("[LoginHandler] Character loaded: name=%q id=%d job=%d HP=%d pos=(%.2f,%.2f)",
		record.Username, record.ID, record.JobClass, record.HP, record.X, record.Y)

	// 4. Đồng bộ dữ liệu nhân vật vào session
	session.Player.ID       = record.ID
	session.Player.Username = record.Username
	session.Player.JobClass = record.JobClass
	session.Player.Stats    = player.DefaultStats(record.JobClass)
	session.Player.Level    = record.Level
	session.Player.Exp      = record.Exp
	session.Player.MapName  = record.MapName

	// Hồi máu nếu HP = 0 (tránh spawn chết)
	hp := record.HP
	if hp == 0 {
		hp = session.Player.Stats.MaxHP
		log.Printf("[LoginHandler] Character HP=0 → restored to MaxHP=%d", hp)
	}
	session.Player.InitState(mathutil.Vector2{X: record.X, Y: record.Y}, hp)

	mapName := record.MapName
	if mapName == "" { mapName = "Map1" }

	log.Printf("[LoginHandler] Login OK: %s (ID=%d) → Char=%s (ID=%d) Job=%d Level=%d Map=%q Pos=(%.2f,%.2f) from %s",
		req.Username, accountID, record.Username, record.ID, record.JobClass, record.Level, mapName, record.X, record.Y, clientAddr)

	session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
		Success:  true,
		PlayerID: record.ID,
		JobClass: uint8(record.JobClass),
		Level:    uint16(record.Level),
		Exp:      uint32(record.Exp),
		HP:       hp,
		MaxHP:    session.Player.Stats.MaxHP,
		X:        record.X,
		Y:        record.Y,
		MapName:  mapName,
		Message:  "OK",
	}))
}
