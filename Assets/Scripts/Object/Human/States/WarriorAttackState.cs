using UnityEngine;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// Attack state dành riêng cho Warrior — melee cận chiến.
///
/// Cơ chế:
///   1. Enter(): lock input + trigger animation (kế thừa base).
///   2. Tại 50% thời gian attack (SWING_NORMALIZED_TIME): quét OverlapCircle
///      quanh vị trí nhân vật → gây dame vật lý cho tất cả BaseObject
///      trong attackRange (ngoại trừ chính mình).
///   3. Sau AttackSpeed giây → về Idle (kế thừa base).
///
/// Tuân thủ OCP: mở rộng HumanAttackState, không sửa logic base.
/// Tuân thủ SRP: chỉ xử lý melee hit logic.
/// Tuân thủ LSP: thay thế hoàn toàn HumanAttackState trong StateMachine.
/// </summary>
public class WarriorAttackState : HumanAttackState
{
    /// <summary>Normalized time khi swing chạm (0.5 = 50% animation).</summary>
    private const float SWING_NORMALIZED_TIME = 0.5f;

    /// <summary>Layer mask cho melee hit — mặc định check tất cả layer.</summary>
    private static readonly int HitLayerMask = ~0;

    private bool hasHit;          // chỉ gây dame 1 lần mỗi swing
    private Vector2 attackDirection;

    public WarriorAttackState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine, animatorController)
    {
    }

    public override void Enter()
    {
        hasHit = false;
        attackDirection = GetFacingDirection();
        owner.FaceDirection(attackDirection.x);
        base.Enter(); // lock input + trigger attack animation
    }

    public override void Update()
    {
        // Thực hiện melee hit tại SWING_NORMALIZED_TIME của AttackSpeed
        if (!hasHit && ElapsedTime >= owner.AttackSpeed * SWING_NORMALIZED_TIME)
        {
            hasHit = true;
            PerformMeleeHit();
        }

        base.Update(); // xử lý dash interrupt + chuyển về Idle
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Tính hướng nhân vật đang nhìn dựa trên controller.
    /// Fallback là Vector2.right.
    /// </summary>
    private Vector2 GetFacingDirection()
    {
        IHumanController ctrl = owner.Controller;
        if (ctrl == null) return Vector2.right;

        // Ưu tiên hướng aim (chuột)
        if (ctrl.AimDirection.sqrMagnitude > 0.01f)
            return ctrl.AimDirection.normalized;

        // Fallback: hướng di chuyển
        if (ctrl.MoveInput.sqrMagnitude > 0.01f)
            return ctrl.MoveInput.normalized;

        return Vector2.right;
    }

    /// <summary>
    /// Quét OverlapCircle quanh nhân vật với bán kính AttackRange.
    /// Gây dame vật lý cho tất cả BaseObject trong vùng (ngoại trừ chính mình).
    /// </summary>
    private void PerformMeleeHit()
    {
        // Tâm hit lệch về phía nhân vật đang nhìn để cảm giác melee rõ hơn
        float offsetX = attackDirection.x >= 0f ? 0.6f : -0.6f;
        Vector2 hitCenter = (Vector2)owner.transform.position + new Vector2(offsetX, 0.2f);

        Collider2D[] hits = Physics2D.OverlapCircleAll(hitCenter, owner.AttackRange, HitLayerMask);

        bool didHitAnyone = false;
        foreach (Collider2D col in hits)
        {
            // Bỏ qua bản thân và con của bản thân
            if (col.gameObject == owner.gameObject) continue;
            if (col.transform.IsChildOf(owner.transform)) continue;

            BaseObject target = col.GetComponentInParent<BaseObject>();
            if (target == null) continue;
            if (target == owner as BaseObject) continue;

            int damage = owner.ATKPhysical;
            target.TakePhysicalDamage(damage);
            owner.OnDealtDamage(damage, target);

            didHitAnyone = true;
            Debug.Log($"[WarriorAttack] Trúng: {target.gameObject.name} — dame: {damage}");
        }

        if (!didHitAnyone)
            Debug.Log($"[WarriorAttack] Swing — không trúng mục tiêu nào (center={hitCenter}, r={owner.AttackRange:F2})");
    }
}
