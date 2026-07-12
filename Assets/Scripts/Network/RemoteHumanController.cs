using UnityEngine;

/// <summary>
/// RemoteHumanController — IHumanController dành cho remote player.
///
/// Nhận MoveInput từ RemotePlayer (WorldState snapshot) → feed vào Human StateMachine.
/// Giúp Human.MoveState, IdleState, AnimatorController hoạt động đúng như local player.
///
/// DIP: Human states chỉ biết IHumanController, không biết remote hay local.
/// SRP: chỉ bridge RemotePlayer snapshot → IHumanController interface.
/// </summary>
public class RemoteHumanController : MonoBehaviour, IHumanController
{
    // ─── IHumanController ────────────────────────────────────────────────────

    public Vector2 MoveInput      { get; private set; }
    public Vector2 AimDirection   { get; private set; }
    public bool    IsDashPressed     => false;
    public bool    IsDashPressedRaw  => false;
    public bool    IsAttackPressed   => false;

    public void StopNavigation()
    {
        MoveInput = Vector2.zero;
    }

    // ─── State ───────────────────────────────────────────────────────────────

    private RemotePlayer remotePlayer;
    private Vector2      lastPos;

    // ─── Init ─────────────────────────────────────────────────────────────────

    /// <summary>Liên kết với RemotePlayer để lấy snapshot data mỗi frame.</summary>
    public void LinkRemotePlayer(RemotePlayer rp)
    {
        remotePlayer = rp;
        lastPos      = new Vector2(transform.position.x, transform.position.y);
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        if (remotePlayer == null) return;

        // Tính MoveInput từ delta vị trí (RemotePlayer lerp về targetPos mỗi frame)
        Vector2 currentPos = new Vector2(transform.position.x, transform.position.y);
        Vector2 delta      = currentPos - lastPos;
        lastPos = currentPos;

        // Nếu đang di chuyển → MoveInput = hướng di chuyển
        if (delta.sqrMagnitude > 0.00001f)
            MoveInput = delta.normalized;
        else
            MoveInput = Vector2.zero;

        // AimDirection = hướng đang nhìn (từ DirX/Y của snapshot)
        AimDirection = remotePlayer.LastDir.sqrMagnitude > 0.001f
                       ? remotePlayer.LastDir
                       : Vector2.right;
    }
}
