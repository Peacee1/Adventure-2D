/// <summary>
/// Thứ tự tính toán của một buff/debuff.
/// Flat được tính TRƯỚC, Percent được tính SAU.
/// </summary>
public enum BuffMode
{
    /// <summary>Cộng trực tiếp: finalStat = baseStat + value</summary>
    FlatAdd,
    /// <summary>Trừ trực tiếp: finalStat = baseStat - value</summary>
    FlatSub,
    /// <summary>Nhân tăng theo % (tính sau flat): finalStat *= (1 + value), value = 0.52 → +52%</summary>
    PercentAdd,
    /// <summary>Nhân giảm theo % (tính sau flat): finalStat *= (1 - value), value = 0.20 → -20%</summary>
    PercentSub,
}

/// <summary>
/// Đại diện cho một buff hoặc debuff đang hoạt động trên một đối tượng.
/// </summary>
[System.Serializable]
public class BuffEntry
{
    /// <summary>
    /// Định danh nguồn gốc buff (ví dụ: "advancement", "skill_fire", "poison").
    /// Dùng để tìm kiếm và xoá theo nhóm.
    /// </summary>
    public string   source;

    /// <summary>Chỉ số bị ảnh hưởng.</summary>
    public StatType stat;

    /// <summary>Kiểu tính toán (Flat trước, Percent sau).</summary>
    public BuffMode mode;

    /// <summary>
    /// Giá trị buff:
    ///  - FlatAdd/FlatSub: số đơn vị tuyệt đối (ví dụ: +50 HP)
    ///  - PercentAdd/Sub: tỉ lệ thập phân (0.52 = 52%)
    /// </summary>
    public float value;

    /// <summary>Thời gian tồn tại (giây). -1 = vĩnh viễn.</summary>
    public float duration;

    // ─── Runtime ─────────────────────────────────────────────────────────────

    /// <summary>Thời gian đã trôi qua kể từ khi buff được thêm.</summary>
    [System.NonSerialized] public float elapsedTime;

    // ─── Properties ──────────────────────────────────────────────────────────

    public bool IsPermanent => duration < 0f;
    public bool IsExpired   => !IsPermanent && elapsedTime >= duration;

    // ─── Factory helpers ─────────────────────────────────────────────────────

    /// <summary>Tạo nhanh một buff vĩnh viễn Flat.</summary>
    public static BuffEntry Flat(string source, StatType stat, BuffMode mode, float value)
        => new BuffEntry { source = source, stat = stat, mode = mode, value = value, duration = -1f };

    /// <summary>Tạo nhanh một buff vĩnh viễn Percent.</summary>
    public static BuffEntry Percent(string source, StatType stat, BuffMode mode, float value)
        => new BuffEntry { source = source, stat = stat, mode = mode, value = value, duration = -1f };

    /// <summary>Tạo nhanh một buff có thời hạn.</summary>
    public static BuffEntry Timed(string source, StatType stat, BuffMode mode, float value, float duration)
        => new BuffEntry { source = source, stat = stat, mode = mode, value = value, duration = duration };
}
