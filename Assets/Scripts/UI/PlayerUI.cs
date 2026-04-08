using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class PlayerUI : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI scoreText;
    public Image panelBackground;
    public Image characterPortrait; // [NEW] ภาพประจำตัวละคร (ลาก Image จาก UI มาใส่)
    public bool isBot; // ระบุว่าเป็นบอทหรือไม่

    [Header("Resources & Score")]
    public int currentScore = 0; 
    public int[] coins = new int[6]; 
    public TextMeshProUGUI[] coinTexts; 

    [Header("Card Bonuses")]
    public int[] bonuses = new int[5]; 
    public TextMeshProUGUI[] bonusTexts; 

    [Header("Timer UI")]
    public Image timerBarFill; 

    [Header("Reserved Cards (การ์ดที่จองไว้)")]
    public List<CardData> reservedCards = new List<CardData>(); 
    public Transform reservedAreaTransform; // <--- เพิ่มบรรทัดนี้ เพื่อบอกว่าการ์ดจองต้องไปเกิดที่ไหน

    public void SetupPlayer(string newName)
    {
        if (nameText != null) nameText.text = newName;
        currentScore = 0;
        System.Array.Clear(coins, 0, 6);
        System.Array.Clear(bonuses, 0, 5);
        reservedCards.Clear();
        if (reservedAreaTransform != null) {
            foreach (Transform child in reservedAreaTransform) Destroy(child.gameObject);
        }
        UpdateUI();
        if (scoreText != null) scoreText.text = "0";
    }

    public void AddScore(int points) {
        currentScore += points;
        if (scoreText != null) scoreText.text = currentScore.ToString();
    }

    public void AddBonus(int bonusIndex) {
        if (bonusIndex >= 0 && bonusIndex < 5) {
            bonuses[bonusIndex]++; UpdateUI();
        }
    }

    public void ReceiveCoins(int[] pickedCoins) {
        for (int i = 0; i < 6; i++) coins[i] += pickedCoins[i];
        UpdateUI();
    }

    public void UpdateUI() {
        for (int i = 0; i < coinTexts.Length; i++) if (coinTexts[i] != null) coinTexts[i].text = coins[i].ToString();
        for (int i = 0; i < bonusTexts.Length; i++) if (bonusTexts[i] != null) bonusTexts[i].text = bonuses[i].ToString();
    }

    public void SetActiveTurn(bool isActive) {
        // null-safe: ถ้าไม่ได้ผูก panelBackground ไว้ก็ไม่ crash
        if (panelBackground != null)
            panelBackground.color = isActive ? new Color(1, 0.9f, 0.4f, 1) : new Color(0.2f, 0.2f, 0.2f, 0.8f);
        if (!isActive && timerBarFill != null) timerBarFill.fillAmount = 0;
    }

    public void UpdateTimerBar(float fillAmount) {
        if (timerBarFill != null) timerBarFill.fillAmount = fillAmount;
    }
}