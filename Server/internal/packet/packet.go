// Package packet defines PacketType constants and the shared header format.
//
// Header format (4 bytes, Little-Endian):
//   [PacketType: uint16][PayloadLen: uint16][Payload: N bytes]
package packet

// PacketType identifies the type of a packet.
type PacketType uint16

const (
	// ── Auth ──────────────────────────────────────────────────────────────────
	TypeLoginReq         PacketType = 0x0001 // C→S  Login request
	TypeLoginAck         PacketType = 0x0002 // S→C  Login result
	TypeRegisterReq      PacketType = 0x0003 // C→S  Register account
	TypeRegisterAck      PacketType = 0x0004 // S→C  Register result
	TypeGuestLoginReq    PacketType = 0x0005 // C→S  Login with DeviceID (no registration needed)
	TypeGetCharListReq   PacketType = 0x0006 // C→S  Fetch 3 character slots for this account
	TypeGetCharListAck   PacketType = 0x0007 // S→C  Character slot list (3 slots)

	// ── Room ──────────────────────────────────────────────────────────────────
	TypeJoinRoomReq  PacketType = 0x0010 // C→S  Join room request
	TypeJoinRoomAck  PacketType = 0x0011 // S→C  Join room result
	TypePlayerJoined PacketType = 0x0012 // S→C  Broadcast new player
	TypePlayerLeft   PacketType = 0x0013 // S→C  Broadcast player left

	// ── Movement (UDP) ───────────────────────────────────────────────────────
	TypeMoveInput  PacketType = 0x0020 // C→S  Movement input (UDP)
	TypeWorldState PacketType = 0x0021 // S→C  Full world snapshot (UDP)
	TypeMovePath   PacketType = 0x0022 // C→S  NavMesh path waypoints (TCP)
	TypeDashReq    PacketType = 0x0023 // C→S  Dash request (TCP)

	// ── Combat ────────────────────────────────────────────────────────────────
	TypeAttackReq        PacketType = 0x0030 // C→S  Attack request
	TypeDamageEvent      PacketType = 0x0031 // S→C  Broadcast damage
	TypeDieEvent         PacketType = 0x0032 // S→C  Broadcast death
	TypeRespawnReq       PacketType = 0x0033 // C→S  Respawn request
	TypeRespawnAck       PacketType = 0x0034 // S→C  Respawn confirmed
	TypeProjectileSpawn   PacketType = 0x0035 // S→C  Broadcast projectile spawn
	TypeProjectileState   PacketType = 0x0036 // S→C  Batch projectile positions per tick (TCP)
	TypeProjectileDestroy PacketType = 0x0037 // S→C  Projectile removed (hit or out-of-range)
	TypeHitboxConfigReq   PacketType = 0x003e // C→S  Request hitbox shape and size
	TypeHitboxConfigAck   PacketType = 0x003f // S→C  Response hitbox config

	// ── System ────────────────────────────────────────────────────────────────
	TypePing PacketType = 0xFF00 // C↔S  Ping/Pong
	TypePong PacketType = 0xFF01
)

// HeaderSize is the fixed header size for every packet.
const HeaderSize = 4
