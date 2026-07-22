package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
)

// SpendSkillPointHandler processes requests to exchange skill points for stats.
//
// Exchange rates (1 skill point each):
//   - HP         → +100 MaxHP  (and +100 current HP)
//   - MP         → +100 MaxMP  (and +100 current MP)
//   - ATKPhysical → +10 ATK Physical
//   - ATKMagic   → +10 ATK Magic
//   - DEF        → +10 DEF Physical AND +10 DEF Magic
//
// SRP: only handles skill-point spending logic.
// OCP: new upgrade types can be added without modifying other handlers.
type SpendSkillPointHandler struct {
	repo player.Repository
}

// NewSpendSkillPointHandler creates a SpendSkillPointHandler.
func NewSpendSkillPointHandler(repo player.Repository) *SpendSkillPointHandler {
	return &SpendSkillPointHandler{repo: repo}
}

// Handle processes a TypeSpendSkillPointReq packet.
func (h *SpendSkillPointHandler) Handle(payload []byte, sess *player.Session) {
	req, err := packet.DecodeSpendSkillPointReq(payload)
	if err != nil {
		log.Printf("[SpendSP] Decode error from player %d: %v", sess.Player.ID, err)
		h.sendFail(sess, 2) // invalid packet
		return
	}

	p := sess.Player
	p.GetMu().Lock()
	defer p.GetMu().Unlock()

	// ── Validation ────────────────────────────────────────────────────────────

	if p.SkillPoints < 1 {
		log.Printf("[SpendSP] Player %d (%s) has no skill points", p.ID, p.Username)
		h.sendFailLocked(sess, p, 1) // not enough SP
		return
	}

	if req.StatType > packet.UpgradeDEF {
		log.Printf("[SpendSP] Player %d sent invalid StatType=%d", p.ID, uint8(req.StatType))
		h.sendFailLocked(sess, p, 2) // invalid type
		return
	}

	// ── Apply upgrade ─────────────────────────────────────────────────────────

	p.SkillPoints--

	switch req.StatType {
	case packet.UpgradeHP:
		p.Stats.MaxHP += 100
		p.HP += 100 // also restore HP up to new max
		if p.HP > p.Stats.MaxHP {
			p.HP = p.Stats.MaxHP
		}

	case packet.UpgradeMP:
		p.Stats.MaxMP += 100
		p.MP += 100
		if p.MP > p.Stats.MaxMP {
			p.MP = p.Stats.MaxMP
		}

	case packet.UpgradeATKPhysical:
		p.Stats.ATKPhysical += 10

	case packet.UpgradeATKMagic:
		p.Stats.ATKMagic += 10

	case packet.UpgradeDEF:
		p.Stats.DEFPhysical += 10
		p.Stats.DEFMagic += 10
	}

	log.Printf("[SpendSP] Player %d (%s) upgraded stat=%d → SP=%d MaxHP=%d MaxMP=%d ATKPhy=%d ATKMag=%d DEFPhy=%d DEFMag=%d",
		p.ID, p.Username, uint8(req.StatType),
		p.SkillPoints, p.Stats.MaxHP, p.Stats.MaxMP,
		p.Stats.ATKPhysical, p.Stats.ATKMagic,
		p.Stats.DEFPhysical, p.Stats.DEFMagic,
	)

	// ── Respond ───────────────────────────────────────────────────────────────

	ack := packet.SpendSkillPointAckPacket{
		Success:        true,
		FailReason:     0,
		NewSkillPoints: uint32(p.SkillPoints),
		NewMaxHP:       p.Stats.MaxHP,
		NewMaxMP:       p.Stats.MaxMP,
		NewATKPhysical: p.Stats.ATKPhysical,
		NewATKMagic:    p.Stats.ATKMagic,
		NewDEFPhysical: p.Stats.DEFPhysical,
		NewDEFMagic:    p.Stats.DEFMagic,
	}
	sess.Send(packet.EncodeSpendSkillPointAck(ack))

	// ── Persist ───────────────────────────────────────────────────────────────
	// Save is fire-and-forget; errors are logged inside Save.
	go func() {
		if err := h.repo.Save(p); err != nil {
			log.Printf("[SpendSP] Save error for player %d: %v", p.ID, err)
		}
	}()
}

// sendFail sends a failure ack with no stat data (used before lock is acquired).
func (h *SpendSkillPointHandler) sendFail(sess *player.Session, reason uint8) {
	ack := packet.SpendSkillPointAckPacket{Success: false, FailReason: reason}
	sess.Send(packet.EncodeSpendSkillPointAck(ack))
}

// sendFailLocked sends a failure ack with current stat data (caller holds the lock).
func (h *SpendSkillPointHandler) sendFailLocked(sess *player.Session, p *player.Player, reason uint8) {
	ack := packet.SpendSkillPointAckPacket{
		Success:        false,
		FailReason:     reason,
		NewSkillPoints: uint32(p.SkillPoints),
		NewMaxHP:       p.Stats.MaxHP,
		NewMaxMP:       p.Stats.MaxMP,
		NewATKPhysical: p.Stats.ATKPhysical,
		NewATKMagic:    p.Stats.ATKMagic,
		NewDEFPhysical: p.Stats.DEFPhysical,
		NewDEFMagic:    p.Stats.DEFMagic,
	}
	sess.Send(packet.EncodeSpendSkillPointAck(ack))
}
