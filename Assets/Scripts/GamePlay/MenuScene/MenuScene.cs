using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// MenuScene — xử lý giao diện đăng nhập, đăng ký và lựa chọn slot nhân vật.
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

    [Header("Scene Configuration")]
    [SerializeField] private string gameSceneName = "SampleScene";

    private void Awake()
    {
        playBtn.onClick.AddListener(() => Play());
        loginBtn.onClick.AddListener(() => Login());
        registerBtn.onClick.AddListener(() => Register());
    }

    private void Start()
    {
        // Đăng ký nhận sự kiện từ NetworkManager
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnLoginSuccess    += HandleLoginSuccess;
            NetworkManager.Instance.OnLoginFailed     += HandleLoginFailed;
            NetworkManager.Instance.OnRegisterResult  += HandleRegisterResult;
            NetworkManager.Instance.OnJoinRoomSuccess += HandleJoinRoomSuccess;
        }
        else
        {
            Debug.LogWarning("[MenuScene] NetworkManager.Instance không tồn tại.");
        }
    }

    private void OnDestroy()
    {
        // Hủy đăng ký sự kiện tránh leak memory
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnLoginSuccess    -= HandleLoginSuccess;
            NetworkManager.Instance.OnLoginFailed     -= HandleLoginFailed;
            NetworkManager.Instance.OnRegisterResult  -= HandleRegisterResult;
            NetworkManager.Instance.OnJoinRoomSuccess -= HandleJoinRoomSuccess;
        }
    }

    /// <summary>
    /// Play kết nối và đăng nhập tài khoản. Chức nghiệp mặc định của nhân vật mới sẽ là Archer (Cung thủ).
    /// </summary>
    private void Play()
    {
        string username = usernameInput.text;
        string password = passwordInput.text;
        byte slot = (byte)slotDropdown.value; // Dropdown có index 0, 1, 2 tương ứng với tối đa 3 nhân vật

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Debug.LogWarning("[Menu] Vui lòng nhập đầy đủ Username và Password.");
            return;
        }

        Debug.Log($"[Menu] Bắt đầu chơi. Tài khoản: {username}, Slot nhân vật: {slot + 1}");
        // Kết nối và đăng nhập. Mặc định phía Server khi tạo mới nhân vật sẽ là Cung thủ (JobClass = 1)
        NetworkManager.Instance.Connect(username, password, slot);
    }

    private void Login()
    {
        string username = usernameInput.text;
        string password = passwordInput.text;
        byte slot = (byte)slotDropdown.value;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Debug.LogWarning("[Menu] Vui lòng nhập đầy đủ Username và Password.");
            return;
        }

        Debug.Log($"[Menu] Yêu cầu đăng nhập tài khoản: {username}");
        NetworkManager.Instance.Connect(username, password, slot);
    }

    private void Register()
    {
        string username = usernameInput.text;
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Debug.LogWarning("[Menu] Vui lòng nhập đầy đủ Username và Password để đăng ký.");
            return;
        }

        Debug.Log($"[Menu] Yêu cầu đăng ký tài khoản mới: {username}");
        NetworkManager.Instance.RegisterAccount(username, password);
    }

    // ─── Network Event Handlers ───────────────────────────────────────────────

    private void HandleLoginSuccess(uint playerID)
    {
        Debug.Log($"[Menu] Đăng nhập thành công! ID nhân vật: {playerID}. Đang vào phòng chơi...");
        NetworkManager.Instance.JoinRoom(""); // Tự động ghép phòng chơi
    }

    private void HandleLoginFailed(string msg)
    {
        Debug.LogError($"[Menu] Đăng nhập thất bại: {msg}");
    }

    private void HandleRegisterResult(bool success, string msg)
    {
        if (success)
            Debug.Log($"[Menu] Đăng ký thành công: {msg}");
        else
            Debug.LogError($"[Menu] Đăng ký thất bại: {msg}");
    }

    private void HandleJoinRoomSuccess(string roomID, List<PlayerInfo> existingPlayers)
    {
        Debug.Log($"[Menu] Đã vào phòng {roomID}. Chuyển cảnh sang Scene game...");
        SceneManager.LoadScene(gameSceneName);
    }
}
