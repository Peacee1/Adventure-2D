using System;
using UnityEngine;

/// <summary>
/// Hệ thống chuyển chức dành cho Cung Thủ khi đạt level 10.
/// Gắn cùng GameObject với Player, LevelSystem và BuffSystem.
///
/// ══════════════════════════════════════════════════════════════════════
///  Nhánh          │ Tỉ lệ │ Bonus / level > 10
/// ─────────────────┼───────┼──────────────────────────────────────────
///  Trường Cung Thủ│ 30%   │ +2% ATK Vật Lý; tầm đánh +25% (one-time)
///  Nỏ Thủ         │ 30%   │ +2% ATK Vật/DEF Vật/DEF Phép; tầm -50%(one); 3 tia
///  Ma Pháp CT     │ 30%   │ +5% MaxMP, +2% ATK Phép; +30% ATK Phép/đòn
///  Bạo Phát CT    │ 9%    │ +5% ATK Vật Lý; AtkSpd ×1.75 slow (one); 230% đòn
///  Diệt Thần CT   │ 1%    │ +2.5% HP/MP, +1% all; Execute < 3% HP
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
public class ArcherAdvancementSystem : MonoBehaviour
{
    // ─── Constants ───────────────────────────────────────────────────────────

    private const int   ADVANCEMENT_LEVEL  = 10;
    private const float EXECUTE_BASE       = 0.03f;   // 3% threshold ban đầu
    private const float EXECUTE_PER_LEVEL  = 0.001f;  // +0.1% / level > 10

    private const string SRC_ADV  = "ct_advancement";   // buff theo level
    private const string SRC_ONE  = "ct_one_time";      // hiệu ứng một lần

    private static readonly (AdvancementClass job, float weight)[] Weights =
    {
        (AdvancementClass.Longbowman,   30f),
        (AdvancementClass.Crossbowman,           30f),
        (AdvancementClass.SpellArcher,   30f),
        (AdvancementClass.BurstArcher,   9f),
        (AdvancementClass.GodSlayer,  1f),
    };

    // ─── State ───────────────────────────────────────────────────────────────

    [Header("Trạng thái (chỉ đọc)")]
    [SerializeField] private AdvancementClass currentAdvancement = AdvancementClass.None;
    [SerializeField] private float executeThreshold = EXECUTE_BASE;

    // ─── Special mechanic flags (đọc bởi Attack system) ─────────────────────

    /// <summary>Nỏ Thủ: mỗi đòn bắn 3 tia đạn thay vì 1.</summary>
    public bool HasTripleShot   => currentAdvancement == AdvancementClass.Crossbowman;

    /// <summary>Ma Pháp Cung Thủ: tỉ lệ cộng ATK Phép mỗi đòn (0.30 = 30%).</summary>
    public float MagicBonusRatio => currentAdvancement == AdvancementClass.SpellArcher ? 0.30f : 0f;

    /// <summary>Bạo Phát Cung Thủ: hệ số nhân sát thương đòn thường (2.30 = 230%).</summary>
    public float AttackMultiplier => currentAdvancement == AdvancementClass.BurstArcher ? 2.30f : 1.00f;

    // ─── References ──────────────────────────────────────────────────────────

    private Human       player;
    private LevelSystem levelSystem;
    private BuffSystem  buffSystem;

    // ─── Events ──────────────────────────────────────────────────────────────

    /// <summary>Phát ra khi chuyển chức xong. Truyền vào nhánh nhận được.</summary>
    public event Action<AdvancementClass> OnAdvancement;

    // ─── Properties ──────────────────────────────────────────────────────────

    public AdvancementClass CurrentAdvancement => currentAdvancement;
    public bool  HasAdvanced      => currentAdvancement != AdvancementClass.None;
    public float ExecuteThreshold => executeThreshold;

    // ─── Unity ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        player      = GetComponent<Human>();
        levelSystem = GetComponent<LevelSystem>();
        buffSystem  = GetComponent<BuffSystem>();

        if (player == null || levelSystem == null || buffSystem == null)
        {
            Debug.LogError("[ArcherAdvancementSystem] Thiếu Human, LevelSystem hoặc BuffSystem!");
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
        // Chỉ xử lý khi là Cung Thủ
        if (player.JobClass != JobClass.Archer) return;

        if (newLevel == ADVANCEMENT_LEVEL && !HasAdvanced)
            RollAdvancement();
        else if (newLevel > ADVANCEMENT_LEVEL && HasAdvanced)
            ApplyAdvancementBuffs(newLevel);
    }

    // ─── Roll ────────────────────────────────────────────────────────────────

    private void RollAdvancement()
    {
        float total = 0f;
        foreach (var (_, w) in Weights) total += w;

        float roll = UnityEngine.Random.Range(0f, total);
        float cumulative = 0f;
        AdvancementClass chosen = AdvancementClass.Longbowman;

        foreach (var (job, weight) in Weights)
        {
            cumulative += weight;
            if (roll <= cumulative) { chosen = job; break; }
        }

        currentAdvancement = chosen;
        ApplyOneTimeBuff(chosen);
        ApplyAdvancementBuffs(ADVANCEMENT_LEVEL);   // beyond = 0 lúc chuyển chức

        Debug.Log($"[ArcherAdv] ★ CHUYỂN CHỨC → [{GetDisplayName(chosen)}] " +
                  $"(roll={roll:F2}/{total:F2})");

        OnAdvancement?.Invoke(chosen);
    }

    // ─── One-time effects ────────────────────────────────────────────────────

    private void ApplyOneTimeBuff(AdvancementClass adv)
    {
        buffSystem.RemoveBuffsBySource(SRC_ONE);

        switch (adv)
        {
            // Tầm đánh +25%
            case AdvancementClass.Longbowman:
                buffSystem.AddBuff(BuffEntry.Percent(SRC_ONE, StatType.AttackRange, BuffMode.PercentAdd, 0.25f));
                Debug.Log("[ArcherAdv] Trường Cung Thủ: Tầm đánh +25%");
                break;

            // Tầm đánh -50%
            case AdvancementClass.Crossbowman:
                buffSystem.AddBuff(BuffEntry.Percent(SRC_ONE, StatType.AttackRange, BuffMode.PercentSub, 0.50f));
                Debug.Log("[ArcherAdv] Nỏ Thủ: Tầm đánh -50%, kích hoạt 3 tia đạn");
                break;

            // Ma Pháp Cung Thủ: không có hiệu ứng one-time (mechanic flag đã xử lý)
            case AdvancementClass.SpellArcher:
                Debug.Log("[ArcherAdv] Ma Pháp Cung Thủ: +30% ATK Phép/đòn (kích hoạt)");
                break;

            // Bạo Phát: tốc đánh ×1.75 (chậm hơn 75%)
            case AdvancementClass.BurstArcher:
                buffSystem.AddBuff(BuffEntry.Percent(SRC_ONE, StatType.AttackSpeed, BuffMode.PercentAdd, 0.75f));
                Debug.Log("[ArcherAdv] Bạo Phát Cung Thủ: AtkSpeed +75% (chậm hơn), đòn 230%");
                break;

            // Diệt Thần: mở khoá LifeSteal 1%, đặt execute threshold
            case AdvancementClass.GodSlayer:
                buffSystem.AddBuff(BuffEntry.Flat(SRC_ONE, StatType.LifeSteal, BuffMode.FlatAdd, 0.01f));
                executeThreshold = EXECUTE_BASE;
                Debug.Log("[ArcherAdv] Diệt Thần Cung Thủ: LifeSteal +1%, Execute ngưỡng 3% HP");
                break;
        }
    }

    // ─── Per-level buffs ──────────────────────────────────────────────────────

    /// <summary>
    /// Tính lại toàn bộ buff advancement theo level.
    /// beyond = level - 10 → buff = beyond × rate.
    /// Luôn ghi đè buff cũ (không cộng dồn).
    /// </summary>
    private void ApplyAdvancementBuffs(int currentLevel)
    {
        int beyond = Mathf.Max(0, currentLevel - ADVANCEMENT_LEVEL);

        // Xoá buff advancement cũ
        buffSystem.RemoveBuffsBySource(SRC_ADV);
        // Xoá sub-sources dùng cho adv nhiều stat
        for (int i = 0; i < 10; i++)
            buffSystem.RemoveBuffsBySource(SRC_ADV + "_" + i);

        switch (currentAdvancement)
        {
            // ── Trường Cung Thủ: +2% ATK Vật Lý / level ───────────────────
            case AdvancementClass.Longbowman:
                if (beyond > 0)
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV, StatType.ATKPhysical, BuffMode.PercentAdd, beyond * 0.02f));
                break;

            // ── Nỏ Thủ: +2% ATK Vật, DEF Vật, DEF Phép / level ───────────
            case AdvancementClass.Crossbowman:
                if (beyond > 0)
                {
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_0", StatType.ATKPhysical, BuffMode.PercentAdd, beyond * 0.02f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_1", StatType.DEFPhysical, BuffMode.PercentAdd, beyond * 0.02f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_2", StatType.DEFMagic,    BuffMode.PercentAdd, beyond * 0.02f));
                }
                break;

            // ── Ma Pháp Cung Thủ: +5% MaxMP, +2% ATK Phép / level ─────────
            case AdvancementClass.SpellArcher:
                if (beyond > 0)
                {
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_0", StatType.MaxMP,   BuffMode.PercentAdd, beyond * 0.05f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_1", StatType.ATKMagic, BuffMode.PercentAdd, beyond * 0.02f));
                }
                break;

            // ── Bạo Phát Cung Thủ: +5% ATK Vật Lý / level ────────────────
            case AdvancementClass.BurstArcher:
                if (beyond > 0)
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV, StatType.ATKPhysical, BuffMode.PercentAdd, beyond * 0.05f));
                break;

            // ── Diệt Thần Cung Thủ: mọi chỉ số tăng / level ──────────────
            case AdvancementClass.GodSlayer:
                if (beyond > 0)
                {
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_0", StatType.MaxHP,       BuffMode.PercentAdd, beyond * 0.025f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_1", StatType.MaxMP,       BuffMode.PercentAdd, beyond * 0.025f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_2", StatType.ATKPhysical, BuffMode.PercentAdd, beyond * 0.01f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_3", StatType.ATKMagic,    BuffMode.PercentAdd, beyond * 0.01f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_4", StatType.Speed,       BuffMode.PercentAdd, beyond * 0.01f));
                    buffSystem.AddBuff(BuffEntry.Flat   (SRC_ADV+"_5", StatType.LifeSteal,   BuffMode.FlatAdd,    beyond * 0.01f));
                    buffSystem.AddBuff(BuffEntry.Percent(SRC_ADV+"_6", StatType.AttackSpeed, BuffMode.PercentSub, beyond * 0.01f));
                    executeThreshold = EXECUTE_BASE + beyond * EXECUTE_PER_LEVEL;
                }
                break;
        }

        // Đảm bảo RecalculateStats chạy 1 lần duy nhất sau khi tất cả buffs được ghi
        buffSystem.RecalculateStats();

        if (beyond > 0)
            Debug.Log($"[ArcherAdv] [{GetDisplayName(currentAdvancement)}] " +
                      $"Lv{currentLevel} beyond={beyond} | " +
                      $"Active buffs: {buffSystem.ActiveBuffCount}");
    }

    // ─── Diệt Thần – Execute ──────────────────────────────────────────────────

    /// <summary>
    /// Kiểm tra và kết liễu mục tiêu nếu HP% ≤ executeThreshold (Diệt Thần CT).
    /// Gọi trước khi apply damage trong attack logic.
    /// </summary>
    public bool TryExecuteTarget(BaseObject target)
    {
        if (currentAdvancement != AdvancementClass.GodSlayer) return false;
        if (target == null || target.MaxHP <= 0) return false;

        float hpRatio = (float)target.HP / target.MaxHP;
        if (hpRatio <= executeThreshold)
        {
            target.TakeDamage(target.HP);
            Debug.Log($"[ArcherAdv] ⚔ DIỆT THẦN KẾT LIỄU {target.gameObject.name} " +
                      $"(HP {hpRatio:P1} ≤ {executeThreshold:P1})");
            return true;
        }
        return false;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    public static string GetDisplayName(AdvancementClass adv) => adv switch
    {
        AdvancementClass.Longbowman   => "Trường Cung Thủ",
        AdvancementClass.Crossbowman           => "Nỏ Thủ",
        AdvancementClass.SpellArcher   => "Ma Pháp Cung Thủ",
        AdvancementClass.BurstArcher  => "Bạo Phát Cung Thủ",
        AdvancementClass.GodSlayer => "Diệt Thần Cung Thủ",
        _                                => "None",
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // STUBS – Các hàm chưa có hệ thống hỗ trợ, viết sẵn để tích hợp sau
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [STUB] Hook chính từ Attack system – gọi ngay sau khi đòn đánh chạm mục tiêu.
    /// Xử lý tất cả cơ chế đặc biệt của Cung Thủ theo nhánh chuyển chức.
    ///
    /// TODO: Gọi từ AttackController / ProjectileHit:
    ///   archerAdvSystem.OnAttackHit(target, actualDamage);
    /// </summary>
    public void OnAttackHit(BaseObject target, int actualDamage)
    {
        if (!HasAdvanced || target == null) return;

        // LifeSteal đã xử lý trong BaseObject.OnDealtDamage – không cần gọi lại
        // (player.OnDealtDamage(actualDamage, target) tự check lifeSteal)

        switch (currentAdvancement)
        {
            case AdvancementClass.SpellArcher:
                ApplyMagicBonusDamage(target);
                break;

            case AdvancementClass.GodSlayer:
                TryExecuteTarget(target);
                break;
        }
    }

    // ─── Damage Calculation Helpers ──────────────────────────────────────────

    /// <summary>
    /// Trả về sát thương đã nhân AttackMultiplier cho Bạo Phát Cung Thủ.
    /// Gọi từ Attack system khi tính raw damage:
    ///   int raw = archerAdvSystem.GetModifiedAttackDamage(player.ATKPhysical);
    ///   target.TakePhysicalDamage(raw);
    /// </summary>
    public int GetModifiedAttackDamage(int baseATK)
    {
        return Mathf.RoundToInt(baseATK * AttackMultiplier);
        // Bạo Phát CT: 230% ATK  |  Các nhánh khác: 100% ATK (không đổi)
    }

    // ─── Ma Pháp Cung Thủ – Bonus Magic Damage ───────────────────────────────

    /// <summary>
    /// [STUB] Ma Pháp Cung Thủ: gây thêm 30% ATK Phép sau mỗi đòn đánh vật lý.
    ///
    /// TODO: Implement khi có Damage system hỗ trợ damage type riêng lẻ.
    /// </summary>
    private void ApplyMagicBonusDamage(BaseObject target)
    {
        if (currentAdvancement != AdvancementClass.SpellArcher) return;

        int magicBonus = Mathf.RoundToInt(player.ATKMagic * MagicBonusRatio);
        target.TakeMagicDamage(magicBonus); // Dùng dame phép, bị giảm bởi DEF Phép

        // Attacker hút máu từ bonus này
        player.OnDealtDamage(magicBonus, target);

        Debug.Log($"[ArcherAdv] Ma Pháp CT: bonus magic {magicBonus} ({MagicBonusRatio:P0} × {player.ATKMagic} ATK Phép)");
    }

    // ─── Nỏ Thủ – Triple Shot ───────────────────────────────────────────────

    /// <summary>
    /// [STUB] Nỏ Thủ: spawn thêm 2 tia đạn phụ hình quạt sau khi tia chính bắn.
    ///
    /// TODO: Gọi từ ProjectileSpawner sau khi bắn tia đạn đầu tiên:
    ///   if (archerAdvSystem.HasTripleShot)
    ///       archerAdvSystem.FireAdditionalProjectiles(direction);
    ///
    /// Yêu cầu: ProjectileManager / ObjectPooler, Projectile prefab
    /// </summary>
    public void FireAdditionalProjectiles(Vector2 direction)
    {
        if (!HasTripleShot) return;

        // TODO: Spawn 2 tia phụ góc ±15° so với tia chính
        // float spreadAngle = 15f;
        // for (int i = 1; i <= 2; i++)
        // {
        //     float angle = (i == 1 ? spreadAngle : -spreadAngle);
        //     Vector2 spreadDir = RotateVector(direction, angle);
        //     var proj = ObjectPooler.Spawn("ArrowProjectile", player.transform.position);
        //     proj.GetComponent<Projectile>().Launch(spreadDir, player.ATKPhysical * 0.8f); // 80% ATK phụ
        // }
        Debug.Log("[ArcherAdv] [STUB] FireAdditionalProjectiles → Chưa có Projectile system");
    }

    /// <summary>[STUB] Nỏ Thủ: Bắn tia đạn chính (tia 1/3).</summary>
    public void FireProjectile(Vector2 direction)
    {
        // TODO: Implement khi có Projectile system
        // var proj = ObjectPooler.Spawn("ArrowProjectile", player.transform.position);
        // proj.GetComponent<Projectile>().Launch(direction, player.ATKPhysical);
        //
        // if (HasTripleShot)
        //     FireAdditionalProjectiles(direction);
        Debug.Log("[ArcherAdv] [STUB] FireProjectile → Chưa có Projectile system");
    }

    // ─── Bạo Phát Cung Thủ – Charge Effect ──────────────────────────────────

    /// <summary>
    /// [STUB] Bạo Phát Cung Thủ: Hiệu ứng charge khi AttackSpeed chậm hơn.
    /// Visual feedback cho player biết đòn sắp ra.
    ///
    /// TODO: Gọi từ Attack animator event hoặc coroutine charge:
    ///   archerAdvSystem.OnChargingAttack(chargeProgress);  // 0f → 1f
    /// </summary>
    public void OnChargingAttack(float chargeProgress)
    {
        if (currentAdvancement != AdvancementClass.BurstArcher) return;

        // TODO: Hiệu ứng visual scale-up bow, particle charge
        // bowChargeVFX?.SetFloat("_ChargeProgress", chargeProgress);
        //
        // TODO: Sound effect tăng dần
        // AudioManager.Play("bow_charge", pitch: 0.8f + chargeProgress * 0.4f);
        //
        // TODO: Screen shake nhẹ khi chargeProgress >= 0.9f
        // if (chargeProgress >= 0.9f) CameraShake.Trigger(0.1f, 0.05f);
        Debug.Log($"[ArcherAdv] [STUB] BaoPhat charging... {chargeProgress:P0}");
    }

    // ─── Diệt Thần – Aura Effect ────────────────────────────────────────────

    /// <summary>
    /// [STUB] Diệt Thần Cung Thủ: Aura gây sợ hãi cho kẻ địch gần.
    ///
    /// TODO: Implement khi có AoE / AI Behavior system.
    /// Ý tưởng: kẻ địch trong radius bị debuff ATK -5%.
    /// </summary>
    public void UpdateDeathGodAura()
    {
        if (currentAdvancement != AdvancementClass.GodSlayer) return;

        // TODO: Tương tự HolyAura của Kiếm Thánh
        // var enemies = Physics2D.OverlapCircleAll(player.transform.position, 5f, enemyLayer);
        // foreach (var e in enemies)
        // {
        //     var bs = e.GetComponent<BuffSystem>();
        //     bs?.AddBuff(BuffEntry.Timed("death_aura", StatType.ATKPhysical, BuffMode.PercentSub, 0.05f, 1.5f));
        // }
    }

    // ─── Shared Utility ─────────────────────────────────────────────────────

    /// <summary>[STUB Utility] Xoay vector 2D theo góc độ.</summary>
    private static Vector2 RotateVector(Vector2 v, float degrees)
    {
        // TODO: Dùng để tính hướng tia đạn phụ của Nỏ Thủ
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(
            v.x * Mathf.Cos(rad) - v.y * Mathf.Sin(rad),
            v.x * Mathf.Sin(rad) + v.y * Mathf.Cos(rad)
        );
    }

    // ─── Debug ───────────────────────────────────────────────────────────────

    [ContextMenu("Debug: Force Trường Cung Thủ")]
    private void DbgTruong()  { currentAdvancement = AdvancementClass.Longbowman;   ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Force Nỏ Thủ")]
    private void DbgNo()      { currentAdvancement = AdvancementClass.Crossbowman;           ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Force Ma Pháp Cung Thủ")]
    private void DbgMaPhap()  { currentAdvancement = AdvancementClass.SpellArcher;   ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Force Bạo Phát Cung Thủ")]
    private void DbgBaoPhat() { currentAdvancement = AdvancementClass.BurstArcher;  ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
    [ContextMenu("Debug: Force Diệt Thần Cung Thủ")]
    private void DbgDietThan(){ currentAdvancement = AdvancementClass.GodSlayer; ApplyOneTimeBuff(currentAdvancement); ApplyAdvancementBuffs(levelSystem.Level); }
}
