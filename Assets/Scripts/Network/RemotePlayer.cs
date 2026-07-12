using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RemotePlayer — đại diện cho player khác trên màn hình local.
///
/// Nhận WorldState từ NetworkManager mỗi tick (60Hz server, ~16ms).
/// Dùng interpolation để di chuyển mượt giữa các WorldState snapshot.
///
/// SRP: chỉ xử lý visual representation của remote player.
/// </summary>
public class RemotePlayer : MonoBehaviour
{
    // ─── State ────────────────────────────────────────────────────────────────

    public uint PlayerID { get; private set; }
    public string Username { get; private set; }

    // Interpolation
    private Vector2 targetPosition;
    private Vector2 currentPosition;
    private float   interpolationSpeed = 15f;

    // Component refs
    private SpriteRenderer spriteRenderer;
    private Animator       animator;

    // ─── Init ─────────────────────────────────────────────────────────────────

    public void Initialize(PlayerInfo info)
    {
        PlayerID = info.PlayerID;
        Username = info.Username;

        currentPosition = new Vector2(info.X, info.Y);
        targetPosition  = currentPosition;
        transform.position = new Vector3(currentPosition.x, currentPosition.y, 0f);

        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        animator       = GetComponentInChildren<Animator>();
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Update()
    {
        // Smooth interpolation đến target position
        currentPosition = Vector2.Lerp(currentPosition, targetPosition, interpolationSpeed * Time.deltaTime);
        transform.position = new Vector3(currentPosition.x, currentPosition.y, transform.position.z);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Cập nhật từ WorldState packet (gọi từ NetworkManager event).</summary>
    public void ApplySnapshot(PlayerSnapshot snap)
    {
        targetPosition = new Vector2(snap.X, snap.Y);

        // Flip hướng nhìn
        if (snap.DirX < -0.01f)
            transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        else if (snap.DirX > 0.01f)
            transform.localRotation = Quaternion.identity;

        // Cập nhật animator
        if (animator != null)
        {
            float speed = (snap.State == 1) ? 1f : 0f; // 1=Move
            animator.SetFloat("speed", speed);
        }
    }
}

/// <summary>
/// RemotePlayerManager — quản lý tất cả remote players trên màn hình.
/// Lắng nghe NetworkManager events và spawn/despawn RemotePlayer GameObject.
/// </summary>
public class RemotePlayerManager : MonoBehaviour
{
    [SerializeField] private GameObject remotePlayerPrefab;

    private readonly Dictionary<uint, RemotePlayer> remotePlayers = new();

    private void OnEnable()
    {
        if (NetworkManager.Instance == null) return;

        NetworkManager.Instance.OnJoinRoomSuccess += HandleJoinRoom;
        NetworkManager.Instance.OnPlayerJoined    += HandlePlayerJoined;
        NetworkManager.Instance.OnPlayerLeft      += HandlePlayerLeft;
        NetworkManager.Instance.OnWorldState      += HandleWorldState;
    }

    private void OnDisable()
    {
        if (NetworkManager.Instance == null) return;

        NetworkManager.Instance.OnJoinRoomSuccess -= HandleJoinRoom;
        NetworkManager.Instance.OnPlayerJoined    -= HandlePlayerJoined;
        NetworkManager.Instance.OnPlayerLeft      -= HandlePlayerLeft;
        NetworkManager.Instance.OnWorldState      -= HandleWorldState;
    }

    // ─── Event Handlers ───────────────────────────────────────────────────────

    private void HandleJoinRoom(string roomID, System.Collections.Generic.List<PlayerInfo> existing)
    {
        Debug.Log($"[RemotePlayerManager] Joined room {roomID} with {existing.Count} players");
        foreach (var info in existing)
        {
            if (info.PlayerID == NetworkManager.Instance.LocalPlayerID) continue;
            SpawnRemotePlayer(info);
        }
    }

    private void HandlePlayerJoined(PlayerInfo info)
    {
        if (info.PlayerID == NetworkManager.Instance.LocalPlayerID) return;
        SpawnRemotePlayer(info);
        Debug.Log($"[RemotePlayerManager] Player {info.Username} joined");
    }

    private void HandlePlayerLeft(uint playerID)
    {
        if (remotePlayers.TryGetValue(playerID, out var rp))
        {
            Destroy(rp.gameObject);
            remotePlayers.Remove(playerID);
            Debug.Log($"[RemotePlayerManager] Player {playerID} left");
        }
    }

    private void HandleWorldState(WorldStatePacket ws)
    {
        foreach (var snap in ws.Players)
        {
            if (snap.PlayerID == NetworkManager.Instance.LocalPlayerID) continue;
            if (remotePlayers.TryGetValue(snap.PlayerID, out var rp))
            {
                rp.ApplySnapshot(snap);
            }
        }
    }

    // ─── Private ──────────────────────────────────────────────────────────────

    private void SpawnRemotePlayer(PlayerInfo info)
    {
        if (remotePlayers.ContainsKey(info.PlayerID)) return;

        var go = Instantiate(remotePlayerPrefab,
            new Vector3(info.X, info.Y, 0f), Quaternion.identity, transform);
        go.name = $"RemotePlayer_{info.PlayerID}_{info.Username}";

        var rp = go.GetComponent<RemotePlayer>();
        if (rp == null) rp = go.AddComponent<RemotePlayer>();
        rp.Initialize(info);

        remotePlayers[info.PlayerID] = rp;
    }
}
