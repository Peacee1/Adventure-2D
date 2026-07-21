using UnityEngine;
using UnityEngine.InputSystem;

namespace Freeland.Gameplay
{
    /// <summary>
    /// CameraFollow — camera bám theo local player.
    /// Gắn vào Main Camera.
    ///
    /// SRP: chỉ xử lý camera tracking và zoom.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private float smoothSpeed  = 8f;
        [SerializeField] private Vector3 offset     = new Vector3(0f, 0f, -10f);

        [Header("Orthographic Zoom")]
        [SerializeField] private float zoomSpeed    = 2f;
        [SerializeField] private float minSize      = 3f;
        [SerializeField] private float maxSize      = 20f;
        [SerializeField] private float zoomSmooth   = 8f;

        private Transform target;
        private Camera    cam;
        private float     targetSize;

        private void Awake()
        {
            cam        = GetComponent<Camera>();
            targetSize = cam != null ? cam.orthographicSize : 8f;
        }

        public void SetTarget(Transform t)
        {
            target = t;
            Debug.Log($"[CameraFollow] Target set: {t.name}");
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // ── Position follow ────────────────────────────────────────
            Vector3 desired  = target.position + offset;
            Vector3 smoothed = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
            transform.position = smoothed;

            // ── Scroll wheel zoom (orthographic only) ──────────────────
            if (cam == null || !cam.orthographic) return;

            float scroll = Mouse.current != null
                ? Mouse.current.scroll.ReadValue().y
                : 0f;

            if (scroll != 0f)
                targetSize = Mathf.Clamp(targetSize - scroll * zoomSpeed * Time.deltaTime * 50f, minSize, maxSize);

            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, zoomSmooth * Time.deltaTime);
        }
    }
}
