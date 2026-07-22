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

	// ── EXP / Level ───────────────────────────────────────────────────────────
	TypeExpGain  PacketType = 0x0040 // S→C  EXP gained (sent only to the killer)
	TypeLevelUp  PacketType = 0x0041 // S→C  Level-up notification (sent only to the killer)

	// ── Skill Point spending ───────────────────────────────────────────────────
	TypeSpendSkillPointReq PacketType = 0x0042 // C→S  Spend 1 skill point on a stat
	TypeSpendSkillPointAck PacketType = 0x0043 // S→C  Result of spending a skill point

	// ── System ────────────────────────────────────────────────────────────────
	TypePing PacketType = 0xFF00 // C↔S  Ping/Pong
	TypePong PacketType = 0xFF01
)

// HeaderSize is the fixed header size for every packet.
const HeaderSize = 4

// ── EXP / Level packet structs ────────────────────────────────────────────────

// ExpGainPacket is sent to a player when they kill a monster.
type ExpGainPacket struct {
	PlayerID  uint32
	ExpGained uint32 // how much EXP was awarded this kill
	NewExp    uint32 // current EXP after gain (within the current level)
	NewLevel  uint32 // current level (unchanged unless level-up follows)
}

// LevelUpPacket is sent immediately after ExpGainPacket when a level-up occurs.
type LevelUpPacket struct {
	PlayerID       uint32
	NewLevel       uint32
	NewExp         uint32 // EXP after level-up remainder carry-over
	NewSkillPoints uint32 // total accumulated skill points after this level-up
}

// ── Skill Point packet structs ────────────────────────────────────────────────

// StatUpgradeType identifies which stat to upgrade with a skill point.
type StatUpgradeType uint8

const (
	UpgradeHP         StatUpgradeType = 0 // +100 MaxHP
	UpgradeMP         StatUpgradeType = 1 // +100 MaxMP
	UpgradeATKPhysical StatUpgradeType = 2 // +10 ATK Physical
	UpgradeATKMagic   StatUpgradeType = 3 // +10 ATK Magic
	UpgradeDEF        StatUpgradeType = 4 // +10 DEF Physical AND +10 DEF Magic
)

// SpendSkillPointReqPacket is sent by the client to spend 1 skill point.
type SpendSkillPointReqPacket struct {
	StatType StatUpgradeType // which stat to upgrade
}

// SpendSkillPointAckPacket is the server response after spending a skill point.
type SpendSkillPointAckPacket struct {
	Success        bool   // true = upgrade applied
	FailReason     uint8  // 0 = ok, 1 = not enough SP, 2 = invalid type
	NewSkillPoints uint32
	NewMaxHP       uint16
	NewMaxMP       uint16
	NewATKPhysical uint16
	NewATKMagic    uint16
	NewDEFPhysical uint16
	NewDEFMagic    uint16
}
