package room

import (
	"log"
	"sync"
	"sync/atomic"

	"adventure2d-server/pkg/mathutil"
)

// HitboxWidth and HitboxHeight define the collision box dimensions for each player's hitbox (server-side).
const (
	HitboxWidth      = float32(2.5)
	HitboxHeight     = float32(5.75)
	ProjHitboxWidth  = float32(3.6)
	ProjHitboxHeight = float32(0.6)
)

// projectileIDCounter is a monotonically increasing counter for unique projectile IDs.
var projectileIDCounter uint32

// Projectile represents an active server-side projectile being simulated.
// The client receives a ProjectileSpawnPacket and renders a visual independently.
// The server owns the authoritative collision result.
type Projectile struct {
	ID          uint32
	OwnerID     uint32
	Position    mathutil.Vector2
	Direction   mathutil.Vector2 // normalized
	Speed       float32          // units per second
	MaxRange    float32          // self-destructs after travelling this distance
	Damage      uint16
	IsCrit      bool             // true if damage was already doubled by a critical hit
	Travelled   float32 // total distance covered so far
}

// ProjectilePool tracks all active projectiles in a room.
// All methods are goroutine-safe via mu.
type ProjectilePool struct {
	mu          sync.Mutex
	projectiles []*Projectile
}

// Add registers a new projectile and returns its ID.
func (pp *ProjectilePool) Add(p *Projectile) {
	p.ID = atomic.AddUint32(&projectileIDCounter, 1)
	pp.mu.Lock()
	pp.projectiles = append(pp.projectiles, p)
	pp.mu.Unlock()
}

// Update advances all projectiles by dt seconds, checks collisions against players,
// and returns a list of hit events to broadcast.
//
// HitEvent carries the result of a projectile hitting a player.
type HitEvent struct {
	AttackerID  uint32
	TargetID    uint32
	Damage      uint32
	RemainingHP uint16
	Died        bool
	IsCrit      bool // propagated from Projectile.IsCrit
}

// TickResult is returned by Update() each tick.
type TickResult struct {
	Hits        []HitEvent
	AlivePos    []ProjectilePos // positions of still-active projectiles to broadcast
	DestroyedID []uint32        // IDs of projectiles that were removed this tick
}

// ProjectilePos is the server position of an active projectile for one tick.
type ProjectilePos struct {
	ID   uint32
	X, Y float32
}

func (pp *ProjectilePool) Update(dt float32, players playerSnapshot) TickResult {
	pp.mu.Lock()
	defer pp.mu.Unlock()

	var result TickResult
	alive := pp.projectiles[:0]

	for _, proj := range pp.projectiles {
		step := proj.Speed * dt
		proj.Position.X += proj.Direction.X * step
		proj.Position.Y += proj.Direction.Y * step
		proj.Travelled += step

		// Self-destruct: out of range
		if proj.Travelled >= proj.MaxRange {
			result.DestroyedID = append(result.DestroyedID, proj.ID)
			log.Printf("[Projectile:%d] Out of range (%.1f/%.1f) — destroyed", proj.ID, proj.Travelled, proj.MaxRange)
			continue
		}

		hit := false
		for _, snap := range players {
			if snap.ID == proj.OwnerID {
				continue
			}
			dx := proj.Position.X - snap.Position.X
			dy := proj.Position.Y - snap.Position.Y
			if dx < 0 {
				dx = -dx
			}
			if dy < 0 {
				dy = -dy
			}
			halfW := (HitboxWidth + ProjHitboxWidth) / 2.0
			halfH := (HitboxHeight + ProjHitboxHeight) / 2.0
			log.Printf("[Projectile:%d] check vs player=%d projPos=(%.2f,%.2f) playerPos=(%.2f,%.2f) dx=%.2f dy=%.2f limits=(%.2f,%.2f)",
				proj.ID, snap.ID, proj.Position.X, proj.Position.Y, snap.Position.X, snap.Position.Y, dx, dy, halfW, halfH)
			if dx <= halfW && dy <= halfH {
				remaining, died := snap.ApplyDamage(proj.Damage)
				result.Hits = append(result.Hits, HitEvent{
					AttackerID:  proj.OwnerID,
					TargetID:    snap.ID,
					Damage:      uint32(proj.Damage),
					RemainingHP: remaining,
					Died:        died,
					IsCrit:      proj.IsCrit,
				})
				result.DestroyedID = append(result.DestroyedID, proj.ID)
				log.Printf("[Projectile:%d] Hit: owner=%d → target=%d dmg=%d remaining=%d died=%v",
					proj.ID, proj.OwnerID, snap.ID, proj.Damage, remaining, died)
				hit = true
				break
			}
		}

		if !hit {
			alive = append(alive, proj)
			result.AlivePos = append(result.AlivePos, ProjectilePos{
				ID:   proj.ID,
				X:    proj.Position.X,
				Y:    proj.Position.Y,
			})
		}
	}

	pp.projectiles = alive
	return result
}

// playerTarget is the minimal interface the pool needs per player.
type playerTarget struct {
	ID          uint32
	Position    mathutil.Vector2
	ApplyDamage func(dmg uint16) (remaining uint16, died bool)
}

// playerSnapshot is the list of living targets for a single tick.
type playerSnapshot []playerTarget

