using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Freeland.Gameplay;

/// <summary>
/// GameSceneBootstrap — handles spawning player objects (local and remote)
/// when transitioning into a gameplay scene.
///
/// Persistent Singleton: survives scene transitions (DontDestroyOnLoad).
/// Listens to SceneManager.sceneLoaded and auto-bootstraps any scene whose
/// name contains "Map" (e.g. "Map0", "Map1").
/// Place this once in the first scene (e.g. Menu) — it will carry forward.
/// </summary>
public class GameSceneBootstrap : MonoBehaviour
{
    /// <summary>The single persistent GameSceneBootstrap instance.</summary>
    public static GameSceneBootstrap Instance { get; private set; }

    [Header("Network Spawning")]
    [SerializeField] private Transform remotePlayerRoot;

    private LocalPlayer localPlayer;
    private readonly Dictionary<uint, RemotePlayer> remotePlayers = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GameSceneBootstrap] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Ensure MonsterManager lives on this persistent GameObject
        if (GetComponent<MonsterManager>() == null)
            gameObject.AddComponent<MonsterManager>();

        // Ensure DamagePopupManager lives on this persistent GameObject
        if (GetComponent<DamagePopupManager>() == null)
            gameObject.AddComponent<DamagePopupManager>();

        EnsureDeadPanel();
        EnsureStatusUI();
    }

    /// <summary>
    /// Finds an existing DeadNotificationUI in the scene.
    /// If none is found, loads DeadNouti prefab from Resources and instantiates it.
    /// </summary>
    private void EnsureDeadPanel()
    {
        // FindAnyObjectByType searches active AND inactive objects
        if (FindAnyObjectByType<DeadNotificationUI>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("[GameSceneBootstrap] DeadNotificationUI found in scene.");
            return;
        }

        var prefab = Resources.Load<GameObject>("Prefab/DeadNoti");
        if (prefab == null)
        {
            Debug.LogWarning("[GameSceneBootstrap] DeadNoti prefab not found in Resources/Prefab/. Death UI will be unavailable.");
            return;
        }

        var panel = Instantiate(prefab);
        panel.name = "DeadNouti";
        DontDestroyOnLoad(panel);
        Debug.Log("[GameSceneBootstrap] DeadNotificationUI instantiated from Resources.");
    }

    /// <summary>
    /// Finds an existing PlayerStatusUI in the scene.
    /// If none is found, loads the PlayerStatusUI prefab from Resources and instantiates it.
    /// </summary>
    private void EnsureStatusUI()
    {
        if (FindAnyObjectByType<PlayerStatusUI>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("[GameSceneBootstrap] PlayerStatusUI found in scene.");
            return;
        }

        var prefab = Resources.Load<GameObject>("Prefab/Stats");
        if (prefab == null)
        {
            Debug.LogWarning("[GameSceneBootstrap] Stats prefab not found in Resources/Prefab/. HUD will be unavailable.");
            return;
        }

        var hud = Instantiate(prefab);
        hud.name = "PlayerStatusUI";
        DontDestroyOnLoad(hud);
        Debug.Log("[GameSceneBootstrap] PlayerStatusUI instantiated from Resources.");
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// Called by Unity whenever a scene finishes loading.
    /// Defers player spawning by one frame so all scene objects (NavMesh, etc.) are ready.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Only bootstrap game scenes (e.g. "Map0", "Map1")
        if (!scene.name.StartsWith("Map")) return;

        localPlayer = null;
        remotePlayers.Clear();

        // ProjectileManager lives on this persistent GameObject — re-ensure it exists
        if (GetComponent<ProjectileManager>() == null)
            gameObject.AddComponent<ProjectileManager>();

        Debug.Log($"[Bootstrap] Scene '{scene.name}' loaded — will bootstrap next frame.");
        StartCoroutine(BootstrapNextFrame());
    }

    /// <summary>
    /// Waits one frame so all scene Start() calls finish (NavMesh, etc.) before spawning.
    /// Mirrors the old behaviour when Bootstrap lived in the game scene and ran from Start().
    /// </summary>
    private System.Collections.IEnumerator BootstrapNextFrame()
    {
        yield return null; // wait one frame

        if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected)
        {
            Debug.Log("[Bootstrap] Online mode: spawning network characters.");
            SpawnLocalPlayer();
            SpawnExistingRemotePlayers();
            SubscribeNetworkEvents();
        }
        else
        {
            Debug.LogWarning("[Bootstrap] Offline mode: no server connection. Spawning test player.");
            SpawnLocalPlayerOffline();
        }
    }

    /// <summary>Spawn player without network (for direct scene testing).</summary>
    private void SpawnLocalPlayerOffline()
    {
        GameSession session = GameSession.Instance;
        if (session == null)
        {
            Debug.LogWarning("[Bootstrap] SpawnLocalPlayerOffline skipped — no GameSession available (menu scene?).");
            return;
        }

        string prefabPath = session.GetPrefabPath();

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
            prefab = new GameObject("PlayerFallback");
        }

        Vector3 spawnPos = new Vector3(session.SpawnX, session.SpawnY, 0f);
        var go = Instantiate(prefab, spawnPos, Quaternion.identity);
        go.name = $"LocalPlayer_{session.Username}";

        // Set the character name via BaseObject's ObjectName property (handled automatically by BaseObject)
        var baseObj = go.GetComponent<BaseObject>();
        if (baseObj != null)
        {
            baseObj.ObjectName = session.Username;
        }

        // Attach LocalPlayer component
        localPlayer = go.GetComponent<LocalPlayer>();
        if (localPlayer == null)
            localPlayer = go.AddComponent<LocalPlayer>();

        localPlayer.Initialize(session);

        // Camera follow
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
        NetworkManager.Instance.OnDamageEvent  += HandleDamageEvent;
        NetworkManager.Instance.OnDieEvent     += HandleDieEvent;
        NetworkManager.Instance.OnRespawnAck   += HandleRespawnAck;
    }

    private void UnsubscribeNetworkEvents()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnPlayerJoined -= HandlePlayerJoined;
        NetworkManager.Instance.OnPlayerLeft   -= HandlePlayerLeft;
        NetworkManager.Instance.OnWorldState   -= HandleWorldState;
        NetworkManager.Instance.OnDamageEvent  -= HandleDamageEvent;
        NetworkManager.Instance.OnDieEvent     -= HandleDieEvent;
        NetworkManager.Instance.OnRespawnAck   -= HandleRespawnAck;
    }

    // ─── Damage / Die Events ──────────────────────────────────────────────────

    private void HandleDamageEvent(DamageEventPacket pkt)
    {
        uint localID = GameSession.Instance != null ? GameSession.Instance.PlayerID : 0;

        if (pkt.TargetID == localID)
        {
            // Apply to local player's BaseObject HP — HP bar updates automatically
            if (localPlayer != null)
            {
                var baseObj = localPlayer.GetComponent<BaseObject>();
                if (baseObj != null)
                    baseObj.HP = (int)pkt.RemainingHP;
            }
            Debug.Log($"[Bootstrap] Local player took damage — HP now {pkt.RemainingHP} (from {pkt.AttackerID})");
        }
        else if (remotePlayers.TryGetValue(pkt.TargetID, out var rp))
        {
            rp.ApplyServerDamage(pkt.RemainingHP);
        }
    }

    private void HandleDieEvent(DieEventPacket pkt)
    {
        uint localID = GameSession.Instance != null ? GameSession.Instance.PlayerID : 0;

        if (pkt.PlayerID == localID)
        {
            Debug.Log($"[Bootstrap] Local player died (killed by {pkt.KillerID})");

            // Disable local player input while dead
            // LocalPlayer.isDead blocks input automatically — no need to disable the component.
            // LocalPlayer's own OnDieEvent handler sets isDead = true.

            // Show the death panel
            DeadNotificationUI.Instance?.Show();
        }
        else if (remotePlayers.TryGetValue(pkt.PlayerID, out var rp))
        {
            rp.ServerKill();
        }
    }

    private void HandleRespawnAck(RespawnAckPacket pkt)
    {
        uint localID = GameSession.Instance != null ? GameSession.Instance.PlayerID : 0;
        if (pkt.PlayerID != localID) return;

        Debug.Log($"[Bootstrap] RespawnAck — spawn at ({pkt.X:F1},{pkt.Y:F1}) HP={pkt.HP}");

        // LocalPlayer.OnRespawnAck resets isDead, position and state machine.
        // Bootstrap only needs to sync HP (which LocalPlayer doesn't track from Ack).
        if (localPlayer != null)
        {
            var baseObj = localPlayer.GetComponent<BaseObject>();
            if (baseObj != null)
                baseObj.HP = pkt.HP;
        }

        // Hide the death panel
        DeadNotificationUI.Instance?.Hide();
    }

    // ─── Remote Players ───────────────────────────────────────────────────────

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
        // Diagnostic: log counts every 100 ticks (~5 s at 20 Hz) to confirm receipt
        if (ws.Tick % 100 == 1)
            Debug.Log($"[Bootstrap] WorldState tick={ws.Tick} players={ws.Players?.Length ?? 0} monsters={ws.Monsters?.Length ?? 0}");

        foreach (var snap in ws.Players)
        {
            if (snap.PlayerID == GameSession.Instance.PlayerID) continue;

            if (remotePlayers.TryGetValue(snap.PlayerID, out var rp))
            {
                rp.ApplySnapshot(snap);
            }
            else
            {
                // Fallback: auto-spawn if missed OnPlayerJoined
                Debug.LogWarning($"[Bootstrap] Remote player {snap.PlayerID} not spawned yet, auto-spawning from WorldState");
                var info = new PlayerInfo
                {
                    PlayerID = snap.PlayerID,
                    Username = $"Player_{snap.PlayerID}",
                    JobClass = 1, // Default Archer
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

        // Instantiate inactive to configure settings before Awake
        prefab.SetActive(false);
        var go = Instantiate(prefab, new Vector3(info.X, info.Y, 0f), Quaternion.identity, parent);
        prefab.SetActive(true);

        go.name = $"Remote_{info.PlayerID}_{info.Username}";

        // Disable NavMeshAgent
        var navAgent = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navAgent != null) navAgent.enabled = false;

        // Set Kinematic
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb != null) rb.bodyType = RigidbodyType2D.Kinematic;

        // Remove local scripts
        var localP = go.GetComponent<LocalPlayer>();
        if (localP != null) Destroy(localP);

        // Activate to trigger Awake/Start
        go.SetActive(true);

        // Disable human state machine
        var human = go.GetComponent<Human>();
        if (human != null) human.enabled = false;

        // Attach RemotePlayer script
        var rp = go.GetComponent<RemotePlayer>();
        if (rp == null) rp = go.AddComponent<RemotePlayer>();
        rp.Initialize(info);

        // Set the remote character name via BaseObject's ObjectName property (handled automatically by BaseObject)
        var baseObj = go.GetComponent<BaseObject>();
        if (baseObj != null)
        {
            baseObj.ObjectName = info.Username;
        }

        remotePlayers[info.PlayerID] = rp;
        Debug.Log($"[Bootstrap] ✅ Remote player spawned: {go.name} at ({info.X:F1},{info.Y:F1})");
    }
}
