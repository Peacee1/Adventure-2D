using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// Idle state — pure visual: sets speed=0 on the animator.
/// All state transitions are driven by LocalPlayer via Human.ForceState() based on server WorldState.
/// No input polling, no game logic.
/// </summary>
public class HumanIdleState : BaseState<Human>
{
    private readonly HumanAnimatorController animatorController;

    public HumanIdleState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine)
    {
        this.animatorController = animatorController;
    }

    public override void Enter()
    {
        animatorController.SetSpeed(0f);
    }

    // Update and FixedUpdate are intentionally empty.
    // The server drives all transitions via WorldState → Human.ForceState().
    public override void Update() { }

    public override void FixedUpdate() { }
}
