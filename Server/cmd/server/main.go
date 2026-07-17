package main

import (
	"log"
	"os"
	"os/signal"
	"syscall"

	"adventure2d-server/internal/db"
	"adventure2d-server/internal/mapdata"
	"adventure2d-server/internal/network"
	"adventure2d-server/internal/player"
	"adventure2d-server/internal/room"
)

const (
	tcpAddr    = ":7777"      // TCP port for reliable messages
	udpAddr    = ":7778"      // UDP port for movement sync
	dbPath     = "adventure2d.db"
	mapDataDir = "data/maps" // directory containing *.mapdata files
)

func main() {
	log.SetFlags(log.Ldate | log.Ltime | log.Lmicroseconds | log.Lshortfile)
	log.Println("========================================")
	log.Println("=== Adventure2D Game Server Starting ===")
	log.Println("========================================")
	log.Printf("TCP: %s | UDP: %s | DB: %s", tcpAddr, udpAddr, dbPath)

	// Initialize SQLite database
	log.Println("[Main] Initializing database...")
	database, err := db.NewDatabase(dbPath)
	if err != nil {
		log.Fatalf("[Main] FATAL: Failed to initialize database: %v", err)
	}
	defer func() {
		log.Println("[Main] Closing database...")
		if err := database.Close(); err != nil {
			log.Printf("[Main] ERROR closing database: %v", err)
		}
	}()

	// Load static map data (*.mapdata) — once at startup, then read-only
	log.Printf("[Main] Loading map data from %q...", mapDataDir)
	mapMgr := mapdata.NewManager()
	if err := mapMgr.LoadAll(mapDataDir); err != nil {
		log.Printf("[Main] WARN: map data load error: %v", err)
		log.Println("[Main] Server will continue with fallback spawn/walkability.")
	}
	// Inject into player package so walkability checks use the loaded tile grids
	player.SetMapManager(mapMgr)

	// Initialize Room Manager (shared between TCP and UDP)
	log.Println("[Main] Initializing Room Manager...")
	roomManager := room.NewManager(mapMgr)

	// Khởi tạo UDP server trước để có UDPSender inject vào game loops
	log.Println("[Main] Initializing UDP server...")
	udpServer := network.NewUDPServer(udpAddr, roomManager)

	// Inject UDPSender vào RoomManager để game loops có thể broadcast
	roomManager.SetUDPSender(udpServer.AsUDPSender())
	log.Println("[Main] UDPSender injected into RoomManager")

	// Chạy UDP server trong goroutine riêng
	go func() {
		log.Printf("[Main] Starting UDP server on %s", udpAddr)
		if err := udpServer.Start(); err != nil {
			log.Fatalf("[Main] FATAL: UDP server failed: %v", err)
		}
	}()

	// Chạy TCP server trong goroutine riêng (truyền database repository vào)
	log.Println("[Main] Initializing TCP server...")
	tcpServer := network.NewTCPServer(tcpAddr, roomManager, database)
	go func() {
		log.Printf("[Main] Starting TCP server on %s", tcpAddr)
		if err := tcpServer.Start(); err != nil {
			log.Fatalf("[Main] FATAL: TCP server failed: %v", err)
		}
	}()

	log.Println("========================================")
	log.Println("=== Server Ready — waiting for clients ===")
	log.Println("========================================")

	// Chờ signal để shutdown gracefully
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)
	sig := <-quit

	log.Printf("[Main] Received signal: %v — shutting down gracefully...", sig)
	log.Println("=== Server Shut Down ===")
}
