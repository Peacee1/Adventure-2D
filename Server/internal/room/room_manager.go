package room

import (
	"fmt"
	"log"
	"sync"

	"adventure2d-server/internal/mapdata"
	"github.com/google/uuid"
)

// Manager manages all active rooms.
// Thread-safe.
type Manager struct {
	mu        sync.RWMutex
	rooms     map[string]*Room
	udpSender UDPSender       // injected from UDPServer so game loops can broadcast
	mapMgr    *mapdata.Manager // loaded map data for spawn points and walkability
}

// SetUDPSender inject UDP sender vào manager.
// Gọi sau khi UDP server start, trước khi tạo bất kỳ room nào.
func (m *Manager) SetUDPSender(s UDPSender) {
	m.mu.Lock()
	m.udpSender = s
	m.mu.Unlock()
}


// NewManager creates a new RoomManager with the given mapdata.Manager.
// mapMgr may be nil; in that case rooms fall back to hardcoded spawn points.
func NewManager(mapMgr *mapdata.Manager) *Manager {
	return &Manager{
		rooms:  make(map[string]*Room),
		mapMgr: mapMgr,
	}
}

// GetOrCreate tìm phòng còn chỗ hoặc tạo phòng mới.
// roomID="" → matchmake tự động (vào phòng đầu tiên còn chỗ).
func (m *Manager) GetOrCreate(roomID string) *Room {
	m.mu.Lock()
	defer m.mu.Unlock()

	// Nếu có yêu cầu phòng cụ thể
	if roomID != "" {
		if r, ok := m.rooms[roomID]; ok {
			return r
		}
		return m.createRoom(roomID)
	}

	// Matchmake: tìm phòng còn chỗ
	for _, r := range m.rooms {
		if !r.IsFull() {
			return r
		}
	}

	// Tạo phòng mới
	newID := fmt.Sprintf("room-%s", uuid.New().String()[:8])
	return m.createRoom(newID)
}

// GetRoom tìm phòng theo ID (trả về nil nếu không tìm thấy).
func (m *Manager) GetRoom(roomID string) *Room {
	m.mu.RLock()
	defer m.mu.RUnlock()
	return m.rooms[roomID]
}

// FindRoomByPlayer tìm phòng chứa playerID.
func (m *Manager) FindRoomByPlayer(playerID uint32) *Room {
	m.mu.RLock()
	defer m.mu.RUnlock()
	for _, r := range m.rooms {
		if _, ok := r.GetPlayer(playerID); ok {
			return r
		}
	}
	return nil
}

// RemoveIfEmpty xóa phòng nếu không còn player nào.
func (m *Manager) RemoveIfEmpty(roomID string) {
	m.mu.Lock()
	defer m.mu.Unlock()
	r, ok := m.rooms[roomID]
	if !ok {
		return
	}
	if r.PlayerCount() == 0 {
		r.Stop()
		delete(m.rooms, roomID)
		log.Printf("[RoomManager] Room %s removed (empty)", roomID)
	}
}

// createRoom creates and starts a new room (must hold lock when called).
// mapName defaults to the room ID — for Map1 the convention is roomID = "Map1".
func (m *Manager) createRoom(id string) *Room {
	// Map name is always "Map0" for now — room ID is for internal routing only.
	// mapd.Get() keys on the scene/map name, not the room UUID.
	const mapName = "Map0"
	r := New(id, mapName, m.mapMgr)
	m.rooms[id] = r
	// Inject UDP sender into game loop immediately
	if m.udpSender != nil {
		r.loop.SetUDPSender(m.udpSender)
	}
	r.Start()
	log.Printf("[RoomManager] Room %s created (map=%s)", id, mapName)
	return r
}
