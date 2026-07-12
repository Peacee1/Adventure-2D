using UnityEngine;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// Attack state for the Archer class.
/// Inherits from HumanAttackState and spawns an arrow at 60% of the animation.
///
/// Follows OCP: extends HumanAttackState without modifying it.
/// Follows LSP: can fully replace HumanAttackState in the state machine.
/// </summary>
public class ArcherAttackState : HumanAttackState
{
    private const string ARROW_PREFAB_PATH = "Character/Archer/arrow"; // Assets/Resources/Character/Archer/arrow.prefab

    /// <summary>Normalized time at which the arrow is spawned (0.6 = 60%).</summary>
    private const float SPAWN_NORMALIZED_TIME = 0.6f;

    private bool arrowSpawned;
    private Vector2 attackDirection; // cached at Enter() — consistent facing + spawn direction

    public ArcherAttackState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine, animatorController)
    {
    }

    public override void Enter()
    {
        arrowSpawned = false;

        // Calculate direction to mouse cursor before base.Enter() triggers animation
        attackDirection = GetDirectionToMouse();

        // Face character toward the shoot direction immediately
        owner.FaceDirection(attackDirection.x);

        base.Enter(); // lock input + trigger attack animation
    }

    public override void Update()
    {
        // Spawn arrow at SPAWN_NORMALIZED_TIME (60%) of attack duration
        if (!arrowSpawned && ElapsedTime >= owner.AttackSpeed * SPAWN_NORMALIZED_TIME)
        {
            arrowSpawned = true;
            SpawnArrow();
        }

        // Delegate exit condition to base (elapsed >= AttackSpeed → Idle)
        base.Update();
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private Vector2 GetDirectionToMouse()
    {
        IHumanController ctrl = owner.Controller;
        if (ctrl != null && ctrl.AimDirection.sqrMagnitude > 0.01f)
            return ctrl.AimDirection;

        // Fallback: manual calculation
        Camera cam = Camera.main;
        if (cam == null) return Vector2.right;
        Vector3 mouseWorldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = owner.transform.position.z;
        Vector2 dir = ((Vector2)mouseWorldPos - (Vector2)owner.transform.position).normalized;
        return dir == Vector2.zero ? Vector2.right : dir;
    }

    private void SpawnArrow()
    {
        GameObject prefab = Resources.Load<GameObject>(ARROW_PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"[ArcherAttackState] Arrow prefab not found at: Resources/{ARROW_PREFAB_PATH}");
            return;
        }

        // Spawn offset: x mirrors based on facing direction, y is always +1
        float offsetX = attackDirection.x >= 0f ? 1.85f : -1.85f;
        Vector3 spawnPos = owner.transform.position + new Vector3(offsetX, 1f, 0f);

        GameObject arrowObj = Object.Instantiate(prefab, spawnPos, Quaternion.identity);

        Bullet bullet = arrowObj.GetComponent<Bullet>();
        if (bullet != null)
        {
            bullet.Initialize(
                direction    : attackDirection,
                source       : owner.gameObject,
                rangeOverride: owner.AttackRange,
                atkOverride  : owner.ATKPhysical
            );
        }
        else
        {
            // Fallback if the prefab has no Bullet component
            Rigidbody2D rb = arrowObj.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.gravityScale   = 0f;
                rb.linearVelocity = attackDirection * 15f;
            }

            float angle = Mathf.Atan2(attackDirection.y, attackDirection.x) * Mathf.Rad2Deg;
            arrowObj.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            Object.Destroy(arrowObj, 5f);
        }

        Debug.Log($"[ArcherAttackState] Arrow spawned at {ElapsedTime:F2}s — dir: {attackDirection}");
    }
}
