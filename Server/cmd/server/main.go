package main

import (
	"log"
	"os"
	"os/signal"
	"syscall"

	"adventure2d-server/internal/db"
	"adventure2d-server/internal/network"
	"adventure2d-server/internal/room"
)

const (
	tcpAddr = ":7777" // TCP port cho reliable messages
	udpAddr = ":7778" // UDP port cho movement sync
)

func main() {
	log.SetFlags(log.Ltime | log.Lshortfile)
	log.Println("=== Adventure2D Game Server Starting ===")
	log.Printf("TCP: %s | UDP: %s", tcpAddr, udpAddr)

	// Khởi tạo SQLite Database
	database, err := db.NewDatabase("adventure2d.db")
	if err != nil {
		log.Fatalf("[DB] Failed to initialize database: %v", err)
	}
	defer func() {
		log.Println("[DB] Closing database...")
		if err := database.Close(); err != nil {
			log.Printf("[DB] Error closing database: %v", err)
		}
	}()

	// Khởi tạo Room Manager (dùng chung cho cả TCP và UDP)
	roomManager := room.NewManager()

	// Khởi tạo UDP server trước để có UDPSender inject vào game loops
	udpServer := network.NewUDPServer(udpAddr, roomManager)

	// Inject UDPSender vào RoomManager để game loops có thể broadcast
	roomManager.SetUDPSender(udpServer.AsUDPSender())

	// Chạy UDP server trong goroutine riêng
	go func() {
		if err := udpServer.Start(); err != nil {
			log.Fatalf("[UDP] Failed to start: %v", err)
		}
	}()

	// Chạy TCP server trong goroutine riêng (truyền database repository vào)
	tcpServer := network.NewTCPServer(tcpAddr, roomManager, database)
	go func() {
		if err := tcpServer.Start(); err != nil {
			log.Fatalf("[TCP] Failed to start: %v", err)
		}
	}()

	log.Println("=== Server Ready ===")

	// Chờ signal để shutdown gracefully
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	<-quit

	log.Println("=== Server Shutting Down ===")
}
