using UnityEngine;
using Freeland.StateMachine;

/// <summary>
/// Warrior job class — kế thừa Human.
///
/// Override SetupStateMachine() để đăng ký WarriorAttackState thay cho HumanAttackState.
/// WarriorAttackState được đăng ký dưới key HumanAttackState → các state dùng chung
/// (Idle, Move) gọi ChangeState&lt;HumanAttackState&gt;() và nhận WarriorAttackState.
///
/// Tuân thủ OCP: mở rộng Human mà không sửa code base.
/// Tuân thủ LSP: hoàn toàn thay thế được Human ở bất kỳ đâu.
/// </summary>
public class Warrior : Human
{
    protected override void SetupStateMachine()
    {
        stateMachine = new StateMachine<Human>(this);

        var idleState    = new HumanIdleState    (this, stateMachine, AnimatorController);
        var moveState    = new HumanMoveState    (this, stateMachine, AnimatorController);
        var dashState    = new HumanDashState    (this, stateMachine, AnimatorController);
        var dashEndState = new HumanDashEndState (this, stateMachine, AnimatorController);
        var attackState  = new WarriorAttackState(this, stateMachine, AnimatorController);

        stateMachine.RegisterState(idleState);
        stateMachine.RegisterState(moveState);
        stateMachine.RegisterState(dashState);
        stateMachine.RegisterState(dashEndState);

        // Đăng ký WarriorAttackState dưới key HumanAttackState để HumanIdleState/HumanMoveState
        // gọi ChangeState<HumanAttackState>() và nhận Warrior-specific attack.
        stateMachine.RegisterStateAs<HumanAttackState, WarriorAttackState>(attackState);

        stateMachine.ChangeState<HumanIdleState>();

        Debug.Log($"[Warrior:{gameObject.name}] StateMachine: Idle / Move / Dash / DashEnd / WarriorAttack.");
    }
}
