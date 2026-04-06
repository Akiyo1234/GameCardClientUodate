using UnityEngine;
using UnityEngine.SceneManagement;

public class ModeSelectUI : MonoBehaviour
{
    [Header("---- UI Panels ----")]
    
    public GameObject mainMenuPanel;   // [NEW] หน้าเมนูหลักแรกสุด (Play/Store/Leaderboard)
    public GameObject modeSelectPanel; // หน้าเลือกโหมด (Bot/Room)
    public GameObject lobbyPanel;      // หน้า Lobby (multiplayer)

    // ฟังก์ชันเริ่มเกม (Initialization)
    private void Start()
    {
        // เริ่มต้น: เปิดแค่หน้า Main Menu และปิดหน้าอื่นทั้งหมด
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }

    // ฟังก์ชันกดปุ่ม Play Game จากหน้าหลัก
    public void OnClickMainMenuPlay()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    // ฟังก์ชันย้อนกลับไปหน้าหลัก
    public void OnClickBackToMain()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    // ฟังก์ชันเปิดหน้าต่าง
    public void OpenPanel()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    // ฟังก์ชันปิดหน้าต่าง
    public void ClosePanel()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
    }

    // --- ฟังก์ชันกดปุ่มเลือกโหมด ---

    public void OnClickBotMode()
    {
        Debug.Log("[Mode] เลือกโหมด: เล่นกับบอท (Single Player)");
        SceneManager.LoadScene("SampleScene"); 
    }

    public void OnClickRoomMode()
    {
        Debug.Log("[Mode] เลือกโหมด: สร้างห้อง/เล่นกับเพื่อน");
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }


    public void OnClickAutoMatch()
    {
        Debug.Log("[Mode] เลือกโหมด: ค้นหาห้องอัตโนมัติ");
        // เดี๋ยวเราจะทำหน้านี้ในขั้นตอนถัดไปครับ!
    }
}
