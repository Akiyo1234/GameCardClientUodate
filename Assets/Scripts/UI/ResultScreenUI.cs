using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System;

public class ResultScreenUI : MonoBehaviour
{
    [Header("---- UI Panels ----")]
    public GameObject mainPanel;
    public TextMeshProUGUI titleText;
    public Transform playerListContainer;
    public GameObject playerRowPrefab;

    [Header("---- Buttons & Timer ----")]
    public Button actionButton;
    public TextMeshProUGUI buttonText;
    public float autoCloseTime = 10f;
    private float countdown;
    private bool isDisplaying = false;
    private bool isGameOver = false;
    private bool isAutoCloseEnabled = false;
    public Action onClosed;

    [Header("---- Effects ----")]
    public ParticleSystem victoryParticles; // ช่องใส่เอฟเฟกต์พลุ

    void Awake()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (actionButton != null) actionButton.onClick.AddListener(OnActionButtonClick);
    }

    void Update()
    {
        if (isDisplaying && isAutoCloseEnabled && countdown > 0)
        {
            countdown -= Time.deltaTime;
            // อัปเดตข้อความที่ปุ่มเพื่อบอกเวลาถอยหลัง
            if (buttonText != null)
            {
                string actionLabel = isGameOver ? "กลับหน้าเมนู" : "เริ่มเกมต่อ";
                buttonText.text = $"{actionLabel} ({Mathf.CeilToInt(countdown)}s)";
            }

            if (countdown <= 0)
            {
                OnActionButtonClick();
            }
        }
    }

    // ฟังก์ชันหลักที่ใช้เรียกโชว์ผลการจัดอันดับ (อัปเดตให้รองรับการจุดพลุ)
    public void ShowResults(string title, List<string> playerRankings, bool gameOverStatus, bool playFireworks = false)
    {
        isGameOver = gameOverStatus;
        isAutoCloseEnabled = gameOverStatus;
        titleText.text = title;
        
        // ลบลิสต์เดิมออกก่อน
        foreach (Transform child in playerListContainer) Destroy(child.gameObject);

        // สร้างแถบชื่อผู้เล่นตามลำดับ
        for (int i = 0; i < playerRankings.Count; i++)
        {
            GameObject row = Instantiate(playerRowPrefab, playerListContainer);
            // สมมติใน Prefab มี TextMeshPro ชื่อ "Text" หรือใช้ GetComponentInChildren ก็ได้
            var rowText = row.GetComponentInChildren<TextMeshProUGUI>();
            if (rowText != null)
            {
                rowText.text = $"อันดับที่ {i + 1}: {playerRankings[i]}";
                // ถ้าเป็นที่ 1 ให้ตัวหนังสือเป็นสีทอง
                if (i == 0) rowText.color = new Color(1f, 0.84f, 0f); 
            }
        }

        countdown = autoCloseTime;
        isDisplaying = true;
        mainPanel.SetActive(true);

        if (buttonText != null)
        {
            string actionLabel = isGameOver ? "กลับหน้าเมนู" : "เริ่มเกมต่อ";
            buttonText.text = isAutoCloseEnabled ? $"{actionLabel} ({Mathf.CeilToInt(countdown)}s)" : actionLabel;
        }

        // จุดพลุฉลองถ้าเป็นจบเกม (isGameOver) หรือถูกสั่งให้จุดเฉพาะกิจ (playFireworks)
        if ((isGameOver || playFireworks) && victoryParticles != null)
        {
            victoryParticles.Play();
        }

        Debug.Log($"[ResultUI] แสดงหน้าต่างสรุปผล: {title} (จุดพลุ: {isGameOver || playFireworks})");
    }

    public void OnActionButtonClick()
    {
        isDisplaying = false;
        mainPanel.SetActive(false);

        if (isGameOver)
        {
            // ถ้าจบเกมแล้วกดปุ่ม ให้เด้งกลับหน้าเมนูพรีเมียม "Mainmenu 1"
            SceneManager.LoadScene("Mainmenu 1"); 
        }
        else
        {
            onClosed?.Invoke();
        }
    }
}
