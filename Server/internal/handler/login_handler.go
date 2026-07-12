package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
)

// LoginHandler xử lý LoginReqPacket.
// Phase 1: không cần DB, chỉ cần username.
type LoginHandler struct {
	roomManager *room.Manager
}

func NewLoginHandler(rm *room.Manager) *LoginHandler {
	return &LoginHandler{roomManager: rm}
}

// Handle xử lý login request từ một session.
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

	// Phase 1: sinh ID tự động, không check DB
	if req.Username == "" {
		req.Username = "Player"
	}

	id := player.NextID()
	session.Player.ID = id
	session.Player.Username = req.Username

	log.Printf("[LoginHandler] Login OK: %s → ID=%d", req.Username, id)

	session.Send(packet.EncodeLoginAck(packet.LoginAckPacket{
		Success:  true,
		PlayerID: id,
		Message:  "OK",
	}))
}
