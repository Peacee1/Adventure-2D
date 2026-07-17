using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

/// <summary>
/// CharacterSlotUI — controls the display of a single character slot in the CharacterPickingPanel.
///
/// Each slot contains:
///   - A TMP_Text showing character info (e.g. "Archer level 10" or "No information")
///   - A Button shown when the slot is empty, to create a new character
///   - An Image (with a Button on it) showing the character sprite when a character exists.
///     Clicking the image selects/highlights the slot. Hover effects mirror HoverManager's SetHover pattern.
///
/// SRP: only handles display and selection for one slot.
/// OCP: extend job support by adding cases to GetJobDisplayName and LoadJobSprites.
/// </summary>
public class CharacterSlotUI : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("UI Elements")]
    [SerializeField] private TMP_Text infoText;         // Character description text
    [SerializeField] private Button   createButton;     // "+" button shown on empty slots
    [SerializeField] private Button   characterButton;  // Button on the character image (click to select)
    [SerializeField] private Image    characterImage;   // Character sprite

    [Header("Highlight Settings")]
    [SerializeField] private Color normalBorderColor    = new Color(1f, 1f, 1f, 0f);  // Transparent border by default
    [SerializeField] private Color hoverBorderColor     = new Color(1f, 1f, 0.4f, 0.7f); // Soft yellow on hover
    [SerializeField] private Color selectedBorderColor  = new Color(0.3f, 0.9f, 1f, 1f);  // Cyan when selected
    [SerializeField] private Image  borderImage;        // Separate border Image component for highlight

    [Header("Slot Config")]
    [SerializeField] private int slotIndex; // 0, 1, or 2 — set in Inspector

    // ─── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fired when the player selects this slot (regardless of whether a character exists).</summary>
    public event Action<int> OnSlotSelected;

    // ─── Private State ────────────────────────────────────────────────────────

    private CharacterData _currentData;
    private bool          _isSelected;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (createButton    != null) createButton.onClick.AddListener(HandleSlotSelected);
        if (characterButton != null) characterButton.onClick.AddListener(HandleSlotSelected);

        // Start with no border highlight
        SetBorderColor(normalBorderColor);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes all UI elements for this slot based on character data.
    /// Called from CharacterPickingManager.Initialize().
    /// </summary>
    public void Refresh(CharacterData data)
    {
        _currentData = data;
        _isSelected  = false;
        SetBorderColor(normalBorderColor);

        if (data == null || !data.Exists)
            ShowEmptySlot();
        else
            ShowExistingCharacter(data);
    }

    /// <summary>
    /// Marks this slot as selected (cyan border) or deselected (no border).
    /// Called externally by CharacterPickingManager to enforce single-selection.
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        SetBorderColor(selected ? selectedBorderColor : normalBorderColor);
    }

    // ─── IPointerEnterHandler / IPointerExitHandler ───────────────────────────

    /// <summary>Highlights the slot on mouse hover — mirrors HoverManager's SetHover(true) pattern.</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        // Do not override selected color
        if (!_isSelected)
            SetBorderColor(hoverBorderColor);
    }

    /// <summary>Removes hover highlight — mirrors HoverManager's SetHover(false) pattern.</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_isSelected)
            SetBorderColor(normalBorderColor);
    }

    // ─── Private Methods ──────────────────────────────────────────────────────

    /// <summary>Displays empty slot state: "No information" + create button + hidden image button.</summary>
    private void ShowEmptySlot()
    {
        if (infoText != null)
            infoText.text = "No information";

        if (createButton != null)
            createButton.gameObject.SetActive(true);

        if (characterButton != null)
            characterButton.gameObject.SetActive(false);

        if (characterImage != null)
            characterImage.gameObject.SetActive(false);
    }

    /// <summary>
    /// Displays an existing character: class+level description + hidden create button + character image button.
    /// Loads the first sprite from the job's idle.png spritesheet.
    /// </summary>
    private void ShowExistingCharacter(CharacterData data)
    {
        if (infoText != null)
            infoText.text = $"{GetJobDisplayName(data.JobClass)} level {data.Level}";

        if (createButton != null)
            createButton.gameObject.SetActive(false);

        if (characterButton != null)
            characterButton.gameObject.SetActive(true);

        if (characterImage != null)
        {
            Sprite[] sprites = LoadJobSprites(data.JobClass);
            if (sprites != null && sprites.Length > 0)
            {
                characterImage.sprite = sprites[0];
                characterImage.gameObject.SetActive(true);
            }
            else
            {
                characterImage.gameObject.SetActive(false);
                Debug.LogWarning($"[CharacterSlotUI] Slot {slotIndex}: no sprites found for JobClass={data.JobClass}");
            }
        }
    }

    /// <summary>
    /// Returns the display name for a job class.
    /// Add new cases here when new job classes are supported.
    /// </summary>
    private static string GetJobDisplayName(byte jobClass)
    {
        return jobClass switch
        {
            0 => "Warrior",
            1 => "Archer",
            2 => "Mage",
            3 => "Healer",
            4 => "Assassin",
            5 => "Tank",
            _ => "Unknown",
        };
    }

    /// <summary>
    /// Loads all sprites from the job's idle.png spritesheet in Resources.
    /// The spritesheet must be sliced in Unity (Sprite Mode = Multiple).
    /// Currently only Archer (JobClass=1) is supported.
    /// </summary>
    private static Sprite[] LoadJobSprites(byte jobClass)
    {
        string spritePath = jobClass switch
        {
            1 => "Character/Archer/idle",
            _ => "Character/Archer/idle", // Fallback to Archer
        };

        Sprite[] sprites = Resources.LoadAll<Sprite>(spritePath);
        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogWarning($"[CharacterSlotUI] Could not load sprites at '{spritePath}'. " +
                             "Ensure idle.png is in Resources/Character/Archer/ and has been sliced.");
        }
        return sprites;
    }

    private Outline _slotOutline;

    private void SetBorderColor(Color color)
    {
        // 1. Handle separate border image if assigned in Inspector
        if (borderImage != null)
        {
            borderImage.color = color;
            borderImage.gameObject.SetActive(color.a > 0.01f);
            return;
        }

        // 2. Fallback: Draw a clean rectangular border outline around the Slot GameObject itself
        if (_slotOutline == null)
        {
            _slotOutline = GetComponent<Outline>();
            if (_slotOutline == null)
            {
                _slotOutline = gameObject.AddComponent<Outline>();
                _slotOutline.effectDistance = new Vector2(4f, 4f); // 4px crisp border
            }
        }

        _slotOutline.effectColor = color;
        _slotOutline.enabled = (color.a > 0.01f);

        // Reset character material to default
        if (characterImage != null)
            characterImage.material = null;
    }

    /// <summary>Called when the player clicks this slot (create button or character image button).</summary>
    private void HandleSlotSelected()
    {
        Debug.Log($"[CharacterSlotUI] Slot {slotIndex} selected (exists={_currentData?.Exists})");
        OnSlotSelected?.Invoke(slotIndex);
    }
}
