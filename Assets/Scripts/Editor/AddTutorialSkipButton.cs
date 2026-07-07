using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor utility — เพิ่มปุ่ม Skip ในซีน TutorialScence และผูกเข้ากับ TutorialManager.RequestSkip()
/// เรียกใช้ผ่าน Menu: GameCard/Add Tutorial Skip Button (ต้องเปิดซีน TutorialScence.unity ค้างไว้ก่อน)
/// </summary>
public static class AddTutorialSkipButton
{
    private const string ButtonName = "TutorialSkipButton";
    private const string TutorialScenePath = "Assets/Scenes/TutorialScence.unity";

    // Entry point สำหรับรันจาก command line: -executeMethod AddTutorialSkipButton.RunOnTutorialScene
    public static void RunOnTutorialScene()
    {
        var scene = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);
        Run();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[AddTutorialSkipButton] ✅ Saved TutorialScence.unity");
    }

    [MenuItem("GameCard/Add Tutorial Skip Button")]
    public static void Run()
    {
        TutorialManager tm = Object.FindObjectOfType<TutorialManager>(true);
        if (tm == null)
        {
            Debug.LogError("[AddTutorialSkipButton] ไม่พบ TutorialManager ในซีนที่เปิดอยู่ — เปิด TutorialScence.unity ก่อนรันคำสั่งนี้");
            return;
        }

        GameObject mainCanvasObj = GameObject.Find("MainCanvas");
        if (mainCanvasObj == null)
        {
            Debug.LogError("[AddTutorialSkipButton] ไม่พบ MainCanvas ในซีน");
            return;
        }

        // ป้องกันสร้างซ้ำถ้ารันคำสั่งนี้อีกครั้ง
        Transform existing = mainCanvasObj.transform.Find(ButtonName);
        GameObject buttonGo = existing != null ? existing.gameObject : null;

        if (buttonGo == null)
        {
            buttonGo = new GameObject(ButtonName, typeof(RectTransform));
            buttonGo.transform.SetParent(mainCanvasObj.transform, false);

            RectTransform rt = buttonGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-30f, -30f);
            rt.sizeDelta = new Vector2(160f, 70f);

            Image img = buttonGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.6f);

            buttonGo.AddComponent<Button>();

            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(buttonGo.transform, false);
            RectTransform labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "ข้าม";
            label.fontSize = 32;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/FC Quantum [Non-commercial] SDF.asset");
            if (font != null) label.font = font;
        }

        // ตัว Skip ต้องอยู่เหนือหน้ากาก Tutorial (sortingOrder 32000) เสมอ ไม่งั้นจะถูกหน้ากากบังจนกดไม่ได้
        Canvas canvas = buttonGo.GetComponent<Canvas>();
        if (canvas == null) canvas = buttonGo.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32001;

        if (buttonGo.GetComponent<GraphicRaycaster>() == null)
            buttonGo.AddComponent<GraphicRaycaster>();

        buttonGo.transform.SetAsLastSibling();

        Button btn = buttonGo.GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, tm.RequestSkip);

        EditorUtility.SetDirty(buttonGo);
        EditorSceneManager.MarkSceneDirty(buttonGo.scene);

        Debug.Log("[AddTutorialSkipButton] ✅ สร้าง/ผูกปุ่ม Skip → TutorialManager.RequestSkip() สำเร็จ อย่าลืม Save Scene");
    }
}
