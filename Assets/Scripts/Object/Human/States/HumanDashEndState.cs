using UnityEngine;
using UnityEngine.AI;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// State DashEnd — "landing lag" sau khi dash kết thúc.
///
/// Gọi Human.LockInput(true) trong Enter() → Player.cs trả về Vector2.zero cho MoveInput.
/// Gọi Human.LockInput(false) trong Exit() → input hoạt động bình thường trở lại.
///
/// KHÔNG đọc input trong Update — chỉ đếm thời gian.
/// </summary>
public class HumanDashEndState : BaseState<Human>
{
    private readonly HumanAnimatorController animatorController;
    private float elapsedTime;

    public HumanDashEndState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine)
    {
        this.animatorController = animatorController;
    }

    public override void Enter()
    {
        elapsedTime = 0f;
        animatorController.SetSpeed(0f);
        owner.SetVelocity(Vector2.zero);
        owner.LockInput(true);

        // Dừng NavMesh agent tại vị trí hiện tại — tránh tiếp tục trượt về đích cũ sau dash
        owner.Controller?.StopNavigation();

        // Snap về điểm hợp lệ gần nhất trên NavMesh nếu dash đã đẩy ra ngoài vùng bake
        SnapToNavMesh();

        Debug.Log($"[DashEnd] ENTER on {owner.gameObject.name} — khóa {owner.DashEndLag}s | IsLocked={owner.IsInputLocked}");
    }

    /// <summary>
    /// Nếu vị trí hiện tại nằm ngoài NavMesh, tìm điểm hợp lệ gần nhất (trong vòng bán kính
    /// snapRadius) và dịch chuyển Rigidbody2D về đó — tránh agent bị trạng thái off-mesh.
    /// </summary>
    private void SnapToNavMesh()
    {
        const float snapRadius = 5f; // bán kính tìm kiếm tính từ vị trí hiện tại

        Vector3 currentPos = owner.transform.position;
        if (!NavMesh.SamplePosition(currentPos, out NavMeshHit hit, snapRadius, NavMesh.AllAreas))
        {
            Debug.LogWarning($"[DashEnd] Không tìm được điểm NavMesh hợp lệ trong {snapRadius}m — giữ nguyên vị trí.");
            return;
        }

        // Chỉ snap khi thực sự bị đẩy ra ngoài (khoảng cách đáng kể)
        float dist = Vector3.Distance(currentPos, hit.position);
        if (dist > 0.05f)
        {
            Vector2 snappedXY = new Vector2(hit.position.x, hit.position.y);
            owner.transform.position = new Vector3(snappedXY.x, snappedXY.y, currentPos.z);
            Debug.Log($"[DashEnd] Snap {owner.gameObject.name} về NavMesh: {currentPos} → {hit.position} (dist={dist:F2}m)");
        }
    }

    public override void Update()
    {
        elapsedTime += Time.deltaTime;

        // LOCKOUT — không đọc input, chỉ đếm thời gian
        if (elapsedTime >= owner.DashEndLag)
        {
            // Kiểm tra input CHỈ SAU KHI lag kết thúc
            IHumanController ctrl = owner.Controller;
            bool stillMoving = ctrl != null && ctrl.MoveInput.sqrMagnitude > 0.01f;

            if (stillMoving)
                stateMachine.ChangeState<HumanMoveState>();
            else
                stateMachine.ChangeState<HumanIdleState>();
        }
    }

    public override void FixedUpdate()
    {
        owner.SetVelocity(Vector2.zero);
    }

    public override void Exit()
    {
        // Mở lại input khi thoát (dù thoát qua lý do nào)
        owner.LockInput(false);
    }
}
