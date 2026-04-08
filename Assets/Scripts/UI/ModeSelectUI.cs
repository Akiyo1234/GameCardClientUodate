using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

public class ModeSelectUI : MonoBehaviour
{
    [Header("---- UI Panels ----")]
    
    public GameObject mainMenuPanel;   // [NEW] หน้าเมนูหลักแรกสุด (Play/Store/Leaderboard)
    public GameObject modeSelectPanel; // หน้าเลือกโหมด (Bot/Room)
    public GameObject lobbyPanel;      // หน้า Lobby (multiplayer)

    [Header("---- Player Stats UI ----")]
    public TextMeshProUGUI totalCoinsText;
    public TextMeshProUGUI totalPointsText;

    [Header("---- Character Selection ----")]
    public CharacterData[] availableCharacters;
    public Image characterPreviewImage;
    public TextMeshProUGUI characterNameText;
    private int currentCharacterIndex = 0;

    // ฟังก์ชันเริ่มเกม (Initialization)
    private void Start()
    {
        // เริ่มต้น: เปิดแค่หน้า Main Menu และปิดหน้าอื่นทั้งหมด
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);

        // ดึงสถิติผู้เล่นมาแสดง (ถ้าไม่มีจะใช้ค่า 0)
        int coins = PlayerPrefs.GetInt("TotalCoins", 0);
        int points = PlayerPrefs.GetInt("TotalPoints", 0);

        if (totalCoinsText != null) totalCoinsText.text = coins.ToString();
        if (totalPointsText != null) totalPointsText.text = points.ToString();

        // โหลดตัวละครล่าสุดที่เลือกไว้
        currentCharacterIndex = PlayerPrefs.GetInt("SelectedCharacter", 0);
        UpdateCharacterPreview();
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

    // --- ฟังก์ชันย่อยสำหรับหน้าเลือกตัวละคร ---
    
    public void OnClickNextCharacter()
    {
        if (availableCharacters == null || availableCharacters.Length == 0) return;
        
        currentCharacterIndex++;
        if (currentCharacterIndex >= availableCharacters.Length) currentCharacterIndex = 0;
        
        UpdateCharacterPreview();
        
        // บันทึกตัวละครที่เลือกลงระบบ
        PlayerPrefs.SetInt("SelectedCharacter", currentCharacterIndex);
        PlayerPrefs.Save();
    }

    public void OnClickPrevCharacter()
    {
        if (availableCharacters == null || availableCharacters.Length == 0) return;
        
        currentCharacterIndex--;
        if (currentCharacterIndex < 0) currentCharacterIndex = availableCharacters.Length - 1;
        
        UpdateCharacterPreview();
        
        // บันทึกตัวละครที่เลือกลงระบบ
        PlayerPrefs.SetInt("SelectedCharacter", currentCharacterIndex);
        PlayerPrefs.Save();
    }

    private void UpdateCharacterPreview()
    {
        if (availableCharacters == null || availableCharacters.Length == 0) return;
        
        // ป้องกันค่าบั๊ก
        if (currentCharacterIndex < 0 || currentCharacterIndex >= availableCharacters.Length) currentCharacterIndex = 0;

        CharacterData charData = availableCharacters[currentCharacterIndex];
        
        if (characterPreviewImage != null) characterPreviewImage.sprite = charData.portraitSprite;
        if (characterNameText != null) characterNameText.text = charData.characterName;
    }
}
