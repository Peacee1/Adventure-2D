// Package packet — binary encoder/decoder (Little-Endian).
//
// Wire format per message:
//   [PacketType: uint16 LE][PayloadLen: uint16 LE][Payload: bytes]
package packet

import (
	"bytes"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"math"
)

var le = binary.LittleEndian

// ── Low-level read/write helpers ──────────────────────────────────────────────

func writeUint8(buf *bytes.Buffer, v uint8)  { buf.WriteByte(v) }
func writeUint16(buf *bytes.Buffer, v uint16) {
	b := [2]byte{}; le.PutUint16(b[:], v); buf.Write(b[:])
}
func writeUint32(buf *bytes.Buffer, v uint32) {
	b := [4]byte{}; le.PutUint32(b[:], v); buf.Write(b[:])
}
func writeFloat32(buf *bytes.Buffer, v float32) {
	writeUint32(buf, math.Float32bits(v))
}
func writeBool(buf *bytes.Buffer, v bool) {
	if v { buf.WriteByte(1) } else { buf.WriteByte(0) }
}
func writeString(buf *bytes.Buffer, s string) {
	writeUint16(buf, uint16(len(s)))
	buf.WriteString(s)
}

func readUint8(r io.Reader) (uint8, error) {
	b := [1]byte{}
	_, err := io.ReadFull(r, b[:])
	return b[0], err
}
func readUint16(r io.Reader) (uint16, error) {
	b := [2]byte{}
	_, err := io.ReadFull(r, b[:])
	return le.Uint16(b[:]), err
}
func readUint32(r io.Reader) (uint32, error) {
	b := [4]byte{}
	_, err := io.ReadFull(r, b[:])
	return le.Uint32(b[:]), err
}
func readFloat32(r io.Reader) (float32, error) {
	u, err := readUint32(r)
	return math.Float32frombits(u), err
}
func readBool(r io.Reader) (bool, error) {
	b, err := readUint8(r)
	return b != 0, err
}
func readString(r io.Reader) (string, error) {
	length, err := readUint16(r)
	if err != nil {
		return "", err
	}
	if length > 512 {
		return "", errors.New("string too long")
	}
	buf := make([]byte, length)
	_, err = io.ReadFull(r, buf)
	return string(buf), err
}

// ── Frame encoder/decoder ─────────────────────────────────────────────────────

// EncodeFrame đóng gói payload vào wire frame.
func EncodeFrame(pType PacketType, payload []byte) []byte {
	frame := make([]byte, HeaderSize+len(payload))
	le.PutUint16(frame[0:2], uint16(pType))
	le.PutUint16(frame[2:4], uint16(len(payload)))
	copy(frame[4:], payload)
	return frame
}

// ReadFrame đọc 1 frame từ TCP reader. Trả về (PacketType, payload, error).
func ReadFrame(r io.Reader) (PacketType, []byte, error) {
	header := [HeaderSize]byte{}
	if _, err := io.ReadFull(r, header[:]); err != nil {
		return 0, nil, err
	}
	pType := PacketType(le.Uint16(header[0:2]))
	payLen := le.Uint16(header[2:4])
	if payLen > 4096 {
		return 0, nil, fmt.Errorf("payload too large: %d", payLen)
	}
	payload := make([]byte, payLen)
	if _, err := io.ReadFull(r, payload); err != nil {
		return 0, nil, err
	}
	return pType, payload, nil
}

// ── Encoders ──────────────────────────────────────────────────────────────────

func EncodeLoginAck(p LoginAckPacket) []byte {
	buf := &bytes.Buffer{}
	writeBool(buf, p.Success)
	writeUint32(buf, p.PlayerID)
	writeUint8(buf, p.JobClass)
	writeUint16(buf, p.Level)
	writeUint32(buf, p.Exp)
	writeUint16(buf, p.HP)
	writeUint16(buf, p.MaxHP)
	writeFloat32(buf, p.X)
	writeFloat32(buf, p.Y)
	writeString(buf, p.MapName)
	writeString(buf, p.CharName)
	writeString(buf, p.Message)
	// Combat stats for StatsManager
	writeUint16(buf, p.MaxMP)
	writeUint16(buf, p.ATKPhysical)
	writeUint16(buf, p.ATKMagic)
	writeUint16(buf, p.DEFPhysical)
	writeUint16(buf, p.DEFMagic)
	writeUint32(buf, p.SkillPoints)
	writeFloat32(buf, p.CritRate)
	writeFloat32(buf, p.LifeSteal)
	writeFloat32(buf, p.AttackSpeed)
	return EncodeFrame(TypeLoginAck, buf.Bytes())
}

func EncodeJoinRoomAck(p JoinRoomAckPacket) []byte {
	buf := &bytes.Buffer{}
	writeBool(buf, p.Success)
	writeString(buf, p.RoomID)
	writeUint16(buf, uint16(len(p.ExistingPlayers)))
	for _, pl := range p.ExistingPlayers {
		writePlayerInfo(buf, pl)
	}
	return EncodeFrame(TypeJoinRoomAck, buf.Bytes())
}

func EncodePlayerJoined(p PlayerJoinedPacket) []byte {
	buf := &bytes.Buffer{}
	writePlayerInfo(buf, p.Player)
	return EncodeFrame(TypePlayerJoined, buf.Bytes())
}

func EncodePlayerLeft(p PlayerLeftPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.PlayerID)
	return EncodeFrame(TypePlayerLeft, buf.Bytes())
}

func EncodeWorldState(p WorldStatePacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.Tick)
	writeUint16(buf, uint16(len(p.Players)))
	for _, snap := range p.Players {
		writeUint32(buf, snap.PlayerID)
		writeFloat32(buf, snap.X)
		writeFloat32(buf, snap.Y)
		writeFloat32(buf, snap.DirX)
		writeFloat32(buf, snap.DirY)
		writeUint16(buf, snap.HP)
		writeUint8(buf, snap.State)
	}
	// Monsters follow players in the same packet
	writeUint16(buf, uint16(len(p.Monsters)))
	for _, m := range p.Monsters {
		writeUint32(buf, m.ID)
		writeFloat32(buf, m.X)
		writeFloat32(buf, m.Y)
		writeUint16(buf, m.HP)
		writeUint8(buf, m.State)
	}
	return EncodeFrame(TypeWorldState, buf.Bytes())
}

func EncodeDamageEvent(p DamageEventPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.AttackerID)
	writeUint32(buf, p.TargetID)
	writeUint32(buf, p.Damage)
	writeUint16(buf, p.RemainingHP)
	writeBool(buf, p.IsCrit)
	return EncodeFrame(TypeDamageEvent, buf.Bytes())
}

func EncodeDieEvent(p DieEventPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.PlayerID)
	writeUint32(buf, p.KillerID)
	return EncodeFrame(TypeDieEvent, buf.Bytes())
}

func EncodeRespawnAck(p RespawnAckPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.PlayerID)
	writeFloat32(buf, p.X)
	writeFloat32(buf, p.Y)
	writeUint16(buf, p.HP)
	return EncodeFrame(TypeRespawnAck, buf.Bytes())
}

func EncodePong(p PongPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.Timestamp)
	return EncodeFrame(TypePong, buf.Bytes())
}

func EncodeRegisterAck(p RegisterAckPacket) []byte {
	buf := &bytes.Buffer{}
	writeBool(buf, p.Success)
	writeString(buf, p.Message)
	return EncodeFrame(TypeRegisterAck, buf.Bytes())
}

// EncodeGetCharacterListAck đóng gói danh sách 3 slot nhân vật gửi về client.
// Format payload: [count:uint8][slot:uint8][exists:bool][charName:str][jobClass:uint8][level:uint16] x3
func EncodeGetCharacterListAck(p GetCharacterListAckPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint8(buf, uint8(len(p.Characters)))
	for _, c := range p.Characters {
		writeUint8(buf, c.Slot)
		writeBool(buf, c.Exists)
		writeString(buf, c.CharName)
		writeUint8(buf, c.JobClass)
		writeUint16(buf, c.Level)
	}
	return EncodeFrame(TypeGetCharListAck, buf.Bytes())
}

func EncodeProjectileSpawn(p ProjectileSpawnPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.ProjID)
	writeUint32(buf, p.OwnerID)
	writeFloat32(buf, p.X)
	writeFloat32(buf, p.Y)
	writeFloat32(buf, p.DirX)
	writeFloat32(buf, p.DirY)
	writeFloat32(buf, p.Speed)
	writeFloat32(buf, p.Range)
	writeUint8(buf, p.ProjType)
	return EncodeFrame(TypeProjectileSpawn, buf.Bytes())
}

// EncodeProjectileState encodes a batch of projectile positions for one tick.
// Format: [count uint16][ProjID uint32, X float32, Y float32] × count
func EncodeProjectileState(p ProjectileStatePacket) []byte {
	buf := &bytes.Buffer{}
	writeUint16(buf, uint16(len(p.Projectiles)))
	for _, e := range p.Projectiles {
		writeUint32(buf, e.ProjID)
		writeFloat32(buf, e.X)
		writeFloat32(buf, e.Y)
	}
	return EncodeFrame(TypeProjectileState, buf.Bytes())
}

// EncodeProjectileDestroy encodes a projectile removal notification.
func EncodeProjectileDestroy(p ProjectileDestroyPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.ProjID)
	return EncodeFrame(TypeProjectileDestroy, buf.Bytes())
}


// ── Decoders ──────────────────────────────────────────────────────────────────

func DecodeGuestLoginReq(payload []byte) (GuestLoginReqPacket, error) {
	r := bytes.NewReader(payload)
	var p GuestLoginReqPacket
	var err error
	if p.DeviceID, err = readString(r); err != nil { return p, err }
	if p.Slot,     err = readUint8(r);  err != nil { return p, err }
	return p, nil
}

// DecodeGetCharacterListReq giải mã request lấy danh sách nhân vật từ client.
// Payload rỗng — AccountID được lấy từ session.AccountID (server side).
func DecodeGetCharacterListReq(payload []byte) (GetCharacterListReqPacket, error) {
	// Payload hiện tại trống, AccountID được lưu trên session sau khi login
	return GetCharacterListReqPacket{}, nil
}

func DecodeLoginReq(payload []byte) (LoginReqPacket, error) {
	r := bytes.NewReader(payload)
	var p LoginReqPacket
	var err error
	if p.Username, err = readString(r); err != nil { return p, err }
	if p.Password, err = readString(r); err != nil { return p, err }
	if p.Slot,     err = readUint8(r);  err != nil { return p, err }
	return p, nil
}

func DecodeRegisterReq(payload []byte) (RegisterReqPacket, error) {
	r := bytes.NewReader(payload)
	var p RegisterReqPacket
	var err error
	if p.Username, err = readString(r); err != nil { return p, err }
	if p.Password, err = readString(r); err != nil { return p, err }
	return p, nil
}

func DecodeJoinRoomReq(payload []byte) (JoinRoomReqPacket, error) {
	r := bytes.NewReader(payload)
	var p JoinRoomReqPacket
	var err error
	if p.RoomID, err = readString(r); err != nil { return p, err }
	return p, nil
}

func DecodeMoveInput(payload []byte) (MoveInputPacket, error) {
	r := bytes.NewReader(payload)
	var p MoveInputPacket
	var err error
	if p.PlayerID, err = readUint32(r); err != nil { return p, err }
	if p.DestX,    err = readFloat32(r); err != nil { return p, err }
	if p.DestY,    err = readFloat32(r); err != nil { return p, err }
	if p.DirX,     err = readFloat32(r); err != nil { return p, err }
	if p.DirY,     err = readFloat32(r); err != nil { return p, err }
	if p.Timestamp, err = readUint32(r); err != nil { return p, err }
	return p, nil
}

func DecodeMovePathPacket(payload []byte) (MovePathPacket, error) {
	r := bytes.NewReader(payload)
	var p MovePathPacket
	var err error

	if p.PlayerID, err = readUint32(r); err != nil { return p, err }

	count, err := readUint16(r)
	if err != nil { return p, err }
	if count > 64 { count = 64 } // giới hạn an toàn

	p.Waypoints = make([]WaypointVec2, 0, count)
	for i := 0; i < int(count); i++ {
		var x, y float32
		if x, err = readFloat32(r); err != nil { return p, err }
		if y, err = readFloat32(r); err != nil { return p, err }
		p.Waypoints = append(p.Waypoints, WaypointVec2{X: x, Y: y})
	}
	return p, nil
}

func DecodeAttackReq(payload []byte) (AttackReqPacket, error) {
	r := bytes.NewReader(payload)
	var p AttackReqPacket
	var err error
	if p.PlayerID,  err = readUint32(r);  err != nil { return p, err }
	if p.TargetID,  err = readUint32(r);  err != nil { return p, err }
	if p.DirX,      err = readFloat32(r); err != nil { return p, err }
	if p.DirY,      err = readFloat32(r); err != nil { return p, err }
	return p, nil
}

func DecodePing(payload []byte) (PingPacket, error) {
	r := bytes.NewReader(payload)
	var p PingPacket
	var err error
	if p.Timestamp, err = readUint32(r); err != nil { return p, err }
	return p, nil
}

func DecodeRespawnReq(payload []byte) (RespawnReqPacket, error) {
	r := bytes.NewReader(payload)
	var p RespawnReqPacket
	var err error
	if p.PlayerID, err = readUint32(r); err != nil { return p, err }
	return p, nil
}

func DecodeDashReq(payload []byte) (DashReqPacket, error) {
	r := bytes.NewReader(payload)
	var p DashReqPacket
	var err error

	if p.PlayerID, err = readUint32(r); err != nil { return p, err }
	if p.TotalDistance, err = readFloat32(r); err != nil { return p, err }

	count, err := readUint16(r)
	if err != nil { return p, err }
	if count > 16 { count = 16 } // safety cap

	p.Waypoints = make([]WaypointVec2, 0, count)
	for i := 0; i < int(count); i++ {
		var x, y float32
		if x, err = readFloat32(r); err != nil { return p, err }
		if y, err = readFloat32(r); err != nil { return p, err }
		p.Waypoints = append(p.Waypoints, WaypointVec2{X: x, Y: y})
	}
	return p, nil
}

// ── Helpers ───────────────────────────────────────────────────────────────────

func writePlayerInfo(buf *bytes.Buffer, p PlayerInfo) {
	writeUint32(buf, p.PlayerID)
	writeString(buf, p.Username)
	writeFloat32(buf, p.X)
	writeFloat32(buf, p.Y)
	writeUint16(buf, p.HP)
	writeUint16(buf, p.MaxHP)
	writeUint8(buf, p.JobClass)
}

func EncodeHitboxConfigAck(p HitboxConfigAckPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint8(buf, p.Shape)
	writeFloat32(buf, p.Radius)
	writeFloat32(buf, p.Width)
	writeFloat32(buf, p.Height)
	return EncodeFrame(TypeHitboxConfigAck, buf.Bytes())
}

// EncodeExpGain encodes an EXP gain notification (sent only to the killer).
func EncodeExpGain(p ExpGainPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.PlayerID)
	writeUint32(buf, p.ExpGained)
	writeUint32(buf, p.NewExp)
	writeUint32(buf, p.NewLevel)
	return EncodeFrame(TypeExpGain, buf.Bytes())
}

// EncodeLevelUp encodes a level-up notification (sent only to the killer).
func EncodeLevelUp(p LevelUpPacket) []byte {
	buf := &bytes.Buffer{}
	writeUint32(buf, p.PlayerID)
	writeUint32(buf, p.NewLevel)
	writeUint32(buf, p.NewExp)
	writeUint32(buf, p.NewSkillPoints)
	return EncodeFrame(TypeLevelUp, buf.Bytes())
}

// DecodeSpendSkillPointReq decodes the client request to spend a skill point.
func DecodeSpendSkillPointReq(payload []byte) (SpendSkillPointReqPacket, error) {
	r := bytes.NewReader(payload)
	statByte, err := r.ReadByte()
	if err != nil {
		return SpendSkillPointReqPacket{}, fmt.Errorf("SpendSkillPointReq: %w", err)
	}
	return SpendSkillPointReqPacket{StatType: StatUpgradeType(statByte)}, nil
}

// EncodeSpendSkillPointAck encodes the server response to a skill point spend request.
func EncodeSpendSkillPointAck(p SpendSkillPointAckPacket) []byte {
	buf := &bytes.Buffer{}
	success := uint8(0)
	if p.Success {
		success = 1
	}
	writeUint8(buf, success)
	writeUint8(buf, p.FailReason)
	writeUint32(buf, p.NewSkillPoints)
	writeUint16(buf, p.NewMaxHP)
	writeUint16(buf, p.NewMaxMP)
	writeUint16(buf, p.NewATKPhysical)
	writeUint16(buf, p.NewATKMagic)
	writeUint16(buf, p.NewDEFPhysical)
	writeUint16(buf, p.NewDEFMagic)
	return EncodeFrame(TypeSpendSkillPointAck, buf.Bytes())
}
