using UnityEngine;

namespace Freeland.Gameplay.HumanAnimation
{
    /// <summary>
    /// Đóng gói mọi tương tác với Unity Animator của Human character.
    /// Tuân thủ SRP: chỉ chịu trách nhiệm set parameter cho Animator.
    /// Dùng Animator hash để tránh string lookup mỗi frame.
    /// </summary>
    public class HumanAnimatorController
    {
        private readonly Animator animator;

        // Cache hash 1 lần duy nhất — nhanh hơn SetFloat("speed") mỗi frame
        private static readonly int SpeedHash  = Animator.StringToHash("speed");
        private static readonly int DashHash   = Animator.StringToHash("dash");
        private static readonly int AttackHash = Animator.StringToHash("attack");

        public bool IsValid => animator != null;

        public HumanAnimatorController(Animator animator)
        {
            this.animator = animator;
            if (animator == null)
                Debug.LogWarning("[HumanAnimatorController] Animator is null — animation sẽ không hoạt động.");
        }

        /// <summary>Set float "speed". 0 = Idle, 1 = Moving.</summary>
        public void SetSpeed(float speed)
        {
            if (!IsValid) return;
            animator.SetFloat(SpeedHash, speed);
        }

        /// <summary>
        /// Triggers the "dash" animation at the given playback speed.
        /// speedMultiplier = 1 + floor(max(0, moveSpeed - 10) / 5) * 0.10
        /// Example: MoveSpeed=15 → speedMultiplier=1.1 → clip plays 10% faster.
        /// Call ResetAnimatorSpeed() in HumanDashState.Exit() to restore speed to 1.
        /// </summary>
        public void TriggerDash(float speedMultiplier = 1f)
        {
            if (!IsValid)
            {
                Debug.LogWarning("[HumanAnimatorController] TriggerDash: Animator is null!");
                return;
            }
            animator.speed = speedMultiplier;
            animator.SetTrigger(DashHash);
        }

        /// <summary>Kích hoạt trigger "attack" và set tốc độ animation.
        /// speedMultiplier = clipLength / attackSpeed (VD: 1f/0.5f = 2x nếu attack trong 0.5s).
        /// </summary>
        public void TriggerAttack(float speedMultiplier = 1f)
        {
            if (!IsValid)
            {
                Debug.LogWarning("[HumanAnimatorController] TriggerAttack: Animator is null!");
                return;
            }
            animator.speed = speedMultiplier;
            animator.SetTrigger(AttackHash);
        }

        /// <summary>Reset Animator.speed về 1 (gọi khi thoát AttackState).</summary>
        public void ResetAnimatorSpeed()
        {
            if (IsValid) animator.speed = 1f;
        }
    }
}
