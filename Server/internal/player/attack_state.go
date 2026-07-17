package player

const (
	// DefaultAttackDuration is the fallback attack duration when AttackSpeed is not set.
	DefaultAttackDuration = float32(1.0)
)

// AttackState — freezes the player for the attack animation duration.
// Position is locked; waypoints are ignored.
// Transitions to IdleState when the attack timer expires.
type AttackState struct {
	timer float32
}

func (s *AttackState) StateID() State { return StateAttack }

func (s *AttackState) Enter(p *Player) {
	duration := float32(p.Stats.AttackSpeed)
	if duration <= 0 {
		duration = DefaultAttackDuration
	}
	s.timer = duration
	p.State  = StateAttack
}

func (s *AttackState) Update(p *Player, dt float32) PlayerState {
	s.timer -= dt
	if s.timer <= 0 {
		// Resume movement if player had pending waypoints before the attack
		if len(p.Waypoints) > 0 && p.WaypointIdx < len(p.Waypoints) {
			return &MoveState{}
		}
		return &IdleState{}
	}
	return nil
}

func (s *AttackState) Exit(_ *Player) {}
