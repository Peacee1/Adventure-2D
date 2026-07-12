package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
)

// RegisterHandler xử lý yêu cầu đăng ký tài khoản từ client.
// SRP: Chịu trách nhiệm duy nhất cho việc tiếp nhận và xử lý luồng đăng ký.
type RegisterHandler struct {
	repo player.Repository
}

func NewRegisterHandler(repo player.Repository) *RegisterHandler {
	return &RegisterHandler{repo: repo}
}

// Handle giải mã gói tin đăng ký và thực thi đăng ký trong Database.
func (h *RegisterHandler) Handle(payload []byte, session *player.Session) {
	req, err := packet.DecodeRegisterReq(payload)
	if err != nil {
		log.Printf("[RegisterHandler] Decode error: %v", err)
		session.Send(packet.EncodeRegisterAck(packet.RegisterAckPacket{
			Success: false,
			Message: "invalid packet",
		}))
		return
	}

	if req.Username == "" || req.Password == "" {
		session.Send(packet.EncodeRegisterAck(packet.RegisterAckPacket{
			Success: false,
			Message: "username or password empty",
		}))
		return
	}

	err = h.repo.RegisterAccount(req.Username, req.Password)
	if err != nil {
		log.Printf("[RegisterHandler] Failed to register %s: %v", req.Username, err)
		session.Send(packet.EncodeRegisterAck(packet.RegisterAckPacket{
			Success: false,
			Message: "username already exists",
		}))
		return
	}

	log.Printf("[RegisterHandler] Register OK: %s", req.Username)
	session.Send(packet.EncodeRegisterAck(packet.RegisterAckPacket{
		Success: true,
		Message: "registration successful",
	}))
}
