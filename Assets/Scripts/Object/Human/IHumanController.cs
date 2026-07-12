using UnityEngine;

/// <summary>
/// Interface that any controller (local Player, AI, Network) must implement
/// to drive a Human character.
///
/// Follows DIP: HumanStates only depend on this abstraction,
/// knowing nothing about the input source (keyboard, network, or AI).
///
/// Multiplayer flow:
///   Local:   Player : IHumanController  — reads keyboard/mouse
///   Remote:  NetworkController : IHumanController — receives network packets
///   AI:      AIController : IHumanController — AI decision tree
/// </summary>
public interface IHumanController
{
    /// <summary>Movement direction (not normalized). Updated every frame.</summary>
    Vector2 MoveInput { get; }

    /// <summary>Normalized direction from character toward the aim point (mouse cursor for local player).</summary>
    Vector2 AimDirection { get; }

    /// <summary>True for exactly one frame when the Dash button is pressed. Blocked by input lock.</summary>
    bool IsDashPressed { get; }

    /// <summary>
    /// True for exactly one frame when the Dash button is pressed, REGARDLESS of input lock.
    /// Dùng để Dash có thể chen vào Attack mà không bị LockInput chặn.
    /// </summary>
    bool IsDashPressedRaw { get; }

    /// <summary>True for exactly one frame when the Attack button is pressed.</summary>
    bool IsAttackPressed { get; }

    /// <summary>
    /// Dừng navigation ngay lập tức (set destination về vị trí hiện tại).
    /// Gọi khi nhân vật bị lock input (attack, stun, ...) để tránh agent tiếp tục trượt.
    /// </summary>
    void StopNavigation();
}
