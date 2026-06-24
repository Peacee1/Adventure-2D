using UnityEngine;

namespace Freeland.Gameplay
{
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target Settings")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f); // Default Z offset for 2D camera

        [Header("Smooth Settings")]
        [SerializeField] [Range(0f, 1f)] private float smoothTime = 0.15f; // Duration of smoothing

        private Vector3 velocity = Vector3.zero;

        private void Start()
        {
            // Safeguard: Ensure this script is ONLY active if attached to a Camera GameObject
            if (GetComponent<Camera>() == null)
            {
                Debug.LogError($"[CameraFollow] Script is attached to a non-Camera GameObject ({gameObject.name})! Disabling script to protect position.");
                enabled = false;
                return;
            }
            FindPlayerTarget();
        }

        private void LateUpdate()
        {
            // If the target is lost, try to find the player target again
            if (target == null)
            {
                FindPlayerTarget();
                if (target == null) return;
            }

            // Target position based on player position and offset (lock target Z to 0)
            Vector3 targetPosition = new Vector3(target.position.x + offset.x, target.position.y + offset.y, offset.z);

            // Smoothly interpolate the camera position using SmoothDamp
            Vector3 newPosition = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime);
            
            // Force the Z position to remain strictly at offset.z
            transform.position = new Vector3(newPosition.x, newPosition.y, offset.z);
        }

        private void FindPlayerTarget()
        {
            Player player = FindFirstObjectByType<Player>();
            if (player != null)
            {
                target = player.transform;
            }
        }
    }
}
