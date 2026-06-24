using UnityEngine;
using UnityEditor;

public static class FixTutorialUIMaskHierarchy
{
    [MenuItem("Tools/Fix Tutorial Mask Hierarchy")]
    public static void Fix()
    {
        var mask = Object.FindObjectOfType<TutorialUIMask>(true);
        if (mask != null)
        {
            Debug.Log($"Found TutorialUIMask on GameObject: {mask.gameObject.name}, current parent: {(mask.transform.parent != null ? mask.transform.parent.name : "None")}");
            
            // ย้ายไปเป็นลูกของ MainCanvas
            var canvas = GameObject.Find("MainCanvas");
            if (canvas != null)
            {
                mask.transform.SetParent(canvas.transform, false);
                mask.transform.SetAsLastSibling(); // ให้อยู่ล่างสุด (วาดทับบนสุด)
                Debug.Log("Moved TutorialUIMask to MainCanvas and set as last sibling.");
                
                // บังคับขยายเต็มจอ
                RectTransform rect = mask.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(mask.gameObject.scene);
            }
        }
        else
        {
            Debug.Log("TutorialUIMask component not found in the scene.");
        }
    }
}
