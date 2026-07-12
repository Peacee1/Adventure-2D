using UnityEngine;
using Freeland.StateMachine;
using Freeland.Gameplay.HumanAnimation;

/// <summary>
/// State Attack — Human tấn công.
///
///   - Lock input ngay khi Enter (không nhận WASD, Attack tiếp theo).
///   - Dash là ngoại lệ duy nhất: có thể chen vào bất kỳ lúc nào trong attack.
///   - Thời gian ở lại state = owner.AttackSpeed (giây/đòn).
///   - Animation clip speed = clipLength / AttackSpeed
///     → clip 1s với AttackSpeed=1s: play 1x
///     → clip 1s với AttackSpeed=0.5s: play 2x
///   - Sau khi hết thời gian → về Idle.
///   - Unlock input và reset animator speed trong Exit().
///
/// Tuân thủ SRP: chỉ xử lý logic attack state.
/// Subclass override Enter() để thêm mechanic riêng (Pháp Sư cast, Cung Thủ charge...).
/// </summary>
public class HumanAttackState : BaseState<Human>
{
    private readonly HumanAnimatorController animatorController;

    // Attack clip length in seconds — used to compute animator speed multiplier.
    private const float ATTACK_CLIP_LENGTH = 1f;

    private protected float elapsedTime;

    /// <summary>Elapsed time since attack started. Readable by subclasses.</summary>
    protected float ElapsedTime => elapsedTime;

    public HumanAttackState(Human owner, StateMachine<Human> stateMachine,
        HumanAnimatorController animatorController)
        : base(owner, stateMachine)
    {
        this.animatorController = animatorController;
    }

    public override void Enter()
    {
        elapsedTime = 0f;
        owner.LockInput(true);

        // Dừng NavMesh agent ngay tại vị trí hiện tại — tránh nhân vật trượt khi attack
        owner.Controller?.StopNavigation();

        float speedMultiplier = ATTACK_CLIP_LENGTH / Mathf.Max(0.05f, owner.AttackSpeed);
        animatorController.TriggerAttack(speedMultiplier);

        // Hook cho subclass thêm hiệu ứng (bắn mũi tên, cast spell, ...) mà không sửa class này
        OnAttackExecuted();
    }

    /// <summary>
    /// Được gọi ngay khi attack trigger. Override trong subclass để thêm projectile, FX, v.v.
    /// Tuân thủ OCP: HumanAttackState đóng với sửa đổi, mở cho mở rộng.
    /// </summary>
    protected virtual void OnAttackExecuted() { }

    public override void Update()
    {
        elapsedTime += Time.deltaTime;

        // Dash là ngoại lệ duy nhất được phép chen vào attack.
        // Dùng IsDashPressedRaw để bypass LockInput — IsDashPressed bị !IsLocked chặn.
        IHumanController ctrl = owner.Controller;
        if (ctrl != null && owner.CanDash && ctrl.IsDashPressedRaw)
        {
            stateMachine.ChangeState<HumanDashState>();
            return;
        }

        // Hết thời gian đòn → về Idle
        if (elapsedTime >= owner.AttackSpeed)
        {
            stateMachine.ChangeState<HumanIdleState>();
        }
    }

    public override void FixedUpdate()
    {
        // Giữ nguyên vị trí trong lúc đánh
        owner.SetVelocity(Vector2.zero);
    }

    public override void Exit()
    {
        // Mở input và reset tốc độ animator về bình thường
        owner.LockInput(false);
        animatorController.ResetAnimatorSpeed();
    }
}
