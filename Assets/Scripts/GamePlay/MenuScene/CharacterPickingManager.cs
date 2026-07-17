using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CharacterPickingManager — manages the entire Character Picking Panel.
///
/// Responsibilities:
///   - Receive CharacterData[] from MenuScene after the server returns the character list.
///   - Update each of the 3 slot UIs via CharacterSlotUI.Refresh().
///   - Enforce single-selection: highlight the selected slot, deselect all others.
///   - Show/hide the Start button based on whether a slot is selected.
///   - Forward the selected slot index to MenuScene via OnCharacterSlotConfirmed.
///
/// SRP: only coordinates the 3 slot UIs and routes events; no network logic here.
/// DIP: depends on events rather than calling MenuScene directly.
/// </summary>
public class CharacterPickingManager : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("Character Slots (0, 1, 2)")]
    [SerializeField] private CharacterSlotUI[] slots; // Drag 3 slot GameObjects here

    [Header("Start Button")]
    [SerializeField] private Button startButton; // Shown after a slot is selected — loads Map1

    [Header("Back Button")]
    [SerializeField] private Button backButton;  // Returns to login panel and disconnects

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired when the player clicks Start after selecting a slot.
    /// MenuScene listens to this to call LoginReq + JoinRoom with the chosen slot.
    /// </summary>
    public event System.Action<int> OnCharacterSlotConfirmed;

    /// <summary>
    /// Fired when the player clicks Back.
    /// MenuScene listens to this to disconnect and return to the login panel.
    /// </summary>
    public event System.Action OnBackRequested;

    // ─── Private State ────────────────────────────────────────────────────────

    private int _selectedSlot = -1; // -1 = nothing selected
    private CharacterData[] _characterList;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        ValidateSlots();

        // Start button hidden until a slot is selected
        if (startButton != null)
        {
            startButton.gameObject.SetActive(false);
            startButton.onClick.AddListener(HandleStartClicked);
        }

        // Back button always visible while the panel is active
        if (backButton != null)
            backButton.onClick.AddListener(HandleBackClicked);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes the panel with the character list received from the server.
    /// Called from MenuScene when OnCharacterListReceived fires.
    /// </summary>
    /// <param name="characterList">Array of 3 CharacterData objects (index = slot).</param>
    public void Initialize(CharacterData[] characterList)
    {
        if (characterList == null || characterList.Length < 3)
        {
            Debug.LogError("[CharacterPickingManager] characterList must have exactly 3 elements!");
            return;
        }

        _selectedSlot = -1;
        _characterList = characterList;

        for (int i = 0; i < slots.Length && i < 3; i++)
        {
            if (slots[i] == null)
            {
                Debug.LogWarning($"[CharacterPickingManager] slots[{i}] is null — check Inspector!");
                continue;
            }

            slots[i].OnSlotSelected -= HandleSlotSelected;
            slots[i].OnSlotSelected += HandleSlotSelected;

            slots[i].Refresh(characterList[i]);
        }

        // Hide Start until user picks a slot
        if (startButton != null)
            startButton.gameObject.SetActive(false);

        Debug.Log("[CharacterPickingManager] Panel initialized with server character list.");
    }

    // ─── Private Methods ──────────────────────────────────────────────────────

    /// <summary>
    /// Handles a slot click: if the slot is empty, immediately confirms selection
    /// to trigger auto-creation. If the slot has a character, highlights it and shows Start button.
    /// </summary>
    private void HandleSlotSelected(int slotIndex)
    {
        // 1. If slot is empty, trigger game start immediately
        if (_characterList != null && slotIndex >= 0 && slotIndex < _characterList.Length)
        {
            if (!_characterList[slotIndex].Exists)
            {
                Debug.Log($"[CharacterPickingManager] Slot {slotIndex} is empty. Auto-creating Archer and entering game.");
                OnCharacterSlotConfirmed?.Invoke(slotIndex);
                return;
            }
        }

        // 2. Standard flow for existing characters
        Debug.Log($"[CharacterPickingManager] Player highlighted slot {slotIndex}");
        _selectedSlot = slotIndex;

        // Update highlight on all slots — mirrors HoverManager's single-object selection model
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].SetSelected(i == slotIndex);
        }

        // Reveal Start button
        if (startButton != null)
            startButton.gameObject.SetActive(true);
    }

    /// <summary>
    /// Called when the player clicks Start — confirms the selected slot and fires the event.
    /// MenuScene receives this and calls Connect(slot) → JoinRoom → LoadScene("Map1").
    /// </summary>
    private void HandleStartClicked()
    {
        if (_selectedSlot < 0)
        {
            Debug.LogWarning("[CharacterPickingManager] Start clicked but no slot is selected!");
            return;
        }

        Debug.Log($"[CharacterPickingManager] Start confirmed for slot {_selectedSlot}");
        OnCharacterSlotConfirmed?.Invoke(_selectedSlot);
    }

    /// <summary>
    /// Called when the player clicks Back.
    /// Resets selection state and fires OnBackRequested for MenuScene to handle.
    /// </summary>
    private void HandleBackClicked()
    {
        Debug.Log("[CharacterPickingManager] Back clicked — resetting selection and requesting logout.");

        // Reset highlight on all slots
        _selectedSlot = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                slots[i].SetSelected(false);
        }

        // Hide Start button
        if (startButton != null)
            startButton.gameObject.SetActive(false);

        OnBackRequested?.Invoke();
    }

    /// <summary>Validates slot configuration in Awake to catch errors early.</summary>

    private void ValidateSlots()
    {
        if (slots == null || slots.Length != 3)
        {
            Debug.LogError("[CharacterPickingManager] slots must have exactly 3 elements! Check Inspector.");
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
                Debug.LogWarning($"[CharacterPickingManager] slots[{i}] is not assigned in Inspector!");
        }
    }
}
