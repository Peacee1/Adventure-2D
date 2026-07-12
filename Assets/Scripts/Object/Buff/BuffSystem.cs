using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Component quản lý toàn bộ Buff và Debuff của một BaseObject.
///
/// ═══════════════════════════════════════════════════════════════
/// FORMULA (thứ tự áp dụng mỗi lần RecalculateStats()):
///   1. finalStat = baseStat
///   2. finalStat += sum(FlatAdd)          ← flat TRƯỚC
///   3. finalStat -= sum(FlatSub)          ← flat TRƯỚC
///   4. finalStat *= (1 + totalPercentAdd  ← % SAU
///                     - totalPercentSub)
/// ═══════════════════════════════════════════════════════════════
///
/// CÁCH SỬ DỤNG:
///   // Thêm buff vĩnh viễn:
///   buffSystem.AddBuff(BuffEntry.Percent("advancement", StatType.Speed, BuffMode.PercentAdd, 0.52f));
///
///   // Thêm debuff có thời hạn:
///   buffSystem.AddBuff(BuffEntry.Timed("poison", StatType.HPRegen, BuffMode.FlatSub, 5f, 10f));
///
///   // Xoá tất cả buff của một nguồn:
///   buffSystem.RemoveBuffsBySource("advancement");
///
///   // Cập nhật giá trị buff (ví dụ khi level thay đổi):
///   buffSystem.UpdateBuff("advancement", StatType.Speed, BuffMode.PercentAdd, newValue);
/// </summary>
public class BuffSystem : MonoBehaviour
{
    // ─── State ───────────────────────────────────────────────────────────────

    [Header("Buffs đang hoạt động (chỉ đọc)")]
    [SerializeField] private List<BuffEntry> activeBuffs = new List<BuffEntry>();

    /// <summary>Snapshot chỉ số GỐC (trước khi buff). Được cập nhật bởi SnapshotBaseStats().</summary>
    private readonly Dictionary<StatType, float> baseStats = new Dictionary<StatType, float>();

    private BaseObject obj;

    // ─── Events ──────────────────────────────────────────────────────────────

    /// <summary>Phát ra sau mỗi lần RecalculateStats() hoàn tất.</summary>
    public event Action OnStatsRecalculated;

    // ─── Unity ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        obj = GetComponent<BaseObject>();
        if (obj == null)
            Debug.LogError("[BuffSystem] Không tìm thấy BaseObject trên cùng GameObject!");
    }

    private void Update()
    {
        bool anyExpired = false;
        foreach (var buff in activeBuffs)
        {
            if (buff.IsPermanent) continue;
            buff.elapsedTime += Time.deltaTime;
            if (buff.IsExpired) anyExpired = true;
        }

        if (anyExpired)
        {
            activeBuffs.RemoveAll(b => b.IsExpired);
            RecalculateStats();
        }
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Chụp snapshot chỉ số hiện tại làm BASE.
    /// GỌI sau mỗi lần thay đổi chỉ số GỐC (ApplyJobClassStats, ApplyGrowth).
    /// </summary>
    public void SnapshotBaseStats()
    {
        if (obj == null) return;

        baseStats[StatType.MaxHP]       = obj.MaxHP;
        baseStats[StatType.MaxMP]       = obj.MaxMP;
        baseStats[StatType.ATKPhysical] = obj.ATKPhysical;
        baseStats[StatType.ATKMagic]    = obj.ATKMagic;
        baseStats[StatType.DEFPhysical] = obj.DEFPhysical;
        baseStats[StatType.DEFMagic]    = obj.DEFMagic;
        baseStats[StatType.HPRegen]     = obj.HPRegen;
        baseStats[StatType.MPRegen]     = obj.MPRegen;
        baseStats[StatType.Speed]       = obj.MoveSpeed;
        baseStats[StatType.AttackRange] = obj.AttackRange;
        baseStats[StatType.AttackSpeed] = obj.AttackSpeed;
        baseStats[StatType.LifeSteal]   = obj.LifeSteal;
        baseStats[StatType.HealingPower]= obj.HealingPower;
    }

    /// <summary>
    /// Thêm một buff. Nếu đã có buff cùng (source + stat + mode), sẽ CẬP NHẬT giá trị.
    /// Tự động gọi RecalculateStats().
    /// </summary>
    public void AddBuff(BuffEntry buff)
    {
        var existing = activeBuffs.FirstOrDefault(
            b => b.source == buff.source && b.stat == buff.stat && b.mode == buff.mode);

        if (existing != null)
            existing.value = buff.value;
        else
            activeBuffs.Add(buff);

        RecalculateStats();
    }

    /// <summary>
    /// Cập nhật giá trị của buff đã có (source + stat + mode).
    /// Nếu chưa tồn tại thì thêm mới. Tự động gọi RecalculateStats().
    /// </summary>
    public void UpdateBuff(string source, StatType stat, BuffMode mode, float newValue)
    {
        AddBuff(new BuffEntry
        {
            source   = source,
            stat     = stat,
            mode     = mode,
            value    = newValue,
            duration = -1f,
        });
    }

    /// <summary>
    /// Xoá TẤT CẢ buff có cùng source. Tự động gọi RecalculateStats().
    /// </summary>
    public void RemoveBuffsBySource(string source)
    {
        int removed = activeBuffs.RemoveAll(b => b.source == source);
        if (removed > 0) RecalculateStats();
    }

    /// <summary>
    /// Xoá buff theo (source + stat + mode) cụ thể. Tự động gọi RecalculateStats().
    /// </summary>
    public void RemoveBuff(string source, StatType stat, BuffMode mode)
    {
        int removed = activeBuffs.RemoveAll(b => b.source == source && b.stat == stat && b.mode == mode);
        if (removed > 0) RecalculateStats();
    }

    /// <summary>
    /// Xoá toàn bộ buff, reset về base stats.
    /// </summary>
    public void ClearAllBuffs()
    {
        activeBuffs.Clear();
        RecalculateStats();
    }

    /// <summary>
    /// Tính lại TẤT CẢ chỉ số từ base stats + danh sách buff hiện tại.
    ///
    /// Thứ tự:
    ///   1. flat += FlatAdd  | flat -= FlatSub
    ///   2. final *= (1 + PercentAdd - PercentSub)
    /// </summary>
    public void RecalculateStats()
    {
        if (baseStats.Count == 0 || obj == null) return;

        foreach (StatType stat in Enum.GetValues(typeof(StatType)))
        {
            if (!baseStats.TryGetValue(stat, out float baseVal)) continue;

            // ── Step 1: Flat ────────────────────────────────────────────────
            float flatTotal = 0f;
            foreach (var buff in activeBuffs)
            {
                if (buff.stat != stat) continue;
                if (buff.mode == BuffMode.FlatAdd) flatTotal += buff.value;
                if (buff.mode == BuffMode.FlatSub) flatTotal -= buff.value;
            }
            float afterFlat = baseVal + flatTotal;

            // ── Step 2: Percent ─────────────────────────────────────────────
            float pctTotal = 0f;
            foreach (var buff in activeBuffs)
            {
                if (buff.stat != stat) continue;
                if (buff.mode == BuffMode.PercentAdd) pctTotal += buff.value;
                if (buff.mode == BuffMode.PercentSub) pctTotal -= buff.value;
            }
            float finalVal = afterFlat * (1f + pctTotal);

            // ── Áp dụng lên object ──────────────────────────────────────────
            ApplyToObject(stat, finalVal);
        }

        // Clamp HP/MP theo Max sau khi tính xong
        if (obj.HP > obj.MaxHP) obj.HP = obj.MaxHP;
        if (obj.MP > obj.MaxMP) obj.MP = obj.MaxMP;

        OnStatsRecalculated?.Invoke();
    }

    // ─── Query Helpers ────────────────────────────────────────────────────────

    /// <summary>Trả về tổng flat buff (FlatAdd - FlatSub) của một stat.</summary>
    public float GetTotalFlat(StatType stat)
    {
        float total = 0f;
        foreach (var b in activeBuffs)
        {
            if (b.stat != stat) continue;
            if (b.mode == BuffMode.FlatAdd) total += b.value;
            if (b.mode == BuffMode.FlatSub) total -= b.value;
        }
        return total;
    }

    /// <summary>Trả về tổng % buff (PercentAdd - PercentSub) của một stat.</summary>
    public float GetTotalPercent(StatType stat)
    {
        float total = 0f;
        foreach (var b in activeBuffs)
        {
            if (b.stat != stat) continue;
            if (b.mode == BuffMode.PercentAdd) total += b.value;
            if (b.mode == BuffMode.PercentSub) total -= b.value;
        }
        return total;
    }

    /// <summary>Trả về chỉ số BASE (trước buff) của một stat.</summary>
    public float GetBaseStat(StatType stat)
        => baseStats.TryGetValue(stat, out float v) ? v : 0f;

    /// <summary>Kiểm tra có buff nào từ source không.</summary>
    public bool HasBuff(string source) => activeBuffs.Any(b => b.source == source);

    /// <summary>Tổng số buff đang hoạt động.</summary>
    public int ActiveBuffCount => activeBuffs.Count;

    // ─── Private ─────────────────────────────────────────────────────────────

    private void ApplyToObject(StatType stat, float value)
    {
        int iv = Mathf.RoundToInt(value);          // int cast an toàn

        switch (stat)
        {
            case StatType.MaxHP:        obj.MaxHP        = Mathf.Max(1, iv);     break;
            case StatType.MaxMP:        obj.MaxMP        = Mathf.Max(0, iv);     break;
            case StatType.ATKPhysical:  obj.ATKPhysical  = Mathf.Max(0, iv);     break;
            case StatType.ATKMagic:     obj.ATKMagic     = Mathf.Max(0, iv);     break;
            case StatType.DEFPhysical:  obj.DEFPhysical  = Mathf.Max(0, iv);     break;
            case StatType.DEFMagic:     obj.DEFMagic     = Mathf.Max(0, iv);     break;
            case StatType.HPRegen:      obj.HPRegen      = Mathf.Max(0f, value); break;
            case StatType.MPRegen:      obj.MPRegen      = Mathf.Max(0f, value); break;
            case StatType.Speed:        obj.MoveSpeed    = Mathf.Max(0.1f, value); break;
            case StatType.AttackRange:  obj.AttackRange  = Mathf.Max(0.1f, value); break;
            case StatType.AttackSpeed:  obj.AttackSpeed  = Mathf.Max(0.05f, value); break;
            case StatType.LifeSteal:    obj.LifeSteal    = Mathf.Clamp01(value); break;
            case StatType.HealingPower: obj.HealingPower = Mathf.Max(0f, value); break;
        }
    }

    // ─── Debug / Editor ──────────────────────────────────────────────────────

    [ContextMenu("Debug: Log tất cả buff")]
    private void DebugLogAllBuffs()
    {
        Debug.Log($"[BuffSystem] {gameObject.name} – {activeBuffs.Count} buffs đang hoạt động:");
        foreach (var b in activeBuffs)
        {
            string val = b.mode is BuffMode.PercentAdd or BuffMode.PercentSub
                ? $"{b.value * 100f:F1}%"
                : $"{b.value:F2}";
            string dur = b.IsPermanent ? "∞" : $"{b.duration - b.elapsedTime:F1}s";
            Debug.Log($"  [{b.source}] {b.stat} {b.mode} {val} | {dur}");
        }
        Debug.Log("Base stats: " + string.Join(", ", baseStats.Select(kv => $"{kv.Key}={kv.Value:F2}")));
    }

    [ContextMenu("Debug: Force Recalculate")]
    private void DebugForceRecalculate() => RecalculateStats();
}
