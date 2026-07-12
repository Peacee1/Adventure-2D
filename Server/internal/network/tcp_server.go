package network

import (
	"log"
	"net"

	"adventure2d-server/internal/handler"
	"adventure2d-server/internal/packet"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
)

// TCPServer lắng nghe kết nối TCP từ Unity client.
type TCPServer struct {
	addr        string
	roomManager *room.Manager

	loginHandler  *handler.LoginHandler
	roomHandler   *handler.RoomHandler
	attackHandler *handler.AttackHandler
}

// NewTCPServer tạo TCP server.
func NewTCPServer(addr string, rm *room.Manager) *TCPServer {
	return &TCPServer{
		addr:          addr,
		roomManager:   rm,
		loginHandler:  handler.NewLoginHandler(rm),
		roomHandler:   handler.NewRoomHandler(rm),
		attackHandler: handler.NewAttackHandler(rm),
	}
}

// Start bắt đầu lắng nghe kết nối. Blocking.
func (s *TCPServer) Start() error {
	ln, err := net.Listen("tcp", s.addr)
	if err != nil {
		return err
	}
	defer ln.Close()
	log.Printf("[TCP] Listening on %s", s.addr)

	for {
		conn, err := ln.Accept()
		if err != nil {
			log.Printf("[TCP] Accept error: %v", err)
			continue
		}
		go s.handleConn(conn)
	}
}

// handleConn xử lý một TCP connection — chạy trong goroutine riêng.
func (s *TCPServer) handleConn(conn net.Conn) {
	// Tạo player tạm với ID=0 (sẽ được set sau khi login)
	p := player.New(0, "", player.JobWarrior, conn)
	sess := player.NewSession(conn, p)

	sess.OnPacket = s.dispatch
	sess.OnDisconnect = func(sess *player.Session) {
		s.roomHandler.HandleLeave(sess.Player.ID)
	}

	sess.Start() // blocking
}

// dispatch định tuyến packet đến handler phù hợp.
func (s *TCPServer) dispatch(pTypeRaw uint16, payload []byte, sess *player.Session) {
	pType := packet.PacketType(pTypeRaw)

	switch pType {
	case packet.TypeLoginReq:
		s.loginHandler.Handle(payload, sess)

	case packet.TypeJoinRoomReq:
		s.roomHandler.HandleJoin(payload, sess)

	case packet.TypeAttackReq:
		s.attackHandler.Handle(payload, sess)

	case packet.TypeRespawnReq:
		s.attackHandler.HandleRespawn(payload, sess)

	case packet.TypePing:
		ping, err := packet.DecodePing(payload)
		if err == nil {
			sess.Send(packet.EncodePong(packet.PongPacket{Timestamp: ping.Timestamp}))
		}

	default:
		log.Printf("[TCP] Unknown packet type: 0x%04X", pTypeRaw)
	}
}
