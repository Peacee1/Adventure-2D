package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
	"adventure2d-server/pkg/mathutil"
)

// MovePathHandler handles MovePathPacket — NavMesh path waypoints from the client.
// Unity computes the NavMesh path and sends the corners to the server.
// The server moves the player along these waypoints via the StateMachine.
type MovePathHandler struct {
	roomManager *room.Manager
}

func NewMovePathHandler(rm *room.Manager) *MovePathHandler {
	return &MovePathHandler{roomManager: rm}
}

// Handle receives MovePathPacket over TCP and updates the player's path.
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

	// Convert WaypointVec2 → mathutil.Vector2
	waypoints := make([]mathutil.Vector2, len(req.Waypoints))
	for i, w := range req.Waypoints {
		waypoints[i] = mathutil.Vector2{X: w.X, Y: w.Y}
	}

	// SetPathAndMove sets waypoints and transitions to MoveState via SM
	// (only if player is Idle or already Moving — dash/attack take priority)
	p.GetMu().Lock()
	player.SetPathAndMove(p, waypoints)
	p.GetMu().Unlock()

	log.Printf("[MovePathHandler] Player %d path set: %d waypoints, dest=(%.1f,%.1f)",
		req.PlayerID, len(waypoints),
		waypoints[len(waypoints)-1].X,
		waypoints[len(waypoints)-1].Y)
}
