using UnityEngine;

[CreateAssetMenu(fileName = "New Card", menuName = "Card Data")]
public class CardData : ScriptableObject
{
    public int cardId;
    public int tier;
    
    [Header("Costs (CPU, RAM, Net, Store, Sec, Wild)")]
    public int[] costs = new int[6];
    
    public int victoryPoints;

    [Header("Card Bonus (ส่วนลดที่ให้)")]
    [Tooltip("0:CPU, 1:RAM, 2:Network, 3:Storage, 4:Security")]
    public int bonusType; // <--- เพิ่มบรรทัดนี้ครับ
}