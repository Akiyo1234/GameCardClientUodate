using System;

namespace Game.Core
{
    // ขุนนาง 1 ใบ — แกะจาก NobleData (requiredBonuses[5], victoryPoints)
    // เงื่อนไข: ผู้เล่นต้องมี bonuses[b] >= requiredBonuses[b] ครบทั้ง 5 สี จึง claim ได้
    [Serializable]
    public class NobleState
    {
        public string nobleId;
        public int[] requiredBonuses = new int[CoinIndex.ColorCount]; // [5]
        public int victoryPoints;
        public bool claimed;
        public int claimedBySeat = -1;

        public NobleState Clone() => new NobleState
        {
            nobleId = nobleId,
            requiredBonuses = (int[])requiredBonuses.Clone(),
            victoryPoints = victoryPoints,
            claimed = claimed,
            claimedBySeat = claimedBySeat
        };
    }
}
