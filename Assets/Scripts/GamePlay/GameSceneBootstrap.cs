using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Freeland.Gameplay;

/// <summary>
/// GameSceneBootstrap — handles spawning player objects (local and remote)
/// when transitioning into a gameplay scene.
/// </summary>
public class GameSceneBootstrap : MonoBehaviour
{
    [Header("Network Spawning")]
    [SerializeField] private Transform remotePlayerRoot;

    private LocalPlayer localPlayer;
    private readonly Dictionary<uint, RemotePlayer> remotePlayers = new();

    private void Start()
    {
        // Check if NetworkManager is connected
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsConnected)
        {
            Debug.Log("[Bootstrap] Online mode: spawning network characters");
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
    }

    private void UnsubscribeNetworkEvents()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnPlayerJoined -= HandlePlayerJoined;
        NetworkManager.Instance.OnPlayerLeft   -= HandlePlayerLeft;
        NetworkManager.Instance.OnWorldState   -= HandleWorldState;
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
