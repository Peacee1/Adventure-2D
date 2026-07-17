// Package mapdata provides types and utilities for loading and querying
// static map data exported from Unity (*.mapdata binary files).
//
// All map data is loaded once at server startup and cached in memory.
// Reads are lock-free after initialization.
package mapdata

import "math"

// ── Data Structures ───────────────────────────────────────────────────────────

// MapData is the in-memory representation of a loaded .mapdata file.
// All fields are read-only after Load().
type MapData struct {
	MapID uint32
	Name  string

	// Tiles is the walkability grid derived from the Ground tilemap.
	Tiles *TileGrid

	// Spawns is the list of valid spawn positions in world space.
	Spawns []Vector2
}

// Vector2 is a 2D world-space coordinate (matches Unity's Vector2).
type Vector2 struct {
	X, Y float32
}

// TileGrid is a compact bitset representing walkable tile positions.
// Tiles are stored in row-major order (X inner, Y outer bottom-to-top).
// A tile at grid coord (tx, ty) covers world rect [tx, tx+1) × [ty, ty+1).
type TileGrid struct {
	OriginX int32  // leftmost tile column (grid coords)
	OriginY int32  // bottom tile row (grid coords)
	Width   uint32 // number of columns
	Height  uint32 // number of rows
	bits    []byte // 1 bit per tile, row-major
}

// IsWalkable returns true when the world-space point (wx, wy) falls inside
// a tile that exists in the Ground tilemap.
func (g *TileGrid) IsWalkable(wx, wy float32) bool {
	if g == nil {
		return true // nil grid → fail open
	}
	tx := int32(math.Floor(float64(wx)))
	ty := int32(math.Floor(float64(wy)))

	col := tx - g.OriginX
	row := ty - g.OriginY

	if col < 0 || row < 0 || uint32(col) >= g.Width || uint32(row) >= g.Height {
		return false // out of grid bounds
	}

	idx := uint32(row)*g.Width + uint32(col)
	return (g.bits[idx/8]>>(idx%8))&1 == 1
}

// DefaultSpawn returns a safe world-space position for new or respawning players.
// It returns the first spawn point if any exist, otherwise the world origin.
func (m *MapData) DefaultSpawn() Vector2 {
	if len(m.Spawns) > 0 {
		return m.Spawns[0]
	}
	return Vector2{X: 0, Y: 0}
}

// SafeSpawn returns (x, y) as-is if walkable, otherwise returns the map's
// first spawn point. Used on login to correct stale out-of-bounds DB positions.
func (m *MapData) SafeSpawn(x, y float32) (float32, float32) {
	if m.Tiles.IsWalkable(x, y) {
		return x, y
	}
	sp := m.DefaultSpawn()
	return sp.X, sp.Y
}
