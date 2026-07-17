/// <summary>
/// ProjectileSpawnPacket — decoded from server's ProjectileSpawn packet (0x0035).
/// Server broadcasts this when any player fires a projectile.
/// Client uses this to instantiate the visual projectile.
/// </summary>
public struct ProjectileSpawnPacket
{
    public uint   OwnerID;  // ID of the player who fired
    public float  X;        // spawn position X
    public float  Y;        // spawn position Y
    public float  DirX;     // normalized direction X
    public float  DirY;     // normalized direction Y
    public float  Speed;    // flight speed (units/sec)
    public float  Range;    // max travel distance before self-destruct
    public byte   ProjType; // 0 = Arrow, 1 = Spell, ...
}
