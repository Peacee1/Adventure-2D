package packet

// ── Auth ──────────────────────────────────────────────────────────────────────

// LoginReqPacket: sent by the client on connection.
type LoginReqPacket struct {
	Username string // max 32 bytes (length-prefixed)
	Password string // login password (length-prefixed)
	Slot     uint8  // character slot (0, 1, 2)
}

// LoginAckPacket: server response after authentication.
// Contains only session/spawn data. Stats are kept server-side (DB → player.Stats).
type LoginAckPacket struct {
	Success  bool
	PlayerID uint32
	JobClass uint8   // 0=Warrior 1=Archer ...
	Level    uint16
	Exp      uint32
	HP       uint16
	MaxHP    uint16
	X        float32
	Y        float32
	MapName  string  // Unity scene name to load, e.g. "Map1"
	CharName string  // Character's actual database name (players.username)
	Message  string  // Error message if Success=false
}

// RegisterReqPacket: sent by the client when registering an account.
type RegisterReqPacket struct {
	Username string
	Password string
}

// RegisterAckPacket: server response to a registration request.
type RegisterAckPacket struct {
	Success bool
	Message string
}

// GuestLoginReqPacket: client sends deviceUniqueIdentifier to auto-login without registration.
// Server creates a guest account if one doesn't exist, always returns LoginAck (even for new players).
type GuestLoginReqPacket struct {
	DeviceID string // SystemInfo.deviceUniqueIdentifier from Unity
	Slot     uint8  // character slot (0, 1, 2)
}

// CharacterSummary holds summary info for one character slot.
type CharacterSummary struct {
	Slot     uint8  // 0, 1, or 2
	Exists   bool   // false = slot is empty, no character created yet
	CharName string // character name
	JobClass uint8  // 0=Warrior 1=Archer ...
	Level    uint16 // current level
}

// GetCharacterListReqPacket: sent by the client after successful authentication to fetch the 3 character slots.
type GetCharacterListReqPacket struct {
	AccountID uint32 // AccountID received after authentication (stored on the server session)
}

// GetCharacterListAckPacket: server response with the 3 character slots for this account.
type GetCharacterListAckPacket struct {
	Characters []CharacterSummary // always exactly 3 elements (slots 0, 1, 2)
}

// ── Room ──────────────────────────────────────────────────────────────────────

// JoinRoomReqPacket: client requests to join a room.
type JoinRoomReqPacket struct {
	RoomID string // "" = automatic matchmaking
}

// JoinRoomAckPacket: server confirms room entry.
type JoinRoomAckPacket struct {
	Success bool
	RoomID  string
	// List of players already in the room
	ExistingPlayers []PlayerInfo
}

// PlayerInfo: basic info about one player.
type PlayerInfo struct {
	PlayerID uint32
	Username string
	X, Y     float32
	HP       uint16
	MaxHP    uint16
	JobClass uint8 // 0=Warrior 1=Archer 2=Mage 3=Healer 4=Assassin 5=Tank
}

// PlayerJoinedPacket: broadcast when a new player enters the room.
type PlayerJoinedPacket struct {
	Player PlayerInfo
}

// PlayerLeftPacket: broadcast when a player leaves the room.
type PlayerLeftPacket struct {
	PlayerID uint32
}

// ── Movement (UDP) ────────────────────────────────────────────────────────────

// MoveInputPacket: sent by the client over UDP each frame when moving.
type MoveInputPacket struct {
	PlayerID  uint32
	DestX     float32 // destination point (right-click target)
	DestY     float32
	DirX      float32 // normalized movement direction
	DirY      float32
	Timestamp uint32  // client tick count (for server packet ordering)
}

// MovePathPacket: sent by the client over TCP after NavMesh computes a path.
// Server moves the player along these waypoints instead of in a straight line.
type MovePathPacket struct {
	PlayerID  uint32
	Waypoints []WaypointVec2 // max 64 points
}

// WaypointVec2 is a single point on the path.
type WaypointVec2 struct {
	X, Y float32
}

// PlayerSnapshot: state of one player at one tick.
type PlayerSnapshot struct {
	PlayerID uint32
	X, Y     float32
	DirX     float32
	DirY     float32
	HP       uint16
	State    uint8 // 0=Idle 1=Move 2=Dash 3=Attack 4=Dead
}

// WorldStatePacket: server broadcasts over UDP every tick.
type WorldStatePacket struct {
	Tick    uint32
	Players []PlayerSnapshot
}

// ── Combat ────────────────────────────────────────────────────────────────────

// AttackReqPacket: sent by the client when the attack button is pressed.
type AttackReqPacket struct {
	PlayerID   uint32
	TargetID   uint32   // 0 = AOE / melee hit box
	DirX, DirY float32  // attack direction
}

// DamageEventPacket: broadcast when someone takes damage.
type DamageEventPacket struct {
	AttackerID  uint32
	TargetID    uint32
	Damage      uint32
	RemainingHP uint16
	IsCrit      bool
}

// DieEventPacket: broadcast when someone dies.
type DieEventPacket struct {
	PlayerID uint32
	KillerID uint32
}

// RespawnReqPacket: client requests to respawn.
type RespawnReqPacket struct {
	PlayerID uint32
}

// RespawnAckPacket: server confirms respawn with spawn position and HP.
type RespawnAckPacket struct {
	PlayerID uint32
	X, Y     float32
	HP       uint16
}

// ── System ────────────────────────────────────────────────────────────────────

// PingPacket: measures round-trip latency.
type PingPacket struct {
	Timestamp uint32
}

// PongPacket: response to ping, echoes back the timestamp.
type PongPacket struct {
	Timestamp uint32
}

// DashReqPacket: client requests to dash along a NavMesh-computed path.
// The client calculates the path using NavMesh before sending, so the server
// never needs to check walkability — it just follows the waypoints.
//
// Format: [PlayerID:uint32][TotalDistance:float32][Count:uint16][X:float32,Y:float32 × Count]
type DashReqPacket struct {
	PlayerID      uint32
	TotalDistance float32        // total path length in world units
	Waypoints     []WaypointVec2 // NavMesh path; max 16 points
}


// ProjectileSpawnPacket: server broadcasts when a projectile (arrow, spell, etc.) is fired.
// Client uses this to instantiate the visual projectile. Position is server-driven from this point.
type ProjectileSpawnPacket struct {
	ProjID   uint32  // unique projectile ID — used to correlate State and Destroy packets
	OwnerID  uint32  // player who fired the projectile
	X, Y     float32 // spawn position
	DirX     float32 // normalized flight direction X
	DirY     float32 // normalized flight direction Y
	Speed    float32 // flight speed (units/sec) — for client-side prediction (optional)
	Range    float32 // max range — kept for client interpolation reference
	ProjType uint8   // 0 = Arrow, 1 = Spell (extensible)
}

// ProjectileEntry is one entry in a batch ProjectileStatePacket.
type ProjectileEntry struct {
	ProjID uint32
	X, Y   float32
}

// ProjectileStatePacket: server sends current positions of all active projectiles each tick.
// Sent over TCP alongside WorldState (or could be UDP — using TCP for reliability).
type ProjectileStatePacket struct {
	Projectiles []ProjectileEntry
}

// ProjectileDestroyPacket: server broadcasts when a projectile is removed (hit a target or out-of-range).
type ProjectileDestroyPacket struct {
	ProjID uint32
}
