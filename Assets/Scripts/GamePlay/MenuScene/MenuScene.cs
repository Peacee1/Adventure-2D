using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

/// <summary>
/// MenuScene — handles login, registration, and character slot selection UI.
/// After login + join room succeeds → saves data to GameSession → loads the game scene.
/// </summary>
public class MenuScene : MonoBehaviour
{
    [Header("UI Fields")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_Dropdown   slotDropdown; // Dropdown for Slot 1, 2, 3

    [Header("Buttons")]
    [SerializeField] private Button loginBtn;
    [SerializeField] private Button registerBtn;
    [SerializeField] private Button playBtn;

    [Header("Feedback")]
    [SerializeField] private TMP_Text statusText;

    [Header("Transition")]
    [SerializeField] private CameraFade eyeBlink; // CameraFade component — attach fullscreen black Image

    [Header("UI Panels")]
    [SerializeField] private GameObject           loginPanel;            // Login / register panel
    [SerializeField] private GameObject           characterPickingPanel; // Character selection panel — fades in after video3
    [SerializeField] private CharacterPickingManager characterPickingManager; // Script managing the 3 slot UIs

    [Header("Video Background")]
    [SerializeField] private VideoPlayer backgroundVideo; // Drag 'background' GameObject here
    [SerializeField] private VideoClip   videoClip2;     // Assets/Resources/Video/videobackground2.mp4
    [SerializeField] private VideoClip   videoClip3;     // Assets/Resources/Video/videobackground3.mp4

    // Temporarily stored credentials for auto-login after registration
    private string _pendingUsername;
    private string _pendingPassword;
    private byte   _pendingSlot;

    // Target scene name to load after the sequence completes
    private string _pendingSceneName;

    // Credentials saved after login, used for slot re-login when a character is selected
    private string _loggedInUsername;
    private string _loggedInPassword;

    // Character list received from the server
    private CharacterData[] _characterList;

    private void Awake()
    {
        if (playBtn     != null) playBtn.onClick.AddListener(Play);
        if (loginBtn    != null) loginBtn.onClick.AddListener(Login);
        if (registerBtn != null) registerBtn.onClick.AddListener(Register);

        // Wire up character slot confirmation (Start button) to LoadGameWithSlot
        if (characterPickingManager != null)
        {
            characterPickingManager.OnCharacterSlotConfirmed += LoadGameWithSlot;
            characterPickingManager.OnBackRequested          += HandleBack;
        }

        // Hide the character picking panel immediately on startup
        if (characterPickingPanel != null)
        {
            var cg = characterPickingPanel.GetComponent<CanvasGroup>();
            if (cg == null) cg = characterPickingPanel.AddComponent<CanvasGroup>();
            cg.alpha          = 0f;
            cg.interactable   = false;
            cg.blocksRaycasts = false;
        }
    }

    private void Start()
    {
        EnsureGameSession();

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnLoginSuccess          += HandleLoginSuccess;
            NetworkManager.Instance.OnLoginFailed           += HandleLoginFailed;
            NetworkManager.Instance.OnRegisterResult        += HandleRegisterResult;
            NetworkManager.Instance.OnJoinRoomSuccess       += HandleJoinRoomSuccess;
            NetworkManager.Instance.OnCharacterListReceived += HandleCharacterListReceived;
        }
        else
        {
            Debug.LogError("[MenuScene] NetworkManager.Instance is null! Make sure NetworkManager exists in the scene.");
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnLoginSuccess          -= HandleLoginSuccess;
            NetworkManager.Instance.OnLoginFailed           -= HandleLoginFailed;
            NetworkManager.Instance.OnRegisterResult        -= HandleRegisterResult;
            NetworkManager.Instance.OnJoinRoomSuccess       -= HandleJoinRoomSuccess;
            NetworkManager.Instance.OnCharacterListReceived -= HandleCharacterListReceived;
        }
    }

    // ─── Button Handlers ──────────────────────────────────────────────────────

    private void Play()
    {
        string username = usernameInput?.text ?? "";
        string password = passwordInput?.text ?? "";
        byte   slot     = (byte)(slotDropdown?.value ?? 0);

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowStatus("Please enter both Username and Password.", isError: true);
            return;
        }

        _loggedInUsername = username;
        _loggedInPassword = password;

        ShowStatus($"Connecting... (slot {slot + 1})");
        Debug.Log($"[Menu] Play: user={username} slot={slot}");
        NetworkManager.Instance.Connect(username, password, slot);
    }

    private void Login()
    {
        string username = usernameInput?.text ?? "";
        string password = passwordInput?.text ?? "";
        byte   slot     = (byte)(slotDropdown?.value ?? 0);

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowStatus("Please enter both Username and Password.", isError: true);
            return;
        }

        // Save for slot re-login after character selection
        _loggedInUsername = username;
        _loggedInPassword = password;

        ShowStatus("Logging in...");
        Debug.Log($"[Menu] Login: user={username} slot={slot}");
        NetworkManager.Instance.Connect(username, password, slot);
    }

    private void Register()
    {
        string username = usernameInput?.text ?? "";
        string password = passwordInput?.text ?? "";

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowStatus("Please enter both Username and Password.", isError: true);
            return;
        }

        // Save to auto-login after registration succeeds
        _pendingUsername = username;
        _pendingPassword = password;
        _pendingSlot     = (byte)(slotDropdown?.value ?? 0);

        ShowStatus("Registering...");
        Debug.Log($"[Menu] Register: user={username}");
        NetworkManager.Instance.RegisterAccount(username, password);
    }

    // ─── Network Event Handlers ───────────────────────────────────────────────

    private void HandleLoginSuccess(uint playerID)
    {
        Debug.Log($"[Menu] Login OK! PlayerID={playerID}. Requesting character list...");
        ShowStatus("Login successful! Loading characters...");
        // Do not JoinRoom yet — wait for GetCharListAck first
        NetworkManager.Instance.RequestCharacterList();
    }

    private void HandleLoginFailed(string msg)
    {
        Debug.LogWarning($"[Menu] Login failed: {msg}");
        ShowStatus($"Login failed: {msg}", isError: true);
    }

    /// <summary>
    /// Receives the character list from the server — starts the video + panel transition sequence.
    /// </summary>
    private void HandleCharacterListReceived(CharacterData[] characterList)
    {
        Debug.Log($"[Menu] Character list received: {characterList.Length} slots");
        _characterList = characterList;
        ShowStatus("Loading...");
        StartCoroutine(BlinkAndLoadSequence());
    }

    /// <summary>
    /// Called when the player clicks Back on the CharacterPickingPanel.
    /// Disconnects from the server, clears credentials, and returns to the login panel.
    /// </summary>
    private void HandleBack()
    {
        Debug.Log("[Menu] Back — disconnecting and returning to login panel.");

        // Disconnect from server (logout)
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.Disconnect();

        // Clear stored login credentials
        _loggedInUsername = null;
        _loggedInPassword = null;
        _characterList    = null;

        // Swap panels: hide CharacterPickingPanel, show LoginPanel
        StartCoroutine(SwapToLoginPanel());
    }

    private void HandleRegisterResult(bool success, string msg)
    {
        if (success)
        {
            Debug.Log($"[Menu] Register OK: {msg}. Auto-logging in...");
            ShowStatus("Registration successful! Logging in...");

            // Auto-login with the newly registered account
            if (!string.IsNullOrEmpty(_pendingUsername) && !string.IsNullOrEmpty(_pendingPassword))
            {
                NetworkManager.Instance.Connect(_pendingUsername, _pendingPassword, _pendingSlot);
                _pendingUsername = null;
                _pendingPassword = null;
            }
        }
        else
        {
            ShowStatus($"Registration failed: {msg}", isError: true);
            Debug.LogWarning($"[Menu] Register failed: {msg}");
        }
    }

    private void HandleJoinRoomSuccess(string roomID, List<PlayerInfo> existingPlayers)
    {
        string targetScene = GameSession.Instance != null && !string.IsNullOrEmpty(GameSession.Instance.MapName)
            ? GameSession.Instance.MapName
            : "Map1";

        Debug.Log($"[Menu] Joined room {roomID} (existing={existingPlayers.Count}). Loading scene '{targetScene}'...");
        ShowStatus("Joined room! Loading...");

        if (GameSession.Instance != null)
            GameSession.Instance.SetRoom(roomID, existingPlayers);

        // Load the scene directly — BlinkAndLoadSequence already ran from HandleCharacterListReceived
        SceneManager.LoadScene(targetScene);
    }

    /// <summary>
    /// Sequence after character list received:
    /// Blink1 close → swap videobackground2 → Blink1 open → play video2 to end (or skip via double-tap)
    /// → Blink2 close → swap videobackground3 → Blink2 open → video3 loops (waiting for character selection).
    /// </summary>
    private IEnumerator BlinkAndLoadSequence()
    {
        // ── Step 1: Close eyes + fade out login panel (run in parallel) ──────────
        if (loginPanel != null)
            StartCoroutine(FadeOutPanel(loginPanel, eyeBlink != null ? 0.4f : 0.3f));

        if (eyeBlink != null)
            yield return eyeBlink.CloseEyes();

        // ── Step 2: Swap + prepare video2 while eyes are still closed ────────────
        if (backgroundVideo != null && videoClip2 != null)
        {
            backgroundVideo.Stop();
            backgroundVideo.clip      = videoClip2;
            backgroundVideo.isLooping = false;
            backgroundVideo.Prepare();
            yield return new WaitUntil(() => backgroundVideo.isPrepared);
            backgroundVideo.time = 1.0;
            yield return null; // 1 frame to let VideoPlayer render that frame
        }

        // ── Step 3: Open eyes (video frame is ready, no black camera flash) ──────
        if (eyeBlink != null)
            yield return eyeBlink.OpenEyes();

        // ── Step 4: Play video2 from seeked position, wait for it to finish or skip via double-tap ──────
        if (backgroundVideo != null && videoClip2 != null)
        {
            backgroundVideo.Play();

            // Spawn skip text on Canvas dynamically
            GameObject skipTextGo = null;
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                skipTextGo = new GameObject("SkipText", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                skipTextGo.transform.SetParent(canvas.transform, false);

                var rect = skipTextGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 0f); // Bottom-Right
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot     = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(-50f, 50f);

                var tmp = skipTextGo.GetComponent<TMPro.TextMeshProUGUI>();
                tmp.text = "Double click to skip...";
                tmp.fontSize = 22;
                tmp.alignment = TMPro.TextAlignmentOptions.BottomRight;
                tmp.color = new Color(1f, 1f, 1f, 0.6f); // Soft semi-transparent white

                // Load antiquity-print SDF font from Resources/Font/
                TMPro.TMP_FontAsset fontAsset = Resources.Load<TMPro.TMP_FontAsset>("Font/antiquity-print SDF");
                if (fontAsset != null)
                    tmp.font = fontAsset;
                else
                    Debug.LogWarning("[Menu] Could not load Font/antiquity-print SDF from Resources.");
            }

            float lastClickTime = -999f;
            while (backgroundVideo.isPlaying || backgroundVideo.time == 0)
            {
                bool clicked = false;

                // Check for left-click or touch press
                if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
                    clicked = true;
                else if (UnityEngine.InputSystem.Touchscreen.current != null && UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                    clicked = true;

                if (clicked)
                {
                    if (Time.time - lastClickTime < 0.35f) // 350ms double-tap threshold
                    {
                        Debug.Log("[Menu] Double-tap detected — skipping video 2.");
                        break;
                    }
                    lastClickTime = Time.time;
                }

                yield return null;
            }

            // Clean up skip text immediately after video 2 finishes or is skipped
            if (skipTextGo != null)
                Destroy(skipTextGo);
        }
        else
        {
            yield return new WaitForSeconds(0.5f);
        }

        // ── Step 5: Close eyes second time ───────────────────────────────────────
        if (eyeBlink != null)
            yield return eyeBlink.CloseEyes();

        // ── Step 6: Swap + prepare video3 while eyes are still closed ────────────
        if (backgroundVideo != null && videoClip3 != null)
        {
            backgroundVideo.Stop();
            backgroundVideo.clip      = videoClip3;
            backgroundVideo.isLooping = true; // loop — waiting for character selection
            backgroundVideo.Prepare();
            yield return new WaitUntil(() => backgroundVideo.isPrepared);
            backgroundVideo.time = 0.0;
            yield return null; // 1 frame to render first frame
        }

        // ── Step 7: Open eyes (video3 frame is ready) ────────────────────────────
        if (eyeBlink != null)
            yield return eyeBlink.OpenEyes();

        // ── Step 8: Play video3 loop — fade in CharacterPickingPanel in parallel ─
        if (backgroundVideo != null && videoClip3 != null)
            backgroundVideo.Play();

        if (characterPickingPanel != null)
            StartCoroutine(FadeInPanel(characterPickingPanel, 0.6f));

        // Initialize panel with server character list
        if (characterPickingManager != null && _characterList != null)
            characterPickingManager.Initialize(_characterList);
        else
            Debug.LogWarning("[Menu] characterPickingManager or _characterList is null! Check Inspector.");

        Debug.Log("[Menu] Video3 loop started. CharacterPickingPanel fading in...");
        ShowStatus("Select your character...");
    }

    /// <summary>
    /// Called when the player selects a character slot: sends re-login on same connection then joins a room.
    /// </summary>
    public void LoadGameWithSlot(int slot)
    {
        Debug.Log($"[Menu] Player selected slot {slot} — Sending login update...");
        ShowStatus($"Connecting with slot {slot + 1}...");

        if (!string.IsNullOrEmpty(_loggedInUsername) && !string.IsNullOrEmpty(_loggedInPassword))
        {
            // Unsubscribe the normal login handler before sending the slot login update
            NetworkManager.Instance.OnLoginSuccess -= HandleLoginSuccess;

            // Register a temporary one-shot handler for the slot login response
            System.Action<uint> onSlotLoginSuccess = null;
            onSlotLoginSuccess = (id) =>
            {
                NetworkManager.Instance.OnLoginSuccess -= onSlotLoginSuccess;
                NetworkManager.Instance.JoinRoom("");
            };
            NetworkManager.Instance.OnLoginSuccess += onSlotLoginSuccess;

            // Send LoginReq over the existing TCP connection (no reconnect/disconnect)
            NetworkManager.Instance.SendLoginReq(_loggedInUsername, _loggedInPassword, (byte)slot);
        }
        else
        {
            Debug.LogWarning("[Menu] LoadGameWithSlot: no login credentials stored! Calling JoinRoom directly.");
            NetworkManager.Instance.JoinRoom("");
        }
    }

    /// <summary>
    /// Fades a GameObject in (alpha 0→1) and enables interaction after the fade completes.
    /// Automatically adds CanvasGroup if missing.
    /// </summary>
    private IEnumerator FadeInPanel(GameObject panel, float duration)
    {
        var group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.AddComponent<CanvasGroup>();

        panel.SetActive(true);
        group.alpha          = 0f;
        group.interactable   = false;
        group.blocksRaycasts = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed     += Time.deltaTime;
            group.alpha  = Mathf.Lerp(0f, 1f, elapsed / duration);
            yield return null;
        }

        group.alpha          = 1f;
        group.interactable   = true;
        group.blocksRaycasts = true;
    }

    /// <summary>
    /// Fades a GameObject out then hides it (including all children).
    /// Automatically adds CanvasGroup if missing.
    /// </summary>
    private IEnumerator FadeOutPanel(GameObject panel, float duration)
    {
        var group = panel.GetComponent<CanvasGroup>();
        if (group == null)
            group = panel.AddComponent<CanvasGroup>();

        group.alpha = 1f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed     += Time.deltaTime;
            group.alpha  = Mathf.Lerp(1f, 0f, elapsed / duration);
            yield return null;
        }

        group.alpha          = 0f;
        group.interactable   = false;
        group.blocksRaycasts = false;
        panel.SetActive(false);
    }

    // ─── Panel Swap — Back ────────────────────────────────────────────────────

    /// <summary>
    /// Sequential panel swap after logout:
    ///   1. Fade out CharacterPickingPanel (disconnect already completed in HandleBack)
    ///   2. Fade in LoginPanel only after picking panel is fully hidden
    /// </summary>
    private IEnumerator SwapToLoginPanel()
    {
        const float fadeOut = 0.35f;
        const float fadeIn  = 0.35f;

        // Step 1 — Fade out CharacterPickingPanel; wait for it to finish
        if (characterPickingPanel != null)
            yield return StartCoroutine(FadeOutPanel(characterPickingPanel, fadeOut));

        // Step 2 — Show login panel only after picking panel is fully hidden
        if (loginPanel != null)
        {
            loginPanel.SetActive(true);
            yield return StartCoroutine(FadeInPanel(loginPanel, fadeIn));
        }

        ShowStatus("Peacee1Studio");
        Debug.Log("[Menu] Returned to login panel after logout.");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void ShowStatus(string msg, bool isError = false)
    {
        if (statusText == null) return;
        statusText.text  = msg;
        statusText.color = isError ? UnityEngine.Color.red : UnityEngine.Color.white;
    }

    private static void EnsureGameSession()
    {
        if (GameSession.Instance != null) return;
        var go = new GameObject("GameSession");
        go.AddComponent<GameSession>();
        Debug.Log("[Menu] Created GameSession singleton");
    }
}
