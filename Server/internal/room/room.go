// Package room quản lý một phòng chơi và danh sách player bên trong.
package room

import (
	"log"
	"sync"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/pkg/mathutil"
)

const MaxPlayers = 32

// SpawnPoints là danh sách vị trí spawn mặc định trong phòng.
var SpawnPoints = []mathutil.Vector2{
	{X: 0, Y: 0}, {X: 3, Y: 0}, {X: -3, Y: 0},
	{X: 0, Y: 3}, {X: 0, Y: -3}, {X: 5, Y: 5},
	{X: -5, Y: 5}, {X: 5, Y: -5}, {X: -5, Y: -5},
}

// Room đại diện cho một phòng chơi.
type Room struct {
	mu sync.RWMutex

	ID      string
	players map[uint32]*player.Player

	// game loop
	loop *GameLoop
}

// New tạo Room mới với ID cho trước.
func New(id string) *Room {
	r := &Room{
		ID:      id,
		players: make(map[uint32]*player.Player),
	}
	r.loop = NewGameLoop(r)
	return r
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

	// Lấy spawn point cho player mới
	spawnIdx := len(r.players) % len(SpawnPoints)
	spawn := SpawnPoints[spawnIdx]
	p.SetPosition(spawn)

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
			X:        spawn.X,
			Y:        spawn.Y,
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

	log.Printf("[Room:%s] Player %d (%s) joined. Total: %d", r.ID, p.ID, p.Username, len(r.players))
	return existing
}

// RemovePlayer xóa player khỏi phòng và broadcast PlayerLeft.
func (r *Room) RemovePlayer(playerID uint32) {
	r.mu.Lock()
	defer r.mu.Unlock()

	delete(r.players, playerID)

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

// PlayerCount trả về số player hiện tại.
func (r *Room) PlayerCount() int {
	r.mu.RLock()
	defer r.mu.RUnlock()
	return len(r.players)
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

// Snapshot trả về slice PlayerSnapshot để game loop broadcast qua UDP.
func (r *Room) Snapshot() []packet.PlayerSnapshot {
	r.mu.RLock()
	defer r.mu.RUnlock()

	snaps := make([]packet.PlayerSnapshot, 0, len(r.players))
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
	return snaps
}
