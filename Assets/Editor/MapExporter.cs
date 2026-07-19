#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Unity Editor tool that exports static map data to a .mapdata binary file.
///
/// Usage: Unity menu → Tools → Export Map Data
///
/// What is exported:
///   - TILE section   : Ground tilemap walkability bitset
///   - SPAWN section  : GameObjects tagged "SpawnPoint" in the scene
///
/// Output: Assets/MapData/{SceneName}.mapdata
/// </summary>
public static class MapExporter
{
    private const string OutputDir = "Assets/MapData";

    // ── Menu Entry ────────────────────────────────────────────────────────────

    [MenuItem("Tools/Export Map Data")]
    public static void ExportCurrentScene()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Export(sceneName);
    }

    [MenuItem("Tools/Export Map Data", true)]
    private static bool ValidateExport()
    {
        // Only available in Play-mode-off (Edit mode) to avoid live-scene issues
        return !EditorApplication.isPlaying;
    }

    // ── Core Export ───────────────────────────────────────────────────────────

    /// <summary>
    /// Exports tile + spawn data for the given scene name.
    /// </summary>
    public static void Export(string sceneName)
    {
        Debug.Log($"[MapExporter] Exporting map: {sceneName}");

        // ── 1. Find Ground tilemap ────────────────────────────────────────────
        Tilemap ground = FindGroundTilemap();
        if (ground == null)
        {
            EditorUtility.DisplayDialog("MapExporter Error",
                "Could not find a GameObject tagged 'Ground' with a Tilemap component.\n\n" +
                "Steps to fix:\n" +
                "1. Edit → Project Settings → Tags & Layers → add tag 'Ground'\n" +
                "2. Select the Ground GameObject in the hierarchy\n" +
                "3. Set its Tag to 'Ground'\n" +
                "4. Re-run Export Map Data",
                "OK");
            return;
        }

        // ── 2. Gather spawn points ────────────────────────────────────────────
        List<Vector2> spawnPoints = GatherSpawnPoints();
        if (spawnPoints.Count == 0)
        {
            Debug.LogWarning("[MapExporter] No GameObjects tagged 'SpawnPoint' found. " +
                             "SPAWN section will be empty. Add GameObjects with tag 'SpawnPoint' to the scene.");
        }

        // ── 3. Build output path ──────────────────────────────────────────────
        if (!Directory.Exists(OutputDir))
            Directory.CreateDirectory(OutputDir);

        string outputPath = Path.Combine(OutputDir, $"{sceneName}.mapdata");

        // ── 4. Write binary ───────────────────────────────────────────────────
        uint mapID = (uint)Mathf.Abs(sceneName.GetHashCode() % 100000);

        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        using (var w  = new BinaryWriter(fs))
        {
            MapDataFormat.WriteHeader(w, mapID, sceneName);
            WriteTileSection(w, ground);
            WriteSpawnSection(w, spawnPoints);
            MapDataFormat.WriteSectionID(w, MapDataFormat.SectionEnd);
        }

        AssetDatabase.Refresh();

        long fileSize = new FileInfo(outputPath).Length;
        string msg = $"Map '{sceneName}' exported successfully!\n\n" +
                     $"Tiles:        {CountTiles(ground)}\n" +
                     $"Spawn points: {spawnPoints.Count}\n" +
                     $"Output:       {outputPath}\n" +
                     $"File size:    {fileSize} bytes";

        Debug.Log($"[MapExporter] {msg.Replace('\n', ' ')}");
        EditorUtility.DisplayDialog("MapExporter — Done", msg, "OK");
    }

    // ── Section Writers ───────────────────────────────────────────────────────

    /// <summary>
    /// Writes the TILE section at WORLD-UNIT resolution.
    ///
    /// Because the Grid cell size is non-integer (X=4, Y=3.7), one tile does NOT
    /// equal one world unit. This method samples every 1×1 world-unit square by
    /// probing its center through WorldToCell, then checking HasTile.
    ///
    /// Layout:
    ///   SectionID uint8
    ///   OriginX   int32   (world-space X of the leftmost column)
    ///   OriginY   int32   (world-space Y of the bottom row)
    ///   Width     uint32  (world-unit columns)
    ///   Height    uint32  (world-unit rows)
    ///   ByteCount uint32
    ///   Data      []byte  (row-major, 1 bit per world-unit; 1=walkable)
    ///
    /// The server's IsWalkable(wx,wy) floors world coords then subtracts OriginX/Y
    /// to get column/row — this matches exactly.
    /// </summary>
    private static void WriteTileSection(BinaryWriter w, Tilemap ground)
    {
        BoundsInt cellBounds = ground.cellBounds;

        // Compute world-space bounding box of the entire tilemap
        Vector3 worldMin = ground.CellToWorld(new Vector3Int(cellBounds.xMin, cellBounds.yMin, 0));
        Vector3 worldMax = ground.CellToWorld(new Vector3Int(cellBounds.xMax, cellBounds.yMax, 0));

        int originX    = Mathf.FloorToInt(worldMin.x);
        int originY    = Mathf.FloorToInt(worldMin.y);
        int worldMaxX  = Mathf.CeilToInt(worldMax.x);
        int worldMaxY  = Mathf.CeilToInt(worldMax.y);
        int worldWidth  = worldMaxX - originX;
        int worldHeight = worldMaxY - originY;

        Debug.Log($"[MapExporter] Cell bounds: ({cellBounds.xMin},{cellBounds.yMin}) size=({cellBounds.size.x}x{cellBounds.size.y})");
        Debug.Log($"[MapExporter] World bounds: X[{originX}..{worldMaxX}] Y[{originY}..{worldMaxY}] ({worldWidth}x{worldHeight} world units)");

        // Build bitset at world-unit resolution.
        // For each 1×1 world-unit square, sample its center to find the cell.
        int    totalBits = worldWidth * worldHeight;
        int    byteCount = (totalBits + 7) / 8;
        byte[] bitset    = new byte[byteCount];

        int bitIndex = 0;
        int walkableCount = 0;
        for (int wy = originY; wy < worldMaxY; wy++)
        {
            for (int wx = originX; wx < worldMaxX; wx++)
            {
                // Sample center of this world unit to determine which cell it belongs to
                Vector3    samplePos = new Vector3(wx + 0.5f, wy + 0.5f, 0f);
                Vector3Int cell      = ground.WorldToCell(samplePos);

                if (ground.HasTile(cell))
                {
                    bitset[bitIndex / 8] |= (byte)(1 << (bitIndex % 8));
                    walkableCount++;
                }
                bitIndex++;
            }
        }

        MapDataFormat.WriteSectionID(w, MapDataFormat.SectionTile);
        w.Write(originX);
        w.Write(originY);
        w.Write((uint)worldWidth);
        w.Write((uint)worldHeight);
        w.Write((uint)byteCount);
        w.Write(bitset);

        Debug.Log($"[MapExporter] TILE section: origin=({originX},{originY}) " +
                  $"size=({worldWidth}x{worldHeight} world units) " +
                  $"walkable={walkableCount}/{totalBits} tiles={CountTiles(ground)} bytes={byteCount}");
    }

    /// <summary>
    /// Writes the SPAWN section.
    /// Each spawn point is a world-space float32 X,Y pair.
    /// </summary>
    private static void WriteSpawnSection(BinaryWriter w, List<Vector2> points)
    {
        MapDataFormat.WriteSectionID(w, MapDataFormat.SectionSpawn);
        w.Write((uint)points.Count);
        foreach (var p in points)
        {
            w.Write(p.x);
            w.Write(p.y);
        }
        Debug.Log($"[MapExporter] SPAWN section: {points.Count} spawn points");
    }

    // ── Scene Helpers ─────────────────────────────────────────────────────────

    private static Tilemap FindGroundTilemap()
    {
        // Find the GameObject tagged "Ground" that has a Tilemap component.
        // Tag-based lookup is preferred over name-based lookup because
        // renaming the GameObject won't break the exporter.
        try
        {
            GameObject[] tagged = GameObject.FindGameObjectsWithTag("Ground");
            foreach (var go in tagged)
            {
                Tilemap tm = go.GetComponent<Tilemap>();
                if (tm != null)
                    return tm;
            }
        }
        catch (UnityException)
        {
            Debug.LogWarning("[MapExporter] Tag 'Ground' is not defined in TagManager. " +
                             "Create it via Edit → Project Settings → Tags and Layers, " +
                             "then assign it to the Ground GameObject.");
        }
        return null;
    }

    private static List<Vector2> GatherSpawnPoints()
    {
        var result = new List<Vector2>();
        // Collect all GameObjects tagged "SpawnPoint"
        try
        {
            GameObject[] tagged = GameObject.FindGameObjectsWithTag("SpawnPoint");
            foreach (var go in tagged)
                result.Add(go.transform.position);
        }
        catch (UnityException)
        {
            // Tag "SpawnPoint" not defined — return empty list
            Debug.LogWarning("[MapExporter] Tag 'SpawnPoint' is not defined in TagManager. " +
                             "Create it via Edit → Project Settings → Tags and Layers.");
        }
        return result;
    }

    private static int CountTiles(Tilemap tm)
    {
        int count = 0;
        foreach (var pos in tm.cellBounds.allPositionsWithin)
            if (tm.HasTile(pos)) count++;
        return count;
    }
}
#endif
