using System.Collections.Generic;
using Freeland.Gameplay;
using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// GameSceneBootstrap — MonoBehaviour gắn vào SampleScene.
/// Chịu trách nhiệm khởi tạo toàn bộ game khi scene load:
///   1. Spawn local player prefab đúng JobClass từ GameSession
///   2. Load remote players đang có trong phòng
///   3. Đăng ký events để spawn/despawn remote players real-time
///
/// SRP: chỉ bootstrap, không chứa game logic.
/// </summary>
public class GameSceneBootstrap : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Spawn Point")]
    [Tooltip("Nếu không set, dùng vị trí server trả về")]
    [SerializeField] private Transform spawnRoot;

    [Header("Remote Players")]
    [Tooltip("Container để chứa remote player GameObjects")]
    [SerializeField] private Transform remotePlayerRoot;

    // ─── Runtime ─────────────────────────────────────────────────────────────

    private LocalPlayer localPlayer;
    private readonly Dictionary<uint, RemotePlayer> remotePlayers = new();

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        // ── Đổi gravity cho NavMesh+ 2D ─────────────────────────────────────────
        // NavMesh+ xây NavMesh ở XY plane (Z ≈ -0.08, normal = +Z).
        // NavMeshAgent tìm NavMesh bằng cách cast XUỐNG theo chiều gravity.
        // Mặc định: gravity = (0, -9.81, 0) → cast xuống -Y → không bao giờ thấy XY plane.
        // Fix: đổi sang (0, 0, -9.81) → cast xuống -Z → tìm đúng NavMesh ở XY plane.
        // NOTE: chỉ ảnh hưởng 3D Physics (NavMeshAgent), KHÔNG ảnh hưởng Physics2D.
        Physics.gravity = new Vector3(0f, 0f, -9.81f);

        // ── Build NavMesh trước mọi thứ khác ───────────────────────────────────
        // NavMeshComponents (Assets/NavMeshComponents) yêu cầu gọi BuildNavMesh()
        // thủ công — không tự build như pre-baked NavMesh mặc định của Unity.
        BuildAllNavMeshSurfaces();

        // Fallback nếu test trực tiếp từ game scene (không qua MenuScene)
        if (GameSession.Instance == null)
        {
            Debug.LogWarning("[Bootstrap] GameSession null — tạo session test (Archer, Map1, pos 0,0)");
            var go = new GameObject("GameSession");
            var session = go.AddComponent<GameSession>();
            // Session test mặc định: Archer, Level 1, pos (0,0)
            session.SetPlayerInfo(9999, 1, "TestPlayer", 800, 800, 0f, 0f);
            session.SetLevel(1, 0);
            session.SetMapName("Map1");
        }

        if (NetworkManager.Instance == null)
        {
            Debug.LogWarning("[Bootstrap] NetworkManager null — spawn player offline (không có network sync).");
            SpawnLocalPlayerOffline();
            return;
        }

        Debug.Log($"[Bootstrap] Scene loaded. PlayerID={GameSession.Instance.PlayerID} Job={GameSession.Instance.JobClass} Map={GameSession.Instance.MapName}");

        SpawnLocalPlayer();
        SubscribeNetworkEvents();

        // Nếu đã join room trước khi scene load (vì LoadScene async),
        // remote players cần được spawn từ danh sách đã lưu
        SpawnExistingRemotePlayers();
    }

    /// <summary>
    /// Tìm tất cả NavMeshSurface trong scene và build đồng bộ.
    /// Phải gọi trước khi spawn player để NavMeshAgent hoạt động ngay lập tức.
    /// </summary>
    private void BuildAllNavMeshSurfaces()
    {
        // NavMeshSurface nằm trong global namespace (gói Assets/NavMeshComponents)
        var surfaces = FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
        if (surfaces == null || surfaces.Length == 0)
        {
            Debug.LogWarning("[Bootstrap] Không tìm thấy NavMeshSurface nào trong scene. " +
                             "NavMesh phải pre-baked hoặc thêm NavMeshSurface component vào map.");
            return;
        }

        foreach (var surface in surfaces)
        {
            Debug.Log($"[Bootstrap] Building NavMesh surface: {surface.gameObject.name}...");
            surface.BuildNavMesh();
            Debug.Log($"[Bootstrap] ✅ NavMesh built: {surface.gameObject.name}");
        }
    }

    /// <summary>Spawn player không có network (test trực tiếp từ scene).</summary>
    private void SpawnLocalPlayerOffline()
    {
        GameSession session = GameSession.Instance;
        string prefabPath   = session.GetPrefabPath();

        var prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Bootstrap] Prefab not found: Resources/{prefabPath}");
            return;
        }

        Vector3 spawnPos = new Vector3(session.SpawnX, session.SpawnY, 0f);
        var go = Instantiate(prefab, spawnPos, Quaternion.identity);
        go.name = "LocalPlayer_Offline";

        var cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<CameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();
            follow.SetTarget(go.transform);
        }

        Debug.Log($"[Bootstrap] Offline player spawned at {spawnPos}");
    }

    private void OnDestroy()
    {
        UnsubscribeNetworkEvents();
    }

    // ─── Local Player ─────────────────────────────────────────────────────────

    private void SpawnLocalPlayer()
    {
        GameSession session = GameSession.Instance;
        string prefabPath   = session.GetPrefabPath();

        Debug.Log($"[Bootstrap] Loading local player prefab: Resources/{prefabPath}");
        var prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[Bootstrap] Prefab not found at Resources/{prefabPath}. Fallback to default.");
            // Fallback: tạo GameObject trống
            prefab = new GameObject("PlayerFallback");
        }

        Vector3 spawnPos = new Vector3(session.SpawnX, session.SpawnY, 0f);
        var go = Instantiate(prefab, spawnPos, Quaternion.identity);
        go.name = $"LocalPlayer_{session.Username}";

        // Gắn LocalPlayer component nếu chưa có
        localPlayer = go.GetComponent<LocalPlayer>();
        if (localPlayer == null)
            localPlayer = go.AddComponent<LocalPlayer>();

        localPlayer.Initialize(session);

        // Camera follow (nếu có MainCamera)
        var cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<CameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<CameraFollow>();
            follow.SetTarget(go.transform);
        }

        Debug.Log($"[Bootstrap] Local player spawned: {go.name} at {spawnPos}");
    }

    // ─── Network Events ───────────────────────────────────────────────────────

    private void SubscribeNetworkEvents()
    {
        NetworkManager.Instance.OnPlayerJoined += HandlePlayerJoined;
        NetworkManager.Instance.OnPlayerLeft   += HandlePlayerLeft;
        NetworkManager.Instance.OnWorldState   += HandleWorldState;
    }

    private void UnsubscribeNetworkEvents()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnPlayerJoined -= HandlePlayerJoined;
        NetworkManager.Instance.OnPlayerLeft   -= HandlePlayerLeft;
        NetworkManager.Instance.OnWorldState   -= HandleWorldState;
    }

    // ─── Remote Players ───────────────────────────────────────────────────────

    /// <summary>
    /// Spawn remote players đã có trong phòng (nhận từ JoinRoomAck).
    /// GameSession.ExistingPlayers được lưu trước khi LoadScene.
    /// </summary>
    private void SpawnExistingRemotePlayers()
    {
        var existing = GameSession.Instance.ExistingPlayersAtJoin;
        if (existing == null || existing.Count == 0) return;

        Debug.Log($"[Bootstrap] Spawning {existing.Count} existing remote players");
        foreach (var info in existing)
        {
            if (info.PlayerID == GameSession.Instance.PlayerID) continue;
            SpawnRemotePlayer(info);
        }
    }

    private void HandlePlayerJoined(PlayerInfo info)
    {
        if (info.PlayerID == GameSession.Instance.PlayerID) return;
        SpawnRemotePlayer(info);
        Debug.Log($"[Bootstrap] Remote player joined: {info.Username} (ID={info.PlayerID})");
    }

    private void HandlePlayerLeft(uint playerID)
    {
        if (remotePlayers.TryGetValue(playerID, out var rp))
        {
            Destroy(rp.gameObject);
            remotePlayers.Remove(playerID);
            Debug.Log($"[Bootstrap] Remote player left: ID={playerID}");
        }
    }

    private void HandleWorldState(WorldStatePacket ws)
    {
        foreach (var snap in ws.Players)
        {
            // Bỏ qua local player
            if (snap.PlayerID == GameSession.Instance.PlayerID) continue;

            if (remotePlayers.TryGetValue(snap.PlayerID, out var rp))
            {
                rp.ApplySnapshot(snap);
            }
            else
            {
                // Fallback: player chưa được spawn (OnPlayerJoined bị miss) → spawn từ WorldState
                Debug.LogWarning($"[Bootstrap] Remote player {snap.PlayerID} chưa spawn, auto-spawn từ WorldState");
                var info = new PlayerInfo
                {
                    PlayerID = snap.PlayerID,
                    Username = $"Player_{snap.PlayerID}",
                    JobClass = 1, // Archer mặc định (WorldState không có job)
                    X = snap.X,
                    Y = snap.Y,
                    HP = snap.HP,
                    MaxHP = snap.HP
                };
                SpawnRemotePlayer(info);
            }
        }
    }

    private void SpawnRemotePlayer(PlayerInfo info)
    {
        if (remotePlayers.ContainsKey(info.PlayerID)) return;

        // Load prefab đúng JobClass của remote player
        string prefabPath = GameSession.GetPrefabPathForJob(info.JobClass);
        Debug.Log($"[Bootstrap] Spawning remote player {info.Username} (ID={info.PlayerID} Job={info.JobClass}) prefab=Resources/{prefabPath}");

        var prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[Bootstrap] Remote prefab not found: {prefabPath}, using fallback");
            prefab = Resources.Load<GameObject>("Prefab/Archer");
        }
        if (prefab == null)
        {
            Debug.LogError($"[Bootstrap] Cannot load any prefab for remote player {info.PlayerID}!");
            return;
        }

        Transform parent = remotePlayerRoot != null ? remotePlayerRoot : transform;

        // Instantiate không active → Awake/OnEnable chưa chạy
        // → disable NavMeshAgent trước → SetActive(true) để Awake chạy sau khi đã cấu hình xong
        prefab.SetActive(false);
        var go = Instantiate(prefab, new Vector3(info.X, info.Y, 0f), Quaternion.identity, parent);
        prefab.SetActive(true); // restore prefab (không ảnh hưởng instance đã tạo)

        go.name = $"Remote_{info.PlayerID}_{info.Username}";

        // Disable NavMeshAgent trước khi Awake → agent không bao giờ bind vào NavMesh
        var navAgent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navAgent != null) navAgent.enabled = false;

        // Kinematic để RemotePlayer.Update() lerp transform tự do
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null) rb.bodyType = RigidbodyType2D.Kinematic;

        // Xóa LocalPlayer — remote không cần input/pathfinding local
        var localP = go.GetComponent<LocalPlayer>();
        if (localP != null) Destroy(localP);

        // Activate → Awake/Start/OnEnable chạy (NavMeshAgent đã disabled)
        go.SetActive(true);

        // Tắt Human StateMachine để không conflict với RemotePlayer position control.
        // RemotePlayer sẽ tự điều khiển Rigidbody2D và Animator trực tiếp.
        var human = go.GetComponent<Human>();
        if (human != null) human.enabled = false;

        // Gắn RemotePlayer để nhận WorldState snapshot và điều khiển vị trí + animator
        var rp = go.GetComponent<RemotePlayer>();
        if (rp == null) rp = go.AddComponent<RemotePlayer>();
        rp.Initialize(info);

        remotePlayers[info.PlayerID] = rp;
        Debug.Log($"[Bootstrap] ✅ Remote player spawned: {go.name} at ({info.X:F1},{info.Y:F1})");
    }
}
