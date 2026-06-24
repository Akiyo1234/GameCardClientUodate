using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TutorialUIMask : MonoBehaviour
{
    [Header("Mask Panels (Black with Alpha 0.75)")]
    public RectTransform topPanel;
    public RectTransform bottomPanel;
    public RectTransform leftPanel;
    public RectTransform rightPanel;

    [Header("Dialogue UI")]
    public RectTransform dialogueBox;
    public TextMeshProUGUI dialogueText;
    
    private RectTransform canvasRect;
    private Coroutine transitionCoroutine;

    private void Awake()
    {
        canvasRect = GetComponent<RectTransform>();

        // สำคัญมาก: ย้ายตัวเองไปเป็นลูกของ MainCanvas (ตัวนอกสุด) อัตโนมัติเพื่อไม่ให้โดนจำกัดขนาดโดยหน้าต่างย่อย
        GameObject mainCanvasObj = GameObject.Find("MainCanvas");
        if (mainCanvasObj != null && transform.parent != mainCanvasObj.transform)
        {
            transform.SetParent(mainCanvasObj.transform, false);
            transform.SetAsLastSibling(); // ให้อยู่บนสุดเสมอ
        }

        Canvas myCanvas = gameObject.GetComponent<Canvas>();
        if (myCanvas == null) myCanvas = gameObject.AddComponent<Canvas>();
        myCanvas.overrideSorting = true;
        myCanvas.sortingOrder = 32000;
        
        GraphicRaycaster gr = gameObject.GetComponent<GraphicRaycaster>();
        if (gr == null) gameObject.AddComponent<GraphicRaycaster>();

        // สำคัญมาก: ป้องกันไม่ให้ตัว Root ของ Mask ไปบังการคลิก
        Image rootImg = GetComponent<Image>();
        if (rootImg != null) {
            rootImg.raycastTarget = false;
        }

        // ป้องกันกล่องข้อความทับปุ่มและบังการคลิก
        if (dialogueBox != null)
        {
            Image diagImg = dialogueBox.GetComponent<Image>();
            if (diagImg != null) diagImg.raycastTarget = false;
            
            // บังคับให้ Anchor ของกล่องข้อความเป็น Center เพื่อไม่ให้เวลาย้ายตำแหน่งแล้วกล่องเบี้ยวหรือขยายเป็นหน้ากากสีขาว
            dialogueBox.anchorMin = new Vector2(0.5f, 0.5f);
            dialogueBox.anchorMax = new Vector2(0.5f, 0.5f);
            dialogueBox.pivot = new Vector2(0.5f, 0.5f);
        }
        if (dialogueText != null)
        {
            dialogueText.raycastTarget = false;
        }

        // บังคับให้ตัว Mask เองขยายเต็มจอ (Stretch to parent / screen)
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;
        canvasRect.localScale = Vector3.one;
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        
        // บังคับให้ Panel ทั้ง 4 เป็น Anchor แบบ Center และมี Pivot ตรงกลางเสมอ
        // เพื่อให้การคำนวณตำแหน่งผ่านโค้ดทำงานได้อย่างถูกต้อง 100% 
        // ไม่ว่าผู้ใช้จะลากสร้างภาพแบบไหนมาก็ตาม
        SetupPanel(topPanel);
        SetupPanel(bottomPanel);
        SetupPanel(leftPanel);
        SetupPanel(rightPanel);

        // ปิดตัวเองไว้ก่อน รอให้ TutorialManager สั่งเปิด
        gameObject.SetActive(false);
    }

    private void SetupPanel(RectTransform panel)
    {
        if (panel == null) return;
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        
        // บังคับสีเป็นดำโปร่งใส 75% และเอาขอบขาว/กรอบแปลกๆ ออก
        Image img = panel.GetComponent<Image>();
        if (img != null) {
            img.sprite = null;
            img.color = new Color(0, 0, 0, 0.75f);
            img.raycastTarget = true; // เปิดบล็อกการคลิกทะลุ
        }
    }

    // ฟังก์ชันเจาะรูหน้าจอที่เป้าหมาย
    public void FocusOn(RectTransform target, string message)
    {
        gameObject.SetActive(true);
        if (dialogueText != null) dialogueText.text = message;

        Vector2 targetTopPos, targetTopSize;
        Vector2 targetBotPos, targetBotSize;
        Vector2 targetLeftPos, targetLeftSize;
        Vector2 targetRightPos, targetRightSize;
        Vector2 targetDiagPos;

        float canvasW = canvasRect.rect.width;
        float canvasH = canvasRect.rect.height;
        float canvasLeft = -canvasW / 2;
        float canvasRight = canvasW / 2;
        float canvasBottom = -canvasH / 2;
        float canvasTop = canvasH / 2;

        if (target == null)
        {
            // ปิดรูมืดหมด -> เอา Top Panel ขยายเต็มจอ
            targetTopSize = new Vector2(canvasW, canvasH);
            targetTopPos = new Vector2(0, 0);

            targetBotSize = Vector2.zero;
            targetBotPos = Vector2.zero;
            targetLeftSize = Vector2.zero;
            targetLeftPos = Vector2.zero;
            targetRightSize = Vector2.zero;
            targetRightPos = Vector2.zero;

            targetDiagPos = Vector2.zero; // กลางจอ
        }
        else
        {
            // คำนวณตำแหน่งและขนาดของ target บน Canvas
            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);

            // แปลงพิกัด World ของเป้าหมายให้อยู่ใน Local Space ของหน้ากากโดยตรง (แก้ปัญหา Camera/Screen Space)
            Vector2 localBL = canvasRect.InverseTransformPoint(corners[0]);
            Vector2 localTR = canvasRect.InverseTransformPoint(corners[2]);

            float holeX = localBL.x;
            float holeY = localBL.y;
            float holeWidth = localTR.x - localBL.x;
            float holeHeight = localTR.y - localBL.y;

            // ขยายรูเผื่อขอบนิดหน่อย
            float padding = 10f;
            float hLeft = holeX - padding;
            float hRight = holeX + holeWidth + padding;
            float hBot = holeY - padding;
            float hTop = holeY + holeHeight + padding;

            // Top panel
            float topH = canvasTop - hTop;
            targetTopSize = new Vector2(canvasW, topH > 0 ? topH : 0);
            targetTopPos = new Vector2(0, hTop + (targetTopSize.y / 2));

            // Bottom panel
            float botH = hBot - canvasBottom;
            targetBotSize = new Vector2(canvasW, botH > 0 ? botH : 0);
            targetBotPos = new Vector2(0, canvasBottom + (targetBotSize.y / 2));

            // Left panel
            float leftW = hLeft - canvasLeft;
            targetLeftSize = new Vector2(leftW > 0 ? leftW : 0, hTop - hBot);
            targetLeftPos = new Vector2(canvasLeft + (targetLeftSize.x / 2), (hTop + hBot) / 2);

            // Right panel
            float rightW = canvasRight - hRight;
            targetRightSize = new Vector2(rightW > 0 ? rightW : 0, hTop - hBot);
            targetRightPos = new Vector2(hRight + (targetRightSize.x / 2), (hTop + hBot) / 2);

            targetDiagPos = new Vector2(0, hTop + 80f);
            if (targetDiagPos.y + 100f > canvasTop) {
                targetDiagPos = new Vector2(0, hBot - 80f);
            }
        }

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }
        transitionCoroutine = StartCoroutine(AnimateMask(targetTopPos, targetTopSize, targetBotPos, targetBotSize, targetLeftPos, targetLeftSize, targetRightPos, targetRightSize, targetDiagPos, 0.3f));
    }

    private IEnumerator AnimateMask(
        Vector2 topPos, Vector2 topSize,
        Vector2 botPos, Vector2 botSize,
        Vector2 leftPos, Vector2 leftSize,
        Vector2 rightPos, Vector2 rightSize,
        Vector2 diagPos, float duration)
    {
        Vector2 startTopPos = topPanel != null ? topPanel.anchoredPosition : Vector2.zero;
        Vector2 startTopSize = topPanel != null ? topPanel.sizeDelta : Vector2.zero;
        
        Vector2 startBotPos = bottomPanel != null ? bottomPanel.anchoredPosition : Vector2.zero;
        Vector2 startBotSize = bottomPanel != null ? bottomPanel.sizeDelta : Vector2.zero;

        Vector2 startLeftPos = leftPanel != null ? leftPanel.anchoredPosition : Vector2.zero;
        Vector2 startLeftSize = leftPanel != null ? leftPanel.sizeDelta : Vector2.zero;

        Vector2 startRightPos = rightPanel != null ? rightPanel.anchoredPosition : Vector2.zero;
        Vector2 startRightSize = rightPanel != null ? rightPanel.sizeDelta : Vector2.zero;

        Vector2 startDiagPos = dialogueBox != null ? dialogueBox.anchoredPosition : Vector2.zero;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t); // smoothstep

            if (topPanel != null)
            {
                topPanel.anchoredPosition = Vector2.Lerp(startTopPos, topPos, t);
                topPanel.sizeDelta = Vector2.Lerp(startTopSize, topSize, t);
            }
            if (bottomPanel != null)
            {
                bottomPanel.anchoredPosition = Vector2.Lerp(startBotPos, botPos, t);
                bottomPanel.sizeDelta = Vector2.Lerp(startBotSize, botSize, t);
            }
            if (leftPanel != null)
            {
                leftPanel.anchoredPosition = Vector2.Lerp(startLeftPos, leftPos, t);
                leftPanel.sizeDelta = Vector2.Lerp(startLeftSize, leftSize, t);
            }
            if (rightPanel != null)
            {
                rightPanel.anchoredPosition = Vector2.Lerp(startRightPos, rightPos, t);
                rightPanel.sizeDelta = Vector2.Lerp(startRightSize, rightSize, t);
            }
            if (dialogueBox != null)
            {
                dialogueBox.anchoredPosition = Vector2.Lerp(startDiagPos, diagPos, t);
            }
            yield return null;
        }

        // Set final values
        if (topPanel != null) { topPanel.anchoredPosition = topPos; topPanel.sizeDelta = topSize; }
        if (bottomPanel != null) { bottomPanel.anchoredPosition = botPos; bottomPanel.sizeDelta = botSize; }
        if (leftPanel != null) { leftPanel.anchoredPosition = leftPos; leftPanel.sizeDelta = leftSize; }
        if (rightPanel != null) { rightPanel.anchoredPosition = rightPos; rightPanel.sizeDelta = rightSize; }
        if (dialogueBox != null) { dialogueBox.anchoredPosition = diagPos; }
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
