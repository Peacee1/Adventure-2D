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

    private void FixedUpdate()
    {
        if (!initialized) return;
        rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);
    }
}
