using UnityEngine;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// State Idle — Human đứng yên, chờ controller ra lệnh.
/// Dùng chung cho mọi Human character (Kiếm Sĩ, Pháp Sư, Cung Thủ, ...).
/// Tuân thủ SRP: chỉ xử lý logic trạng thái Idle.
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

    public override void Update()
    {
        IHumanController ctrl = owner.Controller;
        if (ctrl == null) return;

        // Attack — ưu tiên cao nhất
        if (ctrl.IsAttackPressed)
        {
            stateMachine.ChangeState<HumanAttackState>();
            return;
        }

        // Dash — nếu không trong cooldown
        if (ctrl.IsDashPressed && owner.CanDash)
        {
            stateMachine.ChangeState<HumanDashState>();
            return;
        }

        // Move
        if (ctrl.MoveInput.sqrMagnitude > 0.01f)
        {
            stateMachine.ChangeState<HumanMoveState>();
        }
    }

    public override void FixedUpdate()
    {
        owner.SetVelocity(Vector2.zero);
    }
}
