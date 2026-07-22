using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// PlayerStatusUI — HUD showing HP, MP, EXP bars; Level, Name, and Avatar.
///
/// Designed for Stats.prefab whose hierarchy is:
///   Stats (root)       ← always active, this script lives here
///     └─ Canvas        ← child[0], toggled for visibility
///           └─ Panel   ← container
///                 ├─ Avatar  (Image)         → avatar portrait
///                 ├─ Name    (TMP_Text)      → character name     [NEW — add to prefab]
///                 ├─ Level   (TMP_Text)      → "Lv. 5"            [NEW — add to prefab]
///                 ├─ HP      (TMP_Text)      → "HP" label
///                 ├─ MP      (TMP_Text)      → "MP" label
///                 ├─ red     (Image bg)
///                 │     └─ green (Image fill, Horizontal) → HP fill
///                 ├─ grey    (Image bg)
///                 │     └─ blue  (Image fill, Horizontal) → MP fill
///                 └─ expBg   (Image bg)      [NEW — add to prefab]
///                       └─ expFill (Image fill, Horizontal) → EXP fill
///
/// Children are auto-resolved by name in Awake(); Inspector fields override auto-find.
///
/// SRP: only manages HUD visibility and stat display.
/// </summary>
public class PlayerStatusUI : MonoBehaviour
{
    // ── Inspector overrides (optional — leave null to use auto-find) ──────────

    [Header("Auto-wired from Stats prefab (leave null to auto-find)")]
    [SerializeField] private Image    avatarImage;
    [Tooltip("Shows: 'peacee1 level 15'")]
    [SerializeField] private TMP_Text nameText;

    [Header("HP")]
    [SerializeField] private Image    hpFill;   // child "red/green"
    [SerializeField] private TMP_Text hpText;   // optional "800 / 800"

    [Header("MP")]
    [SerializeField] private Image    mpFill;   // child "grey/blue"
    [SerializeField] private TMP_Text mpText;   // optional "300 / 300"

    [Header("EXP")]
    [SerializeField] private Image    expFill;  // child "expBg/expFill"
    [SerializeField] private TMP_Text expText;  // optional "1200 / 2000"

    // ── Singleton ─────────────────────────────────────────────────────────────

    public static PlayerStatusUI Instance { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private GameObject _canvas;       // child[0] — Stats → Canvas
    private Transform  _panel;        // Canvas → Panel
    private BaseObject _localBase;    // LocalPlayer's BaseObject
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

        // Cache canvas (child[0]) and panel (child[0] of canvas)
        if (transform.childCount > 0)
        {
            _canvas = transform.GetChild(0).gameObject;           // Canvas
            if (_canvas.transform.childCount > 0)
                _panel = _canvas.transform.GetChild(0);           // Panel
        }
        else
        {
            Debug.LogWarning("[PlayerStatusUI] Root has no children — expected Canvas as child[0].");
        }

        AutoWireReferences();
        SetCanvasActive(false); // hidden until entering a Map scene
    }

    private void OnEnable()  => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (!_isMapScene) return;

        if (_localBase == null) TryFindLocalBase();
        if (_localBase == null) return;

        RefreshBars();
    }

    // ── Scene handling ────────────────────────────────────────────────────────

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        bool isMap = scene.name.StartsWith("Map");
        _isMapScene = isMap;
        _localBase  = null;

        SetCanvasActive(isMap);

        if (isMap)
            ApplyCharacterInfo();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Force-refresh all bars immediately (call after level up, respawn, etc.).</summary>
    public void Refresh()
    {
        ApplyCharacterInfo();
        RefreshBars();
    }

    // ── Auto-wiring ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves UI references from the Stats prefab hierarchy by child name.
    /// Inspector-assigned fields take priority; auto-find fills nulls only.
    /// </summary>
    private void AutoWireReferences()
    {
        if (_panel == null) return;

        // Avatar
        if (avatarImage == null)
            avatarImage = FindImageInPanel("Avatar");

        // Name  (TMP_Text child named "Name")
        if (nameText == null)
            nameText = FindTMPInPanel("Name");

        // HP fill:  Panel → red → green
        if (hpFill == null)
            hpFill = FindChildImage("red", "green");

        // HP text label:  Panel → HP (TMP_Text)
        if (hpText == null)
            hpText = FindTMPInPanel("HP");

        // MP fill:  Panel → grey → blue
        if (mpFill == null)
            mpFill = FindChildImage("grey", "blue");

        // MP text label:  Panel → MP (TMP_Text)
        if (mpText == null)
            mpText = FindTMPInPanel("MP");

        // EXP fill:  Panel → expBg → expFill
        if (expFill == null)
            expFill = FindChildImage("expBg", "expFill");

        // EXP text:  Panel → Exp (TMP_Text)
        if (expText == null)
            expText = FindTMPInPanel("Exp");

        Debug.Log("[PlayerStatusUI] References wired: " +
                  $"avatar={avatarImage != null} name={nameText != null} " +
                  $"hp={hpFill != null} mp={mpFill != null} exp={expFill != null}");
    }

    private Image FindImageInPanel(string childName)
    {
        var t = _panel.Find(childName);
        return t != null ? t.GetComponent<Image>() : null;
    }

    private TMP_Text FindTMPInPanel(string childName)
    {
        var t = _panel.Find(childName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    /// <summary>Finds Panel → parentName → childName → Image.</summary>
    private Image FindChildImage(string parentName, string childName)
    {
        var parent = _panel.Find(parentName);
        if (parent == null) return null;
        var child  = parent.Find(childName);
        return child != null ? child.GetComponent<Image>() : null;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void SetCanvasActive(bool active)
    {
        if (_canvas != null) _canvas.SetActive(active);
    }

    private void TryFindLocalBase()
    {
        var lp = FindAnyObjectByType<LocalPlayer>();
        if (lp != null) _localBase = lp.GetComponent<BaseObject>();
    }

    /// <summary>Applies static character data (name, avatar, level) from GameSession.</summary>
    private void ApplyCharacterInfo()
    {
        var session = GameSession.Instance;
        if (session == null) return;

        // Name + Level combined: "peacee1 level 15"
        if (nameText != null)
            nameText.text = $"{session.Username} level {session.Level}";

        // Avatar — Resources/Avatar/{JobClass}
        if (avatarImage != null)
        {
            string path   = $"Avatar/{session.JobClass}";
            var    sprite = Resources.Load<Sprite>(path);
            if (sprite != null)
                avatarImage.sprite = sprite;
            else
                Debug.LogWarning($"[PlayerStatusUI] Avatar sprite not found at Resources/{path}");
        }

        // Refresh EXP bar immediately with session data
        RefreshExpBar(session.Level, session.CurrentExp);
    }

    /// <summary>Reads HP/MP from BaseObject and EXP/Level from GameSession each frame.</summary>
    private void RefreshBars()
    {
        // HP
        SetFill(hpFill, _localBase.HP,  _localBase.MaxHP);
        SetText(hpText, _localBase.HP,  _localBase.MaxHP);

        // MP
        SetFill(mpFill, _localBase.MP,  _localBase.MaxMP);
        SetText(mpText, _localBase.MP,  _localBase.MaxMP);

        // EXP (reads from GameSession — updated by server events)
        var session = GameSession.Instance;
        if (session != null)
            RefreshExpBar(session.Level, session.CurrentExp);
    }

    private void RefreshExpBar(int level, int currentExp)
    {
        int maxExp = ComputeMaxExp(level);

        SetFill(expFill, currentExp, maxExp);

        if (expText != null)
            expText.text = $"{currentExp} / {maxExp}";

        // Keep name text in sync if level changes mid-session
        var session = GameSession.Instance;
        if (nameText != null && session != null)
            nameText.text = $"{session.Username} level {level}";
    }

    private static void SetFill(Image fill, int current, int max)
    {
        if (fill != null)
            fill.fillAmount = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
    }

    private static void SetText(TMP_Text label, int current, int max)
    {
        if (label != null)
            label.text = $"{Mathf.Max(current, 0)} / {max}";
    }

    /// <summary>
    /// Returns the EXP required to reach the next level.
    /// Formula: level * 1000 (adjust to match server formula).
    /// </summary>
    private static int ComputeMaxExp(int level)
        => Mathf.Max(level, 1) * 1000;
}
