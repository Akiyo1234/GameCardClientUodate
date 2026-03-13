using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro; // สำคัญมาก ต้องมีเพื่อใช้ TextMeshPro

public class ResourceButton : MonoBehaviour, IPointerClickHandler
{
    public Image iconImage;
    public TextMeshProUGUI pendingAmountText; // ช่องสำหรับใส่ตัวหนังสือ x1, x2
    public string resourceType;
    
    private GameController gameController;

    void Awake() 
    {
        gameController = FindFirstObjectByType<GameController>();
    }

    public void Setup(string type)
    {
        resourceType = type;
        
        // โหลดรูปภาพเหรียญตามชื่อ (คุณตั้งชื่อไฟล์เหรียญไว้ในโฟลเดอร์ Resources/Tokens/ ถูกต้องไหมครับ)
        Sprite s = Resources.Load<Sprite>("Tokens/" + type);
        if (s != null && iconImage != null) {
            iconImage.sprite = s;
        }

        UpdatePendingUI(0); // เริ่มเกมมาให้ตัวเลขเป็น 0 (ซ่อนไว้)
    }

    // ฟังก์ชันนี้ GameController จะเป็นคนเรียกใช้เพื่อสั่งเปลี่ยนตัวเลข
    public void UpdatePendingUI(int amount)
    {
        if (pendingAmountText != null)
        {
            // ถ้า amount มากกว่า 0 ให้โชว์ข้อความ เช่น "x1" ถ้าเป็น 0 ให้เป็นค่าว่าง "" (ซ่อนไว้)
            pendingAmountText.text = amount > 0 ? "x" + amount.ToString() : ""; 
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (gameController != null) {
            // ส่ง "ตัวมันเอง" (this) ไปให้ GameController ตรวจสอบกฎการหยิบ
            gameController.OnResourceClicked(this); 
        }
    }
}