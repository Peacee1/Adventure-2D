using UnityEngine;

/// <summary>
/// Projectile tổng quát — config được cho mũi tên, phép thuật, đạn, v.v.
///
/// Thay vì tồn tại theo giây (lifetime), đạn bay đúng một quãng đường = maxRange
/// rồi tự hủy. maxRange thường được set = AttackRange của người bắn qua Initialize().
///
/// Tuân thủ SRP: chỉ xử lý hành vi bay + va chạm của đạn.
/// Gọi Initialize() ngay sau Instantiate để set hướng bay.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Bullet : MonoBehaviour
{
    // ─── Config (chỉnh trên Prefab) ───────────────────────────────────────────

    [Header("Movement")]
    [SerializeField] private float speed    = 15f;

    /// <summary>
    /// Quãng đường tối đa (units). Mặc định fallback nếu Initialize() không truyền vào.
    /// Thường được ghi đè = AttackRange của người bắn.
    /// </summary>
    [SerializeField] private float maxRange = 10f;

    [Header("Damage")]
    [SerializeField] private int atkPhysical = 0;   // dame vật lý (0 = dùng từ shooter)
    [SerializeField] private int atkMagic    = 0;   // dame phép (0 = không dùng)

    // ─── Runtime ─────────────────────────────────────────────────────────────

    private Rigidbody2D rb;
    private Vector2     flyDirection;
    private GameObject  source;         // object đã bắn (tránh tự bắn vào mình)
    private Vector3     spawnPosition;  // vị trí sinh ra — dùng để tính quãng đường đã bay
    private bool        initialized;

    // ─── Public Init ─────────────────────────────────────────────────────────

    /// <summary>
    /// Khởi tạo đạn sau khi Instantiate.
    /// </summary>
    /// <param name="direction">Hướng bay (sẽ được normalize).</param>
    /// <param name="source">GameObject bắn ra — sẽ bị bỏ qua khi detect collision.</param>
    /// <param name="speedOverride">Ghi đè speed nếu > 0.</param>
    /// <param name="rangeOverride">Ghi đè maxRange nếu > 0. Truyền vào AttackRange của shooter.</param>
    /// <param name="atkOverride">Ghi đè atkPhysical nếu >= 0.</param>
    public void Initialize(Vector2 direction, GameObject source = null,
                           float speedOverride = -1f, float rangeOverride = -1f,
                           int atkOverride = -1)
    {
        flyDirection   = direction.normalized;
        this.source    = source;
        spawnPosition  = transform.position;

        if (speedOverride > 0f) speed    = speedOverride;
        if (rangeOverride > 0f) maxRange = rangeOverride;
        if (atkOverride  >= 0)  atkPhysical = atkOverride;

        // Xoay sprite theo hướng bay
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        initialized = true;
    }

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        rb              = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
    }

    private void FixedUpdate()
    {
        if (!initialized) return;

        rb.linearVelocity = flyDirection * speed;

        // Hủy khi đã bay đủ quãng đường maxRange
        float distanceTravelled = Vector3.Distance(transform.position, spawnPosition);
        if (distanceTravelled >= maxRange)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Bỏ qua va chạm với source (bản thân người bắn hoặc con của họ)
        if (source != null)
        {
            if (other.gameObject == source) return;
            if (other.transform.IsChildOf(source.transform)) return;
        }

        // Cũng bỏ qua va chạm với đạn khác
        if (other.GetComponent<Bullet>() != null) return;

        // Gây dame nếu target là BaseObject
        BaseObject target = other.GetComponentInParent<BaseObject>();
        if (target != null)
        {
            if (atkMagic > 0)
                target.TakeMagicDamage(atkMagic);
            else if (atkPhysical > 0)
                target.TakePhysicalDamage(atkPhysical);
        }

        Debug.Log($"[Bullet] trúng: {other.gameObject.name}");
        Destroy(gameObject);
    }
}
