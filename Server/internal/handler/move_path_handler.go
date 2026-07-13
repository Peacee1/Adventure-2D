package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/room"
	"adventure2d-server/pkg/mathutil"
)

// MovePathHandler xử lý MovePathPacket — NavMesh path waypoints từ client.
// Khi Unity NavMesh tính xong path, client gửi toàn bộ corners lên server.
// Server lưu vào player.Waypoints để game loop di chuyển theo đúng path.
type MovePathHandler struct {
	roomManager *room.Manager
}

func NewMovePathHandler(rm *room.Manager) *MovePathHandler {
	return &MovePathHandler{roomManager: rm}
}

// Handle nhận MovePathPacket qua TCP và cập nhật path cho player.
func (h *MovePathHandler) Handle(payload []byte, sess interface{}) {
	req, err := packet.DecodeMovePathPacket(payload)
	if err != nil {
		log.Printf("[MovePathHandler] Decode error: %v", err)
		return
	}

	if len(req.Waypoints) == 0 {
		return
	}

	r := h.roomManager.FindRoomByPlayer(req.PlayerID)
	if r == nil {
		return
	}

	p, ok := r.GetPlayer(req.PlayerID)
	if !ok {
		return
	}

	// Chuyển đổi WaypointVec2 → mathutil.Vector2
	waypoints := make([]mathutil.Vector2, len(req.Waypoints))
	for i, w := range req.Waypoints {
		waypoints[i] = mathutil.Vector2{X: w.X, Y: w.Y}
	}

	p.SetPath(waypoints)

	log.Printf("[MovePathHandler] Player %d path set: %d waypoints, dest=(%.1f,%.1f)",
		req.PlayerID, len(waypoints),
		waypoints[len(waypoints)-1].X,
		waypoints[len(waypoints)-1].Y)
}
