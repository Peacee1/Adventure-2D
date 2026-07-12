package main

import (
	"fmt"
	"log"
	"os"
	"time"

	_ "github.com/mattn/go-sqlite3"
	"adventure2d-server/internal/db"
)

// Tool nhanh để seed test accounts vào DB.
// Chạy: go run ./cmd/seed/main.go
func main() {
	dbPath := "adventure2d.db"
	if len(os.Args) > 1 {
		dbPath = os.Args[1]
	}

	database, err := db.New(dbPath)
	if err != nil {
		log.Fatalf("DB init error: %v", err)
	}

	accounts := []struct{ username, password string }{
		{"testuser",  "pass123"},
		{"testuser2", "pass123"},
		{"testuser3", "pass123"},
	}

	for _, acc := range accounts {
		err := database.RegisterAccount(acc.username, acc.password)
		if err != nil {
			if err.Error() == "username đã được sử dụng" || err == db.ErrUsernameTaken {
				fmt.Printf("✓ %s đã tồn tại (skip)\n", acc.username)
			} else {
				fmt.Printf("✗ %s lỗi: %v\n", acc.username, err)
			}
		} else {
			fmt.Printf("✅ %s đã tạo\n", acc.username)
		}
		time.Sleep(10 * time.Millisecond) // bcrypt cần thời gian
	}
	fmt.Println("Seed xong!")
}
