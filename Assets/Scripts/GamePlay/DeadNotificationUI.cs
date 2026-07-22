using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DeadNotificationUI — shown when the local player dies.
///
/// Structure expected on the prefab:
///   [Root GameObject] — always active, holds this script
///     └─ [child 0: Panel] — the actual UI panel, toggled active/inactive
///           └─ Button (respawn button)
///           └─ (optional) WaitingIndicator
///
/// The root stays active so Awake() always runs and Instance is set.
/// Show() / Hide() only toggle child[0].
///
/// SRP: manages only death panel visibility and the respawn request.
/// </summary>
public class DeadNotificationUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("UI References")]
    [Tooltip("Resurrect button inside the panel.")]
    [SerializeField] private Button respawnButton;

    [Tooltip("Optional 'waiting' label shown after the request is sent.")]
    [SerializeField] private GameObject waitingIndicator;

    // ── Singleton ─────────────────────────────────────────────────────────────

    public static DeadNotificationUI Instance { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private GameObject _panel;  // child[0] — the visual panel
    private bool       _requestSent;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Singleton guard
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Cache child[0] as the panel
        if (transform.childCount > 0)
            _panel = transform.GetChild(0).gameObject;
        else
            Debug.LogWarning("[DeadNotificationUI] No child found — add a Panel as child[0].");

        // Wire button
        if (respawnButton != null)
            respawnButton.onClick.AddListener(OnRespawnClicked);

        // Start hidden
        SetPanelActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Shows the death panel. Called by GameSceneBootstrap on DieEvent.</summary>
    public void Show()
    {
        _requestSent = false;
        HideWaiting();
        if (respawnButton != null) respawnButton.interactable = true;
        SetPanelActive(true);
    }

    /// <summary>Hides the panel. Called by GameSceneBootstrap on RespawnAck.</summary>
    public void Hide()
    {
        _requestSent = false;
        HideWaiting();
        SetPanelActive(false);
    }

    // ── Button handler ────────────────────────────────────────────────────────

    private void OnRespawnClicked()
    {
        if (_requestSent) return;
        if (NetworkManager.Instance == null) return;

        uint pid = GameSession.Instance != null ? GameSession.Instance.PlayerID : 0;
        if (pid == 0)
        {
            Debug.LogWarning("[DeadNotificationUI] Cannot respawn — PlayerID is 0.");
            return;
        }

        _requestSent = true;
        if (respawnButton != null) respawnButton.interactable = false;
        ShowWaiting();

        NetworkManager.Instance.SendRespawn(pid);
        Debug.Log($"[DeadNotificationUI] Sent RespawnReq for player {pid}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetPanelActive(bool active)
    {
        if (_panel != null)
            _panel.SetActive(active);
    }

    private void ShowWaiting()
    {
        if (waitingIndicator != null) waitingIndicator.SetActive(true);
    }

    private void HideWaiting()
    {
        if (waitingIndicator != null) waitingIndicator.SetActive(false);
    }
}
