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
	dbRepo      player.Repository

	loginHandler         *handler.LoginHandler
	registerHandler      *handler.RegisterHandler
	roomHandler          *handler.RoomHandler
	attackHandler        *handler.AttackHandler
	movePathHandler      *handler.MovePathHandler
	characterListHandler *handler.CharacterListHandler
	dashHandler          *handler.DashHandler
}

// NewTCPServer tạo TCP server.
func NewTCPServer(addr string, rm *room.Manager, repo player.Repository) *TCPServer {
	log.Printf("[TCP] Initializing TCP server on %s", addr)
	return &TCPServer{
		addr:                 addr,
		roomManager:          rm,
		dbRepo:               repo,
		loginHandler:         handler.NewLoginHandler(rm, repo),
		registerHandler:      handler.NewRegisterHandler(repo),
		roomHandler:          handler.NewRoomHandler(rm),
		attackHandler:        handler.NewAttackHandler(rm),
		movePathHandler:      handler.NewMovePathHandler(rm),
		characterListHandler: handler.NewCharacterListHandler(repo),
		dashHandler:          handler.NewDashHandler(rm),
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
		log.Printf("[TCP] Accepted new connection from %s", conn.RemoteAddr())
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
		log.Printf("[TCP] OnDisconnect: player %d (%s) — saving & removing from room", sess.Player.ID, sess.Player.Username)
		s.roomHandler.HandleLeave(sess.Player.ID)
		// Lưu trạng thái người chơi vào DB khi ngắt kết nối
		if sess.Player.ID != 0 {
			if err := s.dbRepo.Save(sess.Player); err != nil {
				log.Printf("[TCP] ERROR saving player %d (%s) on disconnect: %v",
					sess.Player.ID, sess.Player.Username, err)
			} else {
				log.Printf("[TCP] Player %d (%s) state saved on disconnect", sess.Player.ID, sess.Player.Username)
			}
		}
	}

	sess.Start() // blocking
}

// dispatch định tuyến packet đến handler phù hợp.
func (s *TCPServer) dispatch(pTypeRaw uint16, payload []byte, sess *player.Session) {
	pType := packet.PacketType(pTypeRaw)

	switch pType {
	case packet.TypeLoginReq:
		s.loginHandler.Handle(payload, sess)

	case packet.TypeRegisterReq:
		s.registerHandler.Handle(payload, sess)

	case packet.TypeJoinRoomReq:
		s.roomHandler.HandleJoin(payload, sess)

	case packet.TypeAttackReq:
		s.attackHandler.Handle(payload, sess)

	case packet.TypeRespawnReq:
		s.attackHandler.HandleRespawn(payload, sess)

	case packet.TypeMovePath:
		s.movePathHandler.Handle(payload, sess)

	case packet.TypeDashReq:
		s.dashHandler.Handle(payload, sess)

	case packet.TypeGetCharListReq:
		s.characterListHandler.Handle(payload, sess)

	case packet.TypePing:
		ping, err := packet.DecodePing(payload)
		if err == nil {
			sess.Send(packet.EncodePong(packet.PongPacket{Timestamp: ping.Timestamp}))
			log.Printf("[TCP] Ping from player %d — Pong sent (ts=%d)", sess.Player.ID, ping.Timestamp)
		} else {
			log.Printf("[TCP] Ping decode error from player %d: %v", sess.Player.ID, err)
		}

	case packet.TypeHitboxConfigReq:
		ack := packet.HitboxConfigAckPacket{
			Shape:  1, // Box
			Radius: 0.0,
			Width:  float32(room.HitboxWidth),
			Height: float32(room.HitboxHeight),
		}
		sess.Send(packet.EncodeHitboxConfigAck(ack))
		log.Printf("[TCP] Sent hitbox config to player %d (shape=Box, size=%.2fx%.2f)", sess.Player.ID, room.HitboxWidth, room.HitboxHeight)

	default:
		log.Printf("[TCP] Unknown packet type=0x%04X from player %d (%s) — ignoring",
			pTypeRaw, sess.Player.ID, sess.Player.Username)
	}
}
