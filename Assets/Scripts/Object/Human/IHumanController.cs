using UnityEngine;

/// <summary>
/// IHumanController — defines the minimal interface that any controller must provide to drive a Human.
///
/// With the server-authoritative architecture, states no longer poll input to drive transitions.
/// This interface now only exposes what LocalPlayer uses to send requests to the server:
///   - AimDirection: for attack/dash direction
///   - MoveInput: unused by SM states but kept for potential AI use
///   - StopNavigation: called by network events
///
/// DIP: Human and all states depend only on this abstraction, never on concrete LocalPlayer.
/// </summary>
public interface IHumanController
{
    /// <summary>Movement direction (not normalized). Kept for AI/future use.</summary>
    Vector2 MoveInput { get; }

    /// <summary>Normalized direction from character toward the aim point (mouse cursor for local player).</summary>
    Vector2 AimDirection { get; }

    /// <summary>Stops navigation immediately (sets destination to current position).</summary>
    void StopNavigation();
}
