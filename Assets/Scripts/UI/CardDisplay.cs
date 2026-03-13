using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class CardDisplay : MonoBehaviour, IPointerClickHandler
{
    [Header("UI Component")]
    public Image displayImage; 

    [Header("Data Reference")]
    public CardData data; 

    private GameController gameController;

    void Awake()
    {
        gameController = FindObjectOfType<GameController>();
    }

    public void LoadCardData(CardData newData)
    {
        data = newData;

        if (data != null && displayImage != null)
        {
            // สร้าง Path ตามระบบใหม่: Cards/Tier1/card_100
            string path = "Cards/Tier" + data.tier + "/card_" + data.cardId;
            
            Sprite loadedSprite = Resources.Load<Sprite>(path);

            if (loadedSprite != null)
            {
                displayImage.sprite = loadedSprite;
            }
            else
            {
                Debug.LogWarning($"⚠️ [Error] หาไฟล์รูปไม่เจอ: {path}");
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (data != null && gameController != null)
        {
            gameController.OnCardClicked(this);
        }
    }
}