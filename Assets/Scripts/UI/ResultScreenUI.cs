using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System;

public class ResultScreenUI : MonoBehaviour
{
    private const string MatchmakingRoomIdPrefsKey = "MatchmakingRoomId";
    private const string MatchmakingRoomCodePrefsKey = "MatchmakingRoomCode";

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
    public ParticleSystem victoryParticles;

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

    public void ShowResults(string title, List<string> playerRankings, bool gameOverStatus, bool playFireworks = false)
    {
        isGameOver = gameOverStatus;
        isAutoCloseEnabled = gameOverStatus;
        titleText.text = title;

        foreach (Transform child in playerListContainer)
        {
            Destroy(child.gameObject);
        }

        for (int i = 0; i < playerRankings.Count; i++)
        {
            GameObject row = Instantiate(playerRowPrefab, playerListContainer);
            var rowText = row.GetComponentInChildren<TextMeshProUGUI>();
            if (rowText != null)
            {
                rowText.text = $"อันดับที่ {i + 1}: {playerRankings[i]}";
                if (i == 0)
                {
                    rowText.color = new Color(1f, 0.84f, 0f);
                }
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
            CloseOnlineRoomAndClearMatchState();
            SceneManager.LoadScene("Mainmenu 1");
        }
        else
        {
            onClosed?.Invoke();
        }
    }

    private void CloseOnlineRoomAndClearMatchState()
    {
        if (FusionManager.Instance != null)
        {
            FusionManager.Instance.Disconnect();
        }

        PlayerPrefs.DeleteKey(MatchmakingRoomIdPrefsKey);
        PlayerPrefs.DeleteKey(MatchmakingRoomCodePrefsKey);
        PlayerPrefs.Save();
    }
}
