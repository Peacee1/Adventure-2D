using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// PacketEncoder — encode packet structs thành binary (Little-Endian).
/// Mirror chính xác với Go encoder.go.
/// </summary>
public static class PacketEncoder
{
    private static readonly byte[] _hdr = new byte[4];

    // ── Frame wrapper ─────────────────────────────────────────────────────────

    private static byte[] MakeFrame(PacketType pType, byte[] payload)
    {
        byte[] frame = new byte[4 + payload.Length];
        BitConverter.GetBytes((ushort)pType).CopyTo(frame, 0);
        BitConverter.GetBytes((ushort)payload.Length).CopyTo(frame, 2);
        payload.CopyTo(frame, 4);
        return frame;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    public static byte[] EncodeLoginReq(string username, string password, byte slot)
    {
        using var ms = new MemoryStream();
        WriteString(ms, username);
        WriteString(ms, password);
        ms.WriteByte(slot);
        return MakeFrame(PacketType.LoginReq, ms.ToArray());
    }

    public static byte[] EncodeRegisterReq(string username, string password)
    {
        using var ms = new MemoryStream();
        WriteString(ms, username);
        WriteString(ms, password);
        return MakeFrame(PacketType.RegisterReq, ms.ToArray());
    }

    /// <summary>
    /// Encode GetCharListReq — payload rỗng, server lấy AccountID từ session.
    /// Gọi sau khi LoginAck thành công và trước khi chọn slot.
    /// </summary>
    public static byte[] EncodeGetCharListReq()
    {
        return MakeFrame(PacketType.GetCharListReq, new byte[0]);
    }

    // ── Room ──────────────────────────────────────────────────────────────────

    public static byte[] EncodeJoinRoomReq(string roomID)
    {
        using var ms = new MemoryStream();
        WriteString(ms, roomID);
        return MakeFrame(PacketType.JoinRoomReq, ms.ToArray());
    }

    // ── Movement (UDP) ────────────────────────────────────────────────────────

    public static byte[] EncodeMoveInput(uint playerID, Vector2 dest, Vector2 dir, uint timestamp)
    {
        using var ms = new MemoryStream();
        WriteUint32(ms, playerID);
        WriteFloat32(ms, dest.x);
        WriteFloat32(ms, dest.y);
        WriteFloat32(ms, dir.x);
        WriteFloat32(ms, dir.y);
        WriteUint32(ms, timestamp);
        return MakeFrame(PacketType.MoveInput, ms.ToArray());
    }

    /// <summary>
    /// Encode NavMesh path waypoints (corners) để gửi lên server qua TCP.
    /// Server sẽ di chuyển player theo đúng các waypoints này.
    /// Format: [PlayerID:uint32][Count:uint16][X:float32,Y:float32 x Count]
    /// </summary>
    public static byte[] EncodeMovePathPacket(uint playerID, Vector3[] corners)
    {
        int count = Mathf.Min(corners.Length, 64); // tối đa 64 waypoints
        using var ms = new MemoryStream();
        WriteUint32(ms, playerID);
        WriteUint16(ms, (ushort)count);
        for (int i = 0; i < count; i++)
        {
            WriteFloat32(ms, corners[i].x);
            WriteFloat32(ms, corners[i].y);
        }
        return MakeFrame(PacketType.MovePath, ms.ToArray());
    }

    // ── Combat ────────────────────────────────────────────────────────────────

    public static byte[] EncodeAttackReq(uint playerID, uint targetID, Vector2 dir)
    {
        using var ms = new MemoryStream();
        WriteUint32(ms, playerID);
        WriteUint32(ms, targetID);
        WriteFloat32(ms, dir.x);
        WriteFloat32(ms, dir.y);
        return MakeFrame(PacketType.AttackReq, ms.ToArray());
    }

    /// <summary>
    /// Encodes a DashReq with a NavMesh-computed path.
    /// Format: [PlayerID:uint32][TotalDistance:float32][Count:uint16][X:float32,Y:float32 × Count]
    /// </summary>
    public static byte[] EncodeDashReq(uint playerID, Vector3[] waypoints, float totalDistance)
    {
        int count = Mathf.Min(waypoints.Length, 16);
        using var ms = new MemoryStream();
        WriteUint32(ms, playerID);
        WriteFloat32(ms, totalDistance);
        WriteUint16(ms, (ushort)count);
        for (int i = 0; i < count; i++)
        {
            WriteFloat32(ms, waypoints[i].x);
            WriteFloat32(ms, waypoints[i].y);
        }
        return MakeFrame(PacketType.DashReq, ms.ToArray());
    }

    public static byte[] EncodeRespawnReq(uint playerID)
    {
        using var ms = new MemoryStream();
        WriteUint32(ms, playerID);
        return MakeFrame(PacketType.RespawnReq, ms.ToArray());
    }

    // ── System ────────────────────────────────────────────────────────────────

    public static byte[] EncodePing(uint timestamp)
    {
        using var ms = new MemoryStream();
        WriteUint32(ms, timestamp);
        return MakeFrame(PacketType.Ping, ms.ToArray());
    }

    public static byte[] EncodeHitboxConfigReq()
    {
        return MakeFrame(PacketType.HitboxConfigReq, new byte[0]);
    }

    // ── Write helpers ─────────────────────────────────────────────────────────

    private static void WriteUint8(MemoryStream ms, byte v)     => ms.WriteByte(v);
    private static void WriteUint16(MemoryStream ms, ushort v)  => ms.Write(BitConverter.GetBytes(v), 0, 2);
    private static void WriteUint32(MemoryStream ms, uint v)    => ms.Write(BitConverter.GetBytes(v), 0, 4);
    private static void WriteFloat32(MemoryStream ms, float v)  => ms.Write(BitConverter.GetBytes(v), 0, 4);
    private static void WriteString(MemoryStream ms, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
        WriteUint16(ms, (ushort)bytes.Length);
        ms.Write(bytes, 0, bytes.Length);
    }
}

/// <summary>
/// PacketDecoder — decode binary payload thành packet structs (Little-Endian).
/// </summary>
public static class PacketDecoder
{
    // ── Frame reader (TCP) ────────────────────────────────────────────────────

    public static (ushort pType, byte[] payload) ReadFrame(NetworkStream stream)
    {
        byte[] header = ReadExact(stream, 4);
        ushort pType  = BitConverter.ToUInt16(header, 0);
        ushort payLen = BitConverter.ToUInt16(header, 2);
        byte[] payload = payLen > 0 ? ReadExact(stream, payLen) : Array.Empty<byte>();
        return (pType, payload);
    }

    private static byte[] ReadExact(NetworkStream stream, int count)
    {
        byte[] buf = new byte[count];
        int total = 0;
        while (total < count)
        {
            int n = stream.Read(buf, total, count - total);
            if (n == 0) throw new EndOfStreamException();
            total += n;
        }
        return buf;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    public struct LoginAckData
    {
        public bool   Success;
        public uint   PlayerID;
        public byte   JobClass;
        public ushort Level;
        public uint   Exp;
        public ushort HP;
        public ushort MaxHP;
        public float  X;
        public float  Y;
        public string MapName;
        public string CharName;
        public string Message;

        // ── Combat stats for StatsManager ────────────────────────
        public ushort MaxMP;
        public ushort ATKPhysical;
        public ushort ATKMagic;
        public ushort DEFPhysical;
        public ushort DEFMagic;
        public uint   SkillPoints;
        public float  CritRate;    // 0.0–1.0
        public float  LifeSteal;   // 0.0–1.0
        public float  AttackSpeed; // seconds per attack (e.g. 0.2s)
    }

    public static LoginAckData DecodeLoginAck(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var r  = new BinaryReader(ms);

        var data = new LoginAckData
        {
            Success      = r.ReadByte() != 0,
            PlayerID     = r.ReadUInt32(),
            JobClass     = r.ReadByte(),
            Level        = r.ReadUInt16(),
            Exp          = r.ReadUInt32(),
            HP           = r.ReadUInt16(),
            MaxHP        = r.ReadUInt16(),
            X            = r.ReadSingle(),
            Y            = r.ReadSingle(),
            MapName      = ReadString(r),
            CharName     = ReadString(r),
            Message      = ReadString(r),
        };

        // Combat stats — only present in servers that support the extended LoginAck.
        // Guard against end-of-stream to stay backward-compatible.
        if (ms.Position < ms.Length) data.MaxMP       = r.ReadUInt16();
        if (ms.Position < ms.Length) data.ATKPhysical = r.ReadUInt16();
        if (ms.Position < ms.Length) data.ATKMagic    = r.ReadUInt16();
        if (ms.Position < ms.Length) data.DEFPhysical = r.ReadUInt16();
        if (ms.Position < ms.Length) data.DEFMagic    = r.ReadUInt16();
        if (ms.Position < ms.Length) data.SkillPoints = r.ReadUInt32();
        if (ms.Position < ms.Length) data.CritRate    = r.ReadSingle();
        if (ms.Position < ms.Length) data.LifeSteal   = r.ReadSingle();
        if (ms.Position < ms.Length) data.AttackSpeed = r.ReadSingle();

        return data;
    }

    public static RegisterAckData DecodeRegisterAck(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return new RegisterAckData
        {
            Success = r.ReadByte() != 0,
            Message = ReadString(r),
        };
    }

    /// <summary>
    /// Giải mã GetCharListAck từ server.
    /// Format payload: [count:uint8][slot:uint8][exists:bool][charName:str][jobClass:uint8][level:uint16] × count
    /// </summary>
    public static CharacterData[] DecodeGetCharListAck(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        byte count = r.ReadByte();

        var result = new CharacterData[count];
        for (int i = 0; i < count; i++)
        {
            byte   slot     = r.ReadByte();
            bool   exists   = r.ReadByte() != 0;
            string charName = ReadString(r);
            byte   jobClass = r.ReadByte();
            ushort level    = r.ReadUInt16();

            result[i] = new CharacterData(slot, exists, charName, jobClass, level);
        }
        return result;
    }

    // ── Room ──────────────────────────────────────────────────────────────────

    public static JoinRoomAckData DecodeJoinRoomAck(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        var result = new JoinRoomAckData
        {
            Success = r.ReadByte() != 0,
            RoomID  = ReadString(r),
        };
        ushort count = r.ReadUInt16();
        result.ExistingPlayers = new List<PlayerInfo>(count);
        for (int i = 0; i < count; i++)
            result.ExistingPlayers.Add(ReadPlayerInfo(r));
        return result;
    }

    public static PlayerInfo DecodePlayerJoined(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return ReadPlayerInfo(r);
    }

    public static uint DecodePlayerLeft(byte[] payload)
    {
        return BitConverter.ToUInt32(payload, 0);
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    public static WorldStatePacket DecodeWorldState(byte[] payload)
    {
        using var r  = new BinaryReader(new MemoryStream(payload));
        uint tick    = r.ReadUInt32();
        ushort pCount = r.ReadUInt16();
        var players  = new PlayerSnapshot[pCount];
        for (int i = 0; i < pCount; i++)
        {
            players[i] = new PlayerSnapshot
            {
                PlayerID = r.ReadUInt32(),
                X        = r.ReadSingle(),
                Y        = r.ReadSingle(),
                DirX     = r.ReadSingle(),
                DirY     = r.ReadSingle(),
                HP       = r.ReadUInt16(),
                State    = r.ReadByte(),
            };
        }

        // Decode monster snapshots (appended after players — backward-compatible)
        MonsterSnapshot[] monsters = System.Array.Empty<MonsterSnapshot>();
        if (r.BaseStream.Position < r.BaseStream.Length)
        {
            ushort mCount = r.ReadUInt16();
            monsters = new MonsterSnapshot[mCount];
            for (int i = 0; i < mCount; i++)
            {
                monsters[i] = new MonsterSnapshot
                {
                    ID    = r.ReadUInt32(),
                    X     = r.ReadSingle(),
                    Y     = r.ReadSingle(),
                    HP    = r.ReadUInt16(),
                    State = r.ReadByte(),
                };
            }
        }

        return new WorldStatePacket { Tick = tick, Players = players, Monsters = monsters };
    }

    // ── Combat ────────────────────────────────────────────────────────────────

    public static DamageEventPacket DecodeDamageEvent(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return new DamageEventPacket
        {
            AttackerID  = r.ReadUInt32(),
            TargetID    = r.ReadUInt32(),
            Damage      = r.ReadUInt32(),
            RemainingHP = r.ReadUInt16(),
            IsCrit      = r.ReadByte() != 0,
        };
    }

    public static DieEventPacket DecodeDieEvent(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return new DieEventPacket
        {
            PlayerID = r.ReadUInt32(),
            KillerID = r.ReadUInt32(),
        };
    }

    public static RespawnAckPacket DecodeRespawnAck(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return new RespawnAckPacket
        {
            PlayerID = r.ReadUInt32(),
            X        = r.ReadSingle(),
            Y        = r.ReadSingle(),
            HP       = r.ReadUInt16(),
        };
    }

    public static ProjectileSpawnPacket DecodeProjectileSpawn(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return new ProjectileSpawnPacket
        {
            ProjID   = r.ReadUInt32(),
            OwnerID  = r.ReadUInt32(),
            X        = r.ReadSingle(),
            Y        = r.ReadSingle(),
            DirX     = r.ReadSingle(),
            DirY     = r.ReadSingle(),
            Speed    = r.ReadSingle(),
            Range    = r.ReadSingle(),
            ProjType = r.ReadByte(),
        };
    }

    /// <summary>Decode a batch ProjectileState packet: [count uint16][ProjID,X,Y] × count.</summary>
    public static ProjectileStatePacket DecodeProjectileState(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        ushort count = r.ReadUInt16();
        var entries  = new ProjectileStateEntry[count];
        for (int i = 0; i < count; i++)
            entries[i] = new ProjectileStateEntry { ProjID = r.ReadUInt32(), X = r.ReadSingle(), Y = r.ReadSingle() };
        return new ProjectileStatePacket { Projectiles = entries };
    }

    /// <summary>Decode a ProjectileDestroy packet: [ProjID uint32].</summary>
    public static ProjectileDestroyPacket DecodeProjectileDestroy(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return new ProjectileDestroyPacket { ProjID = r.ReadUInt32() };
    }

    // ── Read helpers ──────────────────────────────────────────────────────────

    private static string ReadString(BinaryReader r)
    {
        ushort len  = r.ReadUInt16();
        byte[] bytes = r.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static PlayerInfo ReadPlayerInfo(BinaryReader r)
    {
        return new PlayerInfo
        {
            PlayerID = r.ReadUInt32(),
            Username = ReadString(r),
            X        = r.ReadSingle(),
            Y        = r.ReadSingle(),
            HP       = r.ReadUInt16(),
            MaxHP    = r.ReadUInt16(),
            JobClass = r.ReadByte(),
        };
    }

    public struct HitboxConfigPacket
    {
        public byte Shape; // 0 = Circle, 1 = Box
        public float Radius;
        public float Width;
        public float Height;
    }

    public static HitboxConfigPacket DecodeHitboxConfig(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return new HitboxConfigPacket
        {
            Shape = r.ReadByte(),
            Radius = r.ReadSingle(),
            Width = r.ReadSingle(),
            Height = r.ReadSingle(),
        };
    }

    // ── EXP / Level ───────────────────────────────────────────────────────────

    public struct ExpGainPacket
    {
        public uint PlayerID;
        public uint ExpGained;  // EXP awarded this kill
        public uint NewExp;     // current EXP within the level after gain
        public uint NewLevel;   // current level (may be same or higher if leveled up)
    }

    public static ExpGainPacket DecodeExpGain(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return new ExpGainPacket
        {
            PlayerID  = r.ReadUInt32(),
            ExpGained = r.ReadUInt32(),
            NewExp    = r.ReadUInt32(),
            NewLevel  = r.ReadUInt32(),
        };
    }

    public struct LevelUpPacket
    {
        public uint PlayerID;
        public uint NewLevel;
        public uint NewExp;
        public uint NewSkillPoints; // total skill points after level-up
    }

    public static LevelUpPacket DecodeLevelUp(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        return new LevelUpPacket
        {
            PlayerID       = r.ReadUInt32(),
            NewLevel       = r.ReadUInt32(),
            NewExp         = r.ReadUInt32(),
            NewSkillPoints = r.ReadUInt32(),
        };
    }

    // ── Spend Skill Point Packets ──────────────────────────────────────────

    public struct SpendSkillPointAckPacket
    {
        public bool   Success;
        public byte   FailReason;
        public uint   NewSkillPoints;
        public ushort NewMaxHP;
        public ushort NewMaxMP;
        public ushort NewATKPhysical;
        public ushort NewATKMagic;
        public ushort NewDEFPhysical;
        public ushort NewDEFMagic;
    }

    public static byte[] EncodeSpendSkillPointReq(byte statType)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.Write(statType); // 0=HP, 1=MP, 2=ATKPhy, 3=ATKMag, 4=DEF
        return EncodeFrame((ushort)PacketType.SpendSkillPointReq, ms.ToArray());
    }

    public static SpendSkillPointAckPacket DecodeSpendSkillPointAck(byte[] payload)
    {
        using var r = new BinaryReader(new MemoryStream(payload));
        bool success = r.ReadByte() != 0;
        byte reason  = r.ReadByte();
        if (!success)
        {
            return new SpendSkillPointAckPacket
            {
                Success    = false,
                FailReason = reason
            };
        }
        return new SpendSkillPointAckPacket
        {
            Success        = true,
            FailReason     = reason,
            NewSkillPoints = r.ReadUInt32(),
            NewMaxHP       = r.ReadUInt16(),
            NewMaxMP       = r.ReadUInt16(),
            NewATKPhysical = r.ReadUInt16(),
            NewATKMagic    = r.ReadUInt16(),
            NewDEFPhysical = r.ReadUInt16(),
            NewDEFMagic    = r.ReadUInt16()
        };
    }
}
