using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// DamagePopupManager — Singleton that spawns floating damage numbers whenever
/// a DamageEvent packet is received from the server.
///
/// Creates popup GameObjects entirely in code — no prefab required.
///
/// Position lookup:
///   • Monster IDs (>= 10000) → MonsterManager.Instance.TryGetPosition()
///   • Local player           → FindAnyObjectByType&lt;LocalPlayer&gt;()
///   • Remote player          → FindObjectsByType&lt;RemotePlayer&gt;() by PlayerID
///
/// SRP: only manages popup spawning and position resolution.
/// </summary>
public class DamagePopupManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static DamagePopupManager Instance { get; private set; }

    // ── Tuning ────────────────────────────────────────────────────────────────

    [Header("Spawn")]
    [SerializeField] private float spreadX     = 0.4f;  // random horizontal spread
    [SerializeField] private float spawnOffsetY = 1.0f; // offset above target

    private const uint MonsterIDBase = 10000;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; TrySubscribe(); }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; TryUnsubscribe(); }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnSceneLoaded(Scene s, LoadSceneMode m) => TrySubscribe();

    // ── Subscription ──────────────────────────────────────────────────────────

    private bool _subscribed;

    private void TrySubscribe()
    {
        if (_subscribed || NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnDamageEvent += HandleDamageEvent;
        NetworkManager.Instance.OnExpGain     += HandleExpGain;
        _subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!_subscribed || NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnDamageEvent -= HandleDamageEvent;
        NetworkManager.Instance.OnExpGain     -= HandleExpGain;
        _subscribed = false;
    }

    // ── Handler ───────────────────────────────────────────────────────────────

    private void HandleDamageEvent(DamageEventPacket pkt)
    {
        if (pkt.Damage == 0) return;
        if (!TryGetWorldPosition(pkt.TargetID, out Vector3 pos)) return;
        SpawnPopup(pos, pkt.Damage);
    }

    private void HandleExpGain(PacketDecoder.ExpGainPacket pkt)
    {
        // Show green "+EXP" only for the local player (packet is sent exclusively to the killer)
        var lp = FindAnyObjectByType<LocalPlayer>();
        Vector3 pos = lp != null ? lp.transform.position : Vector3.zero;
        SpawnExpPopup(pos, (int)pkt.ExpGained);
    }

    // ── Position resolution ───────────────────────────────────────────────────

    private bool TryGetWorldPosition(uint id, out Vector3 pos)
    {
        // Monster
        if (id >= MonsterIDBase)
        {
            if (MonsterManager.Instance != null &&
                MonsterManager.Instance.TryGetPosition(id, out pos))
                return true;
            pos = Vector3.zero;
            return false;
        }

        // Local player
        if (GameSession.Instance != null && id == GameSession.Instance.PlayerID)
        {
            var lp = FindAnyObjectByType<LocalPlayer>();
            if (lp != null) { pos = lp.transform.position; return true; }
        }

        // Remote player
        foreach (var rp in FindObjectsByType<RemotePlayer>(FindObjectsSortMode.None))
            if (rp.PlayerID == id) { pos = rp.transform.position; return true; }

        pos = Vector3.zero;
        return false;
    }

    // ── Spawn — world space TextMeshPro ──────────────────────────────────────

    private void SpawnPopup(Vector3 worldPos, uint damage)
    {
        float x = worldPos.x + Random.Range(-spreadX, spreadX);
        float y = worldPos.y + spawnOffsetY;

        var go = new GameObject("DmgPopup");
        go.transform.position = new Vector3(x, y, 0f);

        // Scale proportional to camera so text is always readable
        // orthographicSize=5 → scale=1, orthographicSize=10 → scale=2, etc.
        var cam = Camera.main;
        float camScale = cam != null ? cam.orthographicSize / 50f : 1f;
        go.transform.localScale = Vector3.one * camScale;

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200f, 100f); // local units (scaled by camScale)

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text         = $"-{damage}";
        tmp.fontSize     = 36f;
        tmp.fontStyle    = FontStyles.Bold;
        tmp.color        = new Color(1f, 0.18f, 0.18f, 1f);
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.sortingOrder = 100;

        var font = Resources.Load<TMP_FontAsset>("Font/antiquity-print SDF");
        if (font != null) tmp.font = font;

        go.AddComponent<DamageTextPopup>();
    }

    /// <summary>Spawns a green "+N EXP" text above the local player's position.</summary>
    private void SpawnExpPopup(Vector3 worldPos, int expGained)
    {
        float x = worldPos.x + Random.Range(-spreadX * 0.5f, spreadX * 0.5f);
        float y = worldPos.y + spawnOffsetY + 0.8f; // slightly above damage numbers

        var go = new GameObject("ExpPopup");
        go.transform.position = new Vector3(x, y, 0f);

        var cam = Camera.main;
        float camScale = cam != null ? cam.orthographicSize / 50f : 1f;
        go.transform.localScale = Vector3.one * camScale;

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(300f, 100f);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text         = $"+{expGained} EXP";
        tmp.fontSize     = 36f;
        tmp.fontStyle    = FontStyles.Bold;
        tmp.color        = new Color(0.2f, 1f, 0.3f, 1f); // bright green
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.sortingOrder = 100;

        var font = Resources.Load<TMP_FontAsset>("Font/antiquity-print SDF");
        if (font != null) tmp.font = font;

        go.AddComponent<DamageTextPopup>();
    }
}



