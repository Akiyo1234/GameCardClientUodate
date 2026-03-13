using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerUI : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI scoreText;
    public Image panelBackground;

    [Header("Resources & Score")]
    public int currentScore = 0; // เพิ่มตัวแปรเก็บคะแนนรวม
    public int[] coins = new int[6]; 
    public TextMeshProUGUI[] coinTexts; 

    [Header("Timer UI")]
    public Image timerBarFill; 

    public void SetupPlayer(string newName)
    {
        nameText.text = newName;
        currentScore = 0; // เริ่มเกมมาแต้มเป็น 0
        UpdateUI();
        if (scoreText != null) scoreText.text = "0";
    }

    // เปลี่ยนให้เป็นการ "บวกแต้มเพิ่ม" ไม่ใช่แค่เขียนทับ
    public void AddScore(int points) 
    {
        currentScore += points;
        if (scoreText != null) scoreText.text = currentScore.ToString();
    }

    public void ReceiveCoins(int[] pickedCoins)
    {
        for (int i = 0; i < 6; i++) coins[i] += pickedCoins[i];
        UpdateUI();
    }

    public void UpdateUI()
    {
        for (int i = 0; i < coinTexts.Length; i++)
        {
            if (coinTexts[i] != null) coinTexts[i].text = coins[i].ToString();
        }
    }

    public void SetActiveTurn(bool isActive)
    {
        panelBackground.color = isActive ? new Color(1, 0.9f, 0.4f, 1) : new Color(0.2f, 0.2f, 0.2f, 0.8f);
        if (!isActive && timerBarFill != null) timerBarFill.fillAmount = 0;
    }

    public void UpdateTimerBar(float fillAmount)
    {
        if (timerBarFill != null) timerBarFill.fillAmount = fillAmount;
    }
}