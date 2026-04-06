using UnityEngine;
using TMPro;
using System.Threading.Tasks;
using UnityEngine.SceneManagement; // เพิ่มบรรทัดนี้เพื่อใช้คำสั่งเปลี่ยน Scene

public class AuthManagerUI : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject loginPanel;
    public GameObject registerPanel;

    [Header("Input Fields")]
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;
    // เราจะไม่ใช้ confirmPasswordInput และ usernameInput ใน Unity UI เนื่องจากการสมัครทำบนเว็บ
    // แต่ยังคงเก็บไว้เพื่อความเข้ากันได้ของโค้ดเก่า (ไม่ใช้ใน UI)
    public TMP_InputField confirmPasswordInput; // [KEEP] ไม่ได้ใช้
    public TMP_InputField usernameInput;       // [KEEP] ไม่ได้ใช้

    [Header("Status Text")]
    public TextMeshProUGUI statusText;

    [Header("การจัดการหน้าจอ (Scene)")]
    // ให้กรอกชื่อหน้าจอหลักใน Inspector (เช่น "MainMenu 1" หรือ "SampleScene")
    public string nextSceneName = "MainMenu 1"; 
    // URL ของหน้า Register บนเว็บ (สามารถเปลี่ยนเป็น URL ภายนอกได้)
    public string registerUrl = "file:///D:/ProjectGameCard/GameCardClient/Assets/Scripts/index.html"; // ปรับตามที่ต้องการ

    private void Start()
    {
        Debug.Log("<color=lime>[AuthManagerUI] Start - ระบบ Login แยก Scene พร้อมทำงาน!</color>");
        ShowLoginPanel(); // เริ่มต้นที่หน้า Login
    }

    public void ShowLoginPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(true);
        // ปิด panel การสมัคร (เราจะใช้เว็บสำหรับ Register)
        if (registerPanel != null) registerPanel.SetActive(false);
        if (statusText != null) {
            statusText.text = "กรุณาเข้าสู่ระบบ";
            statusText.color = Color.white;
        }
    }

    public void ShowRegisterPanel()
    {
        // ไม่ใช้ panel Register ภายใน Unity อีกต่อไป
        if (loginPanel != null) loginPanel.SetActive(false);
        if (registerPanel != null) registerPanel.SetActive(false);
        if (statusText != null) {
            statusText.text = "กรอกข้อมูลเพื่อสมัครสมาชิก (เว็บ)";
            statusText.color = Color.white;
        }
    }

    // ผูกกับปุ่ม Login ใน Inspector
    public void OnLoginButtonClicked()
    {
        Debug.Log($"<color=yellow>[AuthManagerUI] ปุ่ม Login ทำงานแล้ว! Email='{(emailInput != null ? emailInput.text : "NULL")}'</color>");

        if (emailInput == null || passwordInput == null || statusText == null)
        {
            Debug.LogError("[AuthManagerUI] UI fields ไม่ได้เชื่อมใน Inspector!");
            return;
        }

        if (string.IsNullOrEmpty(emailInput.text) || string.IsNullOrEmpty(passwordInput.text))
        {
            statusText.text = "กรุณากรอกข้อมูลให้ครบถ้วน";
            statusText.color = Color.red;
            return;
        }

        statusText.text = "กำลังตรวจสอบ...";
        statusText.color = Color.yellow;
        
        _ = LoginAsync(emailInput.text, passwordInput.text);
    }

    // ปุ่มข้ามล็อกอิน สำหรับเทสตอนกำลังสร้างเกมรัวๆ
    public void OnSkipLoginClicked()
    {
        Debug.Log($"<color=cyan>[AuthManagerUI] ข้ามล็อกอิน - โหลดเข้า Scene : {nextSceneName} โดยตรง!</color>");
        SceneManager.LoadScene(nextSceneName);
    }

    private async Task LoginAsync(string email, string password)
    {
        if (SupabaseManager.Instance == null)
        {
            Debug.LogError("[AuthManagerUI] SupabaseManager.Instance เป็น null!");
            statusText.text = "ข้อผิดพลาด: ไม่พบ SupabaseManager";
            statusText.color = Color.red;
            return;
        }

        var (success, errorMsg) = await SupabaseManager.Instance.SignInUser(email, password);
        
        if (success)
        {
            statusText.text = "เข้าสู่ระบบสำเร็จ! กำลังไปที่หน้าหลัก...";
            statusText.color = Color.green;
            
            await Task.Delay(1500); // หน่วงเวลาให้ผู้เล่นอ่านว่า "สำเร็จ!"
            
            // โหลด Scene หน้าถัดไป
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            string reason = "ไม่สามารถเชื่อมต่อได้";
            if (!string.IsNullOrEmpty(errorMsg))
            {
                if (errorMsg.Contains("Invalid login credentials") || errorMsg.Contains("invalid_grant"))
                    reason = "ไม่พบผู้ใช้นี้ หรือรหัสผ่านผิด!";
                else if (errorMsg.Contains("Email not confirmed"))
                    reason = "กรุณายืนยันอีเมลก่อนเข้าสู่ระบบ!";
                else
                    reason = errorMsg;
            }
            statusText.text = "เข้าสู่ระบบไม่สำเร็จ: " + reason;
            statusText.color = Color.red;
            Debug.LogWarning($"[AuthManagerUI] Login failed: {errorMsg}");
        }
    }

    // ผูกกับปุ่ม Register ใน Inspector (จะเปิดหน้าเว็บ Register)
    public void OnRegisterButtonClicked()
    {
        OpenRegisterWebPage();
    }

    // เปิดหน้าเว็บ Register ที่กำหนดใน registerUrl
    public void OpenRegisterWebPage()
    {
        if (!string.IsNullOrEmpty(registerUrl))
        {
            Debug.Log($"[AuthManagerUI] เปิดหน้า Register: {registerUrl}");
            Application.OpenURL(registerUrl);
        }
        else
        {
            Debug.LogWarning("[AuthManagerUI] Register URL ไม่ได้ตั้งค่า!");
        }
    }
}
