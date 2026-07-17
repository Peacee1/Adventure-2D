using UnityEngine;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// Attack state for the Archer class.
/// Inherits from HumanAttackState and triggers the attack animation with the correct facing direction.
///
/// Arrow spawning is fully server-authoritative:
///   1. Client sends AttackReq (with DirX/DirY) to server.
///   2. Server validates, applies damage, broadcasts DamageEvent + ProjectileSpawnPacket.
///   3. ALL clients (including self) receive ProjectileSpawnPacket → ProjectileManager spawns the arrow.
///
/// Follows OCP: extends HumanAttackState without modifying it.
/// Follows LSP: can fully replace HumanAttackState in the state machine.
/// </summary>
public class ArcherAttackState : HumanAttackState
{
    public ArcherAttackState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine, animatorController)
    {
    }

    public override void Enter()
    {
        // Face character toward the aim direction before triggering animation
        IHumanController ctrl = owner.Controller;
        if (ctrl != null && ctrl.AimDirection.sqrMagnitude > 0.01f)
            owner.FaceDirection(ctrl.AimDirection.x);

        base.Enter(); // triggers attack animation
    }

    // Arrow is spawned by ProjectileManager via server broadcast — no local spawn needed.
    protected override void OnAttackExecuted() { }
}
