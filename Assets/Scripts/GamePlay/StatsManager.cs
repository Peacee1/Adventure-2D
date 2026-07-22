using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// StatsManager — Singleton that manages the StatsManager prefab and displays
/// all player combat stats (HP, MP, ATK, DEF, CritRate, LifeSteal, Level,
/// EXP, SkillPoints).
///
/// Prefab hierarchy expected:
///   StatsManager (root)       ← this script lives here
///     └─ Canvas               ← child[0], toggled for visibility
///           └─ Panel          ← container
///                 ├─ Title          (TMP_Text) → character name + level
///                 ├─ SKP            (TMP_Text) → "Skill Points: 12"
///                 ├─ MaxHP          (TMP_Text) → "HP: 800"
///                 ├─ MaxMP          (TMP_Text) → "MP: 200"
///                 ├─ ATKPhysical    (TMP_Text) → "ATK Phys: 80"
///                 ├─ ATKMagic       (TMP_Text) → "ATK Magic: 10"
///                 ├─ DEFPhysical    (TMP_Text) → "DEF Phys: 30"
///                 ├─ DEFMagic       (TMP_Text) → "DEF Magic: 20"
///                 ├─ CritRate       (TMP_Text) → "Crit: 5.0%"
///                 └─ LifeSteal      (TMP_Text) → "Life Steal: 0.0%"
///
/// Toggle open/close by calling Show()/Hide() or pressing the bound key.
///
/// SRP: only manages the stats display panel.
/// OCP: add new stat rows by extending the prefab and adding a field here.
/// DIP: reads data from GameSession and LocalPlayer (both are abstractions).
/// </summary>
public class StatsManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static StatsManager Instance { get; private set; }

    [Header("UI Text References (Assign in Inspector or leave auto-find)")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text skillPointsText;
    [SerializeField] private TMP_Text maxHPText;
    [SerializeField] private TMP_Text maxMPText;
    [SerializeField] private TMP_Text atkPhysicalText;
    [SerializeField] private TMP_Text atkMagicText;
    [SerializeField] private TMP_Text defPhysicalText;
    [SerializeField] private TMP_Text defMagicText;
    [SerializeField] private TMP_Text critRateText;
    [SerializeField] private TMP_Text lifeStealText;
    [SerializeField] private TMP_Text attackSpeedText;
    [SerializeField] private TMP_Text expText;

    [Header("Stat Upgrade Buttons (Assign in Inspector)")]
    [SerializeField] private Button hpUpgradeButton;
    [SerializeField] private Button mpUpgradeButton;
    [SerializeField] private Button atkPhysicalUpgradeButton;
    [SerializeField] private Button atkMagicUpgradeButton;
    [SerializeField] private Button defUpgradeButton;

    [Header("Optional Stat Level Text References (Assign in Inspector)")]
    [SerializeField] private TMP_Text hpLevelText;
    [SerializeField] private TMP_Text mpLevelText;
    [SerializeField] private TMP_Text atkPhysicalLevelText;
    [SerializeField] private TMP_Text atkMagicLevelText;
    [SerializeField] private TMP_Text defLevelText;

    [Header("Toggle key (default: Tab)")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    // ── Private state ─────────────────────────────────────────────────────────

    private GameObject _canvas;
    private Transform  _panel;
    private bool       _isVisible;
    private bool       _isMapScene;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (transform.childCount > 0)
        {
            _canvas = transform.GetChild(0).gameObject;
            if (_canvas.transform.childCount > 0)
                _panel = _canvas.transform.GetChild(0);
        }
        else
        {
            Debug.LogWarning("[StatsManager] Root has no children — expected Canvas as child[0].");
        }

        BindButtonListeners();
        SetVisible(false);
    }

    private void OnEnable()  => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (!_isMapScene) return;

        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    // ── Scene handling ────────────────────────────────────────────────────────

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _isMapScene = scene.name.StartsWith("Map");
        if (!_isMapScene)
            SetVisible(false);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Show the stats panel and refresh all values. Only works in Map scenes.</summary>
    public void Show()
    {
        if (!_isMapScene) return;
        SetVisible(true);
        Refresh();
    }

    /// <summary>Hide the stats panel.</summary>
    public void Hide() => SetVisible(false);

    /// <summary>Toggle visibility. Only opens in Map scenes.</summary>
    public void Toggle()
    {
        if (_isVisible) Hide();
        else Show(); // Show() already guards _isMapScene
    }

    /// <summary>Force-refresh all displayed stats from GameSession.</summary>
    public void Refresh()
    {
        var session = GameSession.Instance;
        if (session == null) return;

        int maxExp = Mathf.Max(session.Level, 1) * 1000;

        // Title
        SetText(titleText, $"{session.Username}  Lv.{session.Level}");

        // Progression
        SetText(skillPointsText, $"Skill Points:  {session.SkillPoints}");
        SetText(expText,         $"EXP:  {session.CurrentExp} / {maxExp}");

        // Calculate stat levels based on job base stats (starts at Level 1)
        (ushort baseHP, ushort baseMP, ushort baseATKPhy, ushort baseATKMag, ushort baseDEFPhy, _) = GetJobBaseStats(session.JobClass);

        int hpLevel          = 1 + Mathf.Max(0, (session.MaxHP - baseHP) / 100);
        int mpLevel          = 1 + Mathf.Max(0, (session.MaxMP - baseMP) / 100);
        int atkPhysicalLevel = 1 + Mathf.Max(0, (session.ATKPhysical - baseATKPhy) / 10);
        int atkMagicLevel    = 1 + Mathf.Max(0, (session.ATKMagic - baseATKMag) / 10);
        int defLevel         = 1 + Mathf.Max(0, (session.DEFPhysical - baseDEFPhy) / 10);

        // Core stats (includes level tag)
        SetText(maxHPText,       $"HP:            {session.MaxHP} (Lv.{hpLevel})");
        SetText(maxMPText,       $"MP:            {session.MaxMP} (Lv.{mpLevel})");
        SetText(atkPhysicalText, $"ATK Physical:  {session.ATKPhysical} (Lv.{atkPhysicalLevel})");
        SetText(atkMagicText,    $"ATK Magic:     {session.ATKMagic} (Lv.{atkMagicLevel})");
        SetText(defPhysicalText, $"DEF Physical:  {session.DEFPhysical} (Lv.{defLevel})");
        SetText(defMagicText,    $"DEF Magic:     {session.DEFMagic} (Lv.{defLevel})");

        // Dedicated level labels (if assigned in Inspector)
        SetText(hpLevelText,          $"Lv.{hpLevel}");
        SetText(mpLevelText,          $"Lv.{mpLevel}");
        SetText(atkPhysicalLevelText, $"Lv.{atkPhysicalLevel}");
        SetText(atkMagicLevelText,    $"Lv.{atkMagicLevel}");
        SetText(defLevelText,         $"Lv.{defLevel}");

        // Special stats
        SetText(critRateText,    $"Crit Rate:     {session.CritRate * 100f:F1}%");
        SetText(lifeStealText,   $"Life Steal:    {session.LifeSteal * 100f:F1}%");
        SetText(attackSpeedText, $"ASPD:          {session.AttackSpeed:F2}s");
    }

    private static (ushort hp, ushort mp, ushort atkPhy, ushort atkMag, ushort defPhy, ushort defMag) GetJobBaseStats(JobClass job)
    {
        return job switch
        {
            JobClass.Archer   => (800,  300, 80,  10,  30,  20),
            JobClass.Warrior  => (1100, 200, 90,  10,  60,  25),
            JobClass.Mage     => (650,  600, 15,  120, 20,  50),
            JobClass.Healer   => (750,  700, 20,  55,  25,  60),
            JobClass.Assassin => (700,  250, 110, 20,  25,  15),
            JobClass.Tank     => (1500, 150, 50,  10,  100, 80),
            _                 => (800,  200, 80,  10,  30,  20),
        };
    }

    // ── Stat Upgrade API (1 Skill Point per upgrade) ──────────────────────────

    /// <summary>Upgrades MaxHP by +100 (costs 1 Skill Point).</summary>
    public void UpgradeHP()          => RequestUpgrade(0);

    /// <summary>Upgrades MaxMP by +100 (costs 1 Skill Point).</summary>
    public void UpgradeMP()          => RequestUpgrade(1);

    /// <summary>Upgrades ATK Physical by +10 (costs 1 Skill Point).</summary>
    public void UpgradeATKPhysical() => RequestUpgrade(2);

    /// <summary>Upgrades ATK Magic by +10 (costs 1 Skill Point).</summary>
    public void UpgradeATKMagic()    => RequestUpgrade(3);

    /// <summary>Upgrades DEF Physical by +10 AND DEF Magic by +10 (costs 1 Skill Point).</summary>
    public void UpgradeDEF()         => RequestUpgrade(4);

    private void RequestUpgrade(byte statType)
    {
        var session = GameSession.Instance;
        if (session == null || session.SkillPoints < 1)
        {
            Debug.LogWarning("[StatsManager] Cannot upgrade stat: Not enough skill points!");
            return;
        }
        NetworkManager.Instance?.SendSpendSkillPoint(statType);
    }

    // ── Button Listener Binding ───────────────────────────────────────────────

    private void BindButtonListeners()
    {
        BindButton(hpUpgradeButton,          UpgradeHP);
        BindButton(mpUpgradeButton,          UpgradeMP);
        BindButton(atkPhysicalUpgradeButton, UpgradeATKPhysical);
        BindButton(atkMagicUpgradeButton,    UpgradeATKMagic);
        BindButton(defUpgradeButton,         UpgradeDEF);
    }

    private static void BindButton(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn == null) return;
        btn.onClick.RemoveListener(action);
        btn.onClick.AddListener(action);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (_canvas != null) _canvas.SetActive(visible);
    }

    private static void SetText(TMP_Text label, string text)
    {
        if (label != null) label.text = text;
    }
}
