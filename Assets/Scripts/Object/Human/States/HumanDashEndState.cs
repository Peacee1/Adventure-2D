using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// DashEnd state — pure visual: shows the landing-lag idle pose.
/// Server is responsible for the 0.6s end-lag timer and rejecting input.
/// Client simply holds this state until the server broadcasts the next state.
/// </summary>
public class HumanDashEndState : BaseState<Human>
{
    private readonly HumanAnimatorController animatorController;

    public HumanDashEndState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine)
    {
        this.animatorController = animatorController;
    }

    public override void Enter()
    {
        // Show idle pose during landing lag
        animatorController.SetSpeed(0f);
        owner.SetDashTrailActive(false);
    }

    // Server controls the duration and exit transition.
    public override void Update() { }
    public override void FixedUpdate() { }
}
