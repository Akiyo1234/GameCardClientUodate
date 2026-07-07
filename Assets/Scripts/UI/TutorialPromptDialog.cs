using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
// TutorialPromptDialog — กล่องถาม "เล่นบทฝึกสอนก่อนไหม?" หลังล็อกอิน
// สร้าง UI ทั้งหมดด้วยโค้ด (ไม่ต้องผูก prefab/Inspector) โดยยืม TMP font จากข้อความที่มีอยู่ในซีน
// เรียกใช้:  TutorialPromptDialog.Show(onPlay, onSkip);
// ============================================================
public static class TutorialPromptDialog
{
    public static void Show(Action onPlay, Action onSkip)
    {
        EnsureEventSystem();

        // เลือกฟอนต์ที่มี glyph ไทยจริง (มีตัว "ก") — แนวเดียวกับ ReconnectManager.ResolveThaiFont
        TMP_FontAsset font = ResolveThaiFont();

        // ── Canvas overlay อยู่บนสุด ──
        var root = new GameObject("__TutorialPrompt__",
            typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasScaler));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // ── พื้นหลังมืดคลุมจอ (ดักคลิกไม่ให้ทะลุไปเมนู) ──
        var dim = CreateChild(root.transform, "Dim", new Color(0, 0, 0, 0.72f));
        Stretch(dim.GetComponent<RectTransform>());
        dim.GetComponent<Image>().raycastTarget = true;

        // ── กล่อง Panel กลางจอ ──
        var panel = CreateChild(dim.transform, "Panel", new Color(0.12f, 0.14f, 0.20f, 1f));
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(900, 460);

        // ── หัวข้อ ──
        CreateText(panel.transform, font, "บทฝึกสอน", 56, FontStyles.Bold,
            new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(820, 80));

        // ── ข้อความ ──
        CreateText(panel.transform, font,
            "ดูเหมือนคุณยังไม่ได้เล่นบทฝึกสอน\nอยากเรียนรู้วิธีเล่นก่อนไหม?",
            36, FontStyles.Normal,
            new Vector2(0.5f, 1f), new Vector2(0, -190), new Vector2(820, 140));

        // ── ปุ่ม "เล่นเลย" ──
        CreateButton(panel.transform, font, "เล่นเลย", new Color(0.20f, 0.62f, 0.30f, 1f),
            new Vector2(-215, 70), () => { Close(root); onPlay?.Invoke(); });

        // ── ปุ่ม "ข้าม" ──
        CreateButton(panel.transform, font, "ข้าม", new Color(0.45f, 0.45f, 0.50f, 1f),
            new Vector2(215, 70), () => { Close(root); onSkip?.Invoke(); });
    }

    private static void Close(GameObject root)
    {
        if (root != null) UnityEngine.Object.Destroy(root);
    }

    // ── helpers ──

    // เลือกฟอนต์จากซีนที่รองรับสระไทย (มี glyph "ก") — ฟอนต์ TMP ตัวแรกที่เจออาจเป็นฟอนต์อังกฤษล้วน
    private static TMP_FontAsset ResolveThaiFont()
    {
        var texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in texts)
            if (t != null && t.font != null && t.font.HasCharacter('ก')) return t.font;
        foreach (var t in texts)
            if (t != null && t.font != null) return t.font;
        return TMP_Settings.defaultFontAsset;
    }

    // ปุ่มคลิกไม่ได้ถ้าซีนไม่มี EventSystem — สร้างให้ถ้ายังไม่มี
    private static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        var es = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
        UnityEngine.Object.DontDestroyOnLoad(es);
    }

    private static GameObject CreateChild(Transform parent, string name, Color bg)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bg;
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void CreateText(Transform parent, TMP_FontAsset font, string msg, float size,
        FontStyles style, Vector2 anchor, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = msg;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
    }

    private static void CreateButton(Transform parent, TMP_FontAsset font, string label, Color color,
        Vector2 anchoredPos, Action onClick)
    {
        var go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(330, 100);

        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = label;
        tmp.fontSize = 38;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        Stretch(textGo.GetComponent<RectTransform>());
    }
}
