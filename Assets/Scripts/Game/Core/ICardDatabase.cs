using System.Collections.Generic;

namespace Game.Core
{
    // ข้อมูลการ์ดแบบ pure (ไม่ใช่ ScriptableObject) — mirror ของ CardData
    //   costs index:  0=CPU 1=RAM 2=Network 3=Storage 4=Security
    //   bonusType:    0=CPU 1=RAM 2=Network 3=Storage 4=Security
    public struct CardInfo
    {
        public string cardId;
        public int tier;          // 1,2,3
        public int[] costs;       // [5]
        public int victoryPoints;
        public int bonusType;     // 0-4
    }

    // แหล่งข้อมูลการ์ด — ฝั่ง Unity adapt จาก CardDatabaseLoader, ฝั่งเทสต์ใช้ fake
    public interface ICardDatabase
    {
        bool TryGet(string cardId, out CardInfo info);

        // รายชื่อ cardId ทั้งหมดของ tier นั้น (กองต้นฉบับ) — ต้องเรียงคงที่ (deterministic draw)
        IReadOnlyList<string> GetTierCardIds(int tier);
    }
}
