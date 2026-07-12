// Package player quản lý trạng thái server-side của một player.
package player

import (
	"net"
	"sync"

	"adventure2d-server/pkg/mathutil"
)

// State định nghĩa trạng thái animation hiện tại (mirror bên Unity).
type State uint8

const (
	StateIdle   State = 0
	StateMove   State = 1
	StateDash   State = 2
	StateAttack State = 3
	StateDead   State = 4
)

// JobClass mirror với Unity JobClass enum.
type JobClass uint8

const (
	JobWarrior  JobClass = 0
	JobArcher   JobClass = 1
	JobMage     JobClass = 2
	JobHealer   JobClass = 3
	JobAssassin JobClass = 4
	JobTank     JobClass = 5
)

// Stats chứa combat stats của player, đọc từ client hoặc DB.
type Stats struct {
	MaxHP       uint16
	ATKPhysical uint16
	ATKMagic    uint16
	DEFPhysical uint16
	DEFMagic    uint16
	AttackRange float32
	MoveSpeed   float32
}

// DefaultStats trả về stats cơ bản theo job class.
func DefaultStats(job JobClass) Stats {
	switch job {
	case JobArcher:
		return Stats{MaxHP: 800, ATKPhysical: 80, ATKMagic: 10, DEFPhysical: 30, DEFMagic: 20, AttackRange: 15, MoveSpeed: 10}
	case JobMage:
		return Stats{MaxHP: 650, ATKPhysical: 15, ATKMagic: 120, DEFPhysical: 20, DEFMagic: 50, AttackRange: 8, MoveSpeed: 10}
	case JobHealer:
		return Stats{MaxHP: 750, ATKPhysical: 20, ATKMagic: 55, DEFPhysical: 25, DEFMagic: 60, AttackRange: 8, MoveSpeed: 10}
	case JobAssassin:
		return Stats{MaxHP: 700, ATKPhysical: 110, ATKMagic: 20, DEFPhysical: 25, DEFMagic: 15, AttackRange: 1.5, MoveSpeed: 10}
	case JobTank:
		return Stats{MaxHP: 1500, ATKPhysical: 50, ATKMagic: 10, DEFPhysical: 100, DEFMagic: 80, AttackRange: 2, MoveSpeed: 10}
	default: // Warrior
		return Stats{MaxHP: 1100, ATKPhysical: 90, ATKMagic: 10, DEFPhysical: 60, DEFMagic: 25, AttackRange: 2, MoveSpeed: 10}
	}
}

// Player đại diện cho một nhân vật trong game.
// Thread-safe: mọi field đều được bảo vệ bởi mu.
type Player struct {
	mu sync.RWMutex

	ID       uint32
	Username string
	JobClass JobClass
	Stats    Stats

	// Vị trí và hướng di chuyển hiện tại (server authoritative)
	Position  mathutil.Vector2
	Direction mathutil.Vector2

	// HP hiện tại
	HP uint16

	// Trạng thái animation
	State State

	// Timestamp của MoveInput cuối cùng (để lọc out-of-order UDP)
	LastMoveTimestamp uint32

	// UDP address (để server biết gửi WorldState về đâu)
	UDPAddr *net.UDPAddr

	// TCP connection (dùng để gửi reliable message)
	TCPConn net.Conn
}

// New tạo player mới với stats mặc định.
func New(id uint32, username string, job JobClass, conn net.Conn) *Player {
	stats := DefaultStats(job)
	return &Player{
		ID:       id,
		Username: username,
		JobClass: job,
		Stats:    stats,
		HP:       stats.MaxHP,
		State:    StateIdle,
		TCPConn:  conn,
	}
}

// SetPosition cập nhật vị trí (thread-safe).
func (p *Player) SetPosition(pos mathutil.Vector2) {
	p.mu.Lock()
	p.Position = pos
	p.mu.Unlock()
}

// SetDirection cập nhật hướng (thread-safe).
func (p *Player) SetDirection(dir mathutil.Vector2) {
	p.mu.Lock()
	p.Direction = dir
	p.mu.Unlock()
}

// SetState cập nhật state (thread-safe).
func (p *Player) SetState(s State) {
	p.mu.Lock()
	p.State = s
	p.mu.Unlock()
}

// SetUDPAddr lưu UDP address để server biết gửi về đâu.
func (p *Player) SetUDPAddr(addr *net.UDPAddr) {
	p.mu.Lock()
	p.UDPAddr = addr
	p.mu.Unlock()
}

// ApplyDamage trừ HP và trả về HP còn lại. Trả về (remaining, died).
func (p *Player) ApplyDamage(rawDamage uint16) (remaining uint16, died bool) {
	p.mu.Lock()
	defer p.mu.Unlock()

	if p.State == StateDead {
		return p.HP, false
	}

	// DEF giảm damage vật lý
	def := p.Stats.DEFPhysical
	actual := rawDamage
	if def > 0 {
		reduction := uint16(float32(def) * 1.5)
		if reduction >= rawDamage {
			actual = 1 // tối thiểu 1 dame
		} else {
			actual = rawDamage - reduction
		}
	}

	if actual >= p.HP {
		p.HP = 0
		p.State = StateDead
		return 0, true
	}
	p.HP -= actual
	return p.HP, false
}

// Respawn hồi sinh tại vị trí cho trước.
func (p *Player) Respawn(pos mathutil.Vector2) {
	p.mu.Lock()
	defer p.mu.Unlock()
	p.HP = p.Stats.MaxHP
	p.Position = pos
	p.State = StateIdle
}

// Snapshot trả về bản sao immutable để broadcast (không lock caller).
func (p *Player) Snapshot() (id uint32, pos mathutil.Vector2, dir mathutil.Vector2, hp uint16, state State) {
	p.mu.RLock()
	defer p.mu.RUnlock()
	return p.ID, p.Position, p.Direction, p.HP, p.State
}

// GetUDPAddr trả về UDP address đã đăng ký (thread-safe).
func (p *Player) GetUDPAddr() *net.UDPAddr {
	p.mu.RLock()
	defer p.mu.RUnlock()
	return p.UDPAddr
}

// GetLastMoveTimestamp trả về timestamp của MoveInput cuối (thread-safe).
func (p *Player) GetLastMoveTimestamp() uint32 {
	p.mu.RLock()
	defer p.mu.RUnlock()
	return p.LastMoveTimestamp
}

// SetLastMoveTimestamp cập nhật timestamp của MoveInput cuối (thread-safe).
// Trả về false nếu timestamp mới ≤ cũ (out-of-order packet).
func (p *Player) SetLastMoveTimestamp(ts uint32) bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	if ts <= p.LastMoveTimestamp {
		return false
	}
	p.LastMoveTimestamp = ts
	return true
}

// SendTCP gửi dữ liệu qua TCP (fire-and-forget, bỏ qua lỗi nếu disconnected).
func (p *Player) SendTCP(data []byte) {
	p.mu.RLock()
	conn := p.TCPConn
	p.mu.RUnlock()
	if conn != nil {
		conn.Write(data) //nolint
	}
}
