using UnityEngine;
using UnityEngine.InputSystem;

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
        CreateHitboxRenderer();
    }

    private void CreateHitboxRenderer()
    {
        GameObject child = new GameObject("ServerHitboxVisualizer");
        child.transform.SetParent(transform, false);
        hitboxRenderer = child.AddComponent<LineRenderer>();
        hitboxRenderer.widthMultiplier = 0.05f;
        hitboxRenderer.useWorldSpace = false;
        hitboxRenderer.loop = true;
        
        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            hitboxRenderer.material = new Material(spriteShader);
        }
        hitboxRenderer.startColor = Color.red;
        hitboxRenderer.endColor = Color.red;

        UpdateHitboxGeometry();
        hitboxRenderer.gameObject.SetActive(false); // Hidden by default, shown on Tab hold
    }

    private void UpdateHitboxGeometry()
    {
        if (hitboxRenderer == null) return;
        
        byte shape = NetworkManager.ServerHitboxShape;
        if (shape == 0) // Circle
        {
            int segments = 36;
            hitboxRenderer.positionCount = segments;
            Vector3[] points = new Vector3[segments];
            float radius = NetworkManager.ServerHitboxRadius;
            for (int i = 0; i < segments; i++)
            {
                float theta = (2f * Mathf.PI / segments) * i;
                points[i] = new Vector3(Mathf.Cos(theta) * radius, Mathf.Sin(theta) * radius, 0f);
            }
            hitboxRenderer.SetPositions(points);
        }
        else // Box
        {
            hitboxRenderer.positionCount = 4;
            float w = NetworkManager.ServerHitboxWidth / 2f;
            float h = NetworkManager.ServerHitboxHeight / 2f;
            Vector3[] points = new Vector3[]
            {
                new Vector3(-w, -h, 0f),
                new Vector3(w, -h, 0f),
                new Vector3(w, h, 0f),
                new Vector3(-w, h, 0f)
            };
            hitboxRenderer.SetPositions(points);
        }
    }

    private LineRenderer hitboxRenderer;

    private void OnEnable()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnHitboxConfigReceived += HandleHitboxConfigReceived;
    }

    private void OnDisable()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnHitboxConfigReceived -= HandleHitboxConfigReceived;
    }

    private void HandleHitboxConfigReceived(PacketDecoder.HitboxConfigPacket pkt)
    {
        UpdateHitboxGeometry();
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

        UpdateHitboxVisibility();
    }

    private void UpdateHitboxVisibility()
    {
        bool showHitbox = Keyboard.current != null && Keyboard.current.tabKey.isPressed;
        if (hitboxRenderer != null && hitboxRenderer.gameObject.activeSelf != showHitbox)
        {
            hitboxRenderer.gameObject.SetActive(showHitbox);
        }
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

    private void OnDrawGizmos()
    {
        // Draw the server-side hitbox radius (2.5 units) around the remote player position
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 2.5f);

        // Draw a small solid sphere at the exact remote player position
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(transform.position, 0.2f);
    }
}
