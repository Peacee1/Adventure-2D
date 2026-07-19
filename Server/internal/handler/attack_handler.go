package handler

import (
	"log"
	"time"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
	"adventure2d-server/pkg/mathutil"
)

// AttackHandler xử lý AttackReqPacket nhận qua TCP.
type AttackHandler struct {
	roomManager *room.Manager
}

func NewAttackHandler(rm *room.Manager) *AttackHandler {
	return &AttackHandler{roomManager: rm}
}

// Handle thực hiện server-side combat logic.
func (h *AttackHandler) Handle(payload []byte, attackerSession *player.Session) {
	req, err := packet.DecodeAttackReq(payload)
	if err != nil {
		log.Printf("[AttackHandler] Decode error: %v", err)
		return
	}

	attacker := attackerSession.Player

	// BUG FIX: dùng Snapshot() để đọc HP thread-safe thay vì trực tiếp attacker.HP
	_, attackerPos, _, attackerHP, attackerState := attacker.Snapshot()

	if attackerState == player.StateDead || attackerHP == 0 {
		log.Printf("[AttackHandler] Player %d (%s) is dead — attack ignored", attacker.ID, attacker.Username)
		return
	}

	r := h.roomManager.FindRoomByPlayer(attacker.ID)
	if r == nil {
		log.Printf("[AttackHandler] Player %d not in any room — attack ignored", attacker.ID)
		return
	}

	// Tính sát thương (đọc Stats an toàn — Stats không thay đổi sau init)
	rawDamage := uint16(attacker.Stats.ATKPhysical)

	log.Printf("[AttackHandler] Player %d (%s) attacking: targetID=%d dir=(%.2f,%.2f) dmg=%d",
		attacker.ID, attacker.Username, req.TargetID, req.DirX, req.DirY, rawDamage)

	// Nếu có TargetID cụ thể (melee/single target)
	if req.TargetID != 0 {
		target, ok := r.GetPlayer(req.TargetID)
		if !ok {
			log.Printf("[AttackHandler] Target %d not found in room %s", req.TargetID, r.ID)
			return
		}

		// Đọc HP target thread-safe
		_, targetPos, _, targetHP, _ := target.Snapshot()
		if targetHP == 0 {
			log.Printf("[AttackHandler] Target %d already dead — attack skipped", req.TargetID)
			return
		}

		// Kiểm tra tầm đánh server-side
		dist := attackerPos.Distance(targetPos)
		maxRange := attacker.Stats.AttackRange * 1.2 // 20% tolerance
		if dist > maxRange {
			log.Printf("[AttackHandler] Player %d out of range to hit %d (dist=%.2f > range=%.2f)",
				attacker.ID, req.TargetID, dist, maxRange)
			return
		}

		remaining, died := target.ApplyDamage(rawDamage)
		log.Printf("[AttackHandler] Hit: attacker=%d → target=%d rawDmg=%d remaining=%d died=%v",
			attacker.ID, req.TargetID, rawDamage, remaining, died)

		// Transition attacker to AttackState via SM
		attacker.GetMu().Lock()
		if attacker.SM != nil && attacker.State != player.StateDead {
			attacker.SM.TransitionTo(player.StateAttack, attacker)
		}
		attacker.GetMu().Unlock()

		// Broadcast projectile for ranged classes (Archer)
		if attacker.JobClass == player.JobArcher {
			dir := mathutil.Vector2{X: req.DirX, Y: req.DirY}.Normalized()
			offsetX := float32(1.85)
			if dir.X < 0 {
				offsetX = -1.85
			}
			spawnX := attackerPos.X + offsetX
			spawnY := attackerPos.Y + 1.0
			projMsg := packet.EncodeProjectileSpawn(packet.ProjectileSpawnPacket{
				OwnerID:  attacker.ID,
				X:        spawnX,
				Y:        spawnY,
				DirX:     dir.X,
				DirY:     dir.Y,
				Speed:    45.0,
				Range:    attacker.Stats.AttackRange,
				ProjType: 0, // 0 = Arrow
			})
			r.BroadcastTCP(projMsg, 0)
			log.Printf("[AttackHandler] Archer %d fired arrow at (%.1f,%.1f) dir=(%.2f,%.2f)",
				attacker.ID, spawnX, spawnY, dir.X, dir.Y)
		}

		// Broadcast damage event
		dmgMsg := packet.EncodeDamageEvent(packet.DamageEventPacket{
			AttackerID:  attacker.ID,
			TargetID:    req.TargetID,
			Damage:      uint32(rawDamage),
			RemainingHP: remaining,
		})
		r.BroadcastTCP(dmgMsg, 0)

		if died {
			// Transition target to DeadState via SM
			target.GetMu().Lock()
			if target.SM != nil {
				target.SM.TransitionTo(player.StateDead, target)
			}
			target.GetMu().Unlock()

			dieMsg := packet.EncodeDieEvent(packet.DieEventPacket{
				PlayerID: req.TargetID,
				KillerID: attacker.ID,
			})
			r.BroadcastTCP(dieMsg, 0)
			log.Printf("[AttackHandler] Player %d (%s) killed by %d (%s)",
				req.TargetID, target.Username, attacker.ID, attacker.Username)
		}

	} else {
		// AOE / no-target: Archer fires a projectile in AimDirection.
		// For melee classes this would be a hitbox sweep.
		dir := mathutil.Vector2{X: req.DirX, Y: req.DirY}
		if dir.LengthSq() < 0.01 {
			dir = mathutil.Vector2{X: 1, Y: 0}
		}
		dir = dir.Normalized()

		// Transition attacker to AttackState first (locks movement for animation duration)
		attacker.GetMu().Lock()
		if attacker.SM != nil && attacker.State != player.StateDead {
			attacker.SM.TransitionTo(player.StateAttack, attacker)
		}
		attacker.GetMu().Unlock()

		// Archer: broadcast projectile visual — server applies no damage here
		// (AOE hit was removed; Archer is ranged single-target via projectile travel)
		if attacker.JobClass == player.JobArcher {
			offsetX := float32(1.85)
			if dir.X < 0 {
				offsetX = -1.85
			}
			spawnX    := attackerPos.X + offsetX
			spawnY    := attackerPos.Y + 1.0
			windupSec := attacker.Stats.AttackSpeed * 0.6 // fire at 60% of animation cycle (e.g. 1.0s × 0.6 = 0.6s)

			// Capture locals for the goroutine closure
			attackerID := attacker.ID
			damage     := uint16(rawDamage)
			attackRange := attacker.Stats.AttackRange

			go func() {
				// Wait for the windup portion of the attack animation
				time.Sleep(time.Duration(windupSec * float32(time.Second)))

				// Register server-side projectile to get its authoritative ID
				proj := &room.Projectile{
					OwnerID:   attackerID,
					Position:  mathutil.Vector2{X: spawnX, Y: spawnY},
					Direction: dir,
					Speed:     45.0,
					MaxRange:  attackRange,
					Damage:    damage,
				}
				r.AddProjectile(proj)

				projMsg := packet.EncodeProjectileSpawn(packet.ProjectileSpawnPacket{
					ProjID:   proj.ID,
					OwnerID:  attackerID,
					X:        spawnX,
					Y:        spawnY,
					DirX:     dir.X,
					DirY:     dir.Y,
					Speed:    45.0,
					Range:    attackRange,
					ProjType: 0,
				})
				r.BroadcastTCP(projMsg, 0)
				log.Printf("[AttackHandler] Archer %d fired arrow ID=%d after %.2fs windup at (%.1f,%.1f) dir=(%.2f,%.2f) dmg=%d",
					attackerID, proj.ID, windupSec, spawnX, spawnY, dir.X, dir.Y, damage)
			}()
		} else {
			// Melee AOE: find players in range and apply damage
			hitCenter := mathutil.Vector2{
				X: attackerPos.X + dir.X*0.6,
				Y: attackerPos.Y + dir.Y*0.6,
			}
			targets := r.PlayersInRange(hitCenter, attacker.Stats.AttackRange, attacker.ID)
			log.Printf("[AttackHandler] Melee AOE by player %d: center=(%.2f,%.2f) range=%.2f targets=%d",
				attacker.ID, hitCenter.X, hitCenter.Y, attacker.Stats.AttackRange, len(targets))

			for _, target := range targets {
				remaining, died := target.ApplyDamage(rawDamage)
				log.Printf("[AttackHandler] AOE Hit: attacker=%d → target=%d dmg=%d remaining=%d died=%v",
					attacker.ID, target.ID, rawDamage, remaining, died)

				dmgMsg := packet.EncodeDamageEvent(packet.DamageEventPacket{
					AttackerID:  attacker.ID,
					TargetID:    target.ID,
					Damage:      uint32(rawDamage),
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
						PlayerID: target.ID,
						KillerID: attacker.ID,
					})
					r.BroadcastTCP(dieMsg, 0)
					log.Printf("[AttackHandler] AOE: Player %d (%s) killed by %d (%s)",
						target.ID, target.Username, attacker.ID, attacker.Username)
				}
			}
		}
	}
}

// HandleRespawn hồi sinh player tại spawn point.
func (h *AttackHandler) HandleRespawn(payload []byte, session *player.Session) {
	p := session.Player
	log.Printf("[AttackHandler] Respawn request from player %d (%s)", p.ID, p.Username)

	r := h.roomManager.FindRoomByPlayer(p.ID)
	if r == nil {
		log.Printf("[AttackHandler] Respawn: player %d not in any room", p.ID)
		return
	}

	// Kiểm tra player đã chết chưa mới cho phép respawn
	_, _, _, hp, state := p.Snapshot()
	if state != player.StateDead && hp > 0 {
		log.Printf("[AttackHandler] Respawn REJECTED: player %d is still alive (HP=%d)", p.ID, hp)
		return
	}

	spawnPos := room.FallbackSpawn(int(p.ID))

	// Respawn resets HP/position, then SM transitions to Idle
	p.GetMu().Lock()
	p.HP       = p.Stats.MaxHP
	p.Position = spawnPos
	if p.SM != nil {
		p.SM.TransitionTo(player.StateIdle, p)
	}
	p.GetMu().Unlock()

	ackMsg := packet.EncodeRespawnAck(packet.RespawnAckPacket{
		PlayerID: p.ID,
		X:        spawnPos.X,
		Y:        spawnPos.Y,
		HP:       p.Stats.MaxHP,
	})
	session.Send(ackMsg)
	log.Printf("[AttackHandler] Respawn OK: player %d (%s) spawned at (%.1f, %.1f) HP=%d",
		p.ID, p.Username, spawnPos.X, spawnPos.Y, p.Stats.MaxHP)
}
