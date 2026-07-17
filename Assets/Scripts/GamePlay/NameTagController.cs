using UnityEngine;

/// <summary>
/// NameTagController — keeps the name tag text facing correctly in world space.
/// Prevents the text from being flipped/mirrored when the parent character flips left/right.
///
/// SRP: only handles scale normalization for the world-space name tag.
/// </summary>
public class NameTagController : MonoBehaviour
{
    private void LateUpdate()
    {
        if (transform.parent == null) return;

        // 1. Force world-space rotation to identity (always face camera, ignore parent Y-rotation 180)
        transform.rotation = Quaternion.identity;

        // 2. Prevent horizontal mirroring by forcing absolute positive scale in world space
        Vector3 localScale = transform.localScale;
        bool parentIsScaleFlipped = transform.parent.lossyScale.x < 0;

        transform.localScale = new Vector3(
            parentIsScaleFlipped ? -Mathf.Abs(localScale.x) : Mathf.Abs(localScale.x),
            localScale.y,
            localScale.z
        );
    }
}
