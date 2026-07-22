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
	session.Player.ID        = record.ID
	session.Player.Username  = record.Username
	session.Player.JobClass  = record.JobClass
	session.Player.Level     = record.Level
	session.Player.Exp       = record.Exp
	session.Player.MapName   = record.MapName
	session.Player.CreatedAt = record.CreatedAt
	session.Player.Inventory = record.Inventory
	session.Player.Buffs     = record.Buffs
	session.Player.Skills    = record.Skills

	// Populate Stats from DB record.
	// For each field: if the stored value is 0 (legacy/unset row), fall back to DefaultStats.
	def := player.DefaultStats(record.JobClass)

	orDefault16 := func(stored uint16, fallback uint16) uint16 {
		if stored == 0 { return fallback }
		return stored
	}
	orDefaultF := func(stored float32, fallback float32) float32 {
		if stored == 0 { return fallback }
		return stored
	}

	session.Player.Stats = player.Stats{
		MaxHP:       orDefault16(record.MaxHP,       def.MaxHP),
		MaxMP:       orDefault16(record.MaxMP,       def.MaxMP),
		ATKPhysical: orDefault16(record.ATKPhysical, def.ATKPhysical),
		ATKMagic:    orDefault16(record.ATKMagic,    def.ATKMagic),
		DEFPhysical: orDefault16(record.DEFPhysical, def.DEFPhysical),
		DEFMagic:    orDefault16(record.DEFMagic,    def.DEFMagic),
		AttackRange: orDefaultF(record.AttackRange,  def.AttackRange),
		MoveSpeed:   orDefaultF(record.MoveSpeed,    def.MoveSpeed),
		AttackSpeed: orDefaultF(record.AttackSpeed,  def.AttackSpeed),
		CritRate:    record.CritRate,   // 0 is a valid default (no crit)
		LifeSteal:   record.LifeSteal,  // 0 is a valid default (no steal)
	}
	session.Player.SkillPoints = record.SkillPoints

	log.Printf("[LoginHandler] Stats loaded — MaxHP=%d MaxMP=%d ATKPhy=%d ATKMag=%d DEFPhy=%d DEFMag=%d AR=%.1f SP=%d",
		session.Player.Stats.MaxHP, session.Player.Stats.MaxMP,
		session.Player.Stats.ATKPhysical, session.Player.Stats.ATKMagic,
		session.Player.Stats.DEFPhysical, session.Player.Stats.DEFMagic,
		session.Player.Stats.AttackRange, session.Player.SkillPoints)

	// Luôn hồi đầy HP và MP khi đăng nhập nhân vật
	hp := session.Player.Stats.MaxHP
	mp := session.Player.Stats.MaxMP

	mapName := record.MapName
	if mapName == "" { mapName = "Map0" }

	// Guard against stale out-of-bounds positions saved before walkability was enforced.
	// SafeSpawn returns the same position if walkable, or the map's default spawn if not.
	spawnX, spawnY := player.SafeSpawn(mapName, record.X, record.Y)
	if spawnX != record.X || spawnY != record.Y {
		log.Printf("[LoginHandler] Position (%.2f,%.2f) is outside Ground tilemap → reset to safe spawn (%.2f,%.2f)",
			record.X, record.Y, spawnX, spawnY)
	}
	session.Player.InitState(mathutil.Vector2{X: spawnX, Y: spawnY}, hp)

	log.Printf("[LoginHandler] Login OK: %s (ID=%d) → Char=%s (ID=%d) Job=%d Level=%d Map=%q Pos=(%.2f,%.2f) HP=%d/%d MP=%d/%d from %s",
		req.Username, accountID, record.Username, record.ID, record.JobClass, record.Level, mapName, record.X, record.Y, hp, hp, mp, mp, clientAddr)

	// Lưu AccountID vào session để CharacterListHandler có thể dùng sau
	session.AccountID = accountID

	session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
		Success:     true,
		PlayerID:    record.ID,
		JobClass:    uint8(record.JobClass),
		Level:       uint16(record.Level),
		Exp:         uint32(record.Exp),
		HP:          hp,
		MaxHP:       session.Player.Stats.MaxHP,
		X:           spawnX,
		Y:           spawnY,
		MapName:     mapName,
		CharName:    record.Username,
		Message:     "OK",
		// Combat stats for StatsManager
		MaxMP:       session.Player.Stats.MaxMP,
		ATKPhysical: session.Player.Stats.ATKPhysical,
		ATKMagic:    session.Player.Stats.ATKMagic,
		DEFPhysical: session.Player.Stats.DEFPhysical,
		DEFMagic:    session.Player.Stats.DEFMagic,
		SkillPoints: uint32(session.Player.SkillPoints),
		CritRate:    session.Player.Stats.CritRate,
		LifeSteal:   session.Player.Stats.LifeSteal,
		AttackSpeed: session.Player.Stats.AttackSpeed,
	}))
}
