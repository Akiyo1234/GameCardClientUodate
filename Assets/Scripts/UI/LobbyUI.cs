using UnityEngine;
using TMPro;
using Fusion;

public class LobbyUI : MonoBehaviour
{
    public static LobbyUI Instance { get; private set; }

    [Header("---- View Groups ----")]
    public GameObject selectionView; // กลุ่ม UI กรอกชื่อห้อง
    public GameObject roomInfoView;  // กลุ่ม UI ข้อมูลในห้อง

    [Header("---- UI Elements ----")]
    public TMP_InputField roomNameInputField;
    public TMP_Text roomNameText;    // แสดงชื่อห้องที่เข้าอยู่
    public TMP_Text playerListText;  // แสดงรายชื่อคนในห้อง
    public GameObject startButton;   // ปุ่มเริ่มเกม (เฉพาะ Host)

    [Header("---- Panels ----")]
    public GameObject lobbyPanel;
    public GameObject modeSelectPanel; 

    private void Awake()
    { 
        Instance = this; 
    }

    private void Start()
    {
        // เริ่มต้นด้วยหน้าเลือกห้องเสมอ
        SetViewState(false);
    }

    // ปุ่มสร้างห้อง
    public void OnClickCreateRoom()
    {
        string rName = roomNameInputField.text;
        if (string.IsNullOrEmpty(rName)) rName = "Room_" + Random.Range(100, 999);
        
        Debug.Log($"[Lobby] กำลังสร้างห้อง: {rName}");
        FusionManager.Instance.StartGame(GameMode.Host, rName);
    }

    // ปุ่มเข้าห้อง
    public void OnClickJoinRoom()
    {
        string rName = roomNameInputField.text;
        if (string.IsNullOrEmpty(rName)) {
            Debug.LogWarning("[Lobby] กรุณาใส่ชื่อห้องที่ต้องการเข้าร่วม");
            return;
        }

        Debug.Log($"[Lobby] กำลังเข้าร่วมห้อง: {rName}");
        FusionManager.Instance.StartGame(GameMode.Client, rName);
    }

    // --- ระบบสลับหน้าจอ (UI States) ---

    public void SetViewState(bool isInRoom)
    {
        if (selectionView != null) selectionView.SetActive(!isInRoom);
        if (roomInfoView != null) roomInfoView.SetActive(isInRoom);

        if (isInRoom)
        {
            if (roomNameText != null) roomNameText.text = "Room: " + roomNameInputField.text;
            
            // ตรวจสอบว่าเป็น Host หรือไม่ เพื่อโชว์ปุ่ม Start
            if (startButton != null) 
                startButton.SetActive(FusionManager.Instance.IsMasterClient);
        }
    }

    public void UpdatePlayerList(string playerListTextContent)
    {
        if (playerListText != null) playerListText.text = playerListTextContent;
    }

    // ปุ่มออกจากการเชื่อมต่อ / ออกจากหน้าห้อง
    public void OnClickLeaveRoom()
    {
        Debug.Log("[Lobby] ออกจากห้องตัวเอง/การเชื่อมต่อ");
        FusionManager.Instance.Disconnect();
        SetViewState(false);
    }

    public void OnClickStartGame()
    {
        Debug.Log("[Lobby] สั่งเริ่มเกม!");
        // TODO: สั่งโหลด Scene หรือเริ่ม Logic เกมในขั้นตอนถัดไป
    }

    // ปุ่มกลับ
    public void OnClickBack()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }
}
