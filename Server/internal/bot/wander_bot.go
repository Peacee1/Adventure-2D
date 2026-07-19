// Package bot provides server-side NPC bots that appear as players in WorldState.
package bot

import (
	"fmt"
	"math"
	"math/rand"

	"adventure2d-server/internal/packet"
	"adventure2d-server/pkg/mathutil"
)

const (
	// BotIDBase is the starting ID for bots — far above real player IDs to avoid collision.
	BotIDBase = uint32(90000)

	// MoveSpeed is the bot movement speed in units/second (slower than players).
	MoveSpeed = float32(3.5)

	// WanderRadius is the max distance a bot will walk from its spawn point.
	WanderRadius = float32(8.0)

	// MinWaitSec / MaxWaitSec — random idle pause between movements.
	MinWaitSec = float32(5.0)
	MaxWaitSec = float32(10.0)

	// ArrivalThreshold — how close to the destination counts as "arrived".
	ArrivalThreshold = float32(0.25)

	// BotMaxHP for Archer bots.
	BotMaxHP = uint16(800)

	// Map boundary — from Map1.mapdata world-space export: X[-104..24] Y[-19..78].
	// Inset by 2 units on each side to keep bots off edges.
	MapMinX = float32(-102.0)
	MapMaxX = float32(22.0)
	MapMinY = float32(-17.0)
	MapMaxY = float32(74.0)
)

// stateIdle / stateMove mirrors the server player state constants.
const (
	stateIdle = uint8(0)
	stateMove = uint8(1)
)

// WanderBot is a server-simulated NPC that wanders randomly around its spawn point.
// Its snapshot is injected into WorldStatePacket each tick so all clients render it
// as a RemotePlayer without any special client-side code.
//
// SRP: responsible only for autonomous wander behaviour.
type WanderBot struct {
	ID       uint32
	Username string

	position  mathutil.Vector2
	direction mathutil.Vector2
	state     uint8

	spawnPos    mathutil.Vector2
	destination mathutil.Vector2
	moving      bool
	waitTimer   float32 // counts down; when ≤ 0, pick next destination
}

// New creates a new WanderBot at the given spawn position.
// index is used to generate a unique ID and display name.
func New(index int, spawnPos mathutil.Vector2) *WanderBot {
	b := &WanderBot{
		ID:        BotIDBase + uint32(index),
		Username:  fmt.Sprintf("Bot_%d", index+1),
		position:  spawnPos,
		spawnPos:  spawnPos,
		direction: mathutil.Vector2{X: 1, Y: 0},
		state:     stateIdle,
		// Stagger initial wait so bots don't all start moving simultaneously
		waitTimer: MinWaitSec + rand.Float32()*(MaxWaitSec-MinWaitSec)*float32(index)/3.0,
	}
	return b
}

// Update advances the bot's wander simulation by dt seconds.
// Called by the game loop with the same dt as player simulation.
func (b *WanderBot) Update(dt float32) {
	if b.moving {
		b.stepTowardsDestination(dt)
	} else {
		b.waitTimer -= dt
		if b.waitTimer <= 0 {
			b.pickRandomDestination()
		}
	}
}

// Snapshot returns a PlayerSnapshot for inclusion in WorldStatePacket.
func (b *WanderBot) Snapshot() packet.PlayerSnapshot {
	return packet.PlayerSnapshot{
		PlayerID: b.ID,
		X:        b.position.X,
		Y:        b.position.Y,
		DirX:     b.direction.X,
		DirY:     b.direction.Y,
		HP:       BotMaxHP,
		State:    b.state,
	}
}

// ─── Internal helpers ─────────────────────────────────────────────────────────

func (b *WanderBot) stepTowardsDestination(dt float32) {
	diff := b.destination.Sub(b.position)
	dist := diff.Length()

	if dist <= ArrivalThreshold {
		b.position  = b.destination
		b.state     = stateIdle
		b.moving    = false
		b.waitTimer = MinWaitSec + rand.Float32()*(MaxWaitSec-MinWaitSec)
		return
	}

	step := MoveSpeed * dt
	dir  := diff.Normalized()
	b.direction = dir
	b.state     = stateMove

	if step >= dist {
		b.position  = b.destination
		b.state     = stateIdle
		b.moving    = false
		b.waitTimer = MinWaitSec + rand.Float32()*(MaxWaitSec-MinWaitSec)
	} else {
		b.position.X += dir.X * step
		b.position.Y += dir.Y * step
	}
}

func (b *WanderBot) pickRandomDestination() {
	// Pick a random point within WanderRadius of the spawn, clamped to map bounds.
	// Retry up to 5 times to find a valid in-bounds position.
	for attempt := 0; attempt < 5; attempt++ {
		angle  := rand.Float64() * 2 * math.Pi
		radius := rand.Float32() * WanderRadius
		dest := mathutil.Vector2{
			X: b.spawnPos.X + float32(math.Cos(angle))*radius,
			Y: b.spawnPos.Y + float32(math.Sin(angle))*radius,
		}

		// Clamp to map bounds
		if dest.X < MapMinX { dest.X = MapMinX }
		if dest.X > MapMaxX { dest.X = MapMaxX }
		if dest.Y < MapMinY { dest.Y = MapMinY }
		if dest.Y > MapMaxY { dest.Y = MapMaxY }

		b.destination = dest
		b.moving = true
		return
	}
	// Fallback: return to spawn
	b.destination = b.spawnPos
	b.moving = true
}
