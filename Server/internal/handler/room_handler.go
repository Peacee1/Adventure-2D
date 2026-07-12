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
	clientAddr := session.RemoteAddr().String()
	log.Printf("[RoomHandler] JoinRoom request from player %d (%s) at %s",
		session.Player.ID, session.Player.Username, clientAddr)

	req, err := packet.DecodeJoinRoomReq(payload)
	if err != nil {
		log.Printf("[RoomHandler] Decode error from %s: %v", clientAddr, err)
		session.Send(packet.EncodeJoinRoomAck(packet.JoinRoomAckPacket{
			Success: false,
		}))
		return
	}

	log.Printf("[RoomHandler] Decoded: roomID=%q (empty=matchmake) from player %d (%s)",
		req.RoomID, session.Player.ID, session.Player.Username)

	// Kiểm tra player đã login chưa
	if session.Player.ID == 0 {
		log.Printf("[RoomHandler] Player not logged in from %s — rejecting join", clientAddr)
		session.Send(packet.EncodeJoinRoomAck(packet.JoinRoomAckPacket{
			Success: false,
			RoomID:  "not_logged_in",
		}))
		return
	}

	// Kiểm tra player đã ở trong phòng nào chưa (tránh join 2 lần)
	existingRoom := h.roomManager.FindRoomByPlayer(session.Player.ID)
	if existingRoom != nil {
		log.Printf("[RoomHandler] Player %d already in room %s — rejecting duplicate join",
			session.Player.ID, existingRoom.ID)
		session.Send(packet.EncodeJoinRoomAck(packet.JoinRoomAckPacket{
			Success: false,
			RoomID:  "already_in_room",
		}))
		return
	}

	r := h.roomManager.GetOrCreate(req.RoomID)
	if r.IsFull() {
		log.Printf("[RoomHandler] Room %s is full — rejecting player %d", r.ID, session.Player.ID)
		session.Send(packet.EncodeJoinRoomAck(packet.JoinRoomAckPacket{
			Success: false,
			RoomID:  "room_full",
		}))
		return
	}

	// Thêm player vào phòng
	existing := r.AddPlayer(session.Player)

	log.Printf("[RoomHandler] Player %d (%s) joined room %s (existing players: %d)",
		session.Player.ID, session.Player.Username, r.ID, len(existing))

	// Gửi ack kèm danh sách player hiện có
	session.Send(packet.EncodeJoinRoomAck(packet.JoinRoomAckPacket{
		Success:         true,
		RoomID:          r.ID,
		ExistingPlayers: existing,
	}))
}

// HandleLeave xử lý khi player rời phòng (disconnect hoặc explicit leave).
func (h *RoomHandler) HandleLeave(playerID uint32) {
	if playerID == 0 {
		return // player chưa login thì không cần xử lý
	}

	r := h.roomManager.FindRoomByPlayer(playerID)
	if r == nil {
		log.Printf("[RoomHandler] HandleLeave: player %d not in any room", playerID)
		return
	}

	log.Printf("[RoomHandler] Player %d leaving room %s", playerID, r.ID)
	r.RemovePlayer(playerID)
	h.roomManager.RemoveIfEmpty(r.ID)
}
