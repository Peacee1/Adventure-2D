using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Local player controller — right-click to move (NavMesh pathfinding),
/// D to dash, left-click to attack.
///
/// Implements IHumanController so Human knows nothing about the input source.
/// SRP: only reads input and converts it to IHumanController values.
/// DIP: Human receives IHumanController, not Player directly.
///
/// NavMesh setup required:
///   1. Install AI Navigation package (Package Manager).
///   2. Add NavMeshSurface to the scene and bake.
///   3. Add NavMeshAgent to this GameObject.
///
/// Works without NavMeshAgent (falls back to direct linear movement).
/// </summary>
public class Player : MonoBehaviour, IHumanController
{
    // ─── State ────────────────────────────────────────────────────────────────

    private Human        human;
    private NavMeshAgent agent;
    private Camera       cachedCamera;       // cache tránh Camera.main mỗi frame
    private float        lastSyncedSpeed;    // tránh set agent.speed mỗi frame

    private Vector2 destination;
    private bool    hasDestination;
    private bool    dashPressedThisFrame;
    private bool    attackPressedThisFrame;

    private const float ARRIVAL_THRESHOLD = 0.25f;

    // ─── IHumanController ────────────────────────────────────────────────────

    /// <summary>
    /// Direction toward the NavMesh destination.
    /// Returns Vector2.zero when arrived or no destination is set.
    /// </summary>
    public Vector2 MoveInput
    {
        get
        {
            if (IsLocked || !hasDestination) return Vector2.zero;

            // NavMesh path direction (preferred)
            if (agent != null && agent.isOnNavMesh && !agent.pathPending)
            {
                // For 2D XY plane: agent moves in XY so desiredVelocity is in XY
                Vector2 desired = new Vector2(agent.desiredVelocity.x, agent.desiredVelocity.y);
                if (desired.sqrMagnitude > 0.01f) return desired.normalized;
            }

            // Fallback: direct linear movement toward destination
            Vector2 toTarget = destination - (Vector2)transform.position;
            if (toTarget.sqrMagnitude < ARRIVAL_THRESHOLD * ARRIVAL_THRESHOLD)
            {
                hasDestination = false;
                return Vector2.zero;
            }
            return toTarget.normalized;
        }
    }

    /// <summary>
    /// Normalized direction from character toward the mouse cursor (world space).
    /// Used for dash direction and Archer aim.
    /// </summary>
    public Vector2 AimDirection
    {
        get
        {
            if (human == null) return Vector2.right;
            Camera cam = GetCamera();
            if (cam == null) return Vector2.right;
            Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = human.transform.position.z;
            Vector2 dir = ((Vector2)mouseWorld - (Vector2)human.transform.position).normalized;
            return dir == Vector2.zero ? Vector2.right : dir;
        }
    }

    public bool IsDashPressed    => !IsLocked && dashPressedThisFrame;
    public bool IsDashPressedRaw => dashPressedThisFrame;            // không bị chặn bởi lock
    public bool IsAttackPressed  => !IsLocked && attackPressedThisFrame;

    private bool IsLocked => human != null && human.IsInputLocked;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        human        = GetComponent<Human>();
        agent        = GetComponent<NavMeshAgent>();
        cachedCamera = Camera.main;

        if (human == null)
        {
            Debug.LogError("[Player] No Human component (or subclass) found. " +
                           "Make sure Player.cs and a Human subclass are on the same object.");
            enabled = false;
            return;
        }

        if (agent != null)
        {
            agent.updatePosition = false; // Rigidbody2D controls actual position
            agent.updateRotation = false;
            agent.updateUpAxis   = false;
        }
        else
        {
            Debug.LogWarning("[Player] No NavMeshAgent found — using direct linear movement.");
        }
    }

    private void Start()
    {
        human.SetController(this);
    }

    private void Update()
    {
        ReadInput();
        SyncNavMeshAgent();
    }

    // ─── Private ──────────────────────────────────────────────────────────────

    private void ReadInput()
    {
        // Right-click → set move destination
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            SetMoveDestination(GetMouseWorldPosition());
        }

        dashPressedThisFrame   = Keyboard.current != null && Keyboard.current.dKey.wasPressedThisFrame;
        attackPressedThisFrame = Mouse.current    != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    private void SetMoveDestination(Vector2 worldPos)
    {
        destination    = worldPos;
        hasDestination = true;

        if (agent != null && agent.isOnNavMesh)
        {
            // 2D game lives on the XY plane → NavMesh destination stays at Z=0
            agent.SetDestination(new Vector3(worldPos.x, worldPos.y, 0f));
        }
    }

    /// <inheritdoc/>
    public void StopNavigation()
    {
        hasDestination = false;

        if (agent != null && agent.isOnNavMesh)
        {
            // Dừng agent tại vị trí hiện tại — không cho agent tiếp tục trượt sau khi bắt đầu attack
            agent.SetDestination(transform.position);
        }
    }

    private void SyncNavMeshAgent()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        // Chỉ set speed khi thực sự thay đổi — tránh NavMesh recalculate path mỗi frame
        float currentSpeed = human.MoveSpeed;
        if (!Mathf.Approximately(currentSpeed, lastSyncedSpeed))
        {
            agent.speed      = currentSpeed;
            lastSyncedSpeed  = currentSpeed;
        }

        // Keep the NavMesh agent in sync with the actual Rigidbody2D position
        agent.nextPosition = transform.position;

        // Detect arrival
        if (!agent.pathPending && agent.remainingDistance <= ARRIVAL_THRESHOLD)
        {
            hasDestination = false;
            agent.ResetPath();
        }
    }

    /// <summary>Trả về Camera đã cache; tự refresh nếu bị null (scene reload, v.v.).</summary>
    private Camera GetCamera()
    {
        if (cachedCamera == null) cachedCamera = Camera.main;
        return cachedCamera;
    }

    private Vector2 GetMouseWorldPosition()
    {
        Camera cam = GetCamera();
        if (cam == null) return Vector2.zero;
        Vector3 pos = cam.ScreenToWorldPoint(Input.mousePosition);
        return new Vector2(pos.x, pos.y);
    }
}
