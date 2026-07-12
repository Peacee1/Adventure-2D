using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hệ thống chuyển chức dành cho Kiếm Sĩ khi đạt level 10.
/// Tất cả bonus được lưu vào BuffSystem thay vì sửa stats trực tiếp.
///
/// ── Công thức buff theo level ────────────────────────────────────────────────
/// beyond      = currentLevel - 10
/// Đại Kiếm Sĩ : ATKPhysical PercentAdd = beyond × 0.02   (vd level 36: 52%)
/// Song Kiếm Sĩ : Speed       PercentAdd = beyond × 0.02
/// Spellblade  : MaxMP        PercentAdd = beyond × 0.05
///                ATKMagic     PercentAdd = beyond × 0.02
/// Cuồng KS     : LifeSteal    FlatAdd    = 0.05(init) + beyond × 0.01
/// Phi KS       : AttackSpeed  PercentSub = beyond × 0.02
///                AttackRange  PercentAdd = 3.0 (×4, one-time)
/// Kiếm Thánh   : MaxHP/MP     PercentAdd = beyond × 0.025
///                ATK/Speed/LifeSteal/AtkSpd PercentAdd/FlatAdd per beyond
/// </summary>
public class AdvancementSystem : MonoBehaviour
{
    // ─── Constants ───────────────────────────────────────────────────────────

    private const int   ADVANCEMENT_LEVEL    = 10;
    private const float FLIGHT_SPEED_BONUS   = 0.20f;   // +20% khi phi hành
    private const float EXECUTE_BASE         = 0.03f;   // 3% threshold ban đầu
    private const float EXECUTE_PER_LEVEL    = 0.001f;  // +0.1% mỗi level > 10

    // Tên nguồn buff (source) dùng cho BuffSystem
    private const string SRC_ADV = "advancement";       // buff chính theo level
    private const string SRC_ONE = "adv_one_time";      // hiệu ứng một lần

    /// <summary>Bảng trọng số (tổng = 100).</summary>
    private static readonly (AdvancementClass job, float weight)[] AdvancementWeights =
    {
        (AdvancementClass.GreatWarrior,    30f),
        (AdvancementClass.DualBlade,   30f),
        (AdvancementClass.Spellblade, 30f),
        (AdvancementClass.Berserker,  4.5f),
        (AdvancementClass.SwiftBlade,    4.5f),
        (AdvancementClass.SwordSaint,    1f),
    };

    // ─── State ───────────────────────────────────────────────────────────────

    [Header("Trạng thái (chỉ đọc)")]
    [SerializeField] private AdvancementClass currentAdvancement = AdvancementClass.None;
    [SerializeField] private bool  isFlightActive  = false;
    [SerializeField] private float executeThreshold = EXECUTE_BASE;

    // ─── References ──────────────────────────────────────────────────────────

    private Human       player;
    private LevelSystem levelSystem;
    private BuffSystem  buffSystem;

    // ─── Events ──────────────────────────────────────────────────────────────

    public event Action<AdvancementClass> OnAdvancement;

    // ─── Properties ──────────────────────────────────────────────────────────

    public AdvancementClass CurrentAdvancement => currentAdvancement;
    public bool  HasAdvanced     => currentAdvancement != AdvancementClass.None;
    public bool  IsFlightActive  => isFlightActive;
    public float ExecuteThreshold => executeThreshold;

    // ─── Unity ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        player      = GetComponent<Human>();
        levelSystem = GetComponent<LevelSystem>();
        buffSystem  = GetComponent<BuffSystem>();

        if (player == null || levelSystem == null || buffSystem == null)
        {
            Debug.LogError("[AdvancementSystem] Thiếu Human, LevelSystem hoặc BuffSystem trên cùng GameObject!");
            enabled = false;
        }
    }

    private void Start()
    {
        levelSystem.OnLevelUp += HandleLevelUp;
    }

    private void OnDestroy()
    {
        if (levelSystem != null) levelSystem.OnLevelUp -= HandleLevelUp;
    }

    // ─── Core ────────────────────────────────────────────────────────────────

    private void HandleLevelUp(int newLevel)
    {
        if (player.JobClass != JobClass.Warrior) return;

        if (newLevel == ADVANCEMENT_LEVEL && !HasAdvanced)
        {
            RollAdvancement();
        }
        else if (newLevel > ADVANCEMENT_LEVEL && HasAdvanced)
        {
            // Cập nhật buff theo level mới – KHÔNG cộng dồn, chỉ ghi đè
            ApplyAdvancementBuffs(newLevel);
        }
    }

    // ─── Roll ────────────────────────────────────────────────────────────────

    private void RollAdvancement()
    {
        float total = 0f;
        foreach (var (_, w) in AdvancementWeights) total += w;

        float roll = UnityEngine.Random.Range(0f, total);
        float cumulative = 0f;
        AdvancementClass chosen = AdvancementClass.GreatWarrior;

        foreach (var (job, weight) in AdvancementWeights)
        {
            cumulative += weight;
            if (roll <= cumulative) { chosen = job; break; }
        }

        currentAdvancement = chosen;
        ApplyOneTimeBuff(chosen);

        // Áp dụng buff cho level 10 (beyond = 0 → không có bonus % ban đầu)
        ApplyAdvancementBuffs(ADVANCEMENT_LEVEL);

        Debug.Log($"[AdvancementSystem] ★ CHUYỂN CHỨC → [{GetDisplayName(chosen)}] " +
                  $"(roll={roll:F2}/{total:F2})");

        OnAdvancement?.Invoke(chosen);
    }

    // ─── One-time buff (áp dụng đúng 1 lần khi chuyển chức) ─────────────────

    private void ApplyOneTimeBuff(AdvancementClass adv)
    {
        // Xoá hiệu ứng cũ nếu có (an toàn khi debug)
        buffSystem.RemoveBuffsBySource(SRC_ONE);

        switch (adv)
        {
            case AdvancementClass.Berserker:
                // Mở khoá hút máu 5% flat
                buffSystem.AddBuff(BuffEntry.Flat(SRC_ONE, StatType.LifeSteal, BuffMode.FlatAdd, 0.05f));
                Debug.Log("[AdvancementSystem] Cuồng Kiếm Sĩ: Mở khoá LifeSteal +5%");
                break;

            case AdvancementClass.SwiftBlade:
                // Tầm đánh ×4 → PercentAdd +300% (base × (1+3) = base × 4)
                buffSystem.AddBuff(BuffEntry.Percent(SRC_ONE, StatType.AttackRange, BuffMode.PercentAdd, 3.0f));
                Debug.Log($"[AdvancementSystem] Phi Kiếm Sĩ: Tầm đánh ×4 (PercentAdd +300%)");
                break;

            case AdvancementClass.SwordSaint:
                // Mở khoá hút máu 1% flat
                buffSystem.AddBuff(BuffEntry.Flat(SRC_ONE, StatType.LifeSteal, BuffMode.FlatAdd, 0.01f));
                executeThreshold = EXECUTE_BASE;
                Debug.Log("[AdvancementSystem] Kiếm Thánh: LifeSteal +1%, Execute ngưỡng 3% HP");
                break;
        }
    }

    // ─── Per-level buff (gọi mỗi lần level up sau level 10) ──────────────────

    /// <summary>
    /// Cập nhật buff advancement theo level hiện tại.
    /// Dùng công thức: beyond = level - 10, buff = beyond × rate (PercentAdd hoặc FlatAdd).
    /// Mỗi lần gọi sẽ THAY THẾ buff cũ (không cộng dồn).
    /// </summary>
    private void ApplyAdvancementBuffs(int currentLevel)
    {
        int beyond = Mathf.Max(0, currentLevel - ADVANCEMENT_LEVEL);

        // Xoá buff advancement cũ trước khi ghi mới
        buffSystem.RemoveBuffsBySource(SRC_ADV);

        switch (currentAdvancement)
        {
            // ── Đại Kiếm Sĩ: ATK Vật Lý +2%/level ──────────────────────────
            case AdvancementClass.GreatWarrior:
                if (beyond > 0)
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV, StatType.ATKPhysical, BuffMode.PercentAdd, beyond * 0.02f));
                break;

            // ── Song Kiếm Sĩ: Speed +2%/level ───────────────────────────────
            case AdvancementClass.DualBlade:
                if (beyond > 0)
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV, StatType.Speed, BuffMode.PercentAdd, beyond * 0.02f));
                break;

            // ── Ma Pháp Kiếm Sĩ: MaxMP +5%/level, ATKMagic +2%/level ────────
            case AdvancementClass.Spellblade:
                if (beyond > 0)
                {
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV + "_mp",  StatType.MaxMP,   BuffMode.PercentAdd, beyond * 0.05f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV + "_atk", StatType.ATKMagic,BuffMode.PercentAdd, beyond * 0.02f));
                }
                break;

            // ── Cuồng Kiếm Sĩ: LifeSteal +1%/level (flat, trên nền base=0) ──
            case AdvancementClass.Berserker:
                if (beyond > 0)
                    buffSystem.AddBuff(BuffEntry.Flat(SRC_ADV, StatType.LifeSteal, BuffMode.FlatAdd, beyond * 0.01f));
                break;

            // ── Phi Kiếm Sĩ: AttackSpeed -2%/level (nhanh hơn) ─────────────
            case AdvancementClass.SwiftBlade:
                if (beyond > 0)
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV, StatType.AttackSpeed, BuffMode.PercentSub, beyond * 0.02f));
                break;

            // ── Kiếm Thánh: tất cả chỉ số tăng ─────────────────────────────
            case AdvancementClass.SwordSaint:
                if (beyond > 0)
                {
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_hp",  StatType.MaxHP,      BuffMode.PercentAdd, beyond * 0.025f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_mp",  StatType.MaxMP,      BuffMode.PercentAdd, beyond * 0.025f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_ap",  StatType.ATKPhysical,BuffMode.PercentAdd, beyond * 0.01f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_am",  StatType.ATKMagic,   BuffMode.PercentAdd, beyond * 0.01f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_spd", StatType.Speed,      BuffMode.PercentAdd, beyond * 0.01f));
                    buffSystem.AddBuff(BuffEntry.Flat   (SRC_ADV+"_ls",  StatType.LifeSteal,  BuffMode.FlatAdd,    beyond * 0.01f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_as",  StatType.AttackSpeed,BuffMode.PercentSub, beyond * 0.01f));
                    // Execute threshold tăng theo level
                    executeThreshold = EXECUTE_BASE + beyond * EXECUTE_PER_LEVEL;
                }
                break;
        }

        // BuffSystem tự gọi RecalculateStats() trong mỗi AddBuff,
        // nhưng với MaPhap/KiemThanh cần batch. Ta gọi lại 1 lần cuối cho chắc.
        if (currentAdvancement is AdvancementClass.Spellblade or AdvancementClass.SwordSaint)
            buffSystem.RecalculateStats();

        if (beyond > 0)
            Debug.Log($"[AdvancementSystem] [{GetDisplayName(currentAdvancement)}] " +
                      $"Lv{currentLevel} | beyond={beyond} | " +
                      $"Buff sources: {buffSystem.ActiveBuffCount} active");
    }

    // ─── Phi Kiếm Sĩ – Ngự Kiếm Phi Hành ────────────────────────────────────

    /// <summary>
    /// Bật/tắt Ngự Kiếm Phi Hành (chỉ dùng được khi là Phi Kiếm Sĩ).
    /// Dùng BuffSystem để áp dụng +20% speed thay vì sửa trực tiếp.
    /// </summary>
    public void ToggleSwordFlight()
    {
        if (currentAdvancement != AdvancementClass.SwiftBlade)
        {
            Debug.LogWarning("[AdvancementSystem] Chỉ Phi Kiếm Sĩ mới dùng được ToggleSwordFlight!");
            return;
        }

        isFlightActive = !isFlightActive;
        const string FLIGHT_SRC = "phi_flight";

        if (isFlightActive)
            buffSystem.AddBuff(BuffEntry.Percent(FLIGHT_SRC, StatType.Speed, BuffMode.PercentAdd, FLIGHT_SPEED_BONUS));
        else
            buffSystem.RemoveBuffsBySource(FLIGHT_SRC);

        Debug.Log($"[AdvancementSystem] Ngự Kiếm Phi Hành: {(isFlightActive ? "BẬT +" + FLIGHT_SPEED_BONUS * 100 + "% speed" : "TẮT")}");
    }

    // ─── Kiếm Thánh – Tự kết liễu ────────────────────────────────────────────

    /// <summary>
    /// Kiểm tra và thực hiện kết liễu mục tiêu nếu HP% ≤ executeThreshold.
    /// Gọi TRƯỚC khi apply damage trong attack logic.
    /// </summary>
    public bool TryExecuteTarget(BaseObject target)
    {
        if (currentAdvancement != AdvancementClass.SwordSaint) return false;
        if (target == null || target.MaxHP <= 0) return false;

        float hpRatio = (float)target.HP / target.MaxHP;
        if (hpRatio <= executeThreshold)
        {
            target.TakeDamage(target.HP);
            Debug.Log($"[AdvancementSystem] ⚔ KIẾM THÁNH KẾT LIỄU {target.gameObject.name} " +
                      $"(HP {hpRatio:P1} ≤ {executeThreshold:P1})");
            return true;
        }
        return false;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    public static string GetDisplayName(AdvancementClass adv) => adv switch
    {
        AdvancementClass.GreatWarrior    => "Đại Kiếm Sĩ",
        AdvancementClass.DualBlade   => "Song Kiếm Sĩ",
        AdvancementClass.Spellblade => "Ma Pháp Kiếm Sĩ",
        AdvancementClass.Berserker  => "Cuồng Kiếm Sĩ",
        AdvancementClass.SwiftBlade    => "Phi Kiếm Sĩ",
        AdvancementClass.SwordSaint    => "Kiếm Thánh",
        _                             => "None",
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // STUBS – Các hàm chưa có hệ thống hỗ trợ, viết sẵn để tích hợp sau
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [STUB] Hook chính từ Attack system – gọi ngay sau khi đòn đánh chạm mục tiêu.
    ///
    /// TODO: Gọi từ AttackController / WeaponSystem:
    ///   advancementSystem.OnAttackHit(target, actualDamage);
    /// </summary>
    public void OnAttackHit(BaseObject target, int actualDamage)
    {
        if (!HasAdvanced) return;

        // LifeSteal đã được xử lý trong BaseObject.OnDealtDamage – không cần gọi lại ở đây
        // (BaseObject.OnDealtDamage sẽ tự check lifeSteal và gọi Heal)

        // Kiếm Thánh: kiểm tra execute sau mỗi đòn
        if (currentAdvancement == AdvancementClass.SwordSaint)
            TryExecuteTarget(target);

        // TODO: Phi Kiếm Sĩ – hiệu ứng phi kiếm trúng mục tiêu
        // if (currentAdvancement == AdvancementClass.SwiftBlade)
        //     ApplyPhiKiemHitEffect(target, actualDamage);
    }

    // ─── Phi Kiếm Sĩ – Tích hợp chuyển động ────────────────────────────────

    /// <summary>
    /// [STUB] Phi Kiếm Sĩ: Tích hợp với Movement system khi phi hành đang bật.
    ///
    /// TODO: Gọi từ PlayerController.Move() hoặc Rigidbody update:
    ///   if (advancementSystem.IsFlightActive)
    ///       advancementSystem.ApplyFlightMovement(moveInput, rb);
    ///
    /// Yêu cầu: PlayerController cần expose Rigidbody2D ref
    /// </summary>
    public void ApplyFlightMovement(Vector2 moveInput, Rigidbody2D rb)
    {
        if (!isFlightActive || currentAdvancement != AdvancementClass.SwiftBlade) return;

        // TODO: Implement bay theo 8 hướng không bị ảnh hưởng gravity, altitude layer
        // rb.velocity = moveInput * player.MoveSpeed;
        //
        // TODO: Hiệu ứng visual (particle trail, sprite layer change)
        // flightParticle?.Play();
        //
        // TODO: Kiểm tra va chạm với ceiling / platform khi đang bay
    }

    /// <summary>
    /// [STUB] Phi Kiếm Sĩ: Tấn công ngay kiếm – gửi projectile theo quỹ đạo cong.
    ///
    /// TODO: Implement khi có Projectile system.
    /// Kỹ năng đặc biệt: kiếm bay đến mục tiêu rồi quay về tay chủ.
    /// </summary>
    public void FireSwordProjectile(Vector2 targetPosition)
    {
        if (currentAdvancement != AdvancementClass.SwiftBlade) return;

        // TODO: Spawn projectile từ ObjectPooler
        // var proj = ObjectPooler.Spawn("SwordProjectile", player.transform.position);
        // proj.GetComponent<SwordProjectile>().Launch(targetPosition, returnToOwner: true);
        //
        // TODO: Cooldown cho kỹ năng này
        Debug.Log("[AdvancementSystem] [STUB] FireSwordProjectile → Chưa có Projectile system");
    }

    // ─── Kiếm Thánh – Các hiệu ứng chưa implement ──────────────────────────

    /// <summary>
    /// [STUB] Kiếm Thánh: Aura thánh – áp debuff cho kẻ địch trong vùng.
    ///
    /// TODO: Implement khi có AoE / TriggerZone system.
    /// Ý tưởng: mỗi X giây phát aura, kẻ địch trong radius nhận debuff DEF -10%.
    /// </summary>
    public void UpdateHolyAura()
    {
        if (currentAdvancement != AdvancementClass.SwordSaint) return;

        // TODO: Lấy danh sách enemy trong radius
        // var enemies = Physics2D.OverlapCircleAll(player.transform.position, auraRadius, enemyLayer);
        // foreach (var e in enemies)
        // {
        //     var bs = e.GetComponent<BuffSystem>();
        //     bs?.AddBuff(BuffEntry.Timed("holy_aura", StatType.DEFPhysical, BuffMode.PercentSub, 0.10f, 2f));
        // }
        Debug.Log("[AdvancementSystem] [STUB] UpdateHolyAura → Chưa có AoE system");
    }

    // ─── Debug ───────────────────────────────────────────────────────────────

    [ContextMenu("Debug: Force Đại Kiếm Sĩ")]
    private void DbgDai()      { currentAdvancement = AdvancementClass.GreatWarrior;    ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Force Song Kiếm Sĩ")]
    private void DbgSong()     { currentAdvancement = AdvancementClass.DualBlade;   ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Force Ma Pháp Kiếm Sĩ")]
    private void DbgMaPhap()   { currentAdvancement = AdvancementClass.Spellblade; ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Force Cuồng Kiếm Sĩ")]
    private void DbgCuong()    { currentAdvancement = AdvancementClass.Berserker;  ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Force Phi Kiếm Sĩ")]
    private void DbgPhi()      { currentAdvancement = AdvancementClass.SwiftBlade;    ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Force Kiếm Thánh")]
    private void DbgKiemThanh(){ currentAdvancement = AdvancementClass.SwordSaint;    ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Toggle Phi Hành")]
    private void DbgFlight()   => ToggleSwordFlight();
    [ContextMenu("Debug: Log tất cả buff")]
    private void DbgLogBuffs() => buffSystem.SendMessage("DebugLogAllBuffs", SendMessageOptions.DontRequireReceiver);
}
