using UnityEngine;

/// <summary>
/// LockRotation — locks the world-space rotation of this GameObject to Quaternion.identity (zero rotation).
/// Keeps world-space canvases and UI elements facing forward regardless of the parent's rotation or movement.
///
/// SRP: only handles locking the rotation of this GameObject in world space.
/// </summary>
public class LockRotation : MonoBehaviour
{
    private void LateUpdate()
    {
        // Force world-space rotation to face forward (towards the camera)
        transform.rotation = Quaternion.identity;
    }
}
