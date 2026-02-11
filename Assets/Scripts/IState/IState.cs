using UnityEngine;

namespace Freeland.StateMachine
{
    /// <summary>
    /// Interface định nghĩa các phương thức cơ bản cho một State
    /// </summary>
    public interface IState
    {
        /// <summary>
        /// Được gọi khi vào state này
        /// </summary>
        void Enter();

        /// <summary>
        /// Được gọi mỗi frame khi đang ở state này
        /// </summary>
        void Update();

        /// <summary>
        /// Được gọi với fixed timestep (physics)
        /// </summary>
        void FixedUpdate();

        /// <summary>
        /// Được gọi khi rời khỏi state này
        /// </summary>
        void Exit();
    }
}
