package player

import (
	"math"

	"adventure2d-server/pkg/mathutil"
)

const (
	// DashDuration is the dash movement phase in seconds (at default MoveSpeed=10).
	DashDuration = float32(0.75)

	// DashEndLag is the landing/recovery phase after dash (at default MoveSpeed=10).
	DashEndLag = float32(0.45)

	// DashCooldown is the minimum time between two dashes.
	DashCooldown = float32(1.5)

	// BaseDashDistance is the minimum max-dash-distance at default MoveSpeed (10).
	// Increases by 10% for every 5 points of MoveSpeed above the base.
	// Cannot go below this value regardless of speed.
	BaseDashDistance = float32(12.0)

	// BaseMoveSpeed is the reference speed for dash distance scaling.
	BaseMoveSpeed = float32(10.0)

	// DashDistancePerStep is the % gain per 5 speed above BaseMoveSpeed.
	DashDistancePerStep = float32(0.10)
)

// ComputeMaxDashDistance returns the maximum allowed dash path length for a
// player with the given moveSpeed.
//
//	extraSteps = floor(max(0, moveSpeed - BaseMoveSpeed) / 5)
//	maxDist    = BaseDashDistance × (1 + extraSteps × DashDistancePerStep)
//
func ComputeMaxDashDistance(moveSpeed float32) float32 {
	if moveSpeed <= BaseMoveSpeed {
		return BaseDashDistance
	}
	extraSteps := math.Floor(float64(moveSpeed-BaseMoveSpeed) / 5.0)
	return BaseDashDistance * (1 + float32(extraSteps)*DashDistancePerStep)
}

// ComputeDashAnimSpeed returns the animation playback speed multiplier for the
// given moveSpeed — mirrors HumanDashState.ComputeAnimSpeedMultiplier() exactly.
//
//	multiplier = 1 + floor(max(0, moveSpeed - BaseMoveSpeed) / 5) * 0.10
//
// Used to scale DashDuration and DashEndLag so server timing matches animation.
func ComputeDashAnimSpeed(moveSpeed float32) float32 {
	if moveSpeed <= BaseMoveSpeed {
		return 1.0
	}
	extraSteps := math.Floor(float64(moveSpeed-BaseMoveSpeed) / 5.0)
	return 1 + float32(extraSteps)*DashDistancePerStep
}

// DashState moves the player along a NavMesh-computed waypoint path at
// dashSpeed = TotalDistance / DashDuration.
//
// The client (Unity NavMesh) validates that the path is walkable, so the server
// only needs to follow the waypoints — no IsWalkable check required here.
//
// Transitions to DashEndState when all waypoints are consumed or the timer expires.
type DashState struct {
	timer      float32
	dashSpeed  float32          // units/sec = totalDist / DashDuration
	waypoints  []mathutil.Vector2 // path to follow (captured from DashReq)
	wayptIdx   int
}

func (s *DashState) StateID() State { return StateDash }

func (s *DashState) Enter(p *Player) {
	// Scale duration to match animation speed at player's current MoveSpeed
	animSpeed := ComputeDashAnimSpeed(p.Stats.MoveSpeed)
	scaledDuration := DashDuration / animSpeed // e.g. 0.65/1.1 = 0.591s at speed 15

	s.timer    = scaledDuration
	s.wayptIdx = 0
	s.waypoints = p.DashWaypoints // set by dash_handler before TransitionTo

	// Guard: if no waypoints provided, compute minimal forward step
	if len(s.waypoints) == 0 {
		dest := mathutil.Vector2{
			X: p.Position.X + p.Direction.X*2,
			Y: p.Position.Y + p.Direction.Y*2,
		}
		s.waypoints = []mathutil.Vector2{dest}
	}

	// dashSpeed = distance / scaledDuration (player arrives exactly when anim ends)
	totalDist := p.DashTotalDistance
	if totalDist < 0.01 {
		// Recompute from waypoints as fallback
		totalDist = computePathLength(p.Position, s.waypoints)
	}
	// Anti-cheat: cap to dynamic max based on player's current MoveSpeed
	maxDist := ComputeMaxDashDistance(p.Stats.MoveSpeed)
	if totalDist > maxDist {
		totalDist = maxDist
	}
	s.dashSpeed = totalDist / scaledDuration

	// Store scaled end-lag so DashEndState uses the same speed factor
	p.DashEndLagDynamic = DashEndLag / animSpeed

	p.State        = StateDash
	p.LastDashTime = p.GameTime

	// Clear normal movement waypoints so MoveState doesn't resume after DashEnd
	p.Waypoints   = nil
	p.WaypointIdx = 0
}

func (s *DashState) Update(p *Player, dt float32) PlayerState {
	s.timer -= dt

	if s.wayptIdx < len(s.waypoints) {
		target := s.waypoints[s.wayptIdx]
		toTarget := mathutil.Vector2{
			X: target.X - p.Position.X,
			Y: target.Y - p.Position.Y,
		}
		distToTarget := toTarget.Length()
		step := s.dashSpeed * dt

		if step >= distToTarget {
			// Snap to waypoint and advance
			p.Position.X = target.X
			p.Position.Y = target.Y
			p.Direction  = toTarget.Normalized()
			s.wayptIdx++
		} else {
			// Move toward waypoint
			dir := toTarget.Normalized()
			p.Position.X += dir.X * step
			p.Position.Y += dir.Y * step
			p.Direction   = dir
		}
	}

	// End dash when all waypoints consumed or timer expired
	if s.wayptIdx >= len(s.waypoints) || s.timer <= 0 {
		return &DashEndState{}
	}
	return nil
}

func (s *DashState) Exit(_ *Player) {
	// Clear dash-specific fields on player
}

// computePathLength returns the total length of the path from start through waypoints.
func computePathLength(start mathutil.Vector2, waypoints []mathutil.Vector2) float32 {
	total := float32(0)
	prev := start
	for _, wp := range waypoints {
		total += prev.Distance(wp)
		prev = wp
	}
	return total
}
