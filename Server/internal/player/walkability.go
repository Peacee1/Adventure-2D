package player

import "adventure2d-server/internal/mapdata"

// mapManager is injected at startup by main.go via SetMapManager().
// All walkability and spawn queries delegate to it.
var mapManager *mapdata.Manager

// SetMapManager injects the global mapdata.Manager.
// Must be called once before any player sessions start.
func SetMapManager(m *mapdata.Manager) {
	mapManager = m
}

// IsWalkable returns true if the world-space point (wx, wy) is on a walkable
// Ground tile for the given map.
//
// Falls back to true (fail-open) when:
//   - mapManager is nil (not yet injected)
//   - map name is not found in mapManager
func IsWalkable(mapName string, wx, wy float32) bool {
	if mapManager == nil {
		return true
	}
	return mapManager.IsWalkable(mapName, wx, wy)
}

// SafeSpawn corrects an out-of-bounds position loaded from the DB.
// Returns the original (x, y) if walkable, or the map's first spawn point otherwise.
func SafeSpawn(mapName string, x, y float32) (float32, float32) {
	if mapManager == nil {
		return x, y
	}
	return mapManager.SafeSpawn(mapName, x, y)
}
