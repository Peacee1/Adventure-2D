using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Quản lý hệ thống level và EXP của Player.
/// Gắn cùng GameObject với Player.
/// </summary>
public class LevelSystem : MonoBehaviour
{
    // ─── Cấu hình ────────────────────────────────────────────────────────────

    [Header("Level Settings")]
    [SerializeField] private int  level        = 1;
    [SerializeField] private int  maxLevel     = 99;
    [SerializeField] private int  currentExp   = 0;

    [Header("EXP Curve  (expNeeded = baseExp × level ^ exponent)")]
    [SerializeField] private int   baseExp     = 100;   // EXP cần ở level 1
    [SerializeField] private float expExponent = 1.5f;  // Độ dốc đường cong

    // ─── Runtime ─────────────────────────────────────────────────────────────

    private Human         player;
    private JobBaseStats  jobStats;   // cache lại growth rates
    private bool          statsReady = false;

    // ─── Events ──────────────────────────────────────────────────────────────

    /// <summary>Được gọi khi player lên level. Truyền vào level mới.</summary>
    public event Action<int> OnLevelUp;

    /// <summary>Được gọi khi EXP thay đổi. Truyền vào (currentExp, expToNextLevel).</summary>
    public event Action<int, int> OnExpChanged;

    // ─── Properties ──────────────────────────────────────────────────────────

    public int  Level        => level;
    public int  MaxLevel     => maxLevel;
    public int  CurrentExp   => currentExp;
    public int  ExpToNextLevel => CalcExpToNextLevel(level);
    public bool IsMaxLevel   => level >= maxLevel;

    // ─── Unity ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        player = GetComponent<Human>();
        if (player == null)
        {
            Debug.LogError("[LevelSystem] Không tìm thấy Human component trên cùng GameObject!");
            enabled = false;
        }
    }

    private void Start()
    {
        // Lấy job stats sau khi Player.Awake() đã chạy xong
        RefreshJobStats();
        Debug.Log($"[LevelSystem] Khởi tạo – Level:{level} | EXP đến level sau: {ExpToNextLevel}");
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Khôi phục level và EXP trực tiếp từ data server (không trigger OnLevelUp).
    /// Gọi từ LocalPlayer.Initialize() sau khi load scene.
    /// </summary>
    public void SetLevelDirect(int lvl, int exp)
    {
        level      = Mathf.Clamp(lvl, 1, maxLevel);
        currentExp = Mathf.Max(0, exp);
        Debug.Log($"[LevelSystem] Restored from server: Level={level} EXP={currentExp}/{ExpToNextLevel}");
        OnExpChanged?.Invoke(currentExp, ExpToNextLevel);
    }

    /// <summary>
    /// Nhận EXP. Nếu đủ ngưỡng sẽ tự động lên level (có thể lên nhiều level cùng lúc).
    /// </summary>
    public void GainExp(int amount)
    {
        if (IsMaxLevel) return;

        currentExp += Mathf.Max(0, amount);
        Debug.Log($"[LevelSystem] +{amount} EXP → {currentExp}/{ExpToNextLevel}");

        OnExpChanged?.Invoke(currentExp, ExpToNextLevel);

        // Kiểm tra level up (vòng lặp hỗ trợ lên nhiều level)
        while (!IsMaxLevel && currentExp >= ExpToNextLevel)
        {
            currentExp -= ExpToNextLevel;
            LevelUp();
        }

        // Đảm bảo không vượt ngưỡng ở MaxLevel
        if (IsMaxLevel)
        {
            currentExp = 0;
        }
    }

    /// <summary>Cập nhật lại cache job stats (gọi sau khi đổi chức nghiệp).</summary>
    public void RefreshJobStats()
    {
        // Gọi reflection-free: lấy từ Player thông qua method public
        jobStats   = player.GetCurrentJobStats();
        statsReady = true;
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    /// <summary>Thực hiện lên 1 level: tăng chỉ số gốc, snapshot base, recalculate buffs, hồi HP/MP, phát event.</summary>
    private void LevelUp()
    {
        level++;

        if (statsReady)
        {
            ApplyGrowth(jobStats);
        }

        // Sau khi tăng chỉ số gốc → chụp lại base → buffs sẽ được tính trên nền mới
        player.BuffSystem?.SnapshotBaseStats();
        // RecalculateStats sẽ được AdvancementSystem kích hoạt khi nó cập nhật buff,
        // nhưng nếu chưa có advancement thì cần gọi thủ công để đồng bộ
        player.BuffSystem?.RecalculateStats();

        // Hồi đầy HP và MP sau khi lên level (sau khi MaxHP/MP đã được tính xong)
        player.HP = player.MaxHP;
        player.MP = player.MaxMP;

        Debug.Log($"[LevelSystem] ★ LEVEL UP! → Level {level} | " +
                  $"HP:{player.MaxHP} MP:{player.MaxMP} " +
                  $"ATKVat:{player.ATKPhysical} ATKPhep:{player.ATKMagic} " +
                  $"DEFVat:{player.DEFPhysical} DEFPhep:{player.DEFMagic} " +
                  $"(base chụp xong, buffs đang reapply)");

        OnLevelUp?.Invoke(level);
        OnExpChanged?.Invoke(currentExp, ExpToNextLevel);
    }

    /// <summary>Áp dụng tăng chỉ số GỐC theo profile của chức nghiệp (TRƯỚC khi buff).</summary>
    private void ApplyGrowth(JobBaseStats s)
    {
        // Đây là chỉ số GỐC – BuffSystem sẽ snapshot và reapply buff SAU
        player.MaxHP       += s.hpPerLevel;
        player.MaxMP       += s.mpPerLevel;
        player.ATKPhysical += s.atkPhysPerLevel;
        player.ATKMagic    += s.atkMagPerLevel;
        player.DEFPhysical += s.defPhysPerLevel;
        player.DEFMagic    += s.defMagPerLevel;
        player.HPRegen     += s.hpRegenPerLevel;
        player.MPRegen     += s.mpRegenPerLevel;
    }

    /// <summary>
    /// Công thức EXP cần để lên level tiếp theo:
    /// expNeeded(n) = baseExp × n ^ exponent
    /// </summary>
    private int CalcExpToNextLevel(int currentLevel)
    {
        return Mathf.RoundToInt(baseExp * Mathf.Pow(currentLevel, expExponent));
    }

    // ─── Debug / Editor ──────────────────────────────────────────────────────

    [ContextMenu("Debug: +100 EXP")]
    private void DebugGain100Exp() => GainExp(100);

    [ContextMenu("Debug: +1000 EXP")]
    private void DebugGain1000Exp() => GainExp(1000);

    [ContextMenu("Debug: Force Level Up")]
    private void DebugForceLevelUp() => GainExp(ExpToNextLevel - currentExp);
}
