using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CameraFade — hiệu ứng fade màn hình bằng Image đen fullscreen.
/// Tự tạo Image overlay lúc runtime, không cần kéo thả trong Inspector.
///
/// Cách dùng: Gắn script này vào GameObject "Effect" (Canvas Sort Order 100) là xong.
/// </summary>
public class CameraFade : MonoBehaviour
{
    [Header("Thời lượng (giây)")]
    [SerializeField] private float fadeDuration = 0.45f;

    // Image tự tạo runtime — không cần assign trong Inspector
    private Image _overlay;

    private void Awake()
    {
        _overlay = CreateOverlay();
        SetAlpha(0f); // bắt đầu trong suốt
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Fade sang đen (0 → 1). Tương đương "nhắm mắt" / fade out.</summary>
    public IEnumerator CloseEyes() => FadeToBlack(fadeDuration);

    /// <summary>Fade từ đen sang trong (1 → 0). Tương đương "mở mắt" / fade in.</summary>
    public IEnumerator OpenEyes() => FadeFromBlack(fadeDuration);

    /// <summary>Màn hình tối dần trong <paramref name="duration"/> giây.</summary>
    public IEnumerator FadeToBlack(float duration)   => Fade(0f, 1f, duration);

    /// <summary>Màn hình sáng dần trong <paramref name="duration"/> giây.</summary>
    public IEnumerator FadeFromBlack(float duration) => Fade(1f, 0f, duration);

    /// <summary>Đặt alpha ngay lập tức (không animation).</summary>
    public void SetAlpha(float alpha)
    {
        if (_overlay == null) return;
        var c   = _overlay.color;
        c.a     = Mathf.Clamp01(alpha);
        _overlay.color   = c;
        _overlay.enabled = c.a > 0f;
    }

    // ─── Internal ─────────────────────────────────────────────────────────────

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (_overlay == null) yield break;

        _overlay.enabled = true;
        float elapsed    = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.SmoothStep(from, to, elapsed / duration));
            yield return null;
        }

        SetAlpha(to);
        _overlay.enabled = to > 0f; // tắt nếu hoàn toàn trong suốt
    }

    /// <summary>
    /// Tạo Image con phủ toàn màn hình bên trong Canvas cha (hoặc tự tạo Canvas nếu chưa có).
    /// </summary>
    private Image CreateOverlay()
    {
        // Tìm Canvas cha — script được gắn vào "Effect" Canvas
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        // Nếu vẫn không có, tạo Canvas mới
        if (canvas == null)
        {
            var go    = new GameObject("FadeCanvas");
            canvas    = go.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(go);
        }

        // Tạo Image con stretch full
        var imgGo  = new GameObject("_FadeOverlay");
        imgGo.transform.SetParent(canvas.transform, false);

        var rect   = imgGo.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var img    = imgGo.AddComponent<Image>();
        img.color  = new Color(0f, 0f, 0f, 1f); // đen
        img.raycastTarget = false;               // không chặn click

        return img;
    }
}
