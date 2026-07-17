package player

import "adventure2d-server/pkg/mathutil"

// MoveState — advances the player along NavMesh waypoints each tick.
// Transitions back to IdleState when all waypoints are consumed.
type MoveState struct{}

func (s *MoveState) StateID() State { return StateMove }

func (s *MoveState) Enter(p *Player) {
	p.State = StateMove
}

func (s *MoveState) Update(p *Player, dt float32) PlayerState {
	// No remaining waypoints → stop
	if p.WaypointIdx >= len(p.Waypoints) {
		return &IdleState{}
	}

	target := p.Waypoints[p.WaypointIdx]
	diff   := target.Sub(p.Position)
	dist   := diff.Length()

	const stoppingDistance = float32(0.15)
	if dist <= stoppingDistance {
		// Snap to waypoint and advance
		p.Position = target
		p.WaypointIdx++
		if p.WaypointIdx >= len(p.Waypoints) {
			return &IdleState{}
		}
		return nil
	}

	dir      := diff.Normalized()
	moveStep := p.Stats.MoveSpeed * dt
	if moveStep >= dist {
		p.Position = target
	} else {
		p.Position = p.Position.Add(dir.Scale(moveStep))
	}
	p.Direction = dir
	return nil
}

func (s *MoveState) Exit(_ *Player) {}

// SetPath sets new waypoints and transitions to MoveState if player is Idle.
// Called by move_path_handler; SM must already be held under player mutex.
func SetPathAndMove(p *Player, waypoints []mathutil.Vector2) {
	p.Waypoints   = waypoints
	p.WaypointIdx = 0
	if len(waypoints) > 0 {
		p.Destination = waypoints[len(waypoints)-1]
	}
	// Transition only when idle or already moving — dash/attack take priority
	if p.State == StateIdle || p.State == StateMove {
		p.SM.TransitionTo(StateMove, p)
	}
}
