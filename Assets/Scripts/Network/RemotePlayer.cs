using UnityEngine;

/// <summary>
/// RemotePlayer — đại diện visual của player khác trên màn hình local.
/// Nhận WorldState snapshot mỗi tick và interpolate vị trí cho mượt.
///
/// SRP: chỉ xử lý visual representation, không chứa game logic.
/// </summary>
public class RemotePlayer : MonoBehaviour
{
    // ─── State ────────────────────────────────────────────────────────────────

    public uint   PlayerID { get; private set; }
    public string Username { get; private set; }
    public byte   JobClass { get; private set; }
    public Vector2 LastDir { get; private set; }  // hướng nhìn từ snapshot (cho RemoteHumanController)

    private Vector2 targetPos;
    private Vector2 currentPos;

    [SerializeField] private float lerpSpeed = 15f;

    // Optional components
    private Animator animator;

    // ─── Init ─────────────────────────────────────────────────────────────────

    public void Initialize(PlayerInfo info)
    {
        PlayerID = info.PlayerID;
        Username = info.Username;
        JobClass = info.JobClass;

        currentPos = new Vector2(info.X, info.Y);
        targetPos  = currentPos;
        transform.position = new Vector3(currentPos.x, currentPos.y, 0f);

        animator = GetComponentInChildren<Animator>();

        gameObject.name = $"Remote_{info.PlayerID}_{info.Username}";
        Debug.Log($"[RemotePlayer] Init: ID={info.PlayerID} User={info.Username} Job={info.JobClass} Pos={currentPos}");
    }

    // ─── Unity Lifecycle ─────────────────────────────────────────────────────

    private void Update()
    {
        // Smooth interpolation đến server position
        currentPos = Vector2.Lerp(currentPos, targetPos, lerpSpeed * Time.deltaTime);
        transform.position = new Vector3(currentPos.x, currentPos.y, transform.position.z);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Áp dụng snapshot từ WorldState UDP packet (gọi từ GameSceneBootstrap).</summary>
    public void ApplySnapshot(PlayerSnapshot snap)
    {
        targetPos = new Vector2(snap.X, snap.Y);
        LastDir   = new Vector2(snap.DirX, snap.DirY);

        // Flip hướng nhìn
        if      (snap.DirX < -0.01f) transform.localScale = new Vector3(-Mathf.Abs(transform.localScale.x), transform.localScale.y, 1f);
        else if (snap.DirX >  0.01f) transform.localScale = new Vector3( Mathf.Abs(transform.localScale.x), transform.localScale.y, 1f);

        // Animator
        if (animator != null)
            animator.SetFloat("speed", snap.State == 1 ? 1f : 0f); // 1 = Move
    }
}
