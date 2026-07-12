package handler

import (
	"log"

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
		return
	}

	attacker := attackerSession.Player
	if attacker.HP == 0 {
		return // không thể attack khi đang chết
	}

	r := h.roomManager.FindRoomByPlayer(attacker.ID)
	if r == nil {
		return
	}

	// Tính sát thương
	rawDamage := uint16(attacker.Stats.ATKPhysical)

	// Nếu có TargetID cụ thể (melee/single target)
	if req.TargetID != 0 {
		target, ok := r.GetPlayer(req.TargetID)
		if !ok || target.HP == 0 {
			return
		}

		// Kiểm tra tầm đánh server-side
		_, attackerPos, _, _, _ := attacker.Snapshot()
		_, targetPos, _, _, _ := target.Snapshot()
		dist := attackerPos.Distance(targetPos)
		if dist > attacker.Stats.AttackRange*1.2 { // 20% tolerance
			log.Printf("[AttackHandler] Player %d out of range (%.2f > %.2f)", attacker.ID, dist, attacker.Stats.AttackRange)
			return
		}

		remaining, died := target.ApplyDamage(rawDamage)

		// Broadcast damage event
		dmgMsg := packet.EncodeDamageEvent(packet.DamageEventPacket{
			AttackerID:  attacker.ID,
			TargetID:    req.TargetID,
			Damage:      uint32(rawDamage),
			RemainingHP: remaining,
		})
		r.BroadcastTCP(dmgMsg, 0)

		if died {
			dieMsg := packet.EncodeDieEvent(packet.DieEventPacket{
				PlayerID: req.TargetID,
				KillerID: attacker.ID,
			})
			r.BroadcastTCP(dieMsg, 0)
			log.Printf("[AttackHandler] Player %d killed by %d", req.TargetID, attacker.ID)
		}

	} else {
		// AOE / melee hitbox: tìm tất cả player trong AttackRange
		_, attackerPos, attackDir, _, _ := attacker.Snapshot()
		if attackDir.LengthSq() < 0.01 {
			attackDir = mathutil.Vector2{X: 1, Y: 0}
		}
		hitCenter := mathutil.Vector2{
			X: attackerPos.X + attackDir.X*0.6,
			Y: attackerPos.Y + attackDir.Y*0.6,
		}

		targets := r.PlayersInRange(hitCenter, attacker.Stats.AttackRange, attacker.ID)

		for _, target := range targets {
			remaining, died := target.ApplyDamage(rawDamage)
			dmgMsg := packet.EncodeDamageEvent(packet.DamageEventPacket{
				AttackerID:  attacker.ID,
				TargetID:    target.ID,
				Damage:      uint32(rawDamage),
				RemainingHP: remaining,
			})
			r.BroadcastTCP(dmgMsg, 0)

			if died {
				dieMsg := packet.EncodeDieEvent(packet.DieEventPacket{
					PlayerID: target.ID,
					KillerID: attacker.ID,
				})
				r.BroadcastTCP(dieMsg, 0)
			}
		}
	}
}

// HandleRespawn hồi sinh player tại spawn point.
func (h *AttackHandler) HandleRespawn(payload []byte, session *player.Session) {
	p := session.Player
	r := h.roomManager.FindRoomByPlayer(p.ID)
	if r == nil {
		return
	}

	spawnIdx := int(p.ID) % len(room.SpawnPoints)
	spawn := room.SpawnPoints[spawnIdx]
	p.Respawn(spawn)

	ackMsg := packet.EncodeRespawnAck(packet.RespawnAckPacket{
		PlayerID: p.ID,
		X:        spawn.X,
		Y:        spawn.Y,
		HP:       p.Stats.MaxHP,
	})
	session.Send(ackMsg)
	log.Printf("[AttackHandler] Player %d respawned at (%.1f, %.1f)", p.ID, spawn.X, spawn.Y)
}
