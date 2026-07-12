package player

import (
	"log"
	"net"
	"sync/atomic"
)

// idCounter là atomic counter để tạo PlayerID tự tăng.
var idCounter uint32

// NextID sinh PlayerID mới (thread-safe).
func NextID() uint32 {
	return atomic.AddUint32(&idCounter, 1)
}

// Session quản lý vòng đời TCP của một player.
// Mỗi Session chạy trong 1 goroutine riêng.
type Session struct {
	Player  *Player
	conn    net.Conn

	// OnPacket được gọi khi nhận được packet từ client.
	// handler (tcp_server) sẽ set callback này.
	OnPacket func(pType uint16, payload []byte, session *Session)

	// OnDisconnect được gọi khi kết nối bị đóng.
	OnDisconnect func(session *Session)
}

// NewSession tạo Session mới cho một TCP connection.
func NewSession(conn net.Conn, p *Player) *Session {
	return &Session{
		Player: p,
		conn:   conn,
	}
}

// Start bắt đầu read loop cho session này.
// Blocking — nên gọi trong goroutine riêng.
func (s *Session) Start() {
	defer func() {
		s.conn.Close()
		if s.OnDisconnect != nil {
			s.OnDisconnect(s)
		}
		log.Printf("[Session] Player %d (%s) disconnected", s.Player.ID, s.Player.Username)
	}()

	log.Printf("[Session] Player %d (%s) connected", s.Player.ID, s.Player.Username)

	for {
		pTypeRaw, payload, err := readFrame(s.conn)
		if err != nil {
			// Connection closed hoặc lỗi đọc → thoát loop
			return
		}
		if s.OnPacket != nil {
			s.OnPacket(pTypeRaw, payload, s)
		}
	}
}

// Send gửi frame qua TCP của session này.
func (s *Session) Send(data []byte) {
	s.conn.Write(data) //nolint
}

// RemoteAddr trả về địa chỉ IP:Port của client.
func (s *Session) RemoteAddr() net.Addr {
	return s.conn.RemoteAddr()
}

// readFrame đọc 1 frame từ TCP: [type:2][len:2][payload:N]
func readFrame(conn net.Conn) (uint16, []byte, error) {
	header := make([]byte, 4)
	if _, err := readFull(conn, header); err != nil {
		return 0, nil, err
	}
	pType  := uint16(header[0]) | uint16(header[1])<<8
	payLen := uint16(header[2]) | uint16(header[3])<<8
	payload := make([]byte, payLen)
	if payLen > 0 {
		if _, err := readFull(conn, payload); err != nil {
			return 0, nil, err
		}
	}
	return pType, payload, nil
}

func readFull(conn net.Conn, buf []byte) (int, error) {
	total := 0
	for total < len(buf) {
		n, err := conn.Read(buf[total:])
		total += n
		if err != nil {
			return total, err
		}
	}
	return total, nil
}
