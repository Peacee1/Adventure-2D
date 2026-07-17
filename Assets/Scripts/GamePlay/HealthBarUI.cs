using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HealthBarUI — attached directly to the HPBarCanvas prefab.
///
/// Setup:
///   - Attached to the root of the "HPBarCanvas" prefab.
///   - References the parent Image and child fill Image in its Inspector.
///   - Target BaseObject is injected at runtime using SetTarget().
///
/// SRP: only handles rendering the target's health values onto the HP bar images.
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────

    [Header("HP Bar UI Images")]
    [Tooltip("The parent Image representing the background (holds the container).")]
    [SerializeField] private Image parentImage;

    [Tooltip("The child Image representing the current health fill (Image Type must be Filled).")]
    [SerializeField] private Image fillImage;

    [Header("Settings")]
    [Tooltip("If true, the health bar container will be hidden when target is at 100% health.")]
    [SerializeField] private bool hideWhenFull = true;

    // ─── Private State ────────────────────────────────────────────────────────

    private BaseObject _target;
    private float      _lastHP = -1f;
    private float      _lastMaxHP = -1f;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Validate fill image type
        if (fillImage != null && fillImage.type != Image.Type.Filled)
        {
            fillImage.type = Image.Type.Filled;
        }

        UpdateHealthDisplay(true);
    }

    private void Update()
    {
        if (_target == null) return;

        float currentHP = _target.HP;
        float maxHP     = _target.MaxHP;

        // Only update fill amount when health changes to avoid overhead
        if (!Mathf.Approximately(currentHP, _lastHP) || !Mathf.Approximately(maxHP, _lastMaxHP))
        {
            _lastHP    = currentHP;
            _lastMaxHP = maxHP;
            UpdateHealthDisplay(false);
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Injects the target character to track.
    /// Called at runtime by BaseObject when the HP bar prefab is instantiated.
    /// </summary>
    public void SetTarget(BaseObject targetObject)
    {
        _target = targetObject;
        if (_target != null)
        {
            _lastHP    = _target.HP;
            _lastMaxHP = _target.MaxHP;
            UpdateHealthDisplay(true);
        }
    }

    // ─── Private Methods ──────────────────────────────────────────────────────

    private void UpdateHealthDisplay(bool forceUpdate)
    {
        if (_target == null)
        {
            // If no target, keep the container hidden
            if (parentImage != null)
                parentImage.gameObject.SetActive(false);
            return;
        }

        float hp    = _target.HP;
        float maxHp = _target.MaxHP;

        if (maxHp <= 0f) return;

        float fillRatio = Mathf.Clamp01(hp / maxHp);

        // Update fillAmount
        if (fillImage != null)
        {
            fillImage.fillAmount = fillRatio;
        }

        // Toggle background visibility
        if (parentImage != null)
        {
            bool shouldBeVisible = true;

            if (hp <= 0f)
            {
                shouldBeVisible = false; // Hide if dead
            }
            else if (hideWhenFull && Mathf.Approximately(fillRatio, 1f))
            {
                shouldBeVisible = false; // Hide if full health
            }

            if (parentImage.gameObject.activeSelf != shouldBeVisible)
            {
                parentImage.gameObject.SetActive(shouldBeVisible);
            }
        }
    }
}
