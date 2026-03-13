using UnityEngine;

[CreateAssetMenu(fileName = "NewCardData", menuName = "GameData/CardData")]
public class CardData : ScriptableObject
{
    public int cardId;
    public int tier;
    public Sprite cardImage; // ลากรูปการ์ดมาใส่ในไฟล์นี้ได้เลย

    [Header("Costs (0:CPU, 1:RAM, 2:Net, 3:Store, 4:Sec, 5:Wild)")]
    public int[] costs = new int[6]; 
    
    public int victoryPoints; // แต้มของการ์ดใบนี้
}