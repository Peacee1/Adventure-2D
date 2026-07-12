using UnityEngine;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// State Move — Human di chuyển theo hướng controller cung cấp.
/// Dùng chung cho mọi Human character.
/// Tuân thủ SRP: chỉ xử lý logic di chuyển và flip hướng nhìn.
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

        // Không còn input → Idle
        if (ctrl.MoveInput.sqrMagnitude <= 0.01f)
        {
            stateMachine.ChangeState<HumanIdleState>();
            return;
        }

        // Cập nhật hướng nhìn
        owner.FaceDirection(ctrl.MoveInput.x);
    }

    public override void FixedUpdate()
    {
        IHumanController ctrl = owner.Controller;
        if (ctrl == null) return;
        owner.SetVelocity(ctrl.MoveInput.normalized * owner.MoveSpeed);
    }

    public override void Exit()
    {
        animatorController.SetSpeed(0f);
    }
}
