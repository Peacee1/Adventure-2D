package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
	"adventure2d-server/pkg/mathutil"
)

// MoveHandler xử lý MoveInputPacket nhận qua UDP.
type MoveHandler struct {
	roomManager *room.Manager
	seenPlayers map[uint32]bool // track lần đầu nhận packet từ player
}

func NewMoveHandler(rm *room.Manager) *MoveHandler {
	return &MoveHandler{
		roomManager: rm,
		seenPlayers: make(map[uint32]bool),
	}
}

// Handle cập nhật vị trí và hướng di chuyển của player.
// Gọi từ UDP receive goroutine — phải không blocking.
func (h *MoveHandler) Handle(payload []byte, _ interface{}) {
	req, err := packet.DecodeMoveInput(payload)
	if err != nil {
		log.Printf("[MoveHandler] Decode error: %v", err)
		return
	}

	r := h.roomManager.FindRoomByPlayer(req.PlayerID)
	if r == nil {
		return // Không log — UDP có thể đến trước join room
	}

	p, ok := r.GetPlayer(req.PlayerID)
	if !ok {
		return
	}

	// Log lần đầu nhận UDP từ player (quan trọng để debug)
	if !h.seenPlayers[req.PlayerID] {
		h.seenPlayers[req.PlayerID] = true
		log.Printf("[MoveHandler] ✅ Nhận MoveInput đầu tiên từ player %d (room=%s) pos=(%.2f,%.2f)",
			req.PlayerID, r.ID, req.DestX, req.DestY)
	}

	if !p.SetLastMoveTimestamp(req.Timestamp) {
		return // out-of-order, bỏ qua
	}

	// Speed hack check
	dir := mathutil.Vector2{X: req.DirX, Y: req.DirY}
	if dir.LengthSq() > 1.01 {
		log.Printf("[MoveHandler] CHEAT: player %d direction=%.4f > 1.0", req.PlayerID, dir.LengthSq())
		return
	}

	newPos := mathutil.Vector2{X: req.DestX, Y: req.DestY}
	p.SetPosition(newPos)
	p.SetDirection(dir)

	if dir.LengthSq() > 0.01 {
		p.SetState(player.StateMove)
	} else {
		p.SetState(player.StateIdle)
	}
}
