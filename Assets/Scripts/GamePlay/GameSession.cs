using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GameSession — singleton DontDestroyOnLoad lưu state nhận từ server.
/// Bridge dữ liệu giữa MenuScene và SampleScene.
///
/// SRP: chỉ chứa data, không chứa logic.
/// </summary>
public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    // ─── Player Info (từ server) ──────────────────────────────────────────────

    public uint     PlayerID  { get; private set; }
    public JobClass JobClass  { get; private set; }
    public string   Username  { get; private set; }
    public ushort   HP        { get; private set; }
    public ushort   MaxHP     { get; private set; }
    public float    SpawnX    { get; private set; }
    public float    SpawnY    { get; private set; }

    // ─── Level ────────────────────────────────────────────────────────────────

    public int      Level     { get; private set; } = 1;
    public int      CurrentExp{ get; private set; } = 0;

    // ─── Map ──────────────────────────────────────────────────────────────────

    /// <summary>Tên scene Unity cần load. Mặc định "Map1" cho player mới.</summary>
    public string   MapName   { get; private set; } = "Map0";

    // ─── Room ────────────────────────────────────────────────────────────────

    public string          RoomID                 { get; private set; }

    /// <summary>Danh sách players trong phòng tại thời điểm join (từ JoinRoomAck).</summary>
    public List<PlayerInfo> ExistingPlayersAtJoin  { get; private set; } = new();

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Gọi từ MenuScene sau khi nhận LoginAck thành công.</summary>
    public void SetPlayerInfo(uint playerID, byte jobClassByte, string username, ushort hp, ushort maxHP, float x, float y)
    {
        PlayerID = playerID;
        JobClass = ByteToJobClass(jobClassByte);
        Username = username;
        HP       = hp;
        MaxHP    = maxHP > 0 ? maxHP : hp;
        SpawnX   = x;
        SpawnY   = y;

        Debug.Log($"[GameSession] SetPlayerInfo: ID={playerID} Job={JobClass} User={username} HP={hp}/{MaxHP} Pos=({x},{y})");
    }

    /// <summary>Gọi khi JoinRoom thành công. Lưu danh sách players hiện có để Bootstrap spawn.</summary>
    public void SetRoom(string roomID, List<PlayerInfo> existingPlayers)
    {
        RoomID               = roomID;
        ExistingPlayersAtJoin = existingPlayers ?? new List<PlayerInfo>();
        Debug.Log($"[GameSession] SetRoom: {roomID} (existing={ExistingPlayersAtJoin.Count})");
    }

    /// <summary>Gọi từ LevelSystem khi level/EXP thay đổi.</summary>
    public void SetLevel(int level, int exp)
    {
        Level      = level;
        CurrentExp = exp;
        Debug.Log($"[GameSession] SetLevel: {level} EXP={exp}");
    }

    /// <summary>Gọi sau LoginAck để lưu tên scene cần load.</summary>
    public void SetMapName(string mapName)
    {
        if (!string.IsNullOrEmpty(mapName))
            MapName = mapName;
        Debug.Log($"[GameSession] SetMapName: {MapName}");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Trả về path prefab trong Resources theo JobClass.
    /// Mặc định là Archer nếu JobClass không xác định.
    /// </summary>
    public string GetPrefabPath()
    {
        return JobClass switch
        {
            JobClass.Archer   => "Prefab/Archer",
            JobClass.Warrior  => "Prefab/Warrior",
            JobClass.Mage     => "Prefab/Mage",
            JobClass.Healer   => "Prefab/Healer",
            JobClass.Assassin => "Prefab/Assassin",
            JobClass.Tank     => "Prefab/Tank",
            _                 => "Prefab/Archer",
        };
    }

    /// <summary>Trả về path prefab remote player theo jobClass byte từ server.</summary>
    public static string GetPrefabPathForJob(byte jobClassByte)
    {
        return ByteToJobClass(jobClassByte) switch
        {
            JobClass.Archer   => "Prefab/Archer",
            JobClass.Warrior  => "Prefab/Warrior",
            JobClass.Mage     => "Prefab/Mage",
            JobClass.Healer   => "Prefab/Healer",
            JobClass.Assassin => "Prefab/Assassin",
            JobClass.Tank     => "Prefab/Tank",
            _                 => "Prefab/Archer",
        };
    }

    /// <summary>
    /// Convert byte từ server sang JobClass enum Unity.
    /// Server: Warrior=0, Archer=1, Mage=2, Healer=3, Assassin=4, Tank=5
    /// Unity:  Archer=0,  Warrior=1, Mage=2, Healer=3, Assassin=4, Tank=5
    /// </summary>
    private static JobClass ByteToJobClass(byte b)
    {
        return b switch
        {
            0 => JobClass.Warrior,
            1 => JobClass.Archer,
            2 => JobClass.Mage,
            3 => JobClass.Healer,
            4 => JobClass.Assassin,
            5 => JobClass.Tank,
            _ => JobClass.Archer,
        };
    }
}
