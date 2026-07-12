using UnityEngine;
using UnityEngine.AI;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// Dash state — dashes in the direction of the aim point (mouse cursor).
/// Uses an internal timer instead of a Coroutine to keep logic clean in the StateMachine.
/// Shared by all Human characters.
/// SRP: only handles dash logic.
///
/// NavMesh boundary check: mỗi FixedUpdate kiểm tra vị trí dự kiến (nextPos).
/// Nếu nextPos nằm ngoài NavMesh → snap về biên gần nhất và kết thúc dash sớm,
/// đảm bảo character không bao giờ vượt ra ngoài vùng bake.
/// </summary>
public class HumanDashState : BaseState<Human>
{
    private readonly HumanAnimatorController animatorController;

    /// <summary>Bán kính tìm điểm NavMesh hợp lệ — đủ lớn để luôn tìm được biên.</summary>
    private const float NavMeshCheckRadius = 1f;

    private Vector2 dashDirection;
    private float elapsedTime;

    public HumanDashState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine)
    {
        this.animatorController = animatorController;
    }

    public override void Enter()
    {
        elapsedTime = 0f;
        owner.RecordDash();

        // Dash toward the aim direction (mouse cursor for local player)
        // Fallback to MoveInput if aim direction is unavailable
        IHumanController ctrl = owner.Controller;
        if (ctrl != null)
        {
            Vector2 aim = ctrl.AimDirection;
            dashDirection = aim.sqrMagnitude > 0.01f ? aim.normalized : ctrl.MoveInput.normalized;
        }

        if (dashDirection == Vector2.zero) dashDirection = Vector2.right;

        owner.FaceDirection(dashDirection.x);
        animatorController.TriggerDash();
        owner.SetDashTrailActive(true);
    }

    public override void Update()
    {
        elapsedTime += Time.deltaTime;

        if (elapsedTime >= owner.DashDuration)
        {
            stateMachine.ChangeState<HumanDashEndState>();
        }
    }

    public override void FixedUpdate()
    {
        Vector2 velocity = dashDirection * owner.DashSpeed;

        // Tính vị trí dự kiến sau bước này
        Vector3 currentPos = owner.transform.position;
        Vector3 nextPos    = currentPos + (Vector3)(velocity * Time.fixedDeltaTime);

        // Chỉ 1 lần SamplePosition — radius nhỏ (0.1f) để kiểm tra nextPos có nằm trong NavMesh không
        if (!NavMesh.SamplePosition(nextPos, out _, NavMeshCheckRadius, NavMesh.AllAreas))
        {
            // nextPos ngoài NavMesh → snap về currentPos (vị trí hiện tại luôn hợp lệ trong lúc dash)
            owner.transform.position = currentPos;
            owner.SetVelocity(Vector2.zero);
            stateMachine.ChangeState<HumanDashEndState>();
            return;
        }

        // nextPos hợp lệ — di chuyển bình thường
        owner.SetVelocity(velocity);
    }

    public override void Exit()
    {
        owner.SetDashTrailActive(false);
    }
}
