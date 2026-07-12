using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// LocalPlayer — click-to-move online player controller.
///
/// Implements IHumanController để drive Human StateMachine (MoveState, IdleState, etc.)
/// NavMeshAgent tính path → velocity → MoveInput → Human.SetVelocity(MoveInput * MoveSpeed)
///
/// DIP: Human states chỉ biết IHumanController, không biết LocalPlayer.
/// SRP: LocalPlayer chỉ xử lý input + NavMesh + network sync.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class LocalPlayer : MonoBehaviour, IHumanController
{
    // ─── IHumanController ────────────────────────────────────────────────────

    /// <summary>
    /// Hướng di chuyển từ NavMeshAgent velocity, chuẩn hóa về [-1,1].
    /// HumanMoveState dùng: owner.SetVelocity(MoveInput * MoveSpeed)
    /// </summary>
    public Vector2 MoveInput     { get; private set; }
    public Vector2 AimDirection  { get; private set; }
    public bool    IsDashPressed    => false; // click-to-move không có dash key
    public bool    IsDashPressedRaw => false;
    public bool    IsAttackPressed  => Input.GetMouseButtonDown(0); // chuột trái = attack

    public void StopNavigation()
    {
        if (agent != null && agent.isOnNavMesh)
            agent.SetDestination(transform.position);
        MoveInput = Vector2.zero;
    }

    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Network Sync")]
    [SerializeField] private float sendRate      = 20f;
    [SerializeField] private float snapThreshold = 5f;    // chỉ snap khi lệch > 5m
    [SerializeField] private float snapWarmup    = 10f;   // chờ 10s sau spawn (tránh snap về 0,0)
    [SerializeField] private float correctionLerp= 8f;

    [Header("Input")]
    [SerializeField] private int moveMouseButton = 1; // chuột phải = di chuyển

    // ─── Runtime ─────────────────────────────────────────────────────────────

    private uint         playerID;
    private NavMeshAgent agent;
    private Camera       cam;
    private Human        human;   // reference đến Human component (set speed đúng)

    // NavMesh fallback
    private Vector3 moveTarget;
    private bool    hasTarget;
    private bool    useNavMesh;

    // Network
    private Vector2 lastSentDir;
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
    private const float RetryInterval = 0.25f;
    private const int   MaxRetries    = 20;

    // ─── Init ─────────────────────────────────────────────────────────────────

    public void Initialize(GameSession session)
    {
        playerID = session.PlayerID;

        agent = GetComponent<NavMeshAgent>();
        human = GetComponent<Human>();

        // ── Cấu hình NavMeshAgent cho 2D ──────────────────────────────────
        agent.updateRotation = false;
        agent.updateUpAxis   = false;

        // QUAN TRỌNG: tắt auto-move để NavMesh không fight Rigidbody2D
        // Thay vào đó, dùng agent.desiredVelocity → Rigidbody2D qua Human.SetVelocity
        agent.updatePosition = false;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

        // Sync speed với Human MoveSpeed (Archer=10, Warrior=10, ...)
        if (human != null)
        {
            agent.speed            = human.MoveSpeed;
            agent.stoppingDistance = 0.15f;
            Debug.Log($"[LocalPlayer] Agent speed = {agent.speed} (Human.MoveSpeed)");
        }
        else
        {
            agent.speed            = 5f;
            agent.stoppingDistance = 0.15f;
        }

        // Inject controller vào Human StateMachine
        if (human != null)
            human.Controller = this;

        cam = Camera.main;

        var levelSystem = GetComponent<LevelSystem>();
        if (levelSystem != null)
            levelSystem.SetLevelDirect(session.Level, session.CurrentExp);

        // Spawn position từ server (có thể là (0,0) nếu player mới)
        Vector3 serverSpawnPos = new Vector3(session.SpawnX, session.SpawnY, 0f);

        // Tìm điểm hợp lệ trên NavMesh gần nhất (tránh spawn ngoài map)
        Vector3 actualSpawn = FindValidNavMeshPosition(serverSpawnPos);
        transform.position = actualSpawn;
        moveTarget         = actualSpawn;

        if (actualSpawn != serverSpawnPos)
            Debug.LogWarning($"[LocalPlayer] Server spawn {serverSpawnPos} ngoài NavMesh — " +
                             $"dùng điểm gần nhất: {actualSpawn}");

        // Warp agent về vị trí thực tế
        agent.Warp(actualSpawn);
        if (agent.isOnNavMesh)
        {
            useNavMesh    = true;
            isInitialized = true;
            Debug.Log($"[LocalPlayer] ✅ NavMesh OK — ID={playerID} speed={agent.speed} Pos={actualSpawn}");
        }
        else
        {
            useNavMesh = false;
            Debug.LogWarning($"[LocalPlayer] NavMesh chưa sẵn, retry...");
        }
    }

    /// <summary>
    /// Tìm vị trí hợp lệ trên NavMesh gần nhất với pos.
    /// Nếu pos nằm trên NavMesh rồi → trả về luôn.
    /// </summary>
    private Vector3 FindValidNavMeshPosition(Vector3 pos)
    {
        const float searchRadius = 50f; // tìm trong bán kính 50 units
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, searchRadius, UnityEngine.AI.NavMesh.AllAreas))
            return new Vector3(hit.position.x, hit.position.y, 0f);
        return pos; // không tìm được → giữ nguyên
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
                agent.Warp(transform.position);

                if (agent.isOnNavMesh)
                {
                    useNavMesh    = true;
                    isInitialized = true;

                    // Sync speed sau khi NavMesh ready (Human.Awake có thể chưa xong)
                    if (human != null) agent.speed = human.MoveSpeed;

                    Debug.Log($"[LocalPlayer] ✅ NavMesh OK sau retry={navMeshRetryCount} speed={agent.speed}");
                }
                else if (navMeshRetryCount >= MaxRetries)
                {
                    useNavMesh    = false;
                    isInitialized = true;
                    agent.enabled = false; // tắt agent, dùng simple mode
                    Debug.LogWarning("[LocalPlayer] NavMesh timeout → Simple mode (không pathfinding).");
                }
            }
            return;
        }

        if (isDead) return;

        aliveTimer += Time.deltaTime;

        HandleClickInput();
        UpdateMoveInput();  // cập nhật IHumanController.MoveInput từ agent velocity
        UpdateAimDirection();
        TrySendUDP();
        // Animator & velocity được xử lý bởi Human StateMachine
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

        moveTarget = world;
        hasTarget  = true;

        if (useNavMesh && agent.enabled && agent.isOnNavMesh)
            agent.SetDestination(world);
    }

    /// <summary>
    /// Cập nhật MoveInput từ NavMesh desiredVelocity (hướng muốn đi theo path).
    /// Sync agent.nextPosition để NavMesh luôn biết player đang ở đâu (vì updatePosition=false).
    /// HumanMoveState.FixedUpdate() sẽ gọi: owner.SetVelocity(MoveInput * MoveSpeed).
    /// </summary>
    private void UpdateMoveInput()
    {
        if (useNavMesh && agent.enabled)
        {
            // Sync vị trí thực tế về NavMeshAgent (vì updatePosition=false)
            agent.nextPosition = transform.position;

            // ── Manual stopping distance (agent.stoppingDistance không hoạt động khi updatePosition=false) ──
            bool hasPath = agent.hasPath && !agent.pathPending;
            if (hasPath)
            {
                float distToDest = Vector3.Distance(transform.position, agent.destination);
                if (distToDest <= agent.stoppingDistance + 0.05f)
                {
                    // Đã đến đích → dừng hẳn
                    agent.ResetPath();   // xóa path → desiredVelocity = 0
                    MoveInput = Vector2.zero;
                    hasTarget = false;
                    return;
                }
            }

            // desiredVelocity = hướng NavMesh muốn đi (theo path, tránh vật cản)
            Vector2 desired = new Vector2(agent.desiredVelocity.x, agent.desiredVelocity.y);
            MoveInput = desired.sqrMagnitude > 0.01f ? desired.normalized : Vector2.zero;
        }
        else
        {
            // Simple mode: di chuyển thẳng đến target (không NavMesh)
            if (hasTarget)
            {
                float dist = Vector3.Distance(transform.position, moveTarget);
                if (dist > 0.15f)
                {
                    Vector3 dir = (moveTarget - transform.position).normalized;
                    MoveInput = new Vector2(dir.x, dir.y);
                    transform.position += dir * (human != null ? human.MoveSpeed : 5f) * Time.deltaTime;
                }
                else
                {
                    MoveInput = Vector2.zero;
                    hasTarget = false;
                }
            }
            else
            {
                MoveInput = Vector2.zero;
            }
        }
    }

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
        bool posChanged = (transform.position - lastSentPos).sqrMagnitude > 0.0001f;
        bool dirChanged = (MoveInput - lastSentDir).sqrMagnitude > 0.001f;

        if (!timeout && !posChanged && !dirChanged) return;

        sendTimer   = 0f;
        lastSentDir = MoveInput;
        lastSentPos = transform.position;

        Vector2 pos = new Vector2(transform.position.x, transform.position.y);
        NetworkManager.Instance.SendMoveInput(playerID, pos, MoveInput);

        if (udpLogTimer >= 5f)
        {
            udpLogTimer = 0f;
            float speed = useNavMesh && agent.enabled ? agent.velocity.magnitude
                          : (MoveInput.sqrMagnitude > 0.01f ? (human?.MoveSpeed ?? 5f) : 0f);
            Debug.Log($"[LocalPlayer] UDP ♦ pos=({pos.x:F1},{pos.y:F1}) " +
                      $"input=({MoveInput.x:F1},{MoveInput.y:F1}) speed={speed:F1}");
        }
    }

    // ─── Server Correction ────────────────────────────────────────────────────

    private void OnWorldState(WorldStatePacket ws)
    {
        // Bỏ qua trong warmup — tránh snap về (0,0) khi server chưa nhận pos thực từ client
        if (aliveTimer < snapWarmup) return;

        foreach (var snap in ws.Players)
        {
            if (snap.PlayerID != playerID) continue;

            Vector3 serverPos = new Vector3(snap.X, snap.Y, 0f);
            float   drift     = Vector3.Distance(transform.position, serverPos);

            if (drift > snapThreshold)
            {
                // Validate: server pos phải nằm trên NavMesh, không snap ra ngoài map
                Vector3 validPos = FindValidNavMeshPosition(serverPos);
                float   validDrift = Vector3.Distance(serverPos, validPos);

                if (validDrift > 2f)
                {
                    // Server pos quá xa NavMesh → có thể server data cũ/sai, bỏ qua
                    Debug.LogWarning($"[LocalPlayer] Snap BLOCKED: server ({snap.X:F1},{snap.Y:F1}) " +
                                     $"ngoài NavMesh {validDrift:F1}m — bỏ qua");
                    break;
                }

                ApplyPosition(validPos);
                Debug.Log($"[LocalPlayer] ⚡ Snap drift={drift:F1}m → ({validPos.x:F1},{validPos.y:F1})");
            }
            else if (drift > 0.3f)
            {
                // Soft correction: chỉ lerp nhẹ, không Warp
                Vector3 corrected = Vector3.Lerp(transform.position, serverPos, correctionLerp * Time.deltaTime);
                ApplyPosition(corrected);
            }
            break;
        }
    }

    /// <summary>Áp vị trí — Warp nếu NavMesh, transform nếu không.</summary>
    private void ApplyPosition(Vector3 pos)
    {
        pos.z = 0f;
        if (useNavMesh && agent.enabled && agent.isOnNavMesh)
            agent.Warp(pos);
        else
            transform.position = pos;
    }

    // ─── Combat ───────────────────────────────────────────────────────────────

    private void OnDieEvent(DieEventPacket die)
    {
        if (die.PlayerID != playerID) return;
        isDead    = true;
        hasTarget = false;
        MoveInput = Vector2.zero;
        if (agent.enabled) { agent.ResetPath(); agent.velocity = Vector3.zero; }
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
