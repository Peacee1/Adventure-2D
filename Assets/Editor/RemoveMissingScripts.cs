using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor utility: tìm và xóa tất cả Missing Script trong scene hiện tại.
/// Menu: Tools → Remove Missing Scripts
/// </summary>
public static class RemoveMissingScripts
{
    [MenuItem("Tools/Remove Missing Scripts in Scene")]
    public static void RemoveInScene()
    {
        var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        int removedCount = 0;
        int goCount      = 0;

        foreach (var go in allObjects)
        {
            var components = go.GetComponents<Component>();
            var so         = new SerializedObject(go);
            var prop       = so.FindProperty("m_Component");

            for (int i = components.Length - 1; i >= 0; i--)
            {
                if (components[i] == null)
                {
                    prop.DeleteArrayElementAtIndex(i);
                    removedCount++;
                    Debug.Log($"[RemoveMissing] Removed missing script from: {go.name}");
                }
            }

            if (removedCount > 0) { so.ApplyModifiedProperties(); goCount++; }
        }

        if (removedCount == 0)
            Debug.Log("[RemoveMissing] Không tìm thấy missing script nào.");
        else
            Debug.Log($"[RemoveMissing] Done: {removedCount} script(s) from {goCount} GO(s) removed.");
    }
}
