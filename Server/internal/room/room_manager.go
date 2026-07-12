package room

import (
	"fmt"
	"log"
	"sync"

	"github.com/google/uuid"
)

// Manager quản lý tất cả các room đang active.
// Thread-safe.
type Manager struct {
	mu        sync.RWMutex
	rooms     map[string]*Room
	udpSender UDPSender // inject từ UDPServer để game loops có thể broadcast
}

// SetUDPSender inject UDP sender vào manager.
// Gọi sau khi UDP server start, trước khi tạo bất kỳ room nào.
func (m *Manager) SetUDPSender(s UDPSender) {
	m.mu.Lock()
	m.udpSender = s
	m.mu.Unlock()
}


// NewManager tạo RoomManager mới.
func NewManager() *Manager {
	return &Manager{
		rooms: make(map[string]*Room),
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

// createRoom tạo và khởi động phòng mới (phải giữ lock khi gọi).
func (m *Manager) createRoom(id string) *Room {
	r := New(id)
	m.rooms[id] = r
	// Inject UDP sender vào game loop ngay khi tạo
	if m.udpSender != nil {
		r.loop.SetUDPSender(m.udpSender)
	}
	r.Start()
	log.Printf("[RoomManager] Room %s created", id)
	return r
}
