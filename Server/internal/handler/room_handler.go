package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
)

// RoomHandler xử lý JoinRoomReqPacket.
type RoomHandler struct {
	roomManager *room.Manager
}

func NewRoomHandler(rm *room.Manager) *RoomHandler {
	return &RoomHandler{roomManager: rm}
}

// HandleJoin xử lý yêu cầu vào phòng.
func (h *RoomHandler) HandleJoin(payload []byte, session *player.Session) {
	req, err := packet.DecodeJoinRoomReq(payload)
	if err != nil {
		log.Printf("[RoomHandler] Decode error: %v", err)
		session.Send(packet.EncodeJoinRoomAck(packet.JoinRoomAckPacket{
			Success: false,
		}))
		return
	}

	// Kiểm tra player đã login chưa
	if session.Player.ID == 0 {
		session.Send(packet.EncodeJoinRoomAck(packet.JoinRoomAckPacket{
			Success: false,
			RoomID:  "not_logged_in",
		}))
		return
	}

	r := h.roomManager.GetOrCreate(req.RoomID)
	if r.IsFull() {
		session.Send(packet.EncodeJoinRoomAck(packet.JoinRoomAckPacket{
			Success: false,
			RoomID:  "room_full",
		}))
		return
	}

	// Thêm player vào phòng
	existing := r.AddPlayer(session.Player)

	// Gửi ack kèm danh sách player hiện có
	session.Send(packet.EncodeJoinRoomAck(packet.JoinRoomAckPacket{
		Success:         true,
		RoomID:          r.ID,
		ExistingPlayers: existing,
	}))

	log.Printf("[RoomHandler] Player %d joined room %s", session.Player.ID, r.ID)
}

// HandleLeave xử lý khi player rời phòng (disconnect hoặc explicit leave).
func (h *RoomHandler) HandleLeave(playerID uint32) {
	r := h.roomManager.FindRoomByPlayer(playerID)
	if r == nil {
		return
	}
	r.RemovePlayer(playerID)
	h.roomManager.RemoveIfEmpty(r.ID)
}
