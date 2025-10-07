using UnityEditor;
using UnityEngine;

public static class RemoveMissingScripts
{
    [MenuItem("Tools/Cleanup/Remove Missing Scripts In Selection")]
    static void RemoveInSelection()
    {
        int goCount = 0, compCount = 0, removedCount = 0;
        foreach (var t in Selection.gameObjects)
        {
            RemoveInGO(t, ref goCount, ref compCount, ref removedCount);
        }
        Debug.Log($"[Cleanup] GameObjects: {goCount}, Components: {compCount}, Removed missing: {removedCount}");
    }

    static void RemoveInGO(GameObject go, ref int goCount, ref int compCount, ref int removedCount)
    {
        goCount++;
        var components = go.GetComponents<Component>();
        var serializedObject = new SerializedObject(go);
        var prop = serializedObject.FindProperty("m_Component");
        int r = 0;
        for (int i = 0; i < components.Length; i++)
        {
            var c = components[i];
            if (c == null)
            {
                prop.DeleteArrayElementAtIndex(i - r);
                removedCount++;
                r++;
            }
            else compCount++;
        }
        serializedObject.ApplyModifiedProperties();
        foreach (Transform child in go.transform)
            RemoveInGO(child.gameObject, ref goCount, ref compCount, ref removedCount);
    }
}