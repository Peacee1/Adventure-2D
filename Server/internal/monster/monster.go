// Package monster provides server-simulated enemy entities.
//
// Design: GoF State Pattern + Flyweight optimisation.
//   - IMonsterState defines the state interface.
//   - Five singleton state objects are allocated ONCE at package init
//     (stateIdle, stateWander, stateChase, stateAttack, stateDead).
//   - Transitions call transitionTo(singleton) — zero heap allocation.
//   - All mutable data lives on the Monster struct; state singletons are
//     purely behavioural (no per-instance fields → safe to share).
//
// SOLID adherence:
//   S — each state struct has a single responsibility.
//   O — add new states without modifying Monster or existing states.
//   L — all states are substitutable via IMonsterState.
//   I — IMonsterState is minimal: Enter, Update, Byte.
//   D — Monster depends on the IMonsterState abstraction.
package monster

import (
	"math"
	"math/rand"

	"adventure2d-server/pkg/mathutil"
)

// MonsterIDBase — monster IDs begin here to avoid collision with player IDs.
const MonsterIDBase = uint32(10000)

// ── State byte codes (shared with clients via MonsterSnapshot.State) ──────────

const (
	StateIdle   uint8 = 0
	StateWander uint8 = 1
	StateChase  uint8 = 2
	StateAttack uint8 = 3
	StateDead   uint8 = 5
)

// ── Tuning ────────────────────────────────────────────────────────────────────

const (
	monsterMaxHP       = uint16(200)
	monsterATK         = uint16(30)
	monsterAttackRange = float32(2.0)  // enter Attack when closer than this
	aggroDropRange     = float32(18.0) // drop aggro when farther than this
	attackCooldown     = float32(1.5)  // seconds between melee swings
	wanderRadius       = float32(6.0)
	wanderMinCD        = float32(2.0)
	wanderMaxCD        = float32(5.0)
	monsterWanderSpeed = float32(3.0)
	monsterChaseSpeed  = float32(5.0)
	deathDisplayTime   = float32(2.0)  // seconds corpse stays visible
	respawnDelay       = float32(10.0) // seconds from death to respawn
)

// ── Singleton state instances (Flyweight) ─────────────────────────────────────
// Allocated once at package init. Zero allocations on every state transition.

var (
	sharedIdle   IMonsterState = &idleState{}
	sharedWander IMonsterState = &wanderState{}
	sharedChase  IMonsterState = &chaseState{}
	sharedAttack IMonsterState = &attackState{}
	sharedDead   IMonsterState = &deadState{}
)

// ── State interface ───────────────────────────────────────────────────────────

// UpdateContext carries per-tick inputs to the active state.
// Avoids passing individual parameters through each state's Update call.
type UpdateContext struct {
	DT        float32
	HasTarget bool
	TargetX   float32
	TargetY   float32
}

// IMonsterState is the contract every monster state must satisfy.
// Implementations must be stateless — all mutable data lives on Monster.
type IMonsterState interface {
	// Enter is called once when the monster transitions into this state.
	Enter(m *Monster)
	// Update runs every game tick and returns true if a melee attack fires.
	Update(m *Monster, ctx UpdateContext) bool
	// Byte returns the uint8 state code sent to clients in WorldState.
	Byte() uint8
}

// ── Monster ───────────────────────────────────────────────────────────────────

// Monster is a server-managed enemy entity.
//
// It stores all mutable fields; AI behaviour is fully delegated to the
// active IMonsterState singleton via currentState.
type Monster struct {
	ID    uint32
	X, Y  float32
	HP    uint16
	MaxHP uint16
	ATK   uint16

	// Active = false when room is empty (alive) or monster is post-death invisible.
	Active bool

	// TargetID: player being chased/attacked; 0 means no aggro.
	TargetID uint32

	homeX, homeY float32 // spawn/wander anchor point

	// currentState points to one of the five package-level singletons.
	currentState IMonsterState

	// Mutable timer fields written by states; zero-value is valid.
	attackTimer       float32
	wanderTargetX     float32
	wanderTargetY     float32
	wanderTimer       float32
	deathDisplayTimer float32
	respawnTimer      float32
}

// New allocates a Monster at pos. Index is used to form a unique ID.
func New(index int, pos mathutil.Vector2) *Monster {
	m := &Monster{
		ID:    MonsterIDBase + uint32(index),
		X:     pos.X,
		Y:     pos.Y,
		HP:    monsterMaxHP,
		MaxHP: monsterMaxHP,
		ATK:   monsterATK,
		homeX: pos.X,
		homeY: pos.Y,
	}
	m.transitionTo(sharedWander) // start wandering — zero allocation
	return m
}

// ── Public API ────────────────────────────────────────────────────────────────

// StateByte returns the current state code for the WorldState packet.
func (m *Monster) StateByte() uint8 {
	if m.currentState == nil {
		return StateIdle
	}
	return m.currentState.Byte()
}

// Activate makes an alive monster visible when players enter the room.
func (m *Monster) Activate() {
	if m.StateByte() == StateDead {
		return // DeadState manages its own lifecycle
	}
	m.Active = true
}

// Deactivate hides an alive monster when the room empties.
func (m *Monster) Deactivate() {
	if m.StateByte() == StateDead {
		return // DeadState keeps running for respawn timer
	}
	m.Active = false
	m.TargetID = 0
}

// SetAggro transitions to ChaseState targeting playerID.
func (m *Monster) SetAggro(playerID uint32) {
	if m.StateByte() == StateDead {
		return
	}
	m.TargetID = playerID
	m.transitionTo(sharedChase)
}

// TakeDamage reduces HP and returns (remaining, died).
// Death automatically transitions to DeadState.
func (m *Monster) TakeDamage(dmg uint16) (remaining uint16, died bool) {
	if m.StateByte() == StateDead {
		return m.HP, false
	}
	if dmg >= m.HP {
		m.HP = 0
		m.TargetID = 0
		m.transitionTo(sharedDead)
		return 0, true
	}
	m.HP -= dmg
	return m.HP, false
}

// Update delegates the tick to the active state singleton.
// Called every tick regardless of Active — DeadState needs its timers ticked.
func (m *Monster) Update(dt float32, hasTarget bool, targetX, targetY float32) bool {
	if m.currentState == nil {
		return false
	}
	return m.currentState.Update(m, UpdateContext{
		DT:        dt,
		HasTarget: hasTarget,
		TargetX:   targetX,
		TargetY:   targetY,
	})
}

// ── Internal helpers ──────────────────────────────────────────────────────────

// transitionTo sets the new state singleton and calls Enter — zero allocation.
func (m *Monster) transitionTo(s IMonsterState) {
	m.currentState = s
	s.Enter(m)
}

func (m *Monster) respawn() {
	m.HP = m.MaxHP
	m.X = m.homeX
	m.Y = m.homeY
	m.TargetID = 0
	m.attackTimer = 0
	m.deathDisplayTimer = 0
	m.respawnTimer = 0
	m.Active = true
	m.transitionTo(sharedWander)
}

func (m *Monster) moveToward(tx, ty, step float32) {
	dx := tx - m.X
	dy := ty - m.Y
	d := dist2D(m.X, m.Y, tx, ty)
	if d <= 0 {
		return
	}
	if step > d {
		step = d
	}
	m.X += dx / d * step
	m.Y += dy / d * step
}

func (m *Monster) pickWanderTarget() {
	angle := rand.Float64() * 2 * math.Pi
	r := rand.Float32() * wanderRadius
	m.wanderTargetX = m.homeX + float32(math.Cos(angle))*r
	m.wanderTargetY = m.homeY + float32(math.Sin(angle))*r
}

func (m *Monster) resetWanderTimer() {
	m.wanderTimer = wanderMinCD + rand.Float32()*(wanderMaxCD-wanderMinCD)
}

func dist2D(x1, y1, x2, y2 float32) float32 {
	dx := x2 - x1
	dy := y2 - y1
	return float32(math.Sqrt(float64(dx*dx + dy*dy)))
}

// ── Concrete state singletons ─────────────────────────────────────────────────

// idleState — monster stands still briefly then transitions to wanderState.
type idleState struct{}

func (s *idleState) Byte() uint8 { return StateIdle }

func (s *idleState) Enter(m *Monster) {
	m.resetWanderTimer()
}

func (s *idleState) Update(m *Monster, ctx UpdateContext) bool {
	if !m.Active {
		return false
	}
	m.wanderTimer -= ctx.DT
	if m.wanderTimer <= 0 {
		m.transitionTo(sharedWander)
	}
	return false
}

// wanderState — monster walks to a random point near its home.
type wanderState struct{}

func (s *wanderState) Byte() uint8 { return StateWander }

func (s *wanderState) Enter(m *Monster) {
	m.resetWanderTimer()
	m.pickWanderTarget()
}

func (s *wanderState) Update(m *Monster, ctx UpdateContext) bool {
	if !m.Active {
		return false
	}
	if dist2D(m.X, m.Y, m.wanderTargetX, m.wanderTargetY) < 0.2 {
		m.transitionTo(sharedIdle)
		return false
	}
	m.moveToward(m.wanderTargetX, m.wanderTargetY, monsterWanderSpeed*ctx.DT)
	return false
}

// chaseState — monster pursues the target player at chase speed.
// Transitions to attackState when in range, wanderState when target lost.
type chaseState struct{}

func (s *chaseState) Byte() uint8 { return StateChase }

func (s *chaseState) Enter(m *Monster) {}

func (s *chaseState) Update(m *Monster, ctx UpdateContext) bool {
	if !m.Active {
		return false
	}
	if !ctx.HasTarget {
		m.TargetID = 0
		m.transitionTo(sharedWander)
		return false
	}
	dist := dist2D(m.X, m.Y, ctx.TargetX, ctx.TargetY)
	switch {
	case dist > aggroDropRange:
		m.TargetID = 0
		m.transitionTo(sharedWander)
	case dist <= monsterAttackRange:
		m.transitionTo(sharedAttack)
	default:
		m.moveToward(ctx.TargetX, ctx.TargetY, monsterChaseSpeed*ctx.DT)
	}
	return false
}

// attackState — monster swings melee at cooldown intervals.
// Returns to chaseState when target moves out of attack range.
type attackState struct{}

func (s *attackState) Byte() uint8 { return StateAttack }

func (s *attackState) Enter(m *Monster) {
	m.attackTimer = 0 // attack fires immediately upon entering this state
}

func (s *attackState) Update(m *Monster, ctx UpdateContext) bool {
	if !m.Active {
		return false
	}
	if !ctx.HasTarget {
		m.TargetID = 0
		m.transitionTo(sharedWander)
		return false
	}
	dist := dist2D(m.X, m.Y, ctx.TargetX, ctx.TargetY)
	switch {
	case dist > aggroDropRange:
		m.TargetID = 0
		m.transitionTo(sharedWander)
		return false
	case dist > monsterAttackRange:
		m.transitionTo(sharedChase)
		return false
	}
	m.attackTimer -= ctx.DT
	if m.attackTimer <= 0 {
		m.attackTimer = attackCooldown
		return true // signal room to apply melee damage
	}
	return false
}

// deadState — two-phase death: visible corpse → invisible → respawn.
// Runs regardless of m.Active so the respawn timer is never paused.
type deadState struct{}

func (s *deadState) Byte() uint8 { return StateDead }

func (s *deadState) Enter(m *Monster) {
	m.deathDisplayTimer = deathDisplayTime
	m.respawnTimer = respawnDelay
}

func (s *deadState) Update(m *Monster, ctx UpdateContext) bool {
	// Phase 1: corpse visible
	if m.Active {
		m.deathDisplayTimer -= ctx.DT
		if m.deathDisplayTimer <= 0 {
			m.Active = false
		}
	}
	// Phase 2: invisible, counting down to respawn
	m.respawnTimer -= ctx.DT
	if m.respawnTimer <= 0 {
		m.respawn()
	}
	return false
}
