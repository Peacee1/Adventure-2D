// Package packet định nghĩa PacketType và header format.
//
// Header format (4 bytes, Little-Endian):
//   [PacketType: uint16][PayloadLen: uint16][Payload: N bytes]
package packet

// PacketType định danh loại packet.
type PacketType uint16

const (
	// ── Auth ──────────────────────────────────────────────────────────────────
	TypeLoginReq    PacketType = 0x0001 // C→S  Đăng nhập
	TypeLoginAck    PacketType = 0x0002 // S→C  Kết quả login
	TypeRegisterReq PacketType = 0x0003 // C→S  Đăng ký tài khoản
	TypeRegisterAck PacketType = 0x0004 // S→C  Kết quả đăng ký
	TypeGuestLoginReq PacketType = 0x0005 // C→S  Login bằng DeviceID (không cần đăng ký)

	// ── Room ──────────────────────────────────────────────────────────────────
	TypeJoinRoomReq  PacketType = 0x0010 // C→S  Vào phòng
	TypeJoinRoomAck  PacketType = 0x0011 // S→C  Kết quả vào phòng
	TypePlayerJoined PacketType = 0x0012 // S→C  Broadcast player mới
	TypePlayerLeft   PacketType = 0x0013 // S→C  Broadcast player rời

	// ── Movement (UDP) ────────────────────────────────────────────────────────
	TypeMoveInput  PacketType = 0x0020 // C→S  Input di chuyển (UDP)
	TypeWorldState PacketType = 0x0021 // S→C  Snapshot toàn bộ (UDP)

	// ── Combat ────────────────────────────────────────────────────────────────
	TypeAttackReq   PacketType = 0x0030 // C→S  Tấn công
	TypeDamageEvent PacketType = 0x0031 // S→C  Broadcast damage
	TypeDieEvent    PacketType = 0x0032 // S→C  Broadcast chết
	TypeRespawnReq  PacketType = 0x0033 // C→S  Hồi sinh
	TypeRespawnAck  PacketType = 0x0034 // S→C  Hồi sinh OK

	// ── System ────────────────────────────────────────────────────────────────
	TypePing PacketType = 0xFF00 // C↔S  Ping/Pong
	TypePong PacketType = 0xFF01
)

// HeaderSize là kích thước fixed header của mỗi packet.
const HeaderSize = 4
