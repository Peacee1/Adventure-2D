using System;

/// <summary>PacketType enum mirror với Go server.</summary>
public enum PacketType : ushort
{
    // Auth
    LoginReq    = 0x0001,
    LoginAck    = 0x0002,
    RegisterReq = 0x0003,
    RegisterAck = 0x0004,

    // Room
    JoinRoomReq  = 0x0010,
    JoinRoomAck  = 0x0011,
    PlayerJoined = 0x0012,
    PlayerLeft   = 0x0013,

    // Movement (UDP)
    MoveInput  = 0x0020,
    WorldState = 0x0021,

    // Combat
    AttackReq   = 0x0030,
    DamageEvent = 0x0031,
    DieEvent    = 0x0032,
    RespawnReq  = 0x0033,
    RespawnAck  = 0x0034,

    // System
    Ping = 0xFF00,
    Pong = 0xFF01,
}
