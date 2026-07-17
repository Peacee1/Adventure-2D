package mapdata

import (
	"encoding/binary"
	"fmt"
	"io"
	"log"
	"os"
	"path/filepath"
	"strings"
	"unsafe"
)

// ── Magic & Section constants (mirror of MapDataFormat.cs) ───────────────────

var magic = [4]byte{'M', 'D', 'A', 'T'}

const (
	currentVersion  uint16 = 1
	sectionTile     byte   = 0x01
	sectionSpawn    byte   = 0x02
	sectionSafeZone byte   = 0x03
	sectionEnd      byte   = 0xFF
)

// ── Manager ───────────────────────────────────────────────────────────────────

// Manager holds all loaded maps, keyed by scene name (e.g. "Map1").
// After LoadAll(), reads are concurrent-safe (immutable data).
type Manager struct {
	maps map[string]*MapData
}

// NewManager creates an empty Manager.
func NewManager() *Manager {
	return &Manager{maps: make(map[string]*MapData)}
}

// LoadAll scans dir for *.mapdata files and loads each one.
// Call once at server startup before serving any clients.
func (m *Manager) LoadAll(dir string) error {
	entries, err := os.ReadDir(dir)
	if err != nil {
		return fmt.Errorf("[MapLoader] ReadDir %q: %w", dir, err)
	}

	loaded := 0
	for _, e := range entries {
		if e.IsDir() || !strings.HasSuffix(e.Name(), ".mapdata") {
			continue
		}
		path := filepath.Join(dir, e.Name())
		md, err := LoadMap(path)
		if err != nil {
			log.Printf("[MapLoader] WARN: skipping %q: %v", e.Name(), err)
			continue
		}
		m.maps[md.Name] = md
		loaded++
		log.Printf("[MapLoader] Loaded %q: id=%d tiles=%d spawns=%d",
			md.Name, md.MapID, m.tileCount(md), len(md.Spawns))
	}

	if loaded == 0 {
		log.Printf("[MapLoader] WARNING: no .mapdata files found in %q", dir)
	}
	return nil
}

// Get returns the MapData for the given scene name, or nil if not loaded.
func (m *Manager) Get(name string) *MapData {
	return m.maps[name]
}

// IsWalkable returns whether (wx, wy) is on a walkable tile for the given map.
// Returns true (fail-open) if the map is unknown.
func (m *Manager) IsWalkable(mapName string, wx, wy float32) bool {
	md, ok := m.maps[mapName]
	if !ok {
		return true
	}
	return md.Tiles.IsWalkable(wx, wy)
}

// SafeSpawn returns a safe spawn position for the given map and candidate position.
func (m *Manager) SafeSpawn(mapName string, x, y float32) (float32, float32) {
	md, ok := m.maps[mapName]
	if !ok {
		return x, y
	}
	return md.SafeSpawn(x, y)
}

func (m *Manager) tileCount(md *MapData) int {
	if md.Tiles == nil {
		return 0
	}
	count := 0
	total := md.Tiles.Width * md.Tiles.Height
	for i := uint32(0); i < total; i++ {
		if (md.Tiles.bits[i/8]>>(i%8))&1 == 1 {
			count++
		}
	}
	return count
}

// ── Loader ────────────────────────────────────────────────────────────────────

// LoadMap reads a single .mapdata binary file and returns a *MapData.
func LoadMap(path string) (*MapData, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer f.Close()

	r := &leReader{r: f}

	// ── Header ────────────────────────────────────────────────────────────────
	var m [4]byte
	if _, err := io.ReadFull(f, m[:]); err != nil {
		return nil, fmt.Errorf("read magic: %w", err)
	}
	if m != magic {
		return nil, fmt.Errorf("invalid magic: %x", m)
	}

	version := r.readU16()
	if version != currentVersion {
		return nil, fmt.Errorf("unsupported version %d (expected %d)", version, currentVersion)
	}

	mapID := r.readU32()

	nameBytes := make([]byte, 20)
	if _, err := io.ReadFull(f, nameBytes); err != nil {
		return nil, fmt.Errorf("read name: %w", err)
	}
	mapName := strings.TrimRight(string(nameBytes), "\x00")

	// 2 reserved bytes
	r.readU16()

	if r.err != nil {
		return nil, fmt.Errorf("header read error: %w", r.err)
	}

	md := &MapData{MapID: mapID, Name: mapName}

	// ── Sections ──────────────────────────────────────────────────────────────
	for {
		secID := r.readU8()
		if r.err != nil {
			break
		}

		switch secID {
		case sectionTile:
			md.Tiles, err = readTileSection(r)
			if err != nil {
				return nil, fmt.Errorf("tile section: %w", err)
			}

		case sectionSpawn:
			md.Spawns, err = readSpawnSection(r)
			if err != nil {
				return nil, fmt.Errorf("spawn section: %w", err)
			}

		case sectionSafeZone:
			// Reserved for future use — skip by reading and discarding
			if err := skipSafeZoneSection(r); err != nil {
				return nil, fmt.Errorf("safezone section: %w", err)
			}

		case sectionEnd:
			goto done

		default:
			return nil, fmt.Errorf("unknown section ID: 0x%02x", secID)
		}
	}

done:
	return md, nil
}

func readTileSection(r *leReader) (*TileGrid, error) {
	originX := r.readI32()
	originY := r.readI32()
	width    := r.readU32()
	height   := r.readU32()
	byteCount := r.readU32()

	bits := make([]byte, byteCount)
	if _, err := io.ReadFull(r.r, bits); err != nil {
		return nil, err
	}
	if r.err != nil {
		return nil, r.err
	}

	return &TileGrid{
		OriginX: originX,
		OriginY: originY,
		Width:   width,
		Height:  height,
		bits:    bits,
	}, nil
}

func readSpawnSection(r *leReader) ([]Vector2, error) {
	count := r.readU32()
	points := make([]Vector2, count)
	for i := uint32(0); i < count; i++ {
		points[i] = Vector2{X: r.readF32(), Y: r.readF32()}
	}
	if r.err != nil {
		return nil, r.err
	}
	return points, nil
}

func skipSafeZoneSection(r *leReader) error {
	count := r.readU32()
	// Each SafeZone is 4 × float32 (minX, minY, maxX, maxY)
	buf := make([]byte, count*16)
	_, err := io.ReadFull(r.r, buf)
	return err
}

// ── Little-endian binary reader helper ───────────────────────────────────────

type leReader struct {
	r   io.Reader
	err error
	buf [8]byte
}

func (r *leReader) readU8() byte {
	if r.err != nil { return 0 }
	_, r.err = io.ReadFull(r.r, r.buf[:1])
	return r.buf[0]
}

func (r *leReader) readU16() uint16 {
	if r.err != nil { return 0 }
	_, r.err = io.ReadFull(r.r, r.buf[:2])
	return binary.LittleEndian.Uint16(r.buf[:2])
}

func (r *leReader) readU32() uint32 {
	if r.err != nil { return 0 }
	_, r.err = io.ReadFull(r.r, r.buf[:4])
	return binary.LittleEndian.Uint32(r.buf[:4])
}

func (r *leReader) readI32() int32 {
	return int32(r.readU32())
}

func (r *leReader) readF32() float32 {
	bits := r.readU32()
	return *(*float32)(unsafe.Pointer(&bits))
}
