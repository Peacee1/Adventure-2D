using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// Move state — pure visual: sets speed=1 on the animator.
/// Position is driven entirely by server WorldState (Lerp in LocalPlayer).
/// No local physics, no transition logic.
/// </summary>
public class HumanMoveState : BaseState<Human>
{
    private readonly HumanAnimatorController animatorController;

    public HumanMoveState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine)
    {
        this.animatorController = animatorController;
    }

    public override void Enter()
    {
        animatorController.SetSpeed(1f);
    }

    public override void Exit()
    {
        animatorController.SetSpeed(0f);
    }

    // No local movement physics — server drives position via WorldState.
    public override void Update() { }
    public override void FixedUpdate() { }
}
