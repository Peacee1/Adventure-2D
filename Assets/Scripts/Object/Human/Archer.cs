using UnityEngine;
using Freeland.StateMachine;

/// <summary>
/// Archer job class — inherits Human.
///
/// Overrides SetupStateMachine() to replace HumanAttackState with ArcherAttackState.
/// ArcherAttackState is registered under the HumanAttackState key so that
/// all base states (Idle, Move) can call ChangeState&lt;HumanAttackState&gt;() without
/// knowing about the Archer-specific implementation.
///
/// Follows OCP: extends Human without modifying it.
/// Follows LSP: fully substitutable for Human anywhere.
/// </summary>
public class Archer : Human
{
    protected override void SetupStateMachine()
    {
        stateMachine = new StateMachine<Human>(this);

        var idleState    = new HumanIdleState    (this, stateMachine, AnimatorController);
        var moveState    = new HumanMoveState    (this, stateMachine, AnimatorController);
        var dashState    = new HumanDashState    (this, stateMachine, AnimatorController);
        var dashEndState = new HumanDashEndState (this, stateMachine, AnimatorController);
        var attackState  = new ArcherAttackState (this, stateMachine, AnimatorController);

        stateMachine.RegisterState(idleState);
        stateMachine.RegisterState(moveState);
        stateMachine.RegisterState(dashState);
        stateMachine.RegisterState(dashEndState);

        // Register ArcherAttackState under HumanAttackState's key so that
        // HumanIdleState/HumanMoveState can call ChangeState<HumanAttackState>()
        // and transparently get ArcherAttackState.
        stateMachine.RegisterStateAs<HumanAttackState, ArcherAttackState>(attackState);

        stateMachine.ChangeState<HumanIdleState>();

        Debug.Log($"[Archer:{gameObject.name}] StateMachine: Idle / Move / Dash / DashEnd / ArcherAttack.");
    }
}
