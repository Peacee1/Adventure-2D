using UnityEngine;

/// <summary>
/// RemotePlayer — đại diện visual của player khác trên màn hình local.
/// Nhận WorldState snapshot mỗi tick và interpolate vị trí cho mượt.
///
/// SRP: chỉ xử lý visual representation, không chứa game logic.
///
/// QUAN TRỌNG: RemotePlayer bypass hoàn toàn Human StateMachine.
/// Vị trí được set trực tiếp qua Rigidbody2D.MovePosition (tránh xung đột physics).
/// Animator được điều khiển trực tiếp từ snapshot.State — không qua IHumanController.
/// </summary>
public class RemotePlayer : MonoBehaviour
{
    // ─── State ────────────────────────────────────────────────────────────────

    public uint    PlayerID { get; private set; }
    public string  Username { get; private set; }
    public byte    JobClass { get; private set; }

    private Vector2 targetPos;
    private Vector2 currentPos;
    private Vector2 lastDir;

    [SerializeField] private float lerpSpeed     = 12f;  // smooth nhưng không quá chậm
    [SerializeField] private float snapThreshold = 10f;  // snap thẳng nếu quá xa (lag spike)

    // Components
    private Rigidbody2D rb;
    private Animator    animator;

    private static readonly int SpeedHash = Animator.StringToHash("speed");

    // ─── Init ─────────────────────────────────────────────────────────────────

    public void Initialize(PlayerInfo info)
    {
        PlayerID = info.PlayerID;
        Username = info.Username;
        JobClass = info.JobClass;

        currentPos = new Vector2(info.X, info.Y);
        targetPos  = currentPos;

        rb       = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();

        // Remote player phải là Kinematic để RemotePlayer.Update kiểm soát hoàn toàn
        if (rb != null)
        {
            rb.bodyType        = RigidbodyType2D.Kinematic;
            rb.linearVelocity  = Vector2.zero;
        }

        // Tắt tất cả collider vật lý — remote player KHÔNG được block local player
        foreach (var col in GetComponentsInChildren<Collider2D>())
            col.isTrigger = true;

        // Tắt NavMeshObstacle nếu có — không được block NavMesh pathfinding của local player
        foreach (var obs in GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>())
            obs.enabled = false;

        transform.position = new Vector3(currentPos.x, currentPos.y, 0f);

        gameObject.name = $"Remote_{info.PlayerID}_{info.Username}";
        Debug.Log($"[RemotePlayer] Init: ID={info.PlayerID} User={info.Username} Job={info.JobClass} Pos={currentPos}");
    }

    // ─── Unity Lifecycle ─────────────────────────────────────────────────────

    private void Update()
    {
        float dist = Vector2.Distance(currentPos, targetPos);

        // Snap ngay nếu quá xa (lag spike hoặc respawn)
        if (dist > snapThreshold)
        {
            currentPos = targetPos;
        }
        else
        {
            // Lerp mượt về targetPos
            currentPos = Vector2.Lerp(currentPos, targetPos, lerpSpeed * Time.deltaTime);
        }

        // Di chuyển Rigidbody2D (không conflict physics vì Kinematic)
        if (rb != null)
            rb.MovePosition(currentPos);
        else
            transform.position = new Vector3(currentPos.x, currentPos.y, transform.position.z);

        // Flip hướng nhìn
        if      (lastDir.x < -0.01f) transform.localScale = new Vector3(-Mathf.Abs(transform.localScale.x), transform.localScale.y, 1f);
        else if (lastDir.x >  0.01f) transform.localScale = new Vector3( Mathf.Abs(transform.localScale.x), transform.localScale.y, 1f);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Áp dụng snapshot từ WorldState UDP packet.</summary>
    public void ApplySnapshot(PlayerSnapshot snap)
    {
        targetPos = new Vector2(snap.X, snap.Y);
        lastDir   = new Vector2(snap.DirX, snap.DirY);

        // Điều khiển Animator trực tiếp từ State — không qua StateMachine
        // State 1 = Move, State 0 = Idle, State 4 = Dead
        if (animator != null)
        {
            float speedParam = (snap.State == 1) ? 1f : 0f;
            animator.SetFloat(SpeedHash, speedParam);
        }
    }
}
