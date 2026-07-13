using UnityEngine;

/// <summary>
/// RemoteHumanController — không còn được sử dụng sau khi chuyển sang
/// điều khiển trực tiếp qua RemotePlayer (bypass Human StateMachine).
///
/// Giữ lại class để tránh lỗi compile nếu có reference cũ,
/// nhưng không có logic thực tế bên trong.
/// </summary>
public class RemoteHumanController : MonoBehaviour, IHumanController
{
    public Vector2 MoveInput      => Vector2.zero;
    public Vector2 AimDirection   => Vector2.right;
    public bool    IsDashPressed     => false;
    public bool    IsDashPressedRaw  => false;
    public bool    IsAttackPressed   => false;

    public void StopNavigation() { }

    /// <summary>Không còn sử dụng — RemotePlayer.cs điều khiển trực tiếp.</summary>
    public void LinkRemotePlayer(RemotePlayer rp) { }
}
