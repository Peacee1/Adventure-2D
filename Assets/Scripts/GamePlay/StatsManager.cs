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

    // ── Inspector overrides (leave null to auto-find by child name) ───────────

    [Header("Auto-wired from prefab hierarchy (leave null to auto-find)")]
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

        AutoWireReferences();
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

        // Core stats
        SetText(maxHPText,       $"HP:            {session.MaxHP}");
        SetText(maxMPText,       $"MP:            {session.MaxMP}");
        SetText(atkPhysicalText, $"ATK Physical:  {session.ATKPhysical}");
        SetText(atkMagicText,    $"ATK Magic:     {session.ATKMagic}");
        SetText(defPhysicalText, $"DEF Physical:  {session.DEFPhysical}");
        SetText(defMagicText,    $"DEF Magic:     {session.DEFMagic}");

        // Special stats
        SetText(critRateText,    $"Crit Rate:     {session.CritRate * 100f:F1}%");
        SetText(lifeStealText,   $"Life Steal:    {session.LifeSteal * 100f:F1}%");
        SetText(attackSpeedText, $"ASPD:          {session.AttackSpeed:F2}s");
    }

    // ── Auto-wiring ───────────────────────────────────────────────────────────

    private void AutoWireReferences()
    {
        if (_panel == null) return;

        titleText       ??= FindTMP("Title");
        skillPointsText ??= FindTMP("SKP");
        expText         ??= FindTMP("Exp");
        maxHPText       ??= FindTMP("MaxHP");
        maxMPText       ??= FindTMP("MaxMP");
        atkPhysicalText ??= FindTMP("ATKPhysical");
        atkMagicText    ??= FindTMP("ATKMagic");
        defPhysicalText ??= FindTMP("DEFPhysical");
        defMagicText    ??= FindTMP("DEFMagic");
        critRateText    ??= FindTMP("CritRate");
        lifeStealText   ??= FindTMP("LifeSteal");
        attackSpeedText ??= FindTMP("ASPD");

        Debug.Log($"[StatsManager] Auto-wire done: title={titleText != null} sp={skillPointsText != null} " +
                  $"hp={maxHPText != null} mp={maxMPText != null} atk={atkPhysicalText != null}");
    }

    private TMP_Text FindTMP(string childName)
    {
        var t = _panel.Find(childName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
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
