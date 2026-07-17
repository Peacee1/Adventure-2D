using UnityEngine;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// Attack state — triggers the attack animator and spawns projectiles/FX.
/// Server owns the exit timer; client holds this state until ForceState() is called with Idle.
///
/// ElapsedTime is kept so subclasses (ArcherAttackState, WarriorAttackState) can schedule
/// mid-animation events (arrow spawn at 60%, melee hit at 50%) without modifying this class.
///
/// OCP: subclasses override OnAttackExecuted() for job-specific logic.
/// </summary>
public class HumanAttackState : BaseState<Human>
{
    private readonly HumanAnimatorController animatorController;

    // Duration of the attack animation clip (seconds) — used to scale animator speed.
    private const float ATTACK_CLIP_LENGTH = 1f;

    // Local elapsed timer — kept so subclasses can schedule mid-animation events.
    // The server owns the exit condition; this timer is for visual timing only.
    private float elapsedTime;

    /// <summary>Elapsed seconds since this attack started. Read-only for subclasses.</summary>
    protected float ElapsedTime => elapsedTime;

    public HumanAttackState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine)
    {
        this.animatorController = animatorController;
    }

    public override void Enter()
    {
        elapsedTime = 0f;
        float speedMultiplier = ATTACK_CLIP_LENGTH / Mathf.Max(0.05f, owner.AttackDuration);
        animatorController.TriggerAttack(speedMultiplier);

        // Hook for subclasses to spawn projectiles/FX (OCP — closed for modification)
        OnAttackExecuted();
    }

    /// <summary>
    /// Called immediately when the attack triggers. Override in subclass to add projectile/FX.
    /// </summary>
    protected virtual void OnAttackExecuted() { }

    public override void Update()
    {
        // Advance local timer so subclasses can schedule mid-animation events
        elapsedTime += Time.deltaTime;
    }

    public override void Exit()
    {
        animatorController.ResetAnimatorSpeed();
    }

    public override void FixedUpdate() { }
}
