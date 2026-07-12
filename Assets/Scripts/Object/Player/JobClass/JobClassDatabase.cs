using UnityEngine;

/// <summary>
/// Database tập hợp chỉ số ban đầu cho tất cả chức nghiệp.
/// Tạo asset mới: Right-click > Create > Adventure2D > JobClass Database
/// Sau đó kéo asset này vào trường JobClassDatabase của Player.
/// </summary>
[CreateAssetMenu(fileName = "JobClassDatabase", menuName = "Adventure2D/JobClass Database")]
public class JobClassDatabase : ScriptableObject
{
    [Header("Chỉ Số Ban Đầu Theo Chức Nghiệp")]
    [SerializeField] private JobBaseStats[] allJobStats = new JobBaseStats[]
    {
        // Mảng này sẽ được khởi tạo mặc định trong OnValidate nếu rỗng
    };

    /// <summary>
    /// Trả về chỉ số ban đầu của một chức nghiệp cụ thể.
    /// </summary>
    public JobBaseStats GetStats(JobClass jobClass)
    {
        foreach (var stats in allJobStats)
        {
            if (stats.jobClass == jobClass)
                return stats;
        }

        Debug.LogWarning($"[JobClassDatabase] Không tìm thấy chỉ số cho chức nghiệp: {jobClass}. Trả về giá trị mặc định.");
        return JobBaseStats.Default;
    }

    private void OnValidate()
    {
        // Nếu mảng rỗng thì tự tạo dữ liệu mặc định
        if (allJobStats == null || allJobStats.Length == 0)
        {
            InitializeDefaults();
        }
    }

    [ContextMenu("Khởi tạo chỉ số mặc định")]
    private void InitializeDefaults()
    {
        allJobStats = new JobBaseStats[]
        {
            // ── Cung Thủ ─────────────────────────────────────────────────
            new JobBaseStats
            {
                jobClass         = JobClass.Archer,
                displayName      = "Archer",
                description      = "Xạ thủ tầm xa. ATK Vật Lý cao, di chuyển linh hoạt nhưng HP thấp.",
                maxHp            = 800,
                maxMp            = 300,
                atkPhysical      = 80,
                atkMagic         = 10,
                defPhysical      = 30,
                defMagic         = 20,
                hpRegen          = 2f,
                mpRegen          = 3f,
                healingPower     = 0f,
                speed            = 6.5f,
                // Tăng trưởng: chú trọng ATK Vật Lý, HP tăng vừa phải
                hpPerLevel       = 30,
                mpPerLevel       = 8,
                atkPhysPerLevel  = 5,
                atkMagPerLevel   = 0,
                defPhysPerLevel  = 1,
                defMagPerLevel   = 0,
                hpRegenPerLevel  = 0.1f,
                mpRegenPerLevel  = 0.1f,
            },

            // ── Kiếm Sĩ ─────────────────────────────────────────────────
            new JobBaseStats
            {
                jobClass         = JobClass.Warrior,
                displayName      = "Warrior",
                description      = "Chiến binh cận chiến. Cân bằng giữa tấn công và phòng thủ vật lý.",
                maxHp            = 1100,
                maxMp            = 200,
                atkPhysical      = 90,
                atkMagic         = 10,
                defPhysical      = 60,
                defMagic         = 25,
                hpRegen          = 4f,
                mpRegen          = 2f,
                healingPower     = 0f,
                speed            = 5f,
                // Tăng trưởng: cân bằng HP, ATK Vật Lý, DEF Vật Lý
                hpPerLevel       = 50,
                mpPerLevel       = 5,
                atkPhysPerLevel  = 5,
                atkMagPerLevel   = 0,
                defPhysPerLevel  = 3,
                defMagPerLevel   = 1,
                hpRegenPerLevel  = 0.2f,
                mpRegenPerLevel  = 0f,
            },

            // ── Pháp Sư ─────────────────────────────────────────────────
            new JobBaseStats
            {
                jobClass         = JobClass.Mage,
                displayName      = "Mage",
                description      = "Sử dụng phép thuật mạnh mẽ. ATK Phép cao nhất nhưng HP và DEF rất thấp.",
                maxHp            = 650,
                maxMp            = 600,
                atkPhysical      = 15,
                atkMagic         = 120,
                defPhysical      = 20,
                defMagic         = 50,
                hpRegen          = 1f,
                mpRegen          = 8f,
                healingPower     = 0f,
                speed            = 4.5f,
                // Tăng trưởng: chú trọng ATK Phép và MP, HP tăng ít nhất
                hpPerLevel       = 20,
                mpPerLevel       = 25,
                atkPhysPerLevel  = 0,
                atkMagPerLevel   = 7,
                defPhysPerLevel  = 0,
                defMagPerLevel   = 2,
                hpRegenPerLevel  = 0f,
                mpRegenPerLevel  = 0.4f,
            },

            // ── Trị Liệu Sư ────────────────────────────────────────────────
            new JobBaseStats
            {
                jobClass         = JobClass.Healer,
                displayName      = "Healer",
                description      = "Hỗ trợ hồi máu đồng đội. MP cao, ATK thấp, DEF Phép tốt.",
                maxHp            = 750,
                maxMp            = 700,
                atkPhysical      = 20,
                atkMagic         = 55,
                defPhysical      = 25,
                defMagic         = 60,
                hpRegen          = 5f,
                mpRegen          = 10f,
                healingPower     = 1.5f,
                speed            = 4.5f,
                // Tăng trưởng: chú trọng MP, HP Regen, MP Regen – hỗ trợ bền bỉ
                hpPerLevel       = 25,
                mpPerLevel       = 30,
                atkPhysPerLevel  = 0,
                atkMagPerLevel   = 3,
                defPhysPerLevel  = 1,
                defMagPerLevel   = 3,
                hpRegenPerLevel  = 0.3f,
                mpRegenPerLevel  = 0.5f,
            },

            // ── Sát Thủ ─────────────────────────────────────────────────
            new JobBaseStats
            {
                jobClass         = JobClass.Assassin,
                displayName      = "Assassin",
                description      = "Tấn công bùng nổ một đòn chí tử. ATK Vật Lý rất cao nhưng HP và DEF thấp.",
                maxHp            = 700,
                maxMp            = 250,
                atkPhysical      = 110,
                atkMagic         = 20,
                defPhysical      = 25,
                defMagic         = 15,
                hpRegen          = 1f,
                mpRegen          = 2f,
                healingPower     = 0f,
                speed            = 8f,
                // Tăng trưởng: ATK Vật Lý tăng mạnh nhất, HP tăng ít
                hpPerLevel       = 20,
                mpPerLevel       = 5,
                atkPhysPerLevel  = 8,
                atkMagPerLevel   = 0,
                defPhysPerLevel  = 0,
                defMagPerLevel   = 0,
                hpRegenPerLevel  = 0f,
                mpRegenPerLevel  = 0f,
            },

            // ── Đỡ Đòn (Tank) ───────────────────────────────────────────────
            new JobBaseStats
            {
                jobClass         = JobClass.Tank,
                displayName      = "Tank",
                description      = "Tanker chịu đòn thay đồng đội. HP và DEF cao nhất, ATK thấp.",
                maxHp            = 1500,
                maxMp            = 150,
                atkPhysical      = 50,
                atkMagic         = 10,
                defPhysical      = 100,
                defMagic         = 80,
                hpRegen          = 8f,
                mpRegen          = 1f,
                healingPower     = 0f,
                speed            = 3.5f,
                // Tăng trưởng: HP và DEF tăng mạnh – trụ cột trên chiến trường
                hpPerLevel       = 80,
                mpPerLevel       = 3,
                atkPhysPerLevel  = 2,
                atkMagPerLevel   = 0,
                defPhysPerLevel  = 5,
                defMagPerLevel   = 4,
                hpRegenPerLevel  = 0.4f,
                mpRegenPerLevel  = 0f,
            },
        };


        Debug.Log("[JobClassDatabase] Đã khởi tạo chỉ số mặc định cho 6 chức nghiệp.");
    }
}

/// <summary>
/// Struct lưu chỉ số cho một chức nghiệp trong Database (dùng thay ScriptableObject riêng lẻ).
/// </summary>
[System.Serializable]
public struct JobBaseStats
{
    public JobClass jobClass;
    public string   displayName;
    [TextArea(2, 3)]
    public string   description;

    [Header("HP / MP")]
    public int maxHp;
    public int maxMp;

    [Header("ATK")]
    public int atkPhysical;   // ATK Vật Lý
    public int atkMagic;      // ATK Phép

    [Header("DEF")]
    public int defPhysical;   // DEF Vật Lý
    public int defMagic;      // DEF Phép

    [Header("Regen / giây")]
    public float hpRegen;     // HP hồi mỗi giây
    public float mpRegen;     // MP hồi mỗi giây

    [Header("Healing")]
    public float healingPower; // Hệ số nhân hiệu quả hồi máu (riêng Trị Liệu Sư > 0)

    [Header("Speed")]
    public float speed;        // Tốc độ di chuyển

    [Header("Combat")]
    public float attackRange;  // Tầm đánh (units)
    public float attackSpeed;  // Giây/đòn (thấp hơn = nhanh hơn)
    public float lifeSteal;    // Hút máu (0–1)

    // ─── Tăng trưởng mỗi level ──────────────────────────────────────────────
    [Header("Tăng trưởng / level")]
    public int   hpPerLevel;              // HP Max tăng/level
    public int   mpPerLevel;              // MP Max tăng/level
    public int   atkPhysPerLevel;         // ATK Vật Lý tăng/level
    public int   atkMagPerLevel;          // ATK Phép tăng/level
    public int   defPhysPerLevel;         // DEF Vật Lý tăng/level
    public int   defMagPerLevel;          // DEF Phép tăng/level
    public float hpRegenPerLevel;         // HP Regen tăng/level
    public float mpRegenPerLevel;         // MP Regen tăng/level
    public float attackSpeedPerLevel;     // Giảm thời gian đòn đánh/level (âm = nhanh hơn)
    public float lifeStealPerLevel;       // Hút máu tăng/level

    /// <summary>Giá trị mặc định khi không tìm thấy chức nghiệp.</summary>
    public static JobBaseStats Default => new JobBaseStats
    {
        jobClass             = JobClass.Warrior,
        displayName          = "Warrior",
        maxHp                = 1000,
        maxMp                = 200,
        atkPhysical          = 80,
        atkMagic             = 10,
        defPhysical          = 50,
        defMagic             = 20,
        speed                = 5f,
        hpRegen              = 4f,
        mpRegen              = 2f,
        attackRange          = 1.5f,
        attackSpeed          = 0.5f,
        lifeSteal            = 0f,
        hpPerLevel           = 40,
        mpPerLevel           = 5,
        atkPhysPerLevel      = 4,
        atkMagPerLevel       = 0,
        defPhysPerLevel      = 2,
        defMagPerLevel       = 1,
        hpRegenPerLevel      = 0.1f,
        mpRegenPerLevel      = 0f,
        attackSpeedPerLevel  = -0.005f,
        lifeStealPerLevel    = 0f,
    };
}

