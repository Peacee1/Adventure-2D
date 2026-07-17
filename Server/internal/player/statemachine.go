// Package player — PlayerStateMachine drives server-side state transitions.
//
// Each Player owns one StateMachine. The game loop calls sm.Update(p, dt) every
// tick; each State returns the next State (or nil to stay) from its Update method.
//
// Responsibilities per state:
//   IdleState     — waits for waypoints/dash/attack input signals
//   MoveState     — advances player along waypoints each tick
//   DashState     — moves player at dash speed for DashDuration seconds
//   DashEndState  — freezes player for DashEndLag seconds (landing lag)
//   AttackState   — freezes player for AttackDuration seconds
//   DeadState     — freezes player until Respawn is called
package player

import "log"

// PlayerState is implemented by every server-side state.
// Update returns the next state to transition into, or nil to remain.
type PlayerState interface {
	Enter(p *Player)
	Update(p *Player, dt float32) PlayerState
	Exit(p *Player)
	StateID() State
}

// PlayerStateMachine manages the active state for one Player.
// Not safe for concurrent use — the game loop holds the player mutex during Update.
type PlayerStateMachine struct {
	current PlayerState
	states  map[State]PlayerState
}

// NewPlayerStateMachine creates a SM and registers all states.
func NewPlayerStateMachine() *PlayerStateMachine {
	sm := &PlayerStateMachine{
		states: make(map[State]PlayerState),
	}

	sm.register(&IdleState{})
	sm.register(&MoveState{})
	sm.register(&DashState{})
	sm.register(&DashEndState{})
	sm.register(&AttackState{})
	sm.register(&DeadState{})

	sm.current = sm.states[StateIdle]
	return sm
}

func (sm *PlayerStateMachine) register(s PlayerState) {
	sm.states[s.StateID()] = s
}

// CurrentStateID returns the ID of the currently active state.
func (sm *PlayerStateMachine) CurrentStateID() State {
	if sm.current == nil {
		return StateIdle
	}
	return sm.current.StateID()
}

// TransitionTo forces an immediate state change.
// Safe to call from handlers (dash_handler, attack_handler) after acquiring the player mutex.
func (sm *PlayerStateMachine) TransitionTo(id State, p *Player) {
	next, ok := sm.states[id]
	if !ok {
		log.Printf("[SM] Player %d: unknown state %d — ignored", p.ID, id)
		return
	}
	if sm.current != nil && sm.current.StateID() == id {
		return // already in this state
	}
	prev := sm.current.StateID()
	if sm.current != nil {
		sm.current.Exit(p)
	}
	sm.current = next
	sm.current.Enter(p)
	log.Printf("[SM] Player %d (%s): %s → %s", p.ID, p.Username, stateName(prev), stateName(id))
}

// Update ticks the current state. Must be called with the player mutex held.
func (sm *PlayerStateMachine) Update(p *Player, dt float32) {
	if sm.current == nil {
		return
	}
	next := sm.current.Update(p, dt)
	if next != nil {
		prev := sm.current.StateID()
		sm.current.Exit(p)
		sm.current = next
		sm.current.Enter(p)
		log.Printf("[SM] Player %d (%s): %s → %s (auto)", p.ID, p.Username, stateName(prev), stateName(next.StateID()))
	}
}

// stateName returns a human-readable name for logging.
func stateName(s State) string {
	switch s {
	case StateIdle:
		return "Idle"
	case StateMove:
		return "Move"
	case StateDash:
		return "Dash"
	case StateDashEnd:
		return "DashEnd"
	case StateAttack:
		return "Attack"
	case StateDead:
		return "Dead"
	default:
		return "Unknown"
	}
}
