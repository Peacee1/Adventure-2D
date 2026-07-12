package room

import (
	"log"
	"net"
	"time"

	"adventure2d-server/internal/packet"
)

// UDPSender cho phép game loop gửi UDP mà không import circular.
type UDPSender interface {
	SendUDP(addr *net.UDPAddr, data []byte)
}

const (
	// TickRate là số tick mỗi giây (60Hz).
	TickRate    = 60
	TickInterval = time.Second / TickRate
)



// GameLoop chạy fixed-tick loop cho một Room.
// Mỗi tick: snapshot trạng thái toàn phòng → broadcast WorldState qua UDP.
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
			gl.broadcastWorldState()
		}
	}
}

// Stop dừng game loop.
func (gl *GameLoop) Stop() {
	close(gl.stopCh)
}

// broadcastWorldState gửi snapshot tất cả player qua UDP đến từng player trong phòng.
func (gl *GameLoop) broadcastWorldState() {
	if gl.udpSender == nil {
		return
	}

	snaps := gl.room.Snapshot()
	if len(snaps) == 0 {
		return
	}

	worldState := packet.EncodeWorldState(packet.WorldStatePacket{
		Tick:    gl.tick,
		Players: snaps,
	})

	// Gửi đến từng player có UDP address đã đăng ký
	gl.room.mu.RLock()
	defer gl.room.mu.RUnlock()

	for _, p := range gl.room.players {
		udpAddr := p.GetUDPAddr()
		if udpAddr != nil {
			gl.udpSender.SendUDP(udpAddr, worldState)
		}
	}
}
