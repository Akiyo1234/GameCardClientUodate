using UnityEngine;
using UnityEngine.UI;

// ============================================================
// CardPreviewPopup — แสดงการ์ด (โดยเฉพาะการ์ดที่จองไว้ซึ่งถูกย่อเล็ก)
// แบบใหญ่เต็มตา พร้อมปุ่ม "ซื้อ" และ "ปิด"
//
// วิธีผูกใน Inspector:
//   • panel            : root GameObject ของ popup (จะถูกเปิด/ปิด)
//   • cardImage        : Image ใหญ่กลางจอสำหรับโชว์รูปการ์ด
//   • buyButton        : ปุ่มซื้อการ์ดที่กำลังดูอยู่
//   • closeButton      : ปุ่มปิด popup
//   • backgroundButton : (optional) ปุ่มคลุมพื้นหลังมืด กดเพื่อปิด
// แล้วลาก component นี้ไปใส่ GameController.cardPreviewPopup
// ============================================================
public class CardPreviewPopup : MonoBehaviour
{
    [Header("UI References (ผูกใน Inspector)")]
    [Tooltip("Root ของ popup — สคริปต์จะเปิด/ปิดอันนี้")]
    public GameObject panel;
    [Tooltip("Image ใหญ่สำหรับโชว์รูปการ์ด")]
    public Image cardImage;
    [Tooltip("ปุ่มซื้อการ์ดที่กำลังดูอยู่")]
    public Button buyButton;
    [Tooltip("ปุ่มปิด popup")]
    public Button closeButton;
    [Tooltip("(optional) ปุ่มคลุมพื้นหลังมืด กดเพื่อปิด")]
    public Button backgroundButton;

    private GameController gameController;
    private CardDisplay currentCard;

    void Awake()
    {
        if (gameController == null) gameController = FindFirstObjectByType<GameController>();

        if (buyButton != null) buyButton.onClick.AddListener(OnBuyClicked);
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (backgroundButton != null) backgroundButton.onClick.AddListener(Hide);

        Hide(); // เริ่มมาให้ซ่อนไว้ก่อน
    }

    /// <summary>เปิด popup แสดงการ์ดใบที่ส่งมาแบบใหญ่</summary>
    public void Show(CardDisplay card)
    {
        if (card == null || card.data == null) return;
        currentCard = card;

        // ใช้ sprite เดียวกับที่การ์ดโหลดไว้แล้ว (ไม่ต้องโหลดจาก Resources ซ้ำ)
        if (cardImage != null)
        {
            Sprite src = card.cardImage != null ? card.cardImage.sprite : null;
            cardImage.sprite = src;
            cardImage.preserveAspect = true;
            cardImage.enabled = src != null;
        }

        if (panel != null) panel.SetActive(true);
    }

    /// <summary>ปิด popup</summary>
    public void Hide()
    {
        currentCard = null;
        if (panel != null) panel.SetActive(false);
    }

    private void OnBuyClicked()
    {
        if (gameController == null) gameController = FindFirstObjectByType<GameController>();

        CardDisplay card = currentCard;
        Hide(); // ปิดก่อน เพราะการซื้อสำเร็จจะ Destroy การ์ด

        // BuyReservedCard จัดการเช็คเทิร์น/เจ้าของ/เหรียญ และแสดง warning เองอยู่แล้ว
        if (gameController != null && card != null && card.data != null)
        {
            gameController.BuyReservedCard(card);
        }
    }
}
