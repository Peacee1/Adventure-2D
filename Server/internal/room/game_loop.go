package room

import (
	"log"
	"net"
	"time"

	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
)

// UDPSender cho phép game loop gửi UDP mà không import circular.
type UDPSender interface {
	SendUDP(addr *net.UDPAddr, data []byte)
}

const (
	// TickRate là số tick mỗi giây (20Hz — đủ smooth, tiết kiệm CPU).
	TickRate     = 20
	TickInterval = time.Second / TickRate
	// dtSec là delta time mỗi tick (giây) dùng cho simulation.
	dtSec = float32(1.0) / float32(TickRate)

	// stoppingDistance: khi player cách destination dưới mức này thì coi như đã đến.
	stoppingDistance = float32(0.15)
)

// GameLoop chạy fixed-tick loop cho một Room.
// Mỗi tick: simulate movement → snapshot → broadcast WorldState qua UDP.
type GameLoop struct {
	room   *Room
	stopCh chan struct{}
	tick   uint32

	// udpSender được set sau khi UDP server khởi động.
	udpSender UDPSender
}

// NewGameLoop tạo GameLoop cho room.
func NewGameLoop(r *Room) *GameLoop {
	return &GameLoop{
		room:   r,
		stopCh: make(chan struct{}),
	}
}

// SetUDPSender inject UDP sender (gọi sau khi UDP server start).
func (gl *GameLoop) SetUDPSender(s UDPSender) {
	gl.udpSender = s
}

// Run chạy vòng lặp game. Blocking — gọi trong goroutine riêng.
func (gl *GameLoop) Run() {
	ticker := time.NewTicker(TickInterval)
	defer ticker.Stop()

	log.Printf("[GameLoop:%s] Started at %dHz", gl.room.ID, TickRate)

	for {
		select {
		case <-gl.stopCh:
			log.Printf("[GameLoop:%s] Stopped", gl.room.ID)
			return

		case <-ticker.C:
			gl.tick++
				gl.simulateMovement()             // real players
				gl.room.UpdateBots(dtSec)          // NPC wander bots
				// Update monsters and apply any melee attacks they trigger
				if attacks := gl.room.UpdateMonsters(dtSec); len(attacks) > 0 {
					gl.room.ApplyMonsterAttacks(attacks)
				}
				gl.room.SimulateProjectiles(dtSec) // projectile collision
				gl.broadcastWorldState()
		}
	}
}

// Stop dừng game loop.
func (gl *GameLoop) Stop() {
	close(gl.stopCh)
}

// simulateMovement delegates physics and state transitions to each player's StateMachine.
// The SM handles movement, dash, dash-end lag, attack freeze, and death internally.
func (gl *GameLoop) simulateMovement() {
	gl.room.mu.RLock()
	players := make([]*player.Player, 0, len(gl.room.players))
	for _, p := range gl.room.players {
		players = append(players, p)
	}
	gl.room.mu.RUnlock()

	for _, p := range players {
		p.GetMu().Lock()
		p.Update(dtSec)
		p.GetMu().Unlock()
	}
}


// broadcastWorldState gửi snapshot tất cả player qua UDP đến từng player trong phòng.
func (gl *GameLoop) broadcastWorldState() {
	if gl.udpSender == nil {
		if gl.tick%(TickRate*5) == 0 {
			log.Printf("[GameLoop:%s] WARN: udpSender nil — không thể broadcast WorldState", gl.room.ID)
		}
		return
	}

	snaps := gl.room.Snapshot()
	monsterSnaps := gl.room.MonsterSnapshot()
	if len(snaps) == 0 && len(monsterSnaps) == 0 {
		return
	}

	worldState := packet.EncodeWorldState(packet.WorldStatePacket{
		Tick:     gl.tick,
		Players:  snaps,
		Monsters: monsterSnaps,
	})

	gl.room.mu.RLock()
	defer gl.room.mu.RUnlock()

	sentCount, noAddrCount := 0, 0
	for _, p := range gl.room.players {
		udpAddr := p.GetUDPAddr()
		if udpAddr != nil {
			gl.udpSender.SendUDP(udpAddr, worldState)
			sentCount++
		} else {
			noAddrCount++
		}
	}

	// Log mỗi 5 giây để kiểm tra ai đang nhận WorldState
	if gl.tick%(TickRate*5) == 0 {
		log.Printf("[GameLoop:%s] tick=%d players=%d sent=%d no-udp-addr=%d",
			gl.room.ID, gl.tick, len(gl.room.players), sentCount, noAddrCount)
	}
}
