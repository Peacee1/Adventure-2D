using UnityEngine;
using UnityEngine.InputSystem;

namespace Freeland.Gameplay.PlayerInput
{
    /// <summary>
    /// Đọc input từ UnityEngine.InputSystem và expose qua IPlayerInputReader.
    /// Tuân thủ SRP: chỉ chịu trách nhiệm đọc và cache trạng thái input.
    /// </summary>
    public class PlayerInputReader : IPlayerInputReader
    {
        public Vector2 MoveInput { get; private set; }
        public bool IsDashPressed { get; private set; }
        public bool IsAttackPressed { get; private set; }

        /// <summary>
        /// Cập nhật tất cả trạng thái input trong một lần gọi mỗi frame.
        /// Việc cache giúp các State đọc nhất quán trong cùng một frame.
        /// </summary>
        public void Tick()
        {
            ReadMovement();
            ReadActions();
        }

        private void ReadMovement()
        {
            MoveInput = Vector2.zero;
            if (Keyboard.current == null) return;

            if (Keyboard.current.wKey.isPressed) MoveInput += Vector2.up;
            if (Keyboard.current.sKey.isPressed) MoveInput += Vector2.down;
            if (Keyboard.current.aKey.isPressed) MoveInput += Vector2.left;
        }

        private void ReadActions()
        {
            if (Mouse.current == null)
            {
                IsDashPressed = false;
                IsAttackPressed = false;
                return;
            }

            IsDashPressed   = Keyboard.current.dKey.wasPressedThisFrame;
            IsAttackPressed = Mouse.current?.leftButton.wasPressedThisFrame ?? false;
        }
    }
}
