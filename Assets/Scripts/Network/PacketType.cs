using System;

/// <summary>PacketType enum mirroring the Go server.</summary>
public enum PacketType : ushort
{
    // Auth
    LoginReq    = 0x0001,
    LoginAck    = 0x0002,
    RegisterReq = 0x0003,
    RegisterAck = 0x0004,
    // 0x0005 = GuestLoginReq (server-side only)
    GetCharListReq = 0x0006, // C→S Fetch 3 character slots
    GetCharListAck = 0x0007, // S→C Character slot list (3 slots)

    // Room
    JoinRoomReq  = 0x0010,
    JoinRoomAck  = 0x0011,
    PlayerJoined = 0x0012,
    PlayerLeft   = 0x0013,

    // Movement (UDP + TCP)
    MoveInput  = 0x0020,  // C→S UDP: movement input every frame
    WorldState = 0x0021,  // S→C UDP: full world snapshot
    MovePath   = 0x0022,  // C→S TCP: NavMesh path waypoints
    DashReq    = 0x0023,  // C→S TCP: Dash request

    // Combat
    AttackReq        = 0x0030,
    DamageEvent      = 0x0031,
    DieEvent         = 0x0032,
    RespawnReq       = 0x0033,
    RespawnAck       = 0x0034,
    ProjectileSpawn   = 0x0035, // S→C: projectile spawned
    ProjectileState   = 0x0036, // S→C: batch position update per tick
    ProjectileDestroy = 0x0037, // S→C: projectile removed (hit or out-of-range)
    HitboxConfigReq   = 0x003e, // C→S: request hitbox config
    HitboxConfigAck   = 0x003f, // S→C: receive hitbox config

    // System
    Ping = 0xFF00,
    Pong = 0xFF01,
}
