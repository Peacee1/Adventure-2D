using UnityEngine;

/// <summary>
/// RemotePlayer — visual representation of other players in the room.
/// Receives WorldState snapshots each tick and interpolates position smoothly.
///
/// SRP: handles visual representation only — no game logic.
///
/// State mapping (mirrors server player.State):
///   0 = Idle    → speed=0, trail off
///   1 = Move    → speed=1
///   2 = Dash    → dash trigger, trail on
///   3 = Attack  → attack trigger
///   4 = Dead    → speed=0
///   5 = DashEnd → speed=0, trail off
/// </summary>
public class RemotePlayer : MonoBehaviour
{
    // ─── State ────────────────────────────────────────────────────────────────

    public uint   PlayerID    { get; private set; }
    public string Username    { get; private set; }
    public byte   JobClass    { get; private set; }
    public uint   CurrentHP   { get; private set; }
    public uint   MaxHP       { get; private set; }

    private Vector2 targetPos;
    private Vector2 currentPos;
    private Vector2 lastDir;
    private byte    lastState = 255; // sentinel

    [SerializeField] private float lerpSpeed     = 12f;
    [SerializeField] private float snapThreshold = 10f;

    // Components
    private Rigidbody2D rb;
    private Animator    animator;
    private GameObject  dashTrailObject;

    private static readonly int SpeedHash  = Animator.StringToHash("speed");
    private static readonly int DashHash   = Animator.StringToHash("dash");
    private static readonly int AttackHash = Animator.StringToHash("attack");

    // ─── Init ─────────────────────────────────────────────────────────────────

    public void Initialize(PlayerInfo info)
    {
        PlayerID   = info.PlayerID;
        Username   = info.Username;
        JobClass   = info.JobClass;
        CurrentHP  = info.HP;
        MaxHP      = info.MaxHP > 0 ? info.MaxHP : info.HP;

        currentPos = new Vector2(info.X, info.Y);
        targetPos  = currentPos;

        rb       = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();

        // Find dash trail the same way Human does
        Transform squareTransform = transform.Find("Square");
        if (squareTransform != null && squareTransform.childCount > 0)
            dashTrailObject = squareTransform.GetChild(0).gameObject;
        if (dashTrailObject != null) dashTrailObject.SetActive(false);

        if (rb != null)
        {
            rb.bodyType       = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
        }

        // Remote players must not physically block the local player
        foreach (var col in GetComponentsInChildren<Collider2D>())
            col.isTrigger = true;
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

        if (dist > snapThreshold)
            currentPos = targetPos;
        else
            currentPos = Vector2.Lerp(currentPos, targetPos, lerpSpeed * Time.deltaTime);

        if (rb != null)
            rb.MovePosition(currentPos);
        else
            transform.position = new Vector3(currentPos.x, currentPos.y, transform.position.z);

        // Flip facing direction from server
        if      (lastDir.x < -0.01f) transform.localScale = new Vector3(-Mathf.Abs(transform.localScale.x), transform.localScale.y, 1f);
        else if (lastDir.x >  0.01f) transform.localScale = new Vector3( Mathf.Abs(transform.localScale.x), transform.localScale.y, 1f);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by GameSceneBootstrap on DamageEvent — updates HP state.
    /// </summary>
    public void ApplyServerDamage(uint remainingHP)
    {
        CurrentHP = remainingHP;
        var baseObj = GetComponent<BaseObject>();
        if (baseObj != null)
            baseObj.HP = (int)remainingHP;
        Debug.Log($"[RemotePlayer:{PlayerID}] HP updated → {remainingHP}/{MaxHP}");
    }

    /// <summary>
    /// Called by GameSceneBootstrap on DieEvent — triggers death animation.
    /// </summary>
    public void ServerKill()
    {
        CurrentHP = 0;
        if (animator != null)
            animator.SetFloat(SpeedHash, 0f);
        Debug.Log($"[RemotePlayer:{PlayerID}] Killed by server");
    }

    /// <summary>Applies a WorldState snapshot — updates position, direction, and animation state.</summary>
    public void ApplySnapshot(PlayerSnapshot snap)
    {
        targetPos = new Vector2(snap.X, snap.Y);
        lastDir   = new Vector2(snap.DirX, snap.DirY);

        // Only trigger animator transitions when state actually changes
        if (snap.State == lastState) return;
        lastState = snap.State;

        ApplyState(snap.State);
    }

    private void ApplyState(byte state)
    {
        if (animator == null) return;

        switch (state)
        {
            case 0: // Idle
                animator.SetFloat(SpeedHash, 0f);
                SetTrail(false);
                break;

            case 1: // Move
                animator.SetFloat(SpeedHash, 1f);
                SetTrail(false);
                break;

            case 2: // Dash
                animator.SetTrigger(DashHash);
                animator.SetFloat(SpeedHash, 1f);
                SetTrail(true);
                break;

            case 3: // Attack
                animator.SetTrigger(AttackHash);
                break;

            case 4: // Dead
                animator.SetFloat(SpeedHash, 0f);
                SetTrail(false);
                break;

            case 5: // DashEnd
                animator.SetFloat(SpeedHash, 0f);
                SetTrail(false);
                break;
        }
    }

    private void SetTrail(bool active)
    {
        if (dashTrailObject != null)
            dashTrailObject.SetActive(active);
    }
}
