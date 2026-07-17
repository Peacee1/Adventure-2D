using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Shared constants and binary write helpers for the .mapdata format.
///
/// Binary layout:
///   Header (32 bytes): magic "MDAT", version uint16, mapID uint32, name [20]byte
///   Sections: each prefixed by SectionID uint8
///     0x01 = TILE    : OriginX/Y int32, Width/Height uint32, bitset []byte
///     0x02 = SPAWN   : Count uint32, [X float32, Y float32] ×N
///     0x03 = SAFEZONE: Count uint32, [MinX/Y MaxX/Y float32] ×N
///     0xFF = END
/// </summary>
public static class MapDataFormat
{
    // ── Magic & Version ───────────────────────────────────────────────────────
    public static readonly byte[] Magic   = Encoding.ASCII.GetBytes("MDAT");
    public const ushort           Version = 1;

    // ── Section IDs ───────────────────────────────────────────────────────────
    public const byte SectionTile     = 0x01;
    public const byte SectionSpawn    = 0x02;
    public const byte SectionSafeZone = 0x03;
    public const byte SectionEnd      = 0xFF;

    // ── Header ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the 32-byte file header.
    /// </summary>
    public static void WriteHeader(BinaryWriter w, uint mapID, string mapName)
    {
        w.Write(Magic);                         // 4 bytes
        w.Write(Version);                       // 2 bytes  (uint16 LE)
        w.Write(mapID);                         // 4 bytes  (uint32 LE)

        // Fixed-length name field: 20 bytes, null-terminated, zero-padded
        byte[] nameBytes = new byte[20];
        byte[] encoded   = Encoding.UTF8.GetBytes(mapName);
        int    copyLen   = Mathf.Min(encoded.Length, 19); // leave room for null terminator
        System.Array.Copy(encoded, nameBytes, copyLen);
        w.Write(nameBytes);                     // 20 bytes

        // Padding to reach 32-byte header (32 - 4 - 2 - 4 - 20 = 2 bytes reserved)
        w.Write((ushort)0);                     // 2 bytes reserved
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a section marker byte.
    /// </summary>
    public static void WriteSectionID(BinaryWriter w, byte id) => w.Write(id);
}
