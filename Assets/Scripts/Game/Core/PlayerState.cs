using System;
using System.Collections.Generic;

namespace Game.Core
{
    // สถานะผู้เล่น 1 คน (1 seat) — C# ล้วน ไม่ผูก Unity
    // แกะมาจาก field ใน PlayerUI.cs (coins, bonuses, currentScore, reservedCards, quizBlackCoins, isBot)
    // เก็บการ์ดเป็น cardId (string) ไม่ใช่ CardData
    [Serializable]
    public class PlayerState
    {
        public int seat;            // 0..3
        public bool isBot;
        public int score;

        public int[] coins = new int[CoinIndex.TotalCount];   // [6] รวมทอง
        public int quizBlackCoins;                            // wildcard ที่ได้จากควิซ
        public int[] bonuses = new int[CoinIndex.ColorCount]; // [5] ส่วนลดถาวรจากการ์ดที่ซื้อ

        public List<string> reservedCardIds = new List<string>(); // ≤ 3 ใบ

        // เหรียญรวมในมือ (รวมทอง) — ใช้เช็คลิมิต 10
        public int TotalCoins()
        {
            int t = 0;
            for (int i = 0; i < coins.Length; i++) t += coins[i];
            return t;
        }

        // สำเนาแบบ deep copy (rules engine ทำงานบนสำเนา ไม่แก้ของเดิม)
        public PlayerState Clone()
        {
            return new PlayerState
            {
                seat = seat,
                isBot = isBot,
                score = score,
                coins = (int[])coins.Clone(),
                quizBlackCoins = quizBlackCoins,
                bonuses = (int[])bonuses.Clone(),
                reservedCardIds = new List<string>(reservedCardIds)
            };
        }
    }
}
