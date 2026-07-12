package handler

import (
	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
	"adventure2d-server/pkg/mathutil"
)

// MoveHandler xử lý MoveInputPacket nhận qua UDP.
type MoveHandler struct {
	roomManager *room.Manager
}

func NewMoveHandler(rm *room.Manager) *MoveHandler {
	return &MoveHandler{roomManager: rm}
}

// Handle cập nhật vị trí và hướng di chuyển của player.
// Gọi từ UDP receive goroutine — phải không blocking.
func (h *MoveHandler) Handle(payload []byte, _ interface{}) {
	// Decode packet
	req, err := packet.DecodeMoveInput(payload)
	if err != nil {
		return
	}

	// Tìm phòng chứa player
	r := h.roomManager.FindRoomByPlayer(req.PlayerID)
	if r == nil {
		return
	}

	p, ok := r.GetPlayer(req.PlayerID)
	if !ok {
		return
	}

	// Lọc out-of-order UDP packet (SetLastMoveTimestamp trả về false nếu cũ hơn)
	if !p.SetLastMoveTimestamp(req.Timestamp) {
		return
	}

	// Server-side speed hack check (đơn giản)
	dir := mathutil.Vector2{X: req.DirX, Y: req.DirY}
	if dir.LengthSq() > 1.01 { // magnitude > 1 = cheat
		return
	}

	// Cập nhật state player
	newPos := mathutil.Vector2{X: req.DestX, Y: req.DestY}
	p.SetPosition(newPos)
	p.SetDirection(dir)

	if dir.LengthSq() > 0.01 {
		p.SetState(player.StateMove)
	} else {
		p.SetState(player.StateIdle)
	}
}
