using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// MapPortal — a teleport gate that loads a target Unity scene when the local
/// player left-clicks the portal object while standing close enough.
///
/// SRP: only handles proximity detection and scene transition.
///
/// Setup:
///   1. Attach to any GameObject that has a Collider2D (Box, Circle, etc.).
///   2. Set "Target Map Name" to match the exact Unity scene name (e.g. "Map1").
///   3. The scene must be added to File → Build Settings → Scenes In Build.
/// </summary>
public class MapPortal : MonoBehaviour
{
    [Header("Portal Settings")]
    [Tooltip("Exact Unity scene name to load (must be in Build Settings).")]
    [SerializeField] private string targetMapName = "Map1";

    [Tooltip("Max distance (world-units) between the player and the portal to allow teleport.")]
    [SerializeField] private float interactDistance = 0.5f;

    [Header("Visual Feedback")]
    [Tooltip("Color shown when player is close enough to interact.")]
    [SerializeField] private Color activeColor   = new Color(0.3f, 1f, 0.6f, 1f);
    [Tooltip("Default portal color.")]
    [SerializeField] private Color inactiveColor = new Color(0.5f, 0.5f, 1f, 0.6f);

    private Collider2D  _col;
    private SpriteRenderer _sr;
    private LocalPlayer    _localPlayer;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
        _sr  = GetComponent<SpriteRenderer>();

        if (_col == null)
            Debug.LogWarning("[MapPortal] No Collider2D found. Click detection will use a 1-unit radius.");

        if (_sr != null)
            _sr.color = inactiveColor;
    }

    private void Update()
    {
        // Lazy-find the local player (spawned after scene load)
        if (_localPlayer == null)
            _localPlayer = FindFirstObjectByType<LocalPlayer>();

        UpdateVisualFeedback();
        HandleClickInput();
    }

    // ── Visual Feedback ───────────────────────────────────────────────────────

    /// <summary>Tints the portal green when the player is within interact range.</summary>
    private void UpdateVisualFeedback()
    {
        if (_sr == null || _localPlayer == null) return;
        float dist = Vector2.Distance(_localPlayer.transform.position, transform.position);
        _sr.color = dist <= interactDistance ? activeColor : inactiveColor;
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    private void HandleClickInput()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

        var cam = Camera.main;
        if (cam == null) return;

        // Convert screen click to world position
        Vector2 mouseWorld = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());

        // Check that the click landed on THIS portal's collider (or within 1 unit of center)
        bool clickedThisPortal = _col != null
            ? _col.OverlapPoint(mouseWorld)
            : Vector2.Distance(mouseWorld, transform.position) <= 1f;

        if (!clickedThisPortal) return;

        // Distance gate: player must be close enough
        if (_localPlayer == null)
        {
            Debug.LogWarning("[MapPortal] Local player not found.");
            return;
        }

        float dist = Vector2.Distance(_localPlayer.transform.position, transform.position);
        if (dist > interactDistance)
        {
            Debug.Log($"[MapPortal] Too far to teleport ({dist:F2} > {interactDistance:F2} units).");
            return;
        }

        Teleport();
    }

    // ── Teleport ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the session map name and loads the target Unity scene.
    /// GameSceneBootstrap will automatically re-spawn the player when the scene loads.
    /// </summary>
    private void Teleport()
    {
        if (string.IsNullOrWhiteSpace(targetMapName))
        {
            Debug.LogWarning("[MapPortal] targetMapName is empty — teleport cancelled.");
            return;
        }

        Debug.Log($"[MapPortal] Teleporting to '{targetMapName}'...");

        // Sync session so Bootstrap spawns the player in the correct map
        if (GameSession.Instance != null)
            GameSession.Instance.SetMapName(targetMapName);

        SceneManager.LoadScene(targetMapName);
    }
}
