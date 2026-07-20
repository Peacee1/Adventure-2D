using UnityEngine;

/// <summary>
/// Client-side projectile visual — fully autonomous after spawn.
///
/// Receives direction + speed from ProjectileSpawnPacket and flies indefinitely.
/// Destroyed only when server sends ProjectileDestroyPacket (hit or out of range).
///
/// Server simulates position internally for collision detection but does NOT
/// broadcast position updates — client dead reckoning is sufficient for straight-line projectiles.
///
/// SRP: renders the projectile visual only.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Bullet : MonoBehaviour
{
    private Rigidbody2D rb;
    private Vector2     velocity;   // direction × speed, set once on Initialize
    private bool        initialized;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Call once after Instantiate. Bullet flies autonomously from this point.
    /// </summary>
    /// <param name="dir">Normalized flight direction (same as server).</param>
    /// <param name="speed">Flight speed in units/sec (same as server).</param>
    public void Initialize(Vector2 dir, float speed)
    {
        velocity    = dir.normalized * speed;
        initialized = true;

        // Orient sprite toward flight direction
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>
    /// Called by ProjectileManager when server sends ProjectileDestroyPacket.
    /// This is the ONLY way a bullet is removed — client never self-destructs.
    /// </summary>
    public void ServerDestroy()
    {
        Destroy(gameObject);
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        rb              = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.bodyType     = RigidbodyType2D.Kinematic;
        rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
    }

    private void Start()
    {
        CreateHitboxRenderer();
    }

    private void CreateHitboxRenderer()
    {
        GameObject child = new GameObject("ArrowHitboxVisualizer");
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

        // Draw a box of 3.6 x 0.6
        hitboxRenderer.positionCount = 4;
        float w = 3.6f / 2f;
        float h = 0.6f / 2f;
        Vector3[] points = new Vector3[]
        {
            new Vector3(-w, -h, 0f),
            new Vector3(w, -h, 0f),
            new Vector3(w, h, 0f),
            new Vector3(-w, h, 0f)
        };
        hitboxRenderer.SetPositions(points);
        hitboxRenderer.gameObject.SetActive(false); // Hidden by default, shown on Tab hold
    }

    private LineRenderer hitboxRenderer;

    private void Update()
    {
        bool showHitbox = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.tabKey.isPressed;
        if (hitboxRenderer != null && hitboxRenderer.gameObject.activeSelf != showHitbox)
        {
            hitboxRenderer.gameObject.SetActive(showHitbox);
        }
    }

    private void FixedUpdate()
    {
        if (!initialized) return;
        rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);
    }
}
