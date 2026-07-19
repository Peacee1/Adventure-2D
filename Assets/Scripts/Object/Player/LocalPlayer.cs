using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// LocalPlayer — server-authoritative controller.
///
/// Responsibilities (SRP):
///   1. Capture player input (click to move, D to dash, left-click to attack).
///   2. Send requests to the server (MovePath, DashReq, AttackReq) — server validates and executes.
///   3. Receive WorldState from the server and synchronize position + animation state.
///
/// The client does NOT self-simulate any game logic:
///   - Position is driven by server WorldState via smooth Lerp.
///   - State machine transitions are driven by server State field via Human.ForceState().
///   - Cooldowns, timers, input locks are all server-side.
///
/// DIP: Human state machine communicates only via IHumanController.
/// </summary>
public class LocalPlayer : MonoBehaviour, IHumanController
{
    // ─── IHumanController ────────────────────────────────────────────────────

    /// <summary>Always zero — local player position is driven by server, not by input velocity.</summary>
    public Vector2 MoveInput    => Vector2.zero;
    public Vector2 AimDirection { get; private set; }

    public void StopNavigation()
    {
        Vector3[] stopPath = new Vector3[] { transform.position };
        NetworkManager.Instance?.SendMovePath(playerID, stopPath);
    }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Server-Authoritative Sync")]
    [Tooltip("Lerp speed toward the server position.")]
    [SerializeField] private float lerpSpeed     = 25f;

    [Tooltip("Maximum allowed deviation before snapping to server position.")]
    [SerializeField] private float snapThreshold = 3f;

    [Header("Network Sync Keep-Alive")]
    [Tooltip("UDP packets per second to keep the server UDP endpoint bound.")]
    [SerializeField] private float sendRate      = 20f;

    [Header("Input")]
    [SerializeField] private int moveMouseButton = 1; // right mouse button

    // ─── Runtime ─────────────────────────────────────────────────────────────

    private uint   playerID;
    private Camera cam;
    private Human  human;

    // NavMesh path calculation
    private NavMeshPath navPath;
    private float       navMeshZ;
    private bool        navReady;
    private bool        isInitialized;

    // NavMesh retry
    private float navMeshRetryTimer;
    private int   navMeshRetryCount;
    private const float RetryInterval = 0.3f;

    // Server-authoritative state
    private Vector3 _serverPosition;
    private Vector2 _serverDirection;
    private byte    _serverState;
    private byte    _lastAppliedState = 255; // sentinel — forces first ForceState call
    private uint    _lastProcessedTick;

    private bool  isDead;
    private float sendTimer;

    // Dash distance scaling — mirrors server's ComputeMaxDashDistance()
    // Updated via UpdateMoveSpeed() when server sends stats.
    private float _moveSpeed = 10f;

    // ─── Init ─────────────────────────────────────────────────────────────────

    public void Initialize(GameSession session)
    {
        playerID = session.PlayerID;
        human    = GetComponent<Human>();
        cam      = Camera.main;
        navPath  = new NavMeshPath();
        _lastProcessedTick = 0;

        var agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
            Debug.Log("[LocalPlayer] NavMeshAgent disabled — position driven by server.");
        }

        if (human != null) human.Controller = this;

        var levelSystem = GetComponent<LevelSystem>();
        if (levelSystem != null)
            levelSystem.SetLevelDirect(session.Level, session.CurrentExp);

        Vector3 spawnPos = new Vector3(session.SpawnX, session.SpawnY, 0f);
        _serverPosition = spawnPos;
        transform.position = spawnPos;

        TryInitNavMesh(spawnPos);
    }

    private bool TryInitNavMesh(Vector3 worldPos)
    {
        if (!NavMesh.SamplePosition(worldPos, out var hit, 500f, NavMesh.AllAreas))
            return false;

        navMeshZ      = hit.position.z;
        navReady      = true;
        isInitialized = true;

        transform.position = new Vector3(hit.position.x, hit.position.y, 0f);
        _serverPosition    = transform.position;

        Debug.Log($"[LocalPlayer] ✅ NavMesh OK — Pos={transform.position}  navMeshZ={navMeshZ:F3}");
        return true;
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnWorldState += OnWorldState;
        NetworkManager.Instance.OnDieEvent   += OnDieEvent;
        NetworkManager.Instance.OnRespawnAck += OnRespawnAck;
    }

    private void OnDisable()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnWorldState -= OnWorldState;
        NetworkManager.Instance.OnDieEvent   -= OnDieEvent;
        NetworkManager.Instance.OnRespawnAck -= OnRespawnAck;
    }

    private void Update()
    {
        if (!isInitialized)
        {
            navMeshRetryTimer += Time.deltaTime;
            if (navMeshRetryTimer >= RetryInterval)
            {
                navMeshRetryTimer = 0f;
                navMeshRetryCount++;
                if (TryInitNavMesh(transform.position)) return;
            }
            return;
        }

        if (isDead) return;

        HandleClickInput();
        HandleAttackInput();
        HandleDashInput();
        UpdateAimDirection();
        UpdatePositionSync();
        TrySendUDP();
    }

    // ─── Stats ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the player's move speed so dash distance scales correctly.
    /// Call this whenever the server sends a stats update.
    /// </summary>
    public void UpdateMoveSpeed(float moveSpeed)
    {
        _moveSpeed = Mathf.Max(10f, moveSpeed); // minimum 10
    }

    /// <summary>
    /// Computes the maximum dash path length for the given move speed.
    /// Mirrors the server's ComputeMaxDashDistance() formula exactly:
    ///   extraSteps = floor(max(0, speed - 10) / 5)
    ///   maxDist    = 6.0 * (1 + extraSteps * 0.10)
    /// </summary>
    private float ComputeMaxDashDistance(float moveSpeed)
    {
        const float baseDist = 12f;
        const float baseSpeed = 10f;
        if (moveSpeed <= baseSpeed) return baseDist;
        float extraSteps = Mathf.Floor((moveSpeed - baseSpeed) / 5f);
        return baseDist * (1f + extraSteps * 0.10f);
    }

    // ─── Input Handlers ───────────────────────────────────────────────────────

    /// <summary>Right-click to move: calculate NavMesh path and send waypoints to server.</summary>
    private void HandleClickInput()
    {
        if (Mouse.current == null) return;

        bool isPressed = moveMouseButton == 0
            ? Mouse.current.leftButton.isPressed
            : Mouse.current.rightButton.isPressed;
        if (!isPressed) return;

        if (cam == null) { cam = Camera.main; return; }

        Vector3 screen = Mouse.current.position.ReadValue();
        screen.z       = Mathf.Abs(cam.transform.position.z);
        Vector3 world  = cam.ScreenToWorldPoint(screen);
        world.z        = 0f;

        SetMoveTarget(world);
    }

    /// <summary>
    /// D key to dash: use NavMesh to compute a valid dash path in AimDirection,
    /// then send waypoints + total distance to the server.
    /// Server computes dashSpeed = totalDistance / DashDuration.
    /// </summary>
    private void HandleDashInput()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.dKey.wasPressedThisFrame) return;
        if (!navReady) return;

        // Dynamic max distance — mirrors server's ComputeMaxDashDistance(_moveSpeed)
        float maxDashDistance = ComputeMaxDashDistance(_moveSpeed);

        // Compute the ideal dash destination: current server position + aim dir * maxDist
        Vector3 origin = new Vector3(_serverPosition.x, _serverPosition.y, navMeshZ);
        Vector2 aimDir = AimDirection.sqrMagnitude > 0.001f ? AimDirection.normalized : Vector2.right;
        Vector3 target = new Vector3(
            _serverPosition.x + aimDir.x * maxDashDistance,
            _serverPosition.y + aimDir.y * maxDashDistance,
            navMeshZ);

        // Sample target onto the NavMesh surface (handles map edges and concave maps)
        if (NavMesh.SamplePosition(target, out var hit, maxDashDistance, NavMesh.AllAreas))
            target = hit.position;

        // Calculate NavMesh path
        var dashPath = new NavMeshPath();
        dashPath.ClearCorners();

        Vector3[] waypoints;
        float totalDistance;

        if (NavMesh.CalculatePath(origin, target, NavMesh.AllAreas, dashPath) &&
            dashPath.corners.Length > 1)
        {
            // Skip the first corner (it's the origin)
            int wpCount = dashPath.corners.Length - 1;
            waypoints = new Vector3[wpCount];
            System.Array.Copy(dashPath.corners, 1, waypoints, 0, wpCount);

            // Compute actual path length along NavMesh
            totalDistance = 0f;
            Vector3 prev = origin;
            foreach (var wp in waypoints)
            {
                totalDistance += Vector3.Distance(prev, wp);
                prev = wp;
            }
        }
        else
        {
            // Fallback: single waypoint as close to target as NavMesh allows
            waypoints = new Vector3[] { target };
            totalDistance = Vector3.Distance(origin, target);
        }

        NetworkManager.Instance?.SendDash(playerID, waypoints, totalDistance);
        Debug.Log($"[LocalPlayer] DashReq sent — waypoints={waypoints.Length} dist={totalDistance:F2} dir=({aimDir.x:F2},{aimDir.y:F2})");
    }

    /// <summary>
    /// Left-click to attack: sends AttackReq(targetID=0, AimDirection) to server.
    /// Server validates cooldown and state; broadcasts ProjectileSpawnPacket to all clients.
    /// Blocked client-side when server state is Attack/Dash/DashEnd/Dead to reduce redundant packets.
    /// </summary>
    private void HandleAttackInput()
    {
        if (Mouse.current == null) return;

        // Debug: log any left-button press detection
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Debug.Log($"[LocalPlayer] LEFT CLICK detected — _serverState={_serverState} playerID={playerID}");

            // Byte values match server State constants: 2=Dash, 3=Attack, 4=DashEnd, 5=Dead
            if (_serverState == 2 || _serverState == 3 || _serverState == 4 || _serverState == 5)
            {
                Debug.Log($"[LocalPlayer] AttackReq BLOCKED by state={_serverState}");
                return;
            }

            NetworkManager.Instance?.SendAttack(playerID, 0, AimDirection);
            Debug.Log($"[LocalPlayer] AttackReq sent — dir=({AimDirection.x:F2},{AimDirection.y:F2})");
        }
    }

    private void SetMoveTarget(Vector3 worldTarget)
    {
        if (!navReady) return;

        Vector3 navFrom = new Vector3(_serverPosition.x, _serverPosition.y, navMeshZ);
        Vector3 navTo   = worldTarget;
        navTo.z         = navMeshZ;

        if (NavMesh.SamplePosition(navTo, out var hitTo, 100f, NavMesh.AllAreas))
            navTo = hitTo.position;

        navPath.ClearCorners();
        if (NavMesh.CalculatePath(navFrom, navTo, NavMesh.AllAreas, navPath) &&
            navPath.corners.Length > 0)
        {
            // Trim the starting point to prevent the server from doubling back
            Vector3[] sentCorners;
            if (navPath.corners.Length > 1)
            {
                sentCorners = new Vector3[navPath.corners.Length - 1];
                System.Array.Copy(navPath.corners, 1, sentCorners, 0, sentCorners.Length);
            }
            else
            {
                sentCorners = navPath.corners;
            }

            NetworkManager.Instance?.SendMovePath(playerID, sentCorners);
        }
    }

    // ─── Server Sync ──────────────────────────────────────────────────────────

    private void UpdatePositionSync()
    {
        float distance = Vector2.Distance(transform.position, _serverPosition);

        if (distance > snapThreshold)
        {
            transform.position = _serverPosition;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, _serverPosition, lerpSpeed * Time.deltaTime);
        }

        // Drive the client state machine from the server state
        if (human != null)
        {
            // Only call ForceState when state actually changes (avoid redundant SM transitions)
            if (_serverState != _lastAppliedState)
            {
                _lastAppliedState = _serverState;
                human.ForceState(_serverState);
            }

            // Direction is always kept up-to-date from server
            if (_serverDirection.sqrMagnitude > 0.001f)
                human.FaceDirection(_serverDirection.x);
        }
    }

    private void UpdateAimDirection()
    {
        if (cam == null || Mouse.current == null) return;
        Vector3 screen = Mouse.current.position.ReadValue();
        screen.z = Mathf.Abs(cam.transform.position.z);
        Vector3 world  = cam.ScreenToWorldPoint(screen);
        world.z = 0f;
        Vector2 dir = (world - transform.position);
        AimDirection = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
    }

    private void TrySendUDP()
    {
        if (NetworkManager.Instance == null) return;

        sendTimer += Time.deltaTime;
        if (sendTimer < 1f / sendRate) return;
        sendTimer = 0f;

        // Send position keep-alive so the server can bind our UDP endpoint
        Vector2 currentPos = new Vector2(transform.position.x, transform.position.y);
        NetworkManager.Instance.SendMoveInput(playerID, currentPos, Vector2.zero);
    }

    // ─── Server Event Handlers ────────────────────────────────────────────────

    private void OnWorldState(WorldStatePacket ws)
    {
        if (ws.Tick <= _lastProcessedTick) return;
        _lastProcessedTick = ws.Tick;

        foreach (var snap in ws.Players)
        {
            if (snap.PlayerID != playerID) continue;

            _serverPosition  = new Vector3(snap.X, snap.Y, 0f);
            _serverDirection = new Vector2(snap.DirX, snap.DirY);
            _serverState     = snap.State;
            break;
        }
    }

    private void OnDieEvent(DieEventPacket die)
    {
        if (die.PlayerID != playerID) return;
        isDead = true;
        _serverState = 4; // Dead
        if (human != null) human.ForceState(4);
        Debug.Log($"[LocalPlayer] 💀 Died! Killer={die.KillerID}");
    }

    private void OnRespawnAck(RespawnAckPacket ack)
    {
        if (ack.PlayerID != playerID) return;
        isDead = false;
        _serverState      = 0; // Idle
        _lastAppliedState = 255; // reset sentinel to force ForceState call
        _serverPosition   = new Vector3(ack.X, ack.Y, 0f);
        transform.position = _serverPosition;
        if (human != null) human.ForceState(0);
        Debug.Log($"[LocalPlayer] 🔄 Respawned at ({ack.X:F1},{ack.Y:F1}) HP={ack.HP}");
    }
}
