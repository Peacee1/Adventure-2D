package player

import "adventure2d-server/pkg/mathutil"

// IdleState — player stands still, waiting for movement, dash, or attack.
// Transitions to MoveState automatically when waypoints are available.
type IdleState struct{}

func (s *IdleState) StateID() State { return StateIdle }

func (s *IdleState) Enter(p *Player) {
	p.State     = StateIdle
	p.Direction = mathutil.Vector2{}
}

func (s *IdleState) Update(p *Player, _ float32) PlayerState {
	// Auto-transition to Move if waypoints arrive (set by move_path_handler)
	if p.WaypointIdx < len(p.Waypoints) {
		return &MoveState{}
	}
	return nil
}

func (s *IdleState) Exit(_ *Player) {}
