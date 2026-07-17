package player

import (
	"log"
	"net"
	"sync/atomic"
)

// idCounter is an atomic counter for generating auto-incrementing PlayerIDs.
var idCounter uint32

// NextID generates a new PlayerID (thread-safe).
func NextID() uint32 {
	return atomic.AddUint32(&idCounter, 1)
}

// Session manages the TCP lifecycle for one player.
// Each Session runs in its own goroutine.
type Session struct {
	Player *Player
	conn   net.Conn

	// AccountID is set after the account is successfully authenticated.
	// Used by GetCharacterListReq to know which account to query.
	AccountID uint32

	// OnPacket is called when a packet is received from the client.
	// The dispatcher (tcp_server) sets this callback.
	OnPacket func(pType uint16, payload []byte, session *Session)

	// OnDisconnect is called when the connection closes.
	OnDisconnect func(session *Session)
}

// NewSession creates a new Session for a TCP connection.
func NewSession(conn net.Conn, p *Player) *Session {
	return &Session{
		Player: p,
		conn:   conn,
	}
}

// Start begins the read loop for this session.
// Blocking — must be called in its own goroutine.
func (s *Session) Start() {
	clientAddr := s.conn.RemoteAddr().String()

	defer func() {
		s.conn.Close()
		if s.OnDisconnect != nil {
			s.OnDisconnect(s)
		}
		log.Printf("[Session] Player %d (%s) disconnected from %s", s.Player.ID, s.Player.Username, clientAddr)
	}()

	log.Printf("[Session] New TCP connection from %s (playerID=%d)", clientAddr, s.Player.ID)

	for {
		pTypeRaw, payload, err := readFrame(s.conn)
		if err != nil {
			log.Printf("[Session] Read error from %s (player=%d %q): %v",
				clientAddr, s.Player.ID, s.Player.Username, err)
			return
		}

		log.Printf("[Session] Packet from %s: type=0x%04X payloadLen=%d", clientAddr, pTypeRaw, len(payload))

		if s.OnPacket != nil {
			s.OnPacket(pTypeRaw, payload, s)
		}
	}
}

// Send writes a frame over this session's TCP connection.
func (s *Session) Send(data []byte) {
	if _, err := s.conn.Write(data); err != nil {
		log.Printf("[Session] Send error to %s (player=%d %q): %v",
			s.conn.RemoteAddr(), s.Player.ID, s.Player.Username, err)
	}
}

// RemoteAddr returns the client's IP:Port address.
func (s *Session) RemoteAddr() net.Addr {
	return s.conn.RemoteAddr()
}

// readFrame reads one frame from the TCP stream: [type:2][len:2][payload:N]
// Enforces a payload size limit to prevent OOM if a client sends an oversized packet.
func readFrame(conn net.Conn) (uint16, []byte, error) {
	const maxPayloadSize = 4096 // max payload: 4 KB

	header := make([]byte, 4)
	if _, err := readFull(conn, header); err != nil {
		return 0, nil, err
	}

	pType  := uint16(header[0]) | uint16(header[1])<<8
	payLen := uint16(header[2]) | uint16(header[3])<<8

	// Drop oversized payloads to prevent OOM
	if payLen > maxPayloadSize {
		log.Printf("[Session] readFrame: payload too large (%d > %d), dropping connection from %s",
			payLen, maxPayloadSize, conn.RemoteAddr())
		conn.Close()
		return 0, nil, net.ErrClosed
	}

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
