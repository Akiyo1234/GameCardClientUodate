using System;

namespace Game.Core
{
    // 1 ช่องบนกระดาน — แทนการ์ด GameObject ใน tier1/2/3Container ด้วยข้อมูลล้วน
    // cardId ว่าง (null/"") = ช่องว่าง (กองของ tier นั้นหมด)
    [Serializable]
    public struct BoardSlot
    {
        public int tier;      // 1, 2, 3
        public string cardId; // อ้างไป cards_database.json; ว่าง = ช่องว่าง

        public bool IsEmpty => string.IsNullOrEmpty(cardId);

        public BoardSlot(int tier, string cardId)
        {
            this.tier = tier;
            this.cardId = cardId;
        }
    }
}
