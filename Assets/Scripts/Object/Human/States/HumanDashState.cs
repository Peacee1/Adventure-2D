using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;
using UnityEngine;

/// <summary>
/// Dash state — pure visual: triggers the dash animator at the correct speed
/// and activates the trail effect.
///
/// Animation speed scaling:
///   speedMultiplier = 1 + floor(max(0, moveSpeed - 10) / 5) * 0.10
///   Example: MoveSpeed=15 → 1.1× (clip plays 10% faster, matches server DashDuration)
///
/// All dash physics (position movement) are computed server-side.
/// Client receives the resulting position via WorldState and Lerps toward it.
/// No FixedUpdate physics, no NavMesh interaction.
/// </summary>
public class HumanDashState : BaseState<Human>
{
    private readonly HumanAnimatorController animatorController;

    public HumanDashState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine)
    {
        this.animatorController = animatorController;
    }

    public override void Enter()
    {
        float speedMultiplier = ComputeAnimSpeedMultiplier(owner.MoveSpeed);
        animatorController.TriggerDash(speedMultiplier);
        owner.SetDashTrailActive(true);
    }

    public override void Exit()
    {
        owner.SetDashTrailActive(false);
        animatorController.ResetAnimatorSpeed(); // restore animator.speed = 1
    }

    // No local physics — server drives position.
    public override void Update() { }
    public override void FixedUpdate() { }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the animation playback speed multiplier from MoveSpeed.
    /// Mirrors the server's ComputeMaxDashDistance formula:
    ///   extraSteps = floor(max(0, moveSpeed - 10) / 5)
    ///   multiplier = 1 + extraSteps * 0.10
    /// </summary>
    private static float ComputeAnimSpeedMultiplier(float moveSpeed)
    {
        const float baseSpeed = 10f;
        if (moveSpeed <= baseSpeed) return 1f;
        float extraSteps = Mathf.Floor((moveSpeed - baseSpeed) / 5f);
        return 1f + extraSteps * 0.10f;
    }
}
