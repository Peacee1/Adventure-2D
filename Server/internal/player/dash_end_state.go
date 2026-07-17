package player

// DashEndState — freezes the player for DashEndLag seconds (landing lag).
// All movement and action requests are rejected while in this state.
// Transitions to IdleState when the lag timer expires.
type DashEndState struct {
	timer float32
}

func (s *DashEndState) StateID() State { return StateDashEnd }

func (s *DashEndState) Enter(p *Player) {
	// Use the speed-scaled lag set by DashState.Enter(); fall back to constant
	// if somehow not set (e.g. first dash before any speed scaling).
	lag := p.DashEndLagDynamic
	if lag < 0.05 {
		lag = DashEndLag
	}
	s.timer    = lag
	p.State    = StateDashEnd
	p.Direction = p.Direction // keep facing direction from dash
}

func (s *DashEndState) Update(p *Player, dt float32) PlayerState {
	s.timer -= dt
	if s.timer <= 0 {
		return &IdleState{}
	}
	return nil
}

func (s *DashEndState) Exit(_ *Player) {}
