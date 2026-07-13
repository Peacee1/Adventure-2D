using NavMeshPlus.Components;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// LocalPlayer — click-to-move online player controller.
///
/// Di chuyển bằng NavMesh.CalculatePath + manual path-following.
/// NavMeshAgent.Warp() không hoạt động với NavMesh+2D (XY plane) nên bị bỏ.
/// SamplePosition + CalculatePath hoạt động tốt với cùng NavMesh data.
///
/// DIP: Human states chỉ biết IHumanController, không biết LocalPlayer.
/// SRP: LocalPlayer chỉ xử lý input + NavMesh + network sync.
/// </summary>
public class LocalPlayer : MonoBehaviour, IHumanController
{
    // ─── IHumanController ────────────────────────────────────────────────────

    /// <summary>
    /// Hướng di chuyển, chuẩn hóa về [-1,1].
    /// HumanMoveState dùng: owner.SetVelocity(MoveInput * MoveSpeed)
    /// </summary>
    public Vector2 MoveInput     { get; private set; }
    public Vector2 AimDirection  { get; private set; }
    public bool    IsDashPressed    => false;
    public bool    IsDashPressedRaw => false;
    public bool    IsAttackPressed  => Input.GetMouseButtonDown(0);

    public void StopNavigation()
    {
        ClearPath();
        MoveInput = Vector2.zero;
    }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Network Sync")]
    [SerializeField] private float sendRate      = 20f;
    [SerializeField] private float snapThreshold = 50f;
    [SerializeField] private float snapWarmup    = 2f;

    [Header("Input")]
    [SerializeField] private int moveMouseButton = 1; // chuột phải

    // ─── Runtime ─────────────────────────────────────────────────────────────

    private uint    playerID;
    private Camera  cam;
    private Human   human;
    private float   moveSpeed => human != null ? human.MoveSpeed : 5f;

    // NavMesh path following
    private NavMeshPath navPath;        // path hiện tại
    private int         cornerIdx;      // corner đang tiến đến
    private float       navMeshZ;       // Z của NavMesh (thường ≈ -0.08 với NavMesh+2D)
    private bool        navReady;       // SamplePosition đã tìm thấy NavMesh?

    // Target tracking
    private Vector3 moveTarget;
    private bool    hasTarget;

    // Network
    private Vector3 lastSentPos;
    private float   sendTimer;
    private float   udpLogTimer;
    private float   aliveTimer;

    // State
    private bool isDead;
    private bool isInitialized;

    // NavMesh retry
    private float navMeshRetryTimer;
    private int   navMeshRetryCount;
    private const float RetryInterval = 0.3f;

    // ─── Init ─────────────────────────────────────────────────────────────────

    public void Initialize(GameSession session)
    {
        playerID = session.PlayerID;
        human    = GetComponent<Human>();
        cam      = Camera.main;
        navPath  = new NavMeshPath();

        // Tắt NavMeshAgent nếu có — không dùng Warp/SetDestination (không tương thích NavMesh+2D)
        var agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
            Debug.Log("[LocalPlayer] NavMeshAgent disabled — dùng CalculatePath thay thế.");
        }

        // Inject controller
        if (human != null) human.Controller = this;

        var levelSystem = GetComponent<LevelSystem>();
        if (levelSystem != null)
            levelSystem.SetLevelDirect(session.Level, session.CurrentExp);

        // Thử kết nối NavMesh ngay — nếu chưa sẵn sẽ retry trong Update
        Vector3 spawnPos = new Vector3(session.SpawnX, session.SpawnY, 0f);
        TryInitNavMesh(spawnPos);
    }

    /// <summary>Thử tìm NavMesh tại pos. Nếu thành công → set navReady + vị trí spawn.</summary>
    private bool TryInitNavMesh(Vector3 worldPos)
    {
        if (!NavMesh.SamplePosition(worldPos, out var hit, 500f, NavMesh.AllAreas))
            return false;

        navMeshZ      = hit.position.z;
        navReady      = true;
        isInitialized = true;

        // Đặt transform về 2D position (giữ Z=0 cho sprite rendering)
        transform.position = new Vector3(hit.position.x, hit.position.y, 0f);
        moveTarget         = transform.position;

        Debug.Log($"[LocalPlayer] ✅ NavMesh OK — Pos={transform.position}  navMeshZ={navMeshZ:F3}  speed={moveSpeed}");
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
        // ── NavMesh retry ────────────────────────────────────────────────────
        if (!isInitialized)
        {
            navMeshRetryTimer += Time.deltaTime;
            if (navMeshRetryTimer >= RetryInterval)
            {
                navMeshRetryTimer = 0f;
                navMeshRetryCount++;

                if (TryInitNavMesh(transform.position))
                {
                    Debug.Log($"[LocalPlayer] ✅ NavMesh sẵn sau {navMeshRetryCount * RetryInterval:F1}s");
                    return;
                }

                if (navMeshRetryCount % Mathf.RoundToInt(3f / RetryInterval) == 0)
                    Debug.LogWarning($"[LocalPlayer] Chờ NavMesh... ({navMeshRetryCount * RetryInterval:F0}s)");
            }
            return;
        }

        if (isDead) return;

        aliveTimer += Time.deltaTime;

        HandleClickInput();
        UpdatePathMovement();
        UpdateAimDirection();
        TrySendUDP();
    }

    // ─── Click Input ──────────────────────────────────────────────────────────

    private void HandleClickInput()
    {
        if (!Input.GetMouseButton(moveMouseButton)) return;
        if (cam == null) { cam = Camera.main; return; }

        Vector3 screen = Input.mousePosition;
        screen.z       = Mathf.Abs(cam.transform.position.z);
        Vector3 world  = cam.ScreenToWorldPoint(screen);
        world.z        = 0f;

        SetMoveTarget(world);
    }

    private void SetMoveTarget(Vector3 worldTarget)
    {
        moveTarget = worldTarget;
        hasTarget  = true;

        if (!navReady)
        {
            // Simple mode — không có NavMesh
            cornerIdx = -1;
            return;
        }

        // Snap target lên NavMesh surface
        Vector3 navFrom = new Vector3(transform.position.x, transform.position.y, navMeshZ);
        Vector3 navTo   = worldTarget;
        navTo.z         = navMeshZ;

        // Snap to nearest walkable
        if (NavMesh.SamplePosition(navTo, out var hitTo, 5f, NavMesh.AllAreas))
            navTo = hitTo.position;

        navPath.ClearCorners();
        if (NavMesh.CalculatePath(navFrom, navTo, NavMesh.AllAreas, navPath) &&
            navPath.corners.Length > 0)
        {
            cornerIdx = 0;

            // Gửi path lên server (async — không block)
            StopCoroutine("SendPathCoroutine");
            StartCoroutine(SendPathCoroutine());
        }
        else
        {
            // CalculatePath thất bại → simple straight-line
            cornerIdx = -1;
            Debug.LogWarning($"[LocalPlayer] CalculatePath failed → simple mode target={worldTarget}");
        }
    }

    // ─── Path Movement ────────────────────────────────────────────────────────

    private void UpdatePathMovement()
    {
        if (!hasTarget)
        {
            MoveInput = Vector2.zero;
            return;
        }

        if (navReady && cornerIdx >= 0 && navPath.corners.Length > 0)
        {
            FollowNavPath();
        }
        else
        {
            MoveToTargetStraight();
        }
    }

    private void FollowNavPath()
    {
        while (cornerIdx < navPath.corners.Length)
        {
            Vector3 corner  = navPath.corners[cornerIdx];
            Vector2 c2D     = new Vector2(corner.x, corner.y);
            Vector2 pos2D   = new Vector2(transform.position.x, transform.position.y);
            float   dist    = Vector2.Distance(pos2D, c2D);

            if (dist < 0.15f) { cornerIdx++; continue; } // đến corner này → tiếp tục

            // Di chuyển về corner
            Vector2 dir = (c2D - pos2D).normalized;
            MoveInput   = dir;
            transform.position += new Vector3(dir.x, dir.y, 0f) * moveSpeed * Time.deltaTime;
            return;
        }

        // Đã đến cuối path
        MoveInput = Vector2.zero;
        hasTarget = false;
        cornerIdx = -1;
    }

    private void MoveToTargetStraight()
    {
        float dist = Vector2.Distance(
            new Vector2(transform.position.x, transform.position.y),
            new Vector2(moveTarget.x, moveTarget.y));

        if (dist > 0.15f)
        {
            Vector3 dir = (moveTarget - transform.position);
            dir.z       = 0f;
            dir         = dir.normalized;
            MoveInput   = new Vector2(dir.x, dir.y);
            transform.position += dir * moveSpeed * Time.deltaTime;
        }
        else
        {
            MoveInput = Vector2.zero;
            hasTarget = false;
        }
    }

    private void ClearPath()
    {
        navPath?.ClearCorners();
        cornerIdx = -1;
        hasTarget = false;
    }

    // ─── Server Path Send ─────────────────────────────────────────────────────

    private System.Collections.IEnumerator SendPathCoroutine()
    {
        yield return null; // 1 frame delay để CalculatePath hoàn thành (đã sync nhưng yield cho consistency)

        if (navPath != null && navPath.corners.Length > 0)
        {
            NetworkManager.Instance?.SendMovePath(playerID, navPath.corners);
            Debug.Log($"[LocalPlayer] Sent path: {navPath.corners.Length} waypoints → " +
                      $"({navPath.corners[^1].x:F1},{navPath.corners[^1].y:F1})");
        }
    }

    // ─── Aim Direction ────────────────────────────────────────────────────────

    private void UpdateAimDirection()
    {
        if (cam == null) return;
        Vector3 screen = Input.mousePosition;
        screen.z = Mathf.Abs(cam.transform.position.z);
        Vector3 world  = cam.ScreenToWorldPoint(screen);
        world.z = 0f;
        Vector2 dir = (world - transform.position);
        AimDirection = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
    }

    // ─── UDP Send ─────────────────────────────────────────────────────────────

    private void TrySendUDP()
    {
        if (NetworkManager.Instance == null) return;

        sendTimer   += Time.deltaTime;
        udpLogTimer += Time.deltaTime;

        bool timeout    = sendTimer >= 1f / sendRate;
        bool hasNewDest = hasTarget && (moveTarget - lastSentPos).sqrMagnitude > 0.01f;

        if (!timeout && !hasNewDest) return;

        sendTimer   = 0f;
        lastSentPos = moveTarget;

        Vector2 dest = new Vector2(moveTarget.x, moveTarget.y);
        NetworkManager.Instance.SendMoveInput(playerID, dest, MoveInput);

        if (udpLogTimer >= 5f)
        {
            udpLogTimer = 0f;
            Debug.Log($"[LocalPlayer] UDP dest=({dest.x:F1},{dest.y:F1}) " +
                      $"input=({MoveInput.x:F1},{MoveInput.y:F1}) speed={moveSpeed:F1}");
        }
    }

    // ─── Server Correction ────────────────────────────────────────────────────

    private void OnWorldState(WorldStatePacket ws)
    {
        if (aliveTimer < snapWarmup) return;

        foreach (var snap in ws.Players)
        {
            if (snap.PlayerID != playerID) continue;

            Vector3 serverPos = new Vector3(snap.X, snap.Y, 0f);
            float   drift     = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.y),
                new Vector2(serverPos.x, serverPos.y));

            if (drift > snapThreshold)
            {
                ApplyPosition(serverPos);
                Debug.Log($"[LocalPlayer] ⚡ Teleport snap drift={drift:F1}m → ({serverPos.x:F1},{serverPos.y:F1})");
            }
            break;
        }
    }

    private void ApplyPosition(Vector3 pos)
    {
        pos.z = 0f;
        transform.position = pos;
        // Recalculate path từ vị trí mới nếu đang có target
        if (hasTarget) SetMoveTarget(moveTarget);
    }

    // ─── Combat ───────────────────────────────────────────────────────────────

    private void OnDieEvent(DieEventPacket die)
    {
        if (die.PlayerID != playerID) return;
        isDead    = true;
        ClearPath();
        MoveInput = Vector2.zero;
        Debug.Log($"[LocalPlayer] 💀 Died! Killer={die.KillerID}");
    }

    private void OnRespawnAck(RespawnAckPacket ack)
    {
        if (ack.PlayerID != playerID) return;
        isDead     = false;
        aliveTimer = 0f;
        MoveInput  = Vector2.zero;
        ApplyPosition(new Vector3(ack.X, ack.Y, 0f));
        Debug.Log($"[LocalPlayer] 🔄 Respawn ({ack.X:F1},{ack.Y:F1}) HP={ack.HP}");
    }
}
