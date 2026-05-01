using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

public class ModeSelectUI : MonoBehaviour
{
    [Header("---- UI Panels ----")]
    public GameObject mainMenuPanel;
    public GameObject modeSelectPanel;
    public GameObject lobbyPanel;

    [Header("---- Player Stats UI ----")]
    public TextMeshProUGUI totalCoinsText;
    public TextMeshProUGUI totalPointsText;

    [Header("---- Character Selection ----")]
    public CharacterData[] availableCharacters;
    public Image characterPreviewImage;
    public TextMeshProUGUI characterNameText;
    private int currentCharacterIndex = 0;

    [Header("---- Matchmaking ----")]
    public MatchmakingClient matchmakingClient;

    private void Start()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);

        int coins = PlayerPrefs.GetInt("TotalCoins", 0);
        int points = PlayerPrefs.GetInt("TotalPoints", 0);

        if (totalCoinsText != null) totalCoinsText.text = coins.ToString();
        if (totalPointsText != null) totalPointsText.text = points.ToString();

        currentCharacterIndex = PlayerPrefs.GetInt("SelectedCharacter", 0);
        UpdateCharacterPreview();
    }

    public void OnClickMainMenuPlay()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    public void OnClickBackToMain()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    public void OpenPanel()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    public void ClosePanel()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
    }

    public void OnClickBotMode()
    {
        Debug.Log("[Mode] Selected bot mode.");
        SceneManager.LoadScene("SampleScene");
    }

    public void OnClickRoomMode()
    {
        Debug.Log("[Mode] Selected private room mode.");
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }

    public void OnClickAutoMatch()
    {
        Debug.Log("[Mode] Selected auto match mode.");

        if (matchmakingClient != null)
        {
            matchmakingClient.FindMatch();
            return;
        }

        Debug.LogWarning("[Mode] MatchmakingClient is not assigned on ModeSelectUI.");
    }

    public void OnClickNextCharacter()
    {
        if (availableCharacters == null || availableCharacters.Length == 0) return;

        currentCharacterIndex++;
        if (currentCharacterIndex >= availableCharacters.Length) currentCharacterIndex = 0;

        UpdateCharacterPreview();
        PlayerPrefs.SetInt("SelectedCharacter", currentCharacterIndex);
        PlayerPrefs.Save();
    }

    public void OnClickPrevCharacter()
    {
        if (availableCharacters == null || availableCharacters.Length == 0) return;

        currentCharacterIndex--;
        if (currentCharacterIndex < 0) currentCharacterIndex = availableCharacters.Length - 1;

        UpdateCharacterPreview();
        PlayerPrefs.SetInt("SelectedCharacter", currentCharacterIndex);
        PlayerPrefs.Save();
    }

    private void UpdateCharacterPreview()
    {
        if (availableCharacters == null || availableCharacters.Length == 0) return;

        if (currentCharacterIndex < 0 || currentCharacterIndex >= availableCharacters.Length)
        {
            currentCharacterIndex = 0;
        }

        CharacterData charData = availableCharacters[currentCharacterIndex];

        if (characterPreviewImage != null) characterPreviewImage.sprite = charData.portraitSprite;
        if (characterNameText != null) characterNameText.text = charData.characterName;
    }
}
