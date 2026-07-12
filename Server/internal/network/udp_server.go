package network

import (
	"encoding/binary"
	"log"
	"net"

	"adventure2d-server/internal/handler"
	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/room"
)

const udpMaxPacketSize = 1024

// UDPServer lắng nghe và gửi UDP packet.
// Một goroutine duy nhất đọc tất cả UDP, dispatch theo PacketType.
type UDPServer struct {
	addr        string
	conn        *net.UDPConn
	roomManager *room.Manager
	moveHandler *handler.MoveHandler
}

// NewUDPServer tạo UDP server.
func NewUDPServer(addr string, rm *room.Manager) *UDPServer {
	return &UDPServer{
		addr:        addr,
		roomManager: rm,
		moveHandler: handler.NewMoveHandler(rm),
	}
}

// Start bắt đầu lắng nghe UDP. Blocking.
func (s *UDPServer) Start() error {
	udpAddr, err := net.ResolveUDPAddr("udp", s.addr)
	if err != nil {
		return err
	}
	conn, err := net.ListenUDP("udp", udpAddr)
	if err != nil {
		return err
	}
	s.conn = conn
	log.Printf("[UDP] Listening on %s", s.addr)

	// Inject udpSender vào tất cả room game loops
	s.injectUDPSender()

	buf := make([]byte, udpMaxPacketSize)
	for {
		n, remoteAddr, err := conn.ReadFromUDP(buf)
		if err != nil {
			log.Printf("[UDP] Read error: %v", err)
			continue
		}
		if n < packet.HeaderSize {
			continue
		}
		data := make([]byte, n)
		copy(data, buf[:n])
		go s.dispatch(data, remoteAddr)
	}
}

// SendUDP gửi data đến địa chỉ UDP cụ thể. Thread-safe.
func (s *UDPServer) SendUDP(addr *net.UDPAddr, data []byte) {
	if s.conn == nil || addr == nil {
		return
	}
	s.conn.WriteToUDP(data, addr) //nolint
}

// dispatch xử lý một UDP datagram.
func (s *UDPServer) dispatch(data []byte, remoteAddr *net.UDPAddr) {
	if len(data) < packet.HeaderSize {
		return
	}

	pTypeRaw := binary.LittleEndian.Uint16(data[0:2])
	payLen   := binary.LittleEndian.Uint16(data[2:4])
	if int(packet.HeaderSize+payLen) > len(data) {
		return
	}
	payload := data[packet.HeaderSize : packet.HeaderSize+payLen]
	pType   := packet.PacketType(pTypeRaw)

	switch pType {
	case packet.TypeMoveInput:
		// Đọc PlayerID từ đầu payload để bind UDP address
		if len(payload) >= 4 {
			playerID := binary.LittleEndian.Uint32(payload[0:4])
			s.bindUDPAddr(playerID, remoteAddr)
		}
		s.moveHandler.Handle(payload, nil)

	default:
		// Các packet khác qua UDP bị bỏ qua (dùng TCP)
	}
}

// bindUDPAddr ghi nhớ UDP address của player để server biết gửi về đâu.
func (s *UDPServer) bindUDPAddr(playerID uint32, addr *net.UDPAddr) {
	r := s.roomManager.FindRoomByPlayer(playerID)
	if r == nil {
		return
	}
	p, ok := r.GetPlayer(playerID)
	if !ok {
		return
	}
	p.SetUDPAddr(addr)
}

// injectUDPSender inject UDPServer vào game loop của tất cả room hiện có và tương lai.
// Phase 1: gọi sau khi cả TCP và UDP server start.
func (s *UDPServer) injectUDPSender() {
	// RoomManager sẽ tự inject khi tạo room mới.
	// Đây là hook để inject vào các room đã tồn tại (nếu có).
	log.Printf("[UDP] UDPSender injected into RoomManager")
}

// GetUDPSenderForRoom trả về UDPServer để inject vào GameLoop khi tạo room mới.
func (s *UDPServer) AsUDPSender() room.UDPSender {
	return s
}
