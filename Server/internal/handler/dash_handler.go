package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
	"adventure2d-server/pkg/mathutil"
)

// DashHandler handles DashReqPacket received over TCP.
// The client sends a NavMesh-computed path (waypoints + total distance).
// The server validates cooldown/state, stores the path, then starts DashState.
type DashHandler struct {
	roomManager *room.Manager
}

func NewDashHandler(rm *room.Manager) *DashHandler {
	return &DashHandler{roomManager: rm}
}

// Handle validates and initiates a dash via the player's StateMachine.
func (h *DashHandler) Handle(payload []byte, session *player.Session) {
	req, err := packet.DecodeDashReq(payload)
	if err != nil {
		log.Printf("[DashHandler] Decode error: %v", err)
		return
	}

	p := session.Player
	_, _, _, hp, state := p.Snapshot()

	if state == player.StateDead || hp == 0 {
		log.Printf("[DashHandler] Player %d (%s) is dead — dash ignored", p.ID, p.Username)
		return
	}

	// Reject during landing lag
	if state == player.StateDashEnd {
		log.Printf("[DashHandler] Player %d (%s) in DashEnd lag — dash rejected", p.ID, p.Username)
		return
	}

	// Server-side cooldown check
	if !p.CanDash() {
		log.Printf("[DashHandler] Player %d (%s) dash on cooldown", p.ID, p.Username)
		return
	}

	// Validate payload has at least 1 waypoint
	if len(req.Waypoints) == 0 {
		log.Printf("[DashHandler] Player %d (%s) sent empty dash waypoints — rejected", p.ID, p.Username)
		return
	}

	// Anti-cheat: cap total distance to the player's dynamic max
	maxDist := player.ComputeMaxDashDistance(p.Stats.MoveSpeed)
	totalDist := req.TotalDistance
	if totalDist > maxDist {
		log.Printf("[DashHandler] Player %d (%s) dash distance %.2f capped to %.2f (speed=%.1f)",
			p.ID, p.Username, totalDist, maxDist, p.Stats.MoveSpeed)
		totalDist = maxDist
	}

	// Convert packet waypoints to mathutil.Vector2
	waypoints := make([]mathutil.Vector2, len(req.Waypoints))
	for i, wp := range req.Waypoints {
		waypoints[i] = mathutil.Vector2{X: wp.X, Y: wp.Y}
	}

	// Set dash path on player — read by DashState.Enter()
	p.GetMu().Lock()
	p.DashWaypoints     = waypoints
	p.DashTotalDistance = totalDist
	p.GetMu().Unlock()

	p.StartDash(mathutil.Vector2{}) // direction unused — DashState uses waypoints

	firstWP := req.Waypoints[0]
	log.Printf("[DashHandler] Player %d (%s) dashing: waypoints=%d dist=%.2f first=(%.2f,%.2f)",
		p.ID, p.Username, len(req.Waypoints), totalDist, firstWP.X, firstWP.Y)
}
