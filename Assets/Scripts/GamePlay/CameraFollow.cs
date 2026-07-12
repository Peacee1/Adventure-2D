using UnityEngine;

namespace Freeland.Gameplay
{
    /// <summary>
    /// CameraFollow — camera bám theo local player.
    /// Gắn vào Main Camera.
    ///
    /// SRP: chỉ xử lý camera tracking.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private float smoothSpeed = 8f;
        [SerializeField] private Vector3 offset    = new Vector3(0f, 0f, -10f);

        private Transform target;

        public void SetTarget(Transform t)
        {
            target = t;
            Debug.Log($"[CameraFollow] Target set: {t.name}");
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desired  = target.position + offset;
            Vector3 smoothed = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
            transform.position = smoothed;
        }
    }
}
