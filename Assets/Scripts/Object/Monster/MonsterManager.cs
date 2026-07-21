using System.Collections.Generic;
using TMPro;
using UnityEngine;

// ─── MonsterView ──────────────────────────────────────────────────────────────
// Attached to each monster GameObject. Owns the name tag, HP bar, and a
// client-side state machine that reflects the server-authoritative state.
//
// SRP: purely visual — no network logic. Data is pushed by MonsterManager.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Client-side monster state (mirrors server constants).</summary>
public enum MonsterClientState : byte
{
    Idle   = 0,
    Wander = 1,
    Chase  = 2,
    Attack = 3,
    Dead   = 5,
}

/// <summary>
/// MonsterView — visual representation of one server-simulated monster.
/// Receives snapshots from MonsterManager and applies position / HP / state.
/// Uses a simple state machine to change the visual appearance per state.
/// </summary>
public class MonsterView : MonoBehaviour
{
    // ── Identity ─────────────────────────────────────────────────────────────
    private const string DisplayName = "Slime";
    private const int    Level       = 1;
    private const float  ClientMaxHP = 200f;

    // ── UI Y offsets above sprite centre ────────────────────────────────────
    private const float NameYOffset  = 1.3f;
    private const float HPBarYOffset = 0.95f;

    // ── Movement interpolation ───────────────────────────────────────────────
    private const float LerpSpeed = 12f; // world-units / s — how fast we snap to server pos

    // ── State colours ────────────────────────────────────────────────────────
    private static readonly Color ColorIdle   = new Color(0.90f, 0.20f, 0.20f); // red
    private static readonly Color ColorWander = new Color(0.85f, 0.30f, 0.10f); // orange-red
    private static readonly Color ColorChase  = new Color(1.00f, 0.55f, 0.00f); // orange (alert)
    private static readonly Color ColorAttack = new Color(1.00f, 0.10f, 0.10f); // bright red
    private static readonly Color ColorDead   = new Color(0.30f, 0.30f, 0.30f); // grey

    // ── References ───────────────────────────────────────────────────────────
    private SpriteRenderer _sr;
    private HealthBarUI    _hpBarUI;
    private TextMeshPro    _nameText;

    // ── Runtime state ────────────────────────────────────────────────────────
    private MonsterClientState _state    = MonsterClientState.Idle;
    private Vector3            _targetPos;   // server-authoritative position we lerp toward

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _sr        = GetComponent<SpriteRenderer>();
        _targetPos = transform.position;
        BuildNameTag();
        BuildHPBar();
        ApplyState(MonsterClientState.Idle);
    }

    private void Update()
    {
        // Smooth interpolation toward server position (all states except Dead)
        if (_state != MonsterClientState.Dead)
            transform.position = Vector3.Lerp(transform.position, _targetPos, LerpSpeed * Time.deltaTime);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by MonsterManager every WorldState tick.
    /// Applies the server-authoritative position, HP, and state.
    /// </summary>
    public void UpdateFromSnapshot(MonsterSnapshot snap)
    {
        _targetPos = new Vector3(snap.X, snap.Y, 0f);

        var newState = (MonsterClientState)snap.State;
        if (newState != _state)
            ApplyState(newState);

        // HP bar — driven by server HP value from WorldState
        _hpBarUI?.SetValues(snap.HP, ClientMaxHP);

        // Dead: snap position immediately (no lerp for corpse)
        if (_state == MonsterClientState.Dead)
            transform.position = _targetPos;
    }

    // ── State machine ─────────────────────────────────────────────────────────

    /// <summary>Transitions to a new state and updates visuals.</summary>
    private void ApplyState(MonsterClientState state)
    {
        _state = state;

        switch (state)
        {
            case MonsterClientState.Idle:
                SetSpriteColor(ColorIdle);
                ShowUI(true);
                break;

            case MonsterClientState.Wander:
                SetSpriteColor(ColorWander);
                ShowUI(true);
                break;

            case MonsterClientState.Chase:
                SetSpriteColor(ColorChase);
                ShowUI(true);
                // Scale up slightly to signal aggro
                transform.localScale = new Vector3(1.7f, 1.7f, 1f);
                break;

            case MonsterClientState.Attack:
                SetSpriteColor(ColorAttack);
                ShowUI(true);
                break;

            case MonsterClientState.Dead:
                SetSpriteColor(ColorDead);
                ShowUI(false);
                transform.localScale = new Vector3(1.5f, 1.5f, 1f); // reset scale
                break;
        }

        // Reset scale when leaving chase state
        if (state != MonsterClientState.Chase && state != MonsterClientState.Dead)
            transform.localScale = new Vector3(1.5f, 1.5f, 1f);
    }

    private void SetSpriteColor(Color c)
    {
        if (_sr != null) _sr.color = c;
    }

    private void ShowUI(bool show)
    {
        if (_nameText != null)  _nameText.gameObject.SetActive(show);
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildNameTag()
    {
        var nameGO = new GameObject("NameTag");
        nameGO.transform.SetParent(transform, false);
        nameGO.transform.localPosition = new Vector3(0f, NameYOffset, 0f);

        _nameText = nameGO.AddComponent<TextMeshPro>();
        _nameText.text         = $"{DisplayName}  Lv.{Level}";
        _nameText.fontSize     = 5f;
        _nameText.alignment    = TextAlignmentOptions.Center;
        _nameText.color        = new Color(1f, 0.72f, 0.72f);
        _nameText.outlineWidth = 0.2f;
        _nameText.outlineColor = Color.black;
        _nameText.fontStyle    = FontStyles.Bold;

        var font = Resources.Load<TMP_FontAsset>("Font/antiquity-print SDF");
        if (font != null) _nameText.font = font;

        var meshRend = nameGO.GetComponent<MeshRenderer>();
        if (_sr != null && meshRend != null)
        {
            meshRend.sortingLayerID = _sr.sortingLayerID;
            meshRend.sortingOrder   = _sr.sortingOrder + 10;
        }

        nameGO.AddComponent<NameTagController>();
    }

    private void BuildHPBar()
    {
        var prefab = Resources.Load<GameObject>("Prefab/HPBarCanvas");
        if (prefab == null)
        {
            Debug.LogWarning("[MonsterView] HPBarCanvas prefab not found.");
            return;
        }

        var barGO = Instantiate(prefab, transform);
        barGO.name = "HPBar_Monster";
        barGO.transform.localPosition = new Vector3(0f, HPBarYOffset, 0f);

        _hpBarUI = barGO.GetComponent<HealthBarUI>();
        if (_hpBarUI != null)
            _hpBarUI.SetValues(ClientMaxHP, ClientMaxHP); // full → hidden by hideWhenFull

        barGO.AddComponent<NameTagController>();
        barGO.AddComponent<LockRotation>();
    }
}

// ─── MonsterManager ───────────────────────────────────────────────────────────

/// <summary>
/// MonsterManager — receives MonsterSnapshot[] from each WorldState UDP packet
/// and creates / updates / hides MonsterView GameObjects.
///
/// All monster data (position, HP, state) is server-authoritative.
/// MonsterView handles visual interpolation and state-machine colours.
///
/// Singleton: persists across scene loads on the Bootstrap GameObject.
/// </summary>
public class MonsterManager : MonoBehaviour
{
    public static MonsterManager Instance { get; private set; }

    // Procedural 1×1 white square used as the monster body sprite
    private Sprite _squareSprite;

    // Active views keyed by server monster ID
    private readonly Dictionary<uint, MonsterView> _views      = new();
    private readonly HashSet<uint>                  _seenThisTick = new();

    // Deferred subscription — NetworkManager may not exist yet at Awake
    private bool _subscribed;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Build 1×1 white sprite at runtime
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        _squareSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    private void OnEnable()  { TrySubscribe(); }
    private void OnDisable() { Unsubscribe(); }

    private void Update()
    {
        if (!_subscribed) TrySubscribe();
    }

    // ── Subscription ──────────────────────────────────────────────────────────

    private void TrySubscribe()
    {
        if (_subscribed || NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnWorldState += OnWorldState;
        _subscribed = true;
        Debug.Log("[MonsterManager] Subscribed to OnWorldState.");
    }

    private void Unsubscribe()
    {
        if (!_subscribed || NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnWorldState -= OnWorldState;
        _subscribed = false;
    }

    // ── WorldState handler ────────────────────────────────────────────────────

    private void OnWorldState(WorldStatePacket ws)
    {
        _seenThisTick.Clear();

        if (ws.Monsters != null)
        {
            foreach (var snap in ws.Monsters)
            {
                _seenThisTick.Add(snap.ID);

                if (!_views.TryGetValue(snap.ID, out var view) || view == null)
                    view = SpawnMonster(snap.ID);

                // Show the GO and apply snapshot (position, HP, state)
                view.gameObject.SetActive(true);
                view.UpdateFromSnapshot(snap);
            }
        }

        // Hide any monster not present in this WorldState tick
        foreach (var kv in _views)
        {
            if (!_seenThisTick.Contains(kv.Key) && kv.Value != null)
                kv.Value.gameObject.SetActive(false);
        }
    }

    // ── Monster creation ──────────────────────────────────────────────────────

    /// <summary>Creates a new monster GameObject with a MonsterView component.</summary>
    private MonsterView SpawnMonster(uint id)
    {
        var go = new GameObject($"Monster_{id}");

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = _squareSprite;
        sr.color        = Color.red;
        sr.sortingOrder = 5;

        // Initial scale: 1.5 world-unit square body
        go.transform.localScale = new Vector3(1.5f, 1.5f, 1f);

        var view = go.AddComponent<MonsterView>();
        _views[id] = view;

        Debug.Log($"[MonsterManager] Spawned monster {id}");
        return view;
    }

    /// <summary>Destroys all monster views — call on scene unload if needed.</summary>
    public void ClearAll()
    {
        foreach (var v in _views.Values)
            if (v != null) Destroy(v.gameObject);
        _views.Clear();
    }
}
