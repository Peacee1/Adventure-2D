package player

// DeadState — player is dead. All actions are rejected.
// Transitions to IdleState only when Respawn() is called externally
// (which directly calls SM.TransitionTo(StateIdle)).
type DeadState struct{}

func (s *DeadState) StateID() State { return StateDead }

func (s *DeadState) Enter(p *Player) {
	p.State     = StateDead
	p.Direction = p.Direction // keep last facing direction
	p.Waypoints   = nil
	p.WaypointIdx = 0
}

// Update does nothing — transition out is triggered externally by Respawn().
func (s *DeadState) Update(_ *Player, _ float32) PlayerState { return nil }

func (s *DeadState) Exit(_ *Player) {}
