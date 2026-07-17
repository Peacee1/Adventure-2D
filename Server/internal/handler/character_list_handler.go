package handler

import (
	"log"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
)

// CharacterListHandler handles GetCharacterListReq — returns the 3 character slots for the account.
//
// SRP: only handles fetching and returning the character list, contains no other logic.
// OCP: extend by adding new handlers; do not modify this handler.
type CharacterListHandler struct {
	repo player.Repository
}

// NewCharacterListHandler creates the handler with the given repository.
func NewCharacterListHandler(repo player.Repository) *CharacterListHandler {
	return &CharacterListHandler{repo: repo}
}

// Handle processes a GetCharacterListReq packet.
// AccountID is taken from the session (set by LoginHandler after successful authentication).
func (h *CharacterListHandler) Handle(payload []byte, session *player.Session) {
	clientAddr := session.RemoteAddr().String()
	log.Printf("[CharacterListHandler] Request from %s (accountID=%d)", clientAddr, session.AccountID)

	if session.AccountID == 0 {
		log.Printf("[CharacterListHandler] AccountID=0 from %s — not authenticated, returning empty slots", clientAddr)
		session.Send(packet.EncodeGetCharacterListAck(packet.GetCharacterListAckPacket{
			Characters: buildEmptySlots(),
		}))
		return
	}

	records, err := h.repo.GetAllCharacters(session.AccountID)
	if err != nil {
		log.Printf("[CharacterListHandler] DB error for accountID=%d from %s: %v", session.AccountID, clientAddr, err)
		session.Send(packet.EncodeGetCharacterListAck(packet.GetCharacterListAckPacket{
			Characters: buildEmptySlots(),
		}))
		return
	}

	// Convert [3]*PlayerRecord → []CharacterSummary
	summaries := make([]packet.CharacterSummary, 3)
	for i := 0; i < 3; i++ {
		summaries[i] = packet.CharacterSummary{
			Slot:   uint8(i),
			Exists: false,
		}
		if records[i] != nil {
			summaries[i].Exists   = true
			summaries[i].CharName = records[i].Username
			summaries[i].JobClass = uint8(records[i].JobClass)
			summaries[i].Level    = uint16(records[i].Level)
		}
	}

	log.Printf("[CharacterListHandler] Sending char list to accountID=%d: slot0=%v slot1=%v slot2=%v",
		session.AccountID, summaries[0].Exists, summaries[1].Exists, summaries[2].Exists)

	session.Send(packet.EncodeGetCharacterListAck(packet.GetCharacterListAckPacket{
		Characters: summaries,
	}))
}

// buildEmptySlots returns 3 empty slots used as a fallback on error.
func buildEmptySlots() []packet.CharacterSummary {
	slots := make([]packet.CharacterSummary, 3)
	for i := range slots {
		slots[i] = packet.CharacterSummary{Slot: uint8(i), Exists: false}
	}
	return slots
}
