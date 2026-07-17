using UnityEngine;
using UnityEngine.AI;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// Base class cho mọi nhân vật Human (Kiếm Sĩ, Pháp Sư, Cung Thủ, ...).
///
/// Trách nhiệm duy nhất: quản lý entity (stats, physics, StateMachine).
/// KHÔNG biết ai đang control — controller được inject qua SetController().
///
/// Multiplayer-ready:
///   Local player → Player.cs gắn thêm vào cùng GO, gọi SetController(this)
///   Remote player → NetworkController.cs gắn thêm vào cùng GO
///   AI           → AIController.cs gắn thêm vào cùng GO
/// </summary>
public class Human : BaseObject
{
    // ─── Serialized Settings ──────────────────────────────────────────────────

    [Header("Job Class")]
    [SerializeField] private JobClass jobClass = JobClass.Warrior;
    [SerializeField] private JobClassDatabase jobClassDatabase;

    [Header("Healing Power (Trị Liệu Sư)")]
    [SerializeField] private float healingPower = 0f;

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Dash Settings")]
    [SerializeField] private float dashSpeedMultiplier = 2.5f; // kept for reference only — server drives dash physics

    // ─── Runtime Internal ─────────────────────────────────────────────────────

    private Rigidbody2D rb;        // optional — chỉ dùng cho Dash khi không có NavMesh
    private NavMeshAgent navAgent;  // cache để kiểm tra updatePosition mode
    private BuffSystem buffSystem;
    protected StateMachine<Human> stateMachine;
    private GameObject dashTrailObject;

    // ─── Public Read-Only API (dùng bởi States) ───────────────────────────────

    /// <summary>
    /// Controller hiện tại đang điều khiển Human này.
    /// Được set bởi Player.cs / AI / NetworkController qua SetController().
    /// </summary>
    public IHumanController Controller { get; set; }

    /// <summary>Animator controller — shared logic for Unity Animator communication.</summary>
    public HumanAnimatorController AnimatorController { get; private set; }

    /// <summary>Dash speed = MoveSpeed × dashSpeedMultiplier (default 250%) — for reference only.</summary>
    public float DashSpeed     => moveSpeed * dashSpeedMultiplier;
    /// <summary>Attack animation duration — passed to AttackState for animator speed scaling.</summary>
    public float AttackDuration => AttackSpeed;

    // ─── Public Methods (gọi bởi States) ─────────────────────────────────────

    /// <summary>
    /// Set velocity — no-op khi NavMesh đang kiểm soát vị trí (updatePosition=true).
    /// Dash sử dụng DashMove() để bypass NavMesh.
    /// </summary>
    public void SetVelocity(Vector2 velocity)
    {
        // Khi NavMesh đang kiểm soát transform → không đụng vào rb.velocity
        if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh && navAgent.updatePosition)
            return;
        if (rb != null)
            rb.linearVelocity = velocity;
    }

    public void RecordDash() { /* no-op — cooldown managed by server */ }

    public void SetDashTrailActive(bool active)
    {
        if (dashTrailObject != null)
            dashTrailObject.SetActive(active);
    }

    // ─── Server-driven State Control ─────────────────────────────────────────

    /// <summary>
    /// Forces the client StateMachine to the state matching the server's broadcast.
    /// Called by LocalPlayer every time the server state changes in WorldState.
    /// Maps server State IDs (0-5) to client SM states.
    /// </summary>
    public void ForceState(byte serverState)
    {
        if (stateMachine == null) return;

        switch (serverState)
        {
            case 0: // Idle
                if (!stateMachine.IsInState<HumanIdleState>())
                    stateMachine.ChangeState<HumanIdleState>();
                break;
            case 1: // Move
                if (!stateMachine.IsInState<HumanMoveState>())
                    stateMachine.ChangeState<HumanMoveState>();
                break;
            case 2: // Dash
                if (!stateMachine.IsInState<HumanDashState>())
                    stateMachine.ChangeState<HumanDashState>();
                break;
            case 3: // Attack
                if (!stateMachine.IsInState<HumanAttackState>())
                    stateMachine.ChangeState<HumanAttackState>();
                break;
            case 4: // Dead
                if (!stateMachine.IsInState<HumanIdleState>())
                    stateMachine.ChangeState<HumanIdleState>();
                break;
            case 5: // DashEnd
                if (!stateMachine.IsInState<HumanDashEndState>())
                    stateMachine.ChangeState<HumanDashEndState>();
                break;
        }
    }

    /// <summary>
    /// Khoá / mở khoá vị trí.
    /// Khi NavMesh active: stop/resume agent.
    /// Khi không có: freeze Rigidbody2D.
    /// </summary>
    public void FreezePosition(bool freeze)
    {
        if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh && navAgent.updatePosition)
        {
            if (freeze) { navAgent.isStopped = true;  navAgent.velocity = Vector3.zero; }
            else        { navAgent.isStopped = false; }
            return;
        }
        if (rb == null) return;
        rb.linearVelocity = Vector2.zero;
        rb.constraints = freeze
            ? RigidbodyConstraints2D.FreezeAll
            : RigidbodyConstraints2D.FreezeRotation;
    }

    /// <summary>
    /// Flip hướng nhìn dựa trên trục X của velocity/input.
    /// Đảm bảo nameText không bị lật ngược theo.
    /// </summary>
    public void FaceDirection(float xVelocity)
    {
        if (xVelocity < -0.01f)
        {
            transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            if (nameTextComponent != null)
                nameTextComponent.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
        else if (xVelocity > 0.01f)
        {
            transform.localRotation = Quaternion.identity;
            if (nameTextComponent != null)
                nameTextComponent.transform.localRotation = Quaternion.identity;
        }
    }

    // ─── Controller Injection ─────────────────────────────────────────────────

    /// <summary>
    /// Đăng ký controller điều khiển Human này.
    /// Gọi từ Player.Start() hoặc AI/NetworkController.Start().
    /// </summary>
    public void SetController(IHumanController controller)
    {
        Controller = controller;
        Debug.Log($"[Human:{gameObject.name}] Controller set to {controller?.GetType().Name ?? "null"}");
    }



    // ─── Public Entry Points (gọi từ ngoài hoặc skill system) ────────────────

    /// <summary>
    /// Yêu cầu thực hiện attack. Override trong subclass để thay đổi behavior.
    /// Tuân thủ OCP: subclass mở rộng, không sửa Human.
    /// </summary>
    public virtual void RequestAttack()
    {
        if (stateMachine == null) return;
        if (!stateMachine.IsInState<HumanAttackState>())
            stateMachine.ChangeState<HumanAttackState>();
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        SetupPhysics();
        SetupJobClass();
        EnsureCameraFollow();
    }

    protected virtual void Start()
    {
        SetupAnimator();
        SetupDashTrail();
        SetupStateMachine();
    }

    private void Update()
    {
        stateMachine?.Update();
    }

    private void FixedUpdate()
    {
        stateMachine?.FixedUpdate();
    }

    // ─── Setup Helpers ────────────────────────────────────────────────────────

    private void SetupPhysics()
    {
        CharacterController oldController = GetComponent<CharacterController>();
        if (oldController != null) Destroy(oldController);

        // Cache NavMeshAgent nếu có (LocalPlayer) — kiểm tra updatePosition mode
        navAgent = GetComponent<NavMeshAgent>();

        // Rigidbody2D là optional: chỉ cần khi không có NavMesh (NPC, Dash fallback)
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
        }

        if (spriteRenderer != null) spriteRenderer.sortingOrder = 5;
    }

    private void SetupJobClass()
    {
        buffSystem = GetComponent<BuffSystem>();
        ApplyJobClassStats();
        if (hp <= 0) hp = maxHp;
        if (mp <= 0) mp = maxMp;
        buffSystem?.SnapshotBaseStats();
    }

    private void SetupAnimator()
    {
        // Tìm trực tiếp bằng GetComponentInChildren — không đi qua collider object
        Animator animator = GetComponentInChildren<Animator>(true);
        AnimatorController = new HumanAnimatorController(animator);
    }

    private void SetupDashTrail()
    {
        Transform squareTransform = transform.Find("Square");
        if (squareTransform != null && squareTransform.childCount > 0)
        {
            dashTrailObject = squareTransform.GetChild(0).gameObject;
            Debug.Log($"[Human:{gameObject.name}] Found dash trail: {dashTrailObject.name}");
        }
        else
        {
            Debug.LogWarning($"[Human:{gameObject.name}] Dash trail not found (child[0] of 'Square').");
        }

        if (dashTrailObject != null) dashTrailObject.SetActive(false);
    }

    /// <summary>
    /// Khởi tạo StateMachine và đăng ký các shared states.
    /// Override trong subclass để thêm state đặc thù (VD: CungThu thêm ChargeState).
    /// Tuân thủ OCP: subclass mở rộng mà không sửa method này.
    /// </summary>
    protected virtual void SetupStateMachine()
    {
        stateMachine = new StateMachine<Human>(this);

        var idleState    = new HumanIdleState   (this, stateMachine, AnimatorController);
        var moveState    = new HumanMoveState   (this, stateMachine, AnimatorController);
        var dashState    = new HumanDashState   (this, stateMachine, AnimatorController);
        var dashEndState = new HumanDashEndState(this, stateMachine, AnimatorController);
        var attackState  = new HumanAttackState (this, stateMachine, AnimatorController);

        stateMachine.RegisterState(idleState);
        stateMachine.RegisterState(moveState);
        stateMachine.RegisterState(dashState);
        stateMachine.RegisterState(dashEndState);
        stateMachine.RegisterState(attackState);

        stateMachine.ChangeState<HumanIdleState>();

        Debug.Log($"[Human:{gameObject.name}] StateMachine initialized: Idle / Move / Dash / DashEnd / Attack.");
    }

    private void EnsureCameraFollow()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;
        if (mainCam.GetComponent<Freeland.Gameplay.CameraFollow>() == null)
        {
            mainCam.gameObject.AddComponent<Freeland.Gameplay.CameraFollow>();
            Debug.Log("[Human] Dynamically attached CameraFollow to Main Camera.");
        }
    }

    // ─── Job Class ────────────────────────────────────────────────────────────

    /// <summary>
    /// Áp dụng chỉ số từ JobClass. Gọi trong Awake hoặc khi đổi chức nghiệp.
    /// </summary>
    public void ApplyJobClassStats()
    {
        JobBaseStats stats = jobClassDatabase != null
            ? jobClassDatabase.GetStats(jobClass)
            : GetHardcodedStats(jobClass);

        if (jobClassDatabase == null)
            Debug.LogWarning("[Human] JobClassDatabase chưa gán — dùng giá trị hardcoded.");

        maxHp        = stats.maxHp;
        maxMp        = stats.maxMp;
        atkPhysical  = stats.atkPhysical;
        atkMagic     = stats.atkMagic;
        defPhysical  = stats.defPhysical;
        defMagic     = stats.defMagic;
        hpRegen      = stats.hpRegen;
        mpRegen      = stats.mpRegen;
        healingPower = stats.healingPower;
        moveSpeed    = stats.speed       > 0f ? stats.speed       : moveSpeed;
        attackRange  = stats.attackRange > 0f ? stats.attackRange : attackRange;
        attackSpeed  = stats.attackSpeed > 0f ? stats.attackSpeed : attackSpeed;
        lifeSteal    = stats.lifeSteal;

        Debug.Log($"[Human:{gameObject.name}] {stats.displayName} | HP:{maxHp} MP:{maxMp} " +
                  $"ATKVat:{atkPhysical} ATKPhep:{atkMagic} Speed:{moveSpeed}");
    }

    private static JobBaseStats GetHardcodedStats(JobClass job)
    {
        return job switch
        {
            JobClass.Archer   => new JobBaseStats { jobClass = job, displayName = "Archer",   maxHp = 800,  maxMp = 300, atkPhysical = 80,  atkMagic = 10,  defPhysical = 30,  defMagic = 20,  hpRegen = 2f, mpRegen = 3f,  healingPower = 0f,   speed = 10f, attackRange = 15f, attackSpeed = 1f },
            JobClass.Warrior  => new JobBaseStats { jobClass = job, displayName = "Warrior",  maxHp = 1100, maxMp = 200, atkPhysical = 90,  atkMagic = 10,  defPhysical = 60,  defMagic = 25,  hpRegen = 4f, mpRegen = 2f,  healingPower = 0f,   speed = 10f, attackRange = 2f,  attackSpeed = 1f },
            JobClass.Mage     => new JobBaseStats { jobClass = job, displayName = "Mage",     maxHp = 650,  maxMp = 600, atkPhysical = 15,  atkMagic = 120, defPhysical = 20,  defMagic = 50,  hpRegen = 1f, mpRegen = 8f,  healingPower = 0f,   speed = 10f, attackRange = 8f,  attackSpeed = 1f },
            JobClass.Healer   => new JobBaseStats { jobClass = job, displayName = "Healer",   maxHp = 750,  maxMp = 700, atkPhysical = 20,  atkMagic = 55,  defPhysical = 25,  defMagic = 60,  hpRegen = 5f, mpRegen = 10f, healingPower = 1.5f, speed = 10f, attackRange = 8f,  attackSpeed = 1f },
            JobClass.Assassin => new JobBaseStats { jobClass = job, displayName = "Assassin", maxHp = 700,  maxMp = 250, atkPhysical = 110, atkMagic = 20,  defPhysical = 25,  defMagic = 15,  hpRegen = 1f, mpRegen = 2f,  healingPower = 0f,   speed = 10f, attackRange = 1.5f,attackSpeed = 1f },
            JobClass.Tank     => new JobBaseStats { jobClass = job, displayName = "Tank",     maxHp = 1500, maxMp = 150, atkPhysical = 50,  atkMagic = 10,  defPhysical = 100, defMagic = 80,  hpRegen = 8f, mpRegen = 1f,  healingPower = 0f,   speed = 10f, attackRange = 2f,  attackSpeed = 1f },
            _                 => JobBaseStats.Default,
        };
    }

    // ─── BaseObject Overrides ─────────────────────────────────────────────────

    public override float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0.1f, value);
    }

    public override float HealingPower
    {
        get => healingPower;
        set => healingPower = Mathf.Max(0f, value);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public JobClass JobClass => jobClass;
    public BuffSystem BuffSystem => buffSystem;

    public JobBaseStats GetCurrentJobStats()
    {
        if (jobClassDatabase != null) return jobClassDatabase.GetStats(jobClass);
        return GetHardcodedStats(jobClass);
    }
}
