using UnityEngine;

namespace Freeland.Gameplay.PlayerInput
{
    /// <summary>
    /// Interface đọc input của Player.
    /// Tuân thủ DIP: các State chỉ phụ thuộc vào abstraction này,
    /// không phụ thuộc trực tiếp vào UnityEngine.InputSystem.
    /// </summary>
    public interface IPlayerInputReader
    {
        /// <summary>Vector2 hướng di chuyển (WASD). Chưa normalize.</summary>
        Vector2 MoveInput { get; }

        /// <summary>True nếu nút Dash được nhấn frame này (Right Click).</summary>
        bool IsDashPressed { get; }

        /// <summary>True nếu nút Attack được nhấn frame này (Left Click).</summary>
        bool IsAttackPressed { get; }

        /// <summary>Cập nhật trạng thái input. Gọi mỗi frame từ Player.Update().</summary>
        void Tick();
    }
}
