using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

// ============================================================
// CardPreviewSetupTool — สร้าง popup ดูการ์ดจองแบบใหญ่ให้อัตโนมัติ
//
// เมนู: Tools/Setup Card Preview Popup
//   • หา Canvas ของเกม + GameController ใน scene ปัจจุบัน
//   • สร้างลำดับชั้น: CardPreviewPopup (active) > CardPreviewPanel (toggled)
//       - Background (ปุ่มมืดคลุมจอ กดเพื่อปิด)
//       - CardImage (รูปการ์ดใหญ่กลางจอ)
//       - BuyButton (ปุ่มซื้อ)
//       - CloseButton (ปุ่มปิด มุมขวาบน)
//   • ผูก reference ทั้งหมดให้ CardPreviewPopup และ GameController.cardPreviewPopup
//
// หมายเหตุ: component วางบน object ที่ active ตลอด ส่วน panel เป็นลูกที่ถูกซ่อน
//   → Awake รันตั้งแต่เริ่ม ผูก listener ปุ่มได้ครบ แม้ panel จะถูกซ่อนอยู่
// ============================================================
public static class CardPreviewSetupTool
{
    [MenuItem("Tools/Setup Card Preview Popup")]
    public static void SetupCardPreviewPopup()
    {
        // กันสร้างซ้ำ
        CardPreviewPopup existing = Object.FindFirstObjectByType<CardPreviewPopup>();
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Card Preview Popup",
                "มี CardPreviewPopup อยู่แล้วใน scene\nต้องการสร้างใหม่ทับไหม? (ของเดิมจะไม่ถูกลบ)",
                "สร้างเพิ่ม", "ยกเลิก"))
            {
                return;
            }
        }

        GameController gameController = Object.FindFirstObjectByType<GameController>();
        if (gameController == null)
        {
            EditorUtility.DisplayDialog("Card Preview Popup",
                "ไม่พบ GameController ใน scene — เปิด scene เกมก่อนแล้วลองใหม่", "OK");
            return;
        }

        // หา Canvas: ใช้ของ confirmReservePanel ถ้ามี, ไม่งั้นหาตัวแรกใน scene
        Canvas canvas = null;
        if (gameController.confirmReservePanel != null)
            canvas = gameController.confirmReservePanel.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Card Preview Popup",
                "ไม่พบ Canvas ใน scene — ต้องมี Canvas ก่อนถึงจะวาง popup ได้", "OK");
            return;
        }

        // ─── Root (active ตลอด) ─────────────────────────────
        GameObject rootObj = new GameObject("CardPreviewPopup");
        rootObj.transform.SetParent(canvas.transform, false);
        CardPreviewPopup popup = rootObj.AddComponent<CardPreviewPopup>();
        StretchFull(rootObj.AddComponent<RectTransform>());

        // ─── Panel (ตัวที่ถูกเปิด/ปิด) ───────────────────────
        GameObject panelObj = new GameObject("CardPreviewPanel");
        panelObj.transform.SetParent(rootObj.transform, false);
        RectTransform panelRect = panelObj.AddComponent<RectTransform>();
        StretchFull(panelRect);

        // Background มืด + เป็นปุ่มกดปิด
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(panelObj.transform, false);
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.75f);
        Button bgBtn = bgObj.AddComponent<Button>();
        StretchFull(bgObj.GetComponent<RectTransform>());

        // รูปการ์ดใหญ่กลางจอ
        GameObject cardObj = new GameObject("CardImage");
        cardObj.transform.SetParent(panelObj.transform, false);
        Image cardImg = cardObj.AddComponent<Image>();
        cardImg.preserveAspect = true;
        cardImg.raycastTarget = false; // กดทะลุไปโดน Background เพื่อปิดได้
        RectTransform cardRect = cardObj.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(560, 780); // อัตราส่วนการ์ดทั่วไป
        cardRect.anchoredPosition = new Vector2(0, 40);

        // ปุ่มซื้อ (ล่างกลาง)
        Button buyBtn = CreateButton(panelObj.transform, "BuyButton", "ซื้อ",
            new Color(0.16f, 0.7f, 0.24f, 1f),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0, 70), new Vector2(280, 90));

        // ปุ่มปิด (มุมขวาบน)
        Button closeBtn = CreateButton(panelObj.transform, "CloseButton", "X",
            new Color(0.8f, 0.2f, 0.2f, 1f),
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-60, -60), new Vector2(80, 80));

        // ─── ผูก reference ───────────────────────────────────
        popup.panel = panelObj;
        popup.cardImage = cardImg;
        popup.buyButton = buyBtn;
        popup.closeButton = closeBtn;
        popup.backgroundButton = bgBtn;
        gameController.cardPreviewPopup = popup;

        // ปิด panel ไว้ก่อน (component อยู่บน root ที่ active → Awake ยังรันปกติ)
        panelObj.SetActive(false);

        // mark dirty + save
        EditorUtility.SetDirty(gameController);
        EditorUtility.SetDirty(popup);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Selection.activeGameObject = rootObj;
        Debug.Log("[CardPreviewSetupTool] สร้าง Card Preview Popup และผูกกับ GameController เรียบร้อย! " +
                  "(อย่าลืม Save Scene / Apply Prefab ถ้า Canvas เป็น prefab)");
    }

    // ─── helpers ─────────────────────────────────────────────
    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Button CreateButton(Transform parent, string name, string label, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        Image img = btnObj.AddComponent<Image>();
        img.color = color;
        Button btn = btnObj.AddComponent<Button>();

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        GameObject txtObj = new GameObject("Text");
        txtObj.transform.SetParent(btnObj.transform, false);
        TextMeshProUGUI txt = txtObj.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 40;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        StretchFull(txtObj.GetComponent<RectTransform>());

        return btn;
    }
}
