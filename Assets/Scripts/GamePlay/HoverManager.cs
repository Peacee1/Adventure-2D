using UnityEngine;
using UnityEngine.InputSystem;

namespace Freeland.Gameplay
{
    public class HoverManager : MonoBehaviour
    {
        public static HoverManager Instance { get; private set; }

        [SerializeField] private LayerMask interactableLayers = ~0; // Default to all layers

        private BaseObject currentHoveredObject;
        private Camera mainCamera;
        private Collider2D lastHitCollider;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            mainCamera = Camera.main;
            Debug.Log("[HoverManager] Initialized.");
        }

        private void Update()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
            }

            // Read mouse position from the new Input System
            if (Mouse.current != null)
            {
                Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
                Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);

                // Raycast in 2D to find if any collider is under the mouse
                RaycastHit2D hit = Physics2D.Raycast(mouseWorldPos, Vector2.zero, 0f, interactableLayers);

                BaseObject newHovered = null;
                if (hit.collider != null)
                {
                    // Search in the hit object or its parents/children
                    newHovered = hit.collider.GetComponent<BaseObject>();
                    if (newHovered == null)
                    {
                        newHovered = hit.collider.GetComponentInParent<BaseObject>();
                    }
                }

                // Log raycast hit only when the hit collider changes to avoid console spam
                if (hit.collider != lastHitCollider)
                {
                    lastHitCollider = hit.collider;
                    if (lastHitCollider != null)
                    {
                        Debug.Log($"[HoverManager] Raycast hit: {lastHitCollider.name} on layer {LayerMask.LayerToName(lastHitCollider.gameObject.layer)} (BaseObject: {(newHovered != null ? "Yes" : "No")})");
                    }
                    else
                    {
                        Debug.Log("[HoverManager] Raycast hit: None");
                    }
                }

                // If the hovered object changed
                if (newHovered != currentHoveredObject)
                {
                    Debug.Log($"[HoverManager] Hover target changing from {(currentHoveredObject != null ? currentHoveredObject.name : "null")} to {(newHovered != null ? newHovered.name : "null")}");

                    if (currentHoveredObject != null)
                    {
                        currentHoveredObject.SetHover(false);
                    }

                    currentHoveredObject = newHovered;

                    if (currentHoveredObject != null)
                    {
                        currentHoveredObject.SetHover(true);
                        
                        // Show the ObjectInformationPanel
                        if (ObjectInformationPanel.Instance != null)
                        {
                            ObjectInformationPanel.Instance.Show(currentHoveredObject);
                        }
                    }
                    else
                    {
                        // Hide the ObjectInformationPanel
                        if (ObjectInformationPanel.Instance != null)
                        {
                            ObjectInformationPanel.Instance.Hide();
                        }
                    }
                }
            }
            else
            {
                // Mouse.current is null - log warning periodically (throttled) or just once
                if (Time.frameCount % 300 == 0)
                {
                    Debug.LogWarning("[HoverManager] Mouse.current is null! Check if Input System is properly configured.");
                }
            }
        }
    }
}
