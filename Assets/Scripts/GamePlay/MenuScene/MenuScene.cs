using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// MenuScene — xử lý giao diện đăng nhập, đăng ký và lựa chọn slot nhân vật.
/// Sau khi login + join room thành công → lưu data vào GameSession → LoadScene.
/// </summary>
public class MenuScene : MonoBehaviour
{
    [Header("UI Fields")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TMP_Dropdown   slotDropdown; // Dropdown chọn Slot 1, 2, 3

    [Header("Buttons")]
    [SerializeField] private Button loginBtn;
    [SerializeField] private Button registerBtn;
    [SerializeField] private Button playBtn;

    [Header("Feedback")]
    [SerializeField] private TMP_Text statusText;


    private void Awake()
    {
        if (playBtn     != null) playBtn.onClick.AddListener(Play);
        if (loginBtn    != null) loginBtn.onClick.AddListener(Login);
        if (registerBtn != null) registerBtn.onClick.AddListener(Register);
    }

    private void Start()
    {
        EnsureGameSession();

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnLoginSuccess    += HandleLoginSuccess;
            NetworkManager.Instance.OnLoginFailed     += HandleLoginFailed;
            NetworkManager.Instance.OnRegisterResult  += HandleRegisterResult;
            NetworkManager.Instance.OnJoinRoomSuccess += HandleJoinRoomSuccess;
        }
        else
        {
            Debug.LogError("[MenuScene] NetworkManager.Instance == null! Hãy đảm bảo NetworkManager có trong scene.");
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnLoginSuccess    -= HandleLoginSuccess;
            NetworkManager.Instance.OnLoginFailed     -= HandleLoginFailed;
            NetworkManager.Instance.OnRegisterResult  -= HandleRegisterResult;
            NetworkManager.Instance.OnJoinRoomSuccess -= HandleJoinRoomSuccess;
        }
    }

    // ─── Button Handlers ─────────────────────────────────────────────────────

    private void Play()
    {
        string username = usernameInput?.text ?? "";
        string password = passwordInput?.text ?? "";
        byte   slot     = (byte)(slotDropdown?.value ?? 0);

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowStatus("Vui lòng nhập đầy đủ Username và Password.", isError: true);
            return;
        }

        ShowStatus($"Đang kết nối... (slot {slot + 1})");
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
            ShowStatus("Vui lòng nhập đầy đủ Username và Password.", isError: true);
            return;
        }

        ShowStatus("Đang đăng nhập...");
        Debug.Log($"[Menu] Login: user={username} slot={slot}");
        NetworkManager.Instance.Connect(username, password, slot);
    }

    private void Register()
    {
        string username = usernameInput?.text ?? "";
        string password = passwordInput?.text ?? "";

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowStatus("Vui lòng nhập đầy đủ Username và Password.", isError: true);
            return;
        }

        ShowStatus("Đang đăng ký...");
        Debug.Log($"[Menu] Register: user={username}");
        NetworkManager.Instance.RegisterAccount(username, password);
    }

    // ─── Network Event Handlers ───────────────────────────────────────────────

    private void HandleLoginSuccess(uint playerID)
    {
        Debug.Log($"[Menu] Login OK! PlayerID={playerID}. Đang vào phòng...");
        ShowStatus("Đăng nhập thành công! Đang vào phòng...");
        NetworkManager.Instance.JoinRoom(""); // Tự động ghép phòng
    }

    private void HandleLoginFailed(string msg)
    {
        Debug.LogError($"[Menu] Login failed: {msg}");
        ShowStatus($"Đăng nhập thất bại: {msg}", isError: true);
    }

    private void HandleRegisterResult(bool success, string msg)
    {
        if (success)
        {
            ShowStatus($"Đăng ký thành công! {msg}");
            Debug.Log($"[Menu] Register OK: {msg}");
        }
        else
        {
            ShowStatus($"Đăng ký thất bại: {msg}", isError: true);
            Debug.LogError($"[Menu] Register failed: {msg}");
        }
    }

    private void HandleJoinRoomSuccess(string roomID, List<PlayerInfo> existingPlayers)
    {
        // Lấy map name từ GameSession (đã set khi nhận LoginAck)
        string targetScene = GameSession.Instance != null && !string.IsNullOrEmpty(GameSession.Instance.MapName)
            ? GameSession.Instance.MapName
            : "Map1";

        Debug.Log($"[Menu] Joined room {roomID} (existing={existingPlayers.Count}). Loading scene '{targetScene}'...");
        ShowStatus($"Đã vào phòng! Đang tải {targetScene}...");

        if (GameSession.Instance != null)
            GameSession.Instance.SetRoom(roomID, existingPlayers);

        SceneManager.LoadScene(targetScene);
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
