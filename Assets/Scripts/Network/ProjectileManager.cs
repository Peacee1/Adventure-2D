using UnityEngine;

/// <summary>
/// ProjectileManager — listens for ProjectileSpawn packets from the server
/// and instantiates the corresponding visual projectile on all clients.
///
/// SRP: handles only projectile visual spawning from network events.
/// DIP: subscribes via NetworkManager event — no direct coupling to game systems.
///
/// Attach to any persistent GameObject in the game scene (e.g., GameSceneBootstrap).
/// Prefab paths are loaded from Resources/. Match paths with the arrow prefab in ArcherAttackState.
/// </summary>
public class ProjectileManager : MonoBehaviour
{
    // Resource paths for each ProjType byte (must match server constants)
    private static readonly string[] ProjTypePaths = new[]
    {
        "Character/Archer/arrow",   // 0 = Arrow
        "Character/Mage/fireball",  // 1 = Spell (extend as needed)
    };

    private void OnEnable()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnProjectileSpawn += HandleProjectileSpawn;
    }

    private void OnDisable()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnProjectileSpawn -= HandleProjectileSpawn;
    }

    private void HandleProjectileSpawn(ProjectileSpawnPacket pkt)
    {
        if (pkt.ProjType >= ProjTypePaths.Length)
        {
            Debug.LogWarning($"[ProjectileManager] Unknown ProjType={pkt.ProjType} — no prefab mapped.");
            return;
        }

        string path = ProjTypePaths[pkt.ProjType];
        GameObject prefab = Resources.Load<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"[ProjectileManager] Prefab not found at Resources/{path}");
            return;
        }

        Vector3 spawnPos = new Vector3(pkt.X, pkt.Y, 0f);
        GameObject arrowObj = Instantiate(prefab, spawnPos, Quaternion.identity);

        Bullet bullet = arrowObj.GetComponent<Bullet>();
        if (bullet != null)
        {
            bullet.Initialize(
                direction     : new Vector2(pkt.DirX, pkt.DirY),
                source        : null,          // server already handled damage — no collision damage needed
                speedOverride : pkt.Speed,
                rangeOverride : pkt.Range,
                atkOverride   : 0              // 0 = no client-side damage (server-authoritative)
            );
        }
        else
        {
            // Fallback: simple rigidbody flight
            Rigidbody2D rb = arrowObj.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.linearVelocity = new Vector2(pkt.DirX, pkt.DirY) * pkt.Speed;

            float angle = Mathf.Atan2(pkt.DirY, pkt.DirX) * Mathf.Rad2Deg;
            arrowObj.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            Destroy(arrowObj, pkt.Range / Mathf.Max(pkt.Speed, 1f));
        }

        Debug.Log($"[ProjectileManager] Arrow spawned for player {pkt.OwnerID} " +
                  $"at ({pkt.X:F1},{pkt.Y:F1}) dir=({pkt.DirX:F2},{pkt.DirY:F2})");
    }
}
