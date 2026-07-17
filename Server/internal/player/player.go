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
	StateIdle     State = 0
	StateMove     State = 1
	StateDash     State = 2
	StateAttack   State = 3
	StateDead     State = 4
	StateDashEnd  State = 5
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
	AttackSpeed float32 // seconds per attack animation (1.0 = 1 attack/sec)
}

// DefaultStats trả về stats cơ bản theo job class.
func DefaultStats(job JobClass) Stats {
	switch job {
	case JobArcher:
		return Stats{MaxHP: 800, ATKPhysical: 80, ATKMagic: 10, DEFPhysical: 30, DEFMagic: 20, AttackRange: 15, MoveSpeed: 10, AttackSpeed: 1.0}
	case JobMage:
		return Stats{MaxHP: 650, ATKPhysical: 15, ATKMagic: 120, DEFPhysical: 20, DEFMagic: 50, AttackRange: 8, MoveSpeed: 10, AttackSpeed: 1.0}
	case JobHealer:
		return Stats{MaxHP: 750, ATKPhysical: 20, ATKMagic: 55, DEFPhysical: 25, DEFMagic: 60, AttackRange: 8, MoveSpeed: 10, AttackSpeed: 1.0}
	case JobAssassin:
		return Stats{MaxHP: 700, ATKPhysical: 110, ATKMagic: 20, DEFPhysical: 25, DEFMagic: 15, AttackRange: 1.5, MoveSpeed: 10, AttackSpeed: 0.7}
	case JobTank:
		return Stats{MaxHP: 1500, ATKPhysical: 50, ATKMagic: 10, DEFPhysical: 100, DEFMagic: 80, AttackRange: 2, MoveSpeed: 10, AttackSpeed: 1.5}
	default: // Warrior
		return Stats{MaxHP: 1100, ATKPhysical: 90, ATKMagic: 10, DEFPhysical: 60, DEFMagic: 25, AttackRange: 2, MoveSpeed: 10, AttackSpeed: 1.0}
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
	Position    mathutil.Vector2
	Direction   mathutil.Vector2
	// Điểm đích cuối cùng (fallback khi không có path)
	Destination mathutil.Vector2
	// NavMesh path waypoints nhận từ client — server di chuyển theo đúng path này
	Waypoints   []mathutil.Vector2
	WaypointIdx int

	// HP hiện tại
	HP uint16

	// Trạng thái animation — driven by PlayerStateMachine
	State State

	// SM is the server-side state machine for this player.
	// Must be accessed under mu.
	SM *PlayerStateMachine

	// GameTime accumulates delta time each tick — used by states for cooldown checks.
	GameTime float32

	// LastDashTime records when the last dash started (in GameTime seconds).
	LastDashTime float32

	// DashWaypoints holds the NavMesh path for the current dash.
	// Set by dash_handler before TransitionTo(StateDash); read once by DashState.Enter().
	DashWaypoints     []mathutil.Vector2
	// DashTotalDistance is the pre-computed path length sent by the client.
	DashTotalDistance float32
	// DashEndLagDynamic is the speed-scaled landing lag set by DashState.Enter().
	// DashEndState reads this instead of the constant to stay in sync with the animation.
	DashEndLagDynamic float32

	// Level và EXP
	Level int
	Exp   int

	// Map cuối cùng player đứng (tên Scene Unity)
	MapName string

	// Ngày tạo nhân vật
	CreatedAt string

	// Dữ liệu dạng text — cập nhật từ DB khi login, ghi lại khi logout
	Inventory string // Kho đồ
	Buffs     string // Hiệu ứng buff đang hoạt động
	Skills    string // Kỹ năng đã mở khóa

	// Timestamp của MoveInput cuối cùng (để lọc out-of-order UDP)
	LastMoveTimestamp uint32

	// UDP address (để server biết gửi WorldState về đâu)
	UDPAddr *net.UDPAddr

	// TCP connection (dùng để gửi reliable message)
	TCPConn net.Conn
}

// New tạo player mới với stats mặc định và khởi tạo StateMachine.
func New(id uint32, username string, job JobClass, conn net.Conn) *Player {
	stats := DefaultStats(job)
	p := &Player{
		ID:       id,
		Username: username,
		JobClass: job,
		Stats:    stats,
		HP:       stats.MaxHP,
		State:    StateIdle,
		TCPConn:  conn,
	}
	p.SM = NewPlayerStateMachine()
	return p
}

// Update ticks the state machine. Called by game_loop each tick with the player mutex held.
func (p *Player) Update(dt float32) {
	p.GameTime += dt
	if p.SM != nil {
		p.SM.Update(p, dt)
	}
}

// CanDash returns true if the dash cooldown has expired.
func (p *Player) CanDash() bool {
	return p.GameTime-p.LastDashTime >= DashCooldown
}

// GetMu trả về mutex để game_loop có thể lock trực tiếp (batch update hiệu quả hơn).
func (p *Player) GetMu() *sync.RWMutex {
	return &p.mu
}

// InitState khởi tạo vị trí và HP của player từ Database (thread-safe).
// QUAN TRỌNG: phải set Destination = Position để game loop không di chuyển player về (0,0).
func (p *Player) InitState(pos mathutil.Vector2, hp uint16) {
	p.mu.Lock()
	p.Position    = pos
	p.Destination = pos // Game loop sẽ dừng ngay tại đây cho đến khi client gửi destination
	p.HP          = hp
	p.mu.Unlock()
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

// SetDestination cập nhật điểm đích di chuyển (thread-safe).
func (p *Player) SetDestination(dest mathutil.Vector2) {
	p.mu.Lock()
	p.Destination = dest
	p.mu.Unlock()
}

// GetDestination trả về điểm đích hiện tại (thread-safe).
func (p *Player) GetDestination() mathutil.Vector2 {
	p.mu.RLock()
	defer p.mu.RUnlock()
	return p.Destination
}

// SetPath lưu NavMesh path waypoints từ client (thread-safe).
// Game loop sẽ di chuyển player qua từng waypoint theo thứ tự.
func (p *Player) SetPath(waypoints []mathutil.Vector2) {
	p.mu.Lock()
	p.Waypoints   = waypoints
	p.WaypointIdx = 0
	if len(waypoints) > 0 {
		p.Destination = waypoints[len(waypoints)-1] // điểm cuối là destination thực
	}
	p.mu.Unlock()
}

// HasWaypoints trả về true nếu còn waypoint chưa đi tới.
func (p *Player) HasWaypoints() bool {
	p.mu.RLock()
	defer p.mu.RUnlock()
	return p.WaypointIdx < len(p.Waypoints)
}

// GetCurrentWaypoint trả về waypoint hiện tại cần di chuyển tới.
func (p *Player) GetCurrentWaypoint() (mathutil.Vector2, bool) {
	p.mu.RLock()
	defer p.mu.RUnlock()
	if p.WaypointIdx >= len(p.Waypoints) {
		return mathutil.Vector2{}, false
	}
	return p.Waypoints[p.WaypointIdx], true
}

// AdvanceWaypoint chuyển sang waypoint tiếp theo.
func (p *Player) AdvanceWaypoint() {
	p.mu.Lock()
	if p.WaypointIdx < len(p.Waypoints) {
		p.WaypointIdx++
	}
	p.mu.Unlock()
}

// ClearPath xóa path (player đã dừng lại).
func (p *Player) ClearPath() {
	p.mu.Lock()
	p.Waypoints   = nil
	p.WaypointIdx = 0
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

// SaveData chứa toàn bộ dữ liệu cần thiết để ghi vào DB khi logout.
type SaveData struct {
	ID          uint32
	Position    mathutil.Vector2
	HP          uint16
	JobClass    JobClass
	Stats       Stats
	Level       int
	Exp         int
	MapName     string
	Inventory   string
	Buffs       string
	Skills      string
}

// GetSaveData thu thập toàn bộ dữ liệu cần lưu DB (thread-safe).
func (p *Player) GetSaveData() SaveData {
	p.mu.RLock()
	defer p.mu.RUnlock()
	return SaveData{
		ID:        p.ID,
		Position:  p.Position,
		HP:        p.HP,
		JobClass:  p.JobClass,
		Stats:     p.Stats,
		Level:     p.Level,
		Exp:       p.Exp,
		MapName:   p.MapName,
		Inventory: p.Inventory,
		Buffs:     p.Buffs,
		Skills:    p.Skills,
	}
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

// StartDash requests a dash transition via the state machine (thread-safe).
// Called by dash_handler after validating the request.
func (p *Player) StartDash(dir mathutil.Vector2) {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.SM == nil {
		return
	}
	// Reject if dead or in DashEnd (landing lag)
	if p.State == StateDead || p.State == StateDashEnd {
		return
	}
	p.Direction = dir
	p.SM.TransitionTo(StateDash, p)
}
