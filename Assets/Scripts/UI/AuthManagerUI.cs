using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AuthManagerUI : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject loginPanel;
    public GameObject registerPanel;

    [Header("Input Fields")]
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;
    public TMP_InputField confirmPasswordInput;
    public TMP_InputField usernameInput;

    [Header("Status Text")]
    public TextMeshProUGUI statusText;

    [Header("Scene")]
    public string nextSceneName = "MainMenu 1";

    [Header("Register Page")]
    [Tooltip("Optional override. Leave empty to open the local HTML file from this project.")]
    public string registerUrl = "";

    private void Start()
    {
        Debug.Log("[AuthManagerUI] Login UI ready.");
        ShowLoginPanel();
    }

    public void ShowLoginPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(true);
        if (registerPanel != null) registerPanel.SetActive(false);

        if (statusText != null)
        {
            statusText.text = "กรุณาเข้าสู่ระบบ";
            statusText.color = Color.white;
        }
    }

    public void ShowRegisterPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(false);
        if (registerPanel != null) registerPanel.SetActive(false);

        if (statusText != null)
        {
            statusText.text = "กรอกข้อมูลเพื่อสมัครสมาชิก (เว็บ)";
            statusText.color = Color.white;
        }
    }

    public void OnLoginButtonClicked()
    {
        Debug.Log($"[AuthManagerUI] Login clicked. Email='{(emailInput != null ? emailInput.text : "NULL")}'");

        if (emailInput == null || passwordInput == null || statusText == null)
        {
            Debug.LogError("[AuthManagerUI] UI fields are not assigned in the Inspector.");
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

    public void OnSkipLoginClicked()
    {
        Debug.Log($"[AuthManagerUI] Skip login. Loading scene: {nextSceneName}");
        SceneManager.LoadScene(nextSceneName);
    }

    private async Task LoginAsync(string email, string password)
    {
        if (SupabaseManager.Instance == null)
        {
            Debug.LogError("[AuthManagerUI] SupabaseManager.Instance is null.");
            statusText.text = "ข้อผิดพลาด: ไม่พบ SupabaseManager";
            statusText.color = Color.red;
            return;
        }

        var (success, errorMsg) = await SupabaseManager.Instance.SignInUser(email, password);

        if (success)
        {
            statusText.text = "เข้าสู่ระบบสำเร็จ! กำลังไปที่หน้าหลัก...";
            statusText.color = Color.green;

            await Task.Delay(1500);
            SceneManager.LoadScene(nextSceneName);
            return;
        }

        string reason = "ไม่สามารถเชื่อมต่อได้";
        if (!string.IsNullOrEmpty(errorMsg))
        {
            if (errorMsg.Contains("Invalid login credentials") || errorMsg.Contains("invalid_grant"))
                reason = "ไม่พบผู้ใช้นี้ หรือรหัสผ่านผิด";
            else if (errorMsg.Contains("Email not confirmed"))
                reason = "กรุณายืนยันอีเมลก่อนเข้าสู่ระบบ";
            else
                reason = errorMsg;
        }

        statusText.text = "เข้าสู่ระบบไม่สำเร็จ: " + reason;
        statusText.color = Color.red;
        Debug.LogWarning($"[AuthManagerUI] Login failed: {errorMsg}");
    }

    public void OnRegisterButtonClicked()
    {
        OpenRegisterWebPage();
    }

    public void OpenRegisterWebPage()
    {
        string finalUrl = registerUrl;
        string fallbackHtmlPath = Path.Combine(Application.dataPath, "StreamingAssets", "Web", "index.html");

        if (string.IsNullOrWhiteSpace(finalUrl))
        {
            if (!File.Exists(fallbackHtmlPath))
            {
                Debug.LogError($"[AuthManagerUI] Register HTML not found: {fallbackHtmlPath}");
                return;
            }

            finalUrl = new Uri(fallbackHtmlPath).AbsoluteUri;
        }
        else if (finalUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            string localPath = new Uri(finalUrl).LocalPath;
            if (!File.Exists(localPath))
            {
                Debug.LogWarning($"[AuthManagerUI] Register file URL points to a missing file, falling back to project HTML: {localPath}");

                if (!File.Exists(fallbackHtmlPath))
                {
                    Debug.LogError($"[AuthManagerUI] Register HTML not found: {fallbackHtmlPath}");
                    return;
                }

                finalUrl = new Uri(fallbackHtmlPath).AbsoluteUri;
            }
        }

        Debug.Log($"[AuthManagerUI] Opening register page: {finalUrl}");
        OpenUrl(finalUrl);
    }

    private void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Debug.LogError("[AuthManagerUI] URL is empty.");
            return;
        }

        try
        {
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                string localPath = new Uri(url).LocalPath;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                Process.Start(new ProcessStartInfo
                {
                    FileName = localPath,
                    UseShellExecute = true
                });
                return;
#endif
            }

            Application.OpenURL(url);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AuthManagerUI] Failed to open URL: {ex.Message}");
        }
    }
}
