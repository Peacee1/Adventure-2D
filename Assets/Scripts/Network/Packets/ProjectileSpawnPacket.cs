/// <summary>
/// ProjectileSpawnPacket — decoded from server's ProjectileSpawn packet (0x0035).
/// Server broadcasts this when any player fires a projectile.
/// Client uses this to instantiate the visual bullet; position is then driven by ProjectileState.
/// </summary>
public struct ProjectileSpawnPacket
{
    public uint   ProjID;   // unique projectile ID — correlates State and Destroy packets
    public uint   OwnerID;  // ID of the player who fired
    public float  X;        // spawn position X
    public float  Y;        // spawn position Y
    public float  DirX;     // normalized direction X
    public float  DirY;     // normalized direction Y
    public float  Speed;    // flight speed (units/sec) — informational
    public float  Range;    // max travel distance — informational
    public byte   ProjType; // 0 = Arrow, 1 = Spell, ...
}

/// <summary>ProjectileStatePacket — one entry in a batch position update from server.</summary>
public struct ProjectileStateEntry { public uint ProjID; public float X, Y; }
public struct ProjectileStatePacket  { public ProjectileStateEntry[] Projectiles; }

/// <summary>ProjectileDestroyPacket — server removed this projectile (hit or out of range).</summary>
public struct ProjectileDestroyPacket { public uint ProjID; }
