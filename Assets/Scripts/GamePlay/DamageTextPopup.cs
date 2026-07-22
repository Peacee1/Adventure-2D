using TMPro;
using UnityEngine;

/// <summary>
/// DamageTextPopup — animates a floating damage number in world space.
/// Moves upward in world units and fades out, then self-destructs.
///
/// SRP: only handles the animation of one popup instance.
/// </summary>
[RequireComponent(typeof(TextMeshPro))]
public class DamageTextPopup : MonoBehaviour
{
    [SerializeField] private float floatSpeed = 1.5f;   // world units per second upward
    [SerializeField] private float duration   = 1.2f;
    [SerializeField] private float fadeStart  = 0.4f;   // fraction of lifetime before fade

    private TextMeshPro _tmp;
    private float       _elapsed;
    private Color       _startColor;

    private void Awake()
    {
        _tmp        = GetComponent<TextMeshPro>();
        _startColor = _tmp != null ? _tmp.color : Color.red;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;

        // Float upward in world space
        transform.position += Vector3.up * floatSpeed * Time.deltaTime;

        // Fade after fadeStart fraction of lifetime
        float fade  = Mathf.InverseLerp(fadeStart * duration, duration, _elapsed);
        float alpha = Mathf.Lerp(1f, 0f, fade);
        if (_tmp != null)
            _tmp.color = new Color(_startColor.r, _startColor.g, _startColor.b, alpha);

        if (_elapsed >= duration)
            Destroy(gameObject);
    }
}
