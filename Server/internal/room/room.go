// Package room manages a game room and the players inside it.
package room

import (
	"log"
	"sync"

	"adventure2d-server/internal/bot"
	"adventure2d-server/internal/mapdata"
	"adventure2d-server/internal/monster"
	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/pkg/mathutil"
)

const MaxPlayers = 32

// fallbackSpawns is used when no .mapdata file has been loaded for the map.
// These match the server-default positions from the old hardcoded list.
var fallbackSpawns = []mathutil.Vector2{
	{X: -15.5, Y: 11.5}, {X: -12.5, Y: 8.5}, {X: -18.5, Y: 14.5},
	{X: -10.5, Y: 11.5}, {X: -15.5, Y: 6.5}, {X: -20.5, Y: 11.5},
	{X: -15.5, Y: 16.5}, {X: -20.5, Y: 6.5}, {X: -10.5, Y: 16.5},
}

// FallbackSpawn returns a hardcoded spawn point for the n-th player (round-robin).
// Used by attack_handler for respawn when no room reference is available.
func FallbackSpawn(n int) mathutil.Vector2 {
	return fallbackSpawns[n%len(fallbackSpawns)]
}

// Room represents a game room.
type Room struct {
	mu sync.RWMutex

	ID      string
	MapName string
	players map[uint32]*player.Player

	mapData *mapdata.MapData

	// projectiles tracks all active server-simulated projectiles for this room.
	projectiles ProjectilePool

	// bots are server-simulated NPC bots included in each WorldState broadcast.
	bots []*bot.WanderBot

	// monsters are server-simulated enemy entities; visible only when players are present.
	monsters []*monster.Monster

	// game loop
	loop *GameLoop
}

// New creates a new Room for the given map scene name.
func New(id string, mapName string, mgr *mapdata.Manager) *Room {
	var md *mapdata.MapData
	if mgr != nil {
		md = mgr.Get(mapName)
	}
	r := &Room{
		ID:      id,
		MapName: mapName,
		players: make(map[uint32]*player.Player),
		mapData: md,
	}
	r.loop = NewGameLoop(r)
	r.SpawnBots()
	r.SpawnMonsters()
	return r
}

// SpawnBots creates the initial set of wander bots for this room.
// Each bot gets a unique spawn position spread across the default spawn area.
func (r *Room) SpawnBots() {
	spawnPoints := r.pickWalkableSpawns(4)
	if len(spawnPoints) == 0 {
		log.Printf("[Room:%s] No walkable tiles found — bots not spawned", r.ID)
		return
	}
	for i, sp := range spawnPoints {
		r.bots = append(r.bots, bot.New(i, sp))
		log.Printf("[Room:%s] Spawned bot %d at walkable (%.1f,%.1f)", r.ID, bot.BotIDBase+uint32(i), sp.X, sp.Y)
	}
}

// pickWalkableSpawns scans the mapdata tile grid and returns n well-spread
// walkable positions. Divides the walkable tile list into n equal sections and
// picks the middle tile of each section, giving a natural geographic spread.
func (r *Room) pickWalkableSpawns(n int) []mathutil.Vector2 {
	if r.mapData == nil || r.mapData.Tiles == nil {
		log.Printf("[Room:%s] No mapData — cannot find walkable spawns", r.ID)
		return nil
	}

	tiles := r.mapData.Tiles
	var walkable []mathutil.Vector2

	for row := uint32(0); row < tiles.Height; row++ {
		for col := uint32(0); col < tiles.Width; col++ {
			// Center of this world-unit square
			wx := float32(tiles.OriginX) + float32(col) + 0.5
			wy := float32(tiles.OriginY) + float32(row) + 0.5
			if tiles.IsWalkable(wx, wy) {
				walkable = append(walkable, mathutil.Vector2{X: wx, Y: wy})
			}
		}
	}

	if len(walkable) == 0 {
		return nil
	}
	if n > len(walkable) {
		n = len(walkable)
	}

	// Pick one tile from each of the n equal sections → geographic spread
	result := make([]mathutil.Vector2, 0, n)
	section := len(walkable) / n
	for i := 0; i < n; i++ {
		mid := i*section + section/2
		if mid >= len(walkable) {
			mid = len(walkable) - 1
		}
		result = append(result, walkable[mid])
	}
	return result
}

// SpawnMonsters creates the initial set of monsters for this room.
// Uses well-spread walkable tiles from mapdata (same algorithm as SpawnBots).
func (r *Room) SpawnMonsters() {
	const numMonsters = 6
	spawnPoints := r.pickWalkableSpawns(numMonsters)
	if len(spawnPoints) == 0 {
		log.Printf("[Room:%s] No walkable tiles — monsters not spawned", r.ID)
		return
	}
	for i, sp := range spawnPoints {
		r.monsters = append(r.monsters, monster.New(i, sp))
		log.Printf("[Room:%s] Spawned monster %d at (%.1f,%.1f)", r.ID, monster.MonsterIDBase+uint32(i), sp.X, sp.Y)
	}
}

// MonsterAttack represents a monster melee strike to be applied this tick.
type MonsterAttack struct {
	MonsterID uint32
	PlayerID  uint32
	Damage    uint16
}

// UpdateMonsters advances all monster AIs by dt seconds.
// ALL monsters are updated, including inactive dead ones so their respawn timers run.
// Returns the list of melee attacks to apply this tick.
// Called by the game loop each tick.
func (r *Room) UpdateMonsters(dt float32) []MonsterAttack {
	// Build a snapshot of living player positions (read-locked).
	r.mu.RLock()
	targetPos := make(map[uint32]mathutil.Vector2, len(r.players))
	for id, p := range r.players {
		if p.HP > 0 {
			targetPos[id] = p.Position
		}
	}
	r.mu.RUnlock()

	var attacks []MonsterAttack
	for _, m := range r.monsters {
		// Note: do NOT skip inactive monsters — dead ones need their respawn timer ticked.
		var hasTarget bool
		var tx, ty float32
		if m.TargetID != 0 {
			if pos, ok := targetPos[m.TargetID]; ok {
				hasTarget = true
				tx, ty = pos.X, pos.Y
			}
		}
		if m.Update(dt, hasTarget, tx, ty) && m.TargetID != 0 {
			attacks = append(attacks, MonsterAttack{
				MonsterID: m.ID,
				PlayerID:  m.TargetID,
				Damage:    m.ATK,
			})
		}
	}
	return attacks
}

// ApplyMonsterAttacks processes melee hits from monsters:
// applies damage to the target player, broadcasts DamageEvent and (if killed) DieEvent.
func (r *Room) ApplyMonsterAttacks(attacks []MonsterAttack) {
	for _, atk := range attacks {
		r.mu.RLock()
		target, ok := r.players[atk.PlayerID]
		r.mu.RUnlock()
		if !ok {
			continue
		}

		remaining, died := target.ApplyDamage(atk.Damage)

		dmgMsg := packet.EncodeDamageEvent(packet.DamageEventPacket{
			AttackerID:  atk.MonsterID,
			TargetID:    atk.PlayerID,
			Damage:      uint32(atk.Damage),
			RemainingHP: remaining,
		})
		r.BroadcastTCP(dmgMsg, 0)

		if died {
			target.GetMu().Lock()
			if target.SM != nil {
				target.SM.TransitionTo(player.StateDead, target)
			}
			target.GetMu().Unlock()

			dieMsg := packet.EncodeDieEvent(packet.DieEventPacket{
				PlayerID: atk.PlayerID,
				KillerID: atk.MonsterID,
			})
			r.BroadcastTCP(dieMsg, 0)
			log.Printf("[Room:%s] Monster %d killed player %d", r.ID, atk.MonsterID, atk.PlayerID)

			// Monster returns to Idle after killing — loses aggro and resumes wander
			for _, m := range r.monsters {
				if m.ID == atk.MonsterID {
					m.LoseAggro() // clears TargetID, transitions directly to IdleState
					break
				}
			}
		}
	}
}

// MonsterSnapshot returns snapshots for all active monsters.
func (r *Room) MonsterSnapshot() []packet.MonsterSnapshot {
	var snaps []packet.MonsterSnapshot
	for _, m := range r.monsters {
		if !m.Active {
			continue
		}
		snaps = append(snaps, packet.MonsterSnapshot{
			ID:    m.ID,
			X:     m.X,
			Y:     m.Y,
			HP:    m.HP,
			State: m.StateByte(),
		})
	}
	return snaps
}

// setMonstersActive activates or deactivates all monsters.
// Monsters are activated when the first player joins and deactivated when the last leaves.
func (r *Room) setMonstersActive(active bool) {
	for _, m := range r.monsters {
		if active {
			m.Activate()
		} else {
			m.Deactivate()
		}
	}
	if active {
		log.Printf("[Room:%s] Monsters activated (%d)", r.ID, len(r.monsters))
	} else {
		log.Printf("[Room:%s] Monsters deactivated — room empty", r.ID)
	}
}

// UpdateBots advances all bot simulations by dt seconds.
// Called by the game loop each tick.
func (r *Room) UpdateBots(dt float32) {
	for _, b := range r.bots {
		b.Update(dt)
	}
}

// AddProjectile registers a server-simulated projectile for this room.
// Called by attack_handler after broadcasting ProjectileSpawnPacket to clients.
func (r *Room) AddProjectile(p *Projectile) {
	r.projectiles.Add(p)
}

// SimulateProjectiles advances all active projectiles by dt seconds,
// applies damage on hit (to players AND monsters), and broadcasts events.
// Called by the game loop each tick.
func (r *Room) SimulateProjectiles(dt float32) {
	r.mu.RLock()
	targets := make(playerSnapshot, 0, len(r.players)+len(r.monsters))

	// Real player targets
	for _, p := range r.players {
		if p.HP == 0 {
			continue
		}
		p_ := p
		targets = append(targets, playerTarget{
			ID:       p_.ID,
			Position: p_.Position,
			ApplyDamage: func(dmg uint16) (uint16, bool) {
				return p_.ApplyDamage(dmg)
			},
		})
	}

	// Monster targets — projectiles can hit monsters too
	for _, m := range r.monsters {
		if !m.Active || m.StateByte() == monster.StateDead {
			continue
		}
		m_ := m
		targets = append(targets, playerTarget{
			ID:       m_.ID,
			Position: mathutil.Vector2{X: m_.X, Y: m_.Y},
			ApplyDamage: func(dmg uint16) (uint16, bool) {
				return m_.TakeDamage(dmg, 0) // 0 = no aggro override; hit loop handles provoke
			},
		})
	}
	r.mu.RUnlock()

	tick := r.projectiles.Update(dt, targets)

	// Broadcast destroy events for projectiles that expired or hit this tick.
	for _, id := range tick.DestroyedID {
		destroyMsg := packet.EncodeProjectileDestroy(packet.ProjectileDestroyPacket{ProjID: id})
		r.BroadcastTCP(destroyMsg, 0)
	}

	// Broadcast damage and death events for hits
	for _, h := range tick.Hits {
		if h.TargetID >= monster.MonsterIDBase {
			// A projectile hit a monster — provoke it and track the killer
			var killerID uint32
			for _, m := range r.monsters {
				if m.ID == h.TargetID {
					m.SetAggro(h.AttackerID, true)           // provoke
					m.LastHitPlayerID = h.AttackerID         // record killer
					killerID = m.LastHitPlayerID
					break
				}
			}
			dmgMsg := packet.EncodeDamageEvent(packet.DamageEventPacket{
				AttackerID:  h.AttackerID,
				TargetID:    h.TargetID,
				Damage:      h.Damage,
				RemainingHP: h.RemainingHP,
				IsCrit:      h.IsCrit,
			})
			r.BroadcastTCP(dmgMsg, 0)

			// Apply life steal to the projectile owner
			r.mu.RLock()
			attackerP, aok := r.players[h.AttackerID]
			r.mu.RUnlock()
			if aok {
				attackerP.GetMu().Lock()
				attackerP.ApplyLifeSteal(uint16(h.Damage))
				attackerP.GetMu().Unlock()
			}

			// Award EXP to the killer on killing blow
			if h.Died && killerID != 0 {
				r.mu.RLock()
				killer, ok := r.players[killerID]
				r.mu.RUnlock()
				if ok {
					killer.GetMu().Lock()
					leveledUp, newLevel, newExp, _ := killer.GainExp(monster.MonsterExpReward)
					newSP := killer.SkillPoints
					killer.GetMu().Unlock()

					expMsg := packet.EncodeExpGain(packet.ExpGainPacket{
						PlayerID:  killerID,
						ExpGained: uint32(monster.MonsterExpReward),
						NewExp:    uint32(newExp),
						NewLevel:  uint32(newLevel),
					})
					killer.SendTCP(expMsg)

					if leveledUp {
						lvlMsg := packet.EncodeLevelUp(packet.LevelUpPacket{
							PlayerID:       killerID,
							NewLevel:       uint32(newLevel),
							NewExp:         uint32(newExp),
							NewSkillPoints: uint32(newSP),
						})
						killer.SendTCP(lvlMsg)
					}
				}
			}
			continue
		}

		// Player hit — existing handling
		dmgMsg := packet.EncodeDamageEvent(packet.DamageEventPacket{
			AttackerID:  h.AttackerID,
			TargetID:    h.TargetID,
			Damage:      h.Damage,
			RemainingHP: h.RemainingHP,
			IsCrit:      h.IsCrit,
		})
		r.BroadcastTCP(dmgMsg, 0)

		// Apply life steal to the projectile owner
		r.mu.RLock()
		attackerP, aok := r.players[h.AttackerID]
		r.mu.RUnlock()
		if aok {
			attackerP.GetMu().Lock()
			attackerP.ApplyLifeSteal(uint16(h.Damage))
			attackerP.GetMu().Unlock()
		}

		if h.Died {
			r.mu.RLock()
			target, ok := r.players[h.TargetID]
			r.mu.RUnlock()
			if ok {
				target.GetMu().Lock()
				if target.SM != nil {
					target.SM.TransitionTo(player.StateDead, target)
				}
				target.GetMu().Unlock()
			}

			dieMsg := packet.EncodeDieEvent(packet.DieEventPacket{
				PlayerID: h.TargetID,
				KillerID: h.AttackerID,
			})
			r.BroadcastTCP(dieMsg, 0)
		}
	}
}

// Start bắt đầu game loop của phòng.
func (r *Room) Start() {
	go r.loop.Run()
}

// Stop dừng game loop.
func (r *Room) Stop() {
	r.loop.Stop()
}

// AddPlayer thêm player vào phòng và broadcast cho các player khác.
// Trả về danh sách player đang có trong phòng (trước khi thêm player mới).
func (r *Room) AddPlayer(p *player.Player) []packet.PlayerInfo {
	r.mu.Lock()
	defer r.mu.Unlock()

	// Use spawn point only for brand-new players (position == 0,0).
	// Returning players keep their last saved DB position (already set by login_handler).
	if p.Position.X == 0 && p.Position.Y == 0 {
		spawn := r.pickSpawn(len(r.players))
		p.SetPosition(spawn)
		log.Printf("[Room] Player %d new → spawn at (%.1f, %.1f)", p.ID, spawn.X, spawn.Y)
	} else {
		log.Printf("[Room] Player %d returning → kept DB position (%.1f, %.1f)", p.ID, p.Position.X, p.Position.Y)
	}

	// Snapshot danh sách player hiện tại để gửi cho player mới
	existing := make([]packet.PlayerInfo, 0, len(r.players))
	for _, existing_p := range r.players {
		id, pos, _, hp, _ := existing_p.Snapshot()
		existing = append(existing, packet.PlayerInfo{
			PlayerID: id,
			Username: existing_p.Username,
			X:        pos.X,
			Y:        pos.Y,
			HP:       hp,
			MaxHP:    existing_p.Stats.MaxHP,
			JobClass: uint8(existing_p.JobClass),
		})
	}

	// Thêm player mới vào map
	r.players[p.ID] = p

	// Broadcast PlayerJoined cho tất cả player đang có (ngoại trừ player mới)
	joinMsg := packet.EncodePlayerJoined(packet.PlayerJoinedPacket{
		Player: packet.PlayerInfo{
			PlayerID: p.ID,
			Username: p.Username,
			X:        p.Position.X,
			Y:        p.Position.Y,
			HP:       p.Stats.MaxHP,
			MaxHP:    p.Stats.MaxHP,
			JobClass: uint8(p.JobClass),
		},
	})
	for _, other := range r.players {
		if other.ID != p.ID {
			other.SendTCP(joinMsg)
		}
	}

	// Activate monsters when the first player joins
	if len(r.players) == 1 {
		r.setMonstersActive(true)
	}

	log.Printf("[Room:%s] Player %d (%s) joined. Total: %d", r.ID, p.ID, p.Username, len(r.players))
	return existing
}

// RemovePlayer xóa player khỏi phòng và broadcast PlayerLeft.
func (r *Room) RemovePlayer(playerID uint32) {
	r.mu.Lock()
	defer r.mu.Unlock()

	delete(r.players, playerID)

	// Deactivate monsters when the last player leaves
	if len(r.players) == 0 {
		r.setMonstersActive(false)
	}

	leftMsg := packet.EncodePlayerLeft(packet.PlayerLeftPacket{PlayerID: playerID})
	for _, other := range r.players {
		other.SendTCP(leftMsg)
	}

	log.Printf("[Room:%s] Player %d left. Total: %d", r.ID, playerID, len(r.players))
}

// GetPlayer trả về player theo ID (thread-safe).
func (r *Room) GetPlayer(id uint32) (*player.Player, bool) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	p, ok := r.players[id]
	return p, ok
}

// GetMonster trả về monster theo ID (thread-safe).
func (r *Room) GetMonster(id uint32) (*monster.Monster, bool) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	for _, m := range r.monsters {
		if m.ID == id {
			return m, true
		}
	}
	return nil, false
}

// PlayerCount trả về số player hiện tại.
func (r *Room) PlayerCount() int {
	r.mu.RLock()
	defer r.mu.RUnlock()
	return len(r.players)
}

// GetExistingPlayers trả về danh sách PlayerInfo của tất cả player trong phòng,
// ngoại trừ player có ID = excludeID (dùng cho reconnect/already_in_room case).
func (r *Room) GetExistingPlayers(excludeID uint32) []packet.PlayerInfo {
	r.mu.RLock()
	defer r.mu.RUnlock()

	result := make([]packet.PlayerInfo, 0, len(r.players))
	for _, p := range r.players {
		if p.ID == excludeID {
			continue
		}
		_, pos, _, hp, _ := p.Snapshot()
		result = append(result, packet.PlayerInfo{
			PlayerID: p.ID,
			Username: p.Username,
			X:        pos.X,
			Y:        pos.Y,
			HP:       hp,
			MaxHP:    p.Stats.MaxHP,
			JobClass: uint8(p.JobClass),
		})
	}
	return result
}

// IsFull kiểm tra phòng đã đầy chưa.
func (r *Room) IsFull() bool {
	return r.PlayerCount() >= MaxPlayers
}

// PlayersInRange trả về danh sách player (ngoại trừ excludeID) trong bán kính r từ center.
// Dùng cho AOE melee hitbox của attack_handler.
func (r *Room) PlayersInRange(center mathutil.Vector2, radius float32, excludeID uint32) []*player.Player {
	r.mu.RLock()
	defer r.mu.RUnlock()

	var result []*player.Player
	for _, p := range r.players {
		if p.ID == excludeID {
			continue
		}
		_, pos, _, hp, _ := p.Snapshot()
		if hp == 0 {
			continue
		}
		if center.Distance(pos) <= radius {
			result = append(result, p)
		}
	}
	return result
}

// BroadcastTCP gửi data cho tất cả player trong phòng qua TCP.
func (r *Room) BroadcastTCP(data []byte, excludeID uint32) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	for _, p := range r.players {
		if p.ID != excludeID {
			p.SendTCP(data)
		}
	}
}

// Snapshot returns a slice of PlayerSnapshot for the game loop to broadcast via UDP.
// Includes both real players and wander bots.
func (r *Room) Snapshot() []packet.PlayerSnapshot {
	r.mu.RLock()
	defer r.mu.RUnlock()

	snaps := make([]packet.PlayerSnapshot, 0, len(r.players)+len(r.bots))
	for _, p := range r.players {
		id, pos, dir, hp, state := p.Snapshot()
		snaps = append(snaps, packet.PlayerSnapshot{
			PlayerID: id,
			X:        pos.X,
			Y:        pos.Y,
			DirX:     dir.X,
			DirY:     dir.Y,
			HP:       hp,
			State:    uint8(state),
		})
	}
	// Append bot snapshots only when real players are present in the room
	if len(r.players) > 0 {
		for _, b := range r.bots {
			snaps = append(snaps, b.Snapshot())
		}
	}
	return snaps
}

// pickSpawn returns a spawn position for the n-th player (round-robin).
// Prefers spawn points loaded from mapdata; falls back to fallbackSpawns.
func (r *Room) pickSpawn(n int) mathutil.Vector2 {
	if r.mapData != nil && len(r.mapData.Spawns) > 0 {
		sp := r.mapData.Spawns[n%len(r.mapData.Spawns)]
		return mathutil.Vector2{X: sp.X, Y: sp.Y}
	}
	fb := fallbackSpawns[n%len(fallbackSpawns)]
	return fb
}
