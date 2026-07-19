using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ProjectileManager — server-authoritative projectile lifecycle, client-side autonomous flight.
///
/// Flow:
///   1. Server sends ProjectileSpawnPacket → spawn visual bullet; it flies autonomously.
///   2. Server sends ProjectileDestroyPacket (hit or out-of-range) → destroy bullet.
///
/// No position update packets required — straight-line projectiles need only
/// direction and speed to reproduce the server trajectory on the client.
///
/// SRP: manages projectile visual lifecycle from network events only.
/// DIP: subscribes to NetworkManager events — no direct coupling to game systems.
/// </summary>
public class ProjectileManager : MonoBehaviour
{
    // Resource paths indexed by ProjType byte (must match server constants)
    private static readonly string[] ProjTypePaths =
    {
        "Prefab/arrow",            // 0 = Arrow
        "Character/Mage/fireball", // 1 = Spell (extend as needed)
    };

    // Active bullets keyed by server-assigned ProjID
    private readonly Dictionary<uint, Bullet> _activeBullets = new();

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnProjectileSpawn   += HandleProjectileSpawn;
        NetworkManager.Instance.OnProjectileDestroy += HandleProjectileDestroy;
    }

    private void OnDisable()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnProjectileSpawn   -= HandleProjectileSpawn;
        NetworkManager.Instance.OnProjectileDestroy -= HandleProjectileDestroy;
    }

    // ─── Handlers ─────────────────────────────────────────────────────────────

    private void HandleProjectileSpawn(ProjectileSpawnPacket pkt)
    {
        if (pkt.ProjType >= ProjTypePaths.Length)
        {
            Debug.LogWarning($"[ProjectileManager] Unknown ProjType={pkt.ProjType}");
            return;
        }

        string path   = ProjTypePaths[pkt.ProjType];
        var    prefab = Resources.Load<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"[ProjectileManager] Prefab not found at Resources/{path}");
            return;
        }

        Vector3    spawnPos = new Vector3(pkt.X, pkt.Y, 0f);
        GameObject arrowObj = Instantiate(prefab, spawnPos, Quaternion.identity);

        // Ensure bullet renders above tilemaps
        var sr = arrowObj.GetComponentInChildren<SpriteRenderer>();
        if (sr != null) { sr.sortingLayerName = "Default"; sr.sortingOrder = 50; }

        Bullet bullet = arrowObj.GetComponent<Bullet>();
        if (bullet != null)
        {
            bullet.Initialize(new Vector2(pkt.DirX, pkt.DirY), pkt.Speed);
            _activeBullets[pkt.ProjID] = bullet;
        }
        else
        {
            Debug.LogWarning($"[ProjectileManager] Prefab '{path}' has no Bullet component!");
        }

        Debug.Log($"[ProjectileManager] ProjID={pkt.ProjID} spawned for player {pkt.OwnerID} " +
                  $"at ({pkt.X:F1},{pkt.Y:F1}) dir=({pkt.DirX:F2},{pkt.DirY:F2}) speed={pkt.Speed}");
    }

    private void HandleProjectileDestroy(ProjectileDestroyPacket pkt)
    {
        if (_activeBullets.TryGetValue(pkt.ProjID, out Bullet bullet))
        {
            _activeBullets.Remove(pkt.ProjID);
            if (bullet != null)
                bullet.ServerDestroy();

            Debug.Log($"[ProjectileManager] ProjID={pkt.ProjID} destroyed by server");
        }
    }
}
