package handler

import (
	"log"
	"strings"
	"unicode/utf8"

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
	clientAddr := session.RemoteAddr().String()
	log.Printf("[RegisterHandler] Register request from %s", clientAddr)

	req, err := packet.DecodeRegisterReq(payload)
	if err != nil {
		log.Printf("[RegisterHandler] Decode error from %s: %v", clientAddr, err)
		session.Send(packet.EncodeRegisterAck(packet.RegisterAckPacket{
			Success: false,
			Message: "invalid packet",
		}))
		return
	}

	log.Printf("[RegisterHandler] Decoded: username=%q (len=%d) from %s",
		req.Username, utf8.RuneCountInString(req.Username), clientAddr)

	// Kiểm tra trường rỗng
	if strings.TrimSpace(req.Username) == "" || req.Password == "" {
		log.Printf("[RegisterHandler] Empty username/password from %s", clientAddr)
		session.Send(packet.EncodeRegisterAck(packet.RegisterAckPacket{
			Success: false,
			Message: "username or password cannot be empty",
		}))
		return
	}

	// Thực hiện đăng ký (validation & hash được xử lý trong db layer)
	err = h.repo.RegisterAccount(req.Username, req.Password)
	if err != nil {
		errMsg := err.Error()
		log.Printf("[RegisterHandler] Register FAILED for username=%q from %s: %v", req.Username, clientAddr, err)

		// Map lỗi nội bộ sang thông báo thân thiện với client
		clientMsg := "registration failed"
		if strings.Contains(errMsg, "already exists") {
			clientMsg = "username already exists"
		} else if strings.Contains(errMsg, "too short") || strings.Contains(errMsg, "too long") ||
			strings.Contains(errMsg, "only contain") {
			clientMsg = errMsg // trả validation message trực tiếp
		}

		session.Send(packet.EncodeRegisterAck(packet.RegisterAckPacket{
			Success: false,
			Message: clientMsg,
		}))
		return
	}

	log.Printf("[RegisterHandler] Register OK: username=%q from %s", req.Username, clientAddr)
	session.Send(packet.EncodeRegisterAck(packet.RegisterAckPacket{
		Success: true,
		Message: "registration successful",
	}))
}
