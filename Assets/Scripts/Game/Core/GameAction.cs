using System;

namespace Game.Core
{
    // "ความตั้งใจ" ที่ client ส่งไปให้ authority — client ไม่แก้ state เอง
    // authority รับ → ตรวจ → apply → broadcast state ใหม่
    [Serializable]
    public abstract class GameAction
    {
        public int seat; // ใครเป็นคนสั่ง (ตรวจกับ CurrentSeat ฝั่ง authority)
    }

    // หยิบเหรียญ — atomic (ส่งชุดเหรียญทั้งก้อนทีเดียว ไม่มี pending)
    //   ตัวอย่าง: [1,1,1,0,0,0] = 3 สีต่างกัน | [0,2,0,0,0,0] = 2 สีเดียวกัน
    //   กติกา (จะ validate ใน GameRules): 3 สีต่าง หรือ 2 สีเดียว (สีนั้นในกองต้อง ≥ 4),
    //   ห้ามหยิบทอง(index5)ตรงๆ, ถือรวมต้องไม่เกิน 10
    [Serializable]
    public class TakeCoinsAction : GameAction
    {
        public int[] coins; // [6] จำนวนที่หยิบต่อสี
    }

    // ซื้อการ์ด — จากกระดานหรือจากการ์ดที่จองไว้
    [Serializable]
    public class BuyCardAction : GameAction
    {
        public string cardId;
        public bool fromReserve; // true = ซื้อจาก reservedCardIds, false = จากกระดาน
    }

    // จองการ์ด — ได้ทอง 1 (ถ้ากองมี & ถือ < 10), เก็บได้ ≤ 3 ใบ
    [Serializable]
    public class ReserveCardAction : GameAction
    {
        public string cardId;
    }

    // ตอบควิซ — ได้ quizBlackCoin (wildcard) ถ้าถูก
    [Serializable]
    public class AnswerQuizAction : GameAction
    {
        public string questionId;
        public int choiceIndex;
    }
}
