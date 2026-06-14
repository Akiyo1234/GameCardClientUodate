using System.Collections.Generic;

namespace Game.Core
{
    // สร้าง GameState เริ่มต้นของแมตช์ (authority เป็นคนสร้าง) — pure, deterministic ตาม seed
    //   bank ตามจำนวนคน, แจกการ์ด 4 ใบ/tier (deterministic), สุ่มขุนนาง playerCount+1 ใบ
    //   noblePool = รายการขุนนางต้นแบบ (Unity adapter แปลงจาก NobleData) — จะถูก clone ไม่แตะของเดิม
    public static class GameStateFactory
    {
        public const int DefaultWinningScore = 20;

        public static GameState NewGame(
            int playerCount,
            ICardDatabase cards,
            IReadOnlyList<NobleState> noblePool,
            int seed)
        {
            if (playerCount < 2) playerCount = 2;
            if (playerCount > 4) playerCount = 4;

            var s = new GameState
            {
                winningScore = DefaultWinningScore,
                boardSeed = seed,
                drawCounter = 0,
                currentPlayerIndex = 0,
                currentRound = 1,
                totalTurnCount = 0,
                isGameOver = false,
                winnerSeat = -1,
                usedCardIds = new HashSet<string>(),
            };

            // bank: สี 4/5/7 ตาม 2/3/4 คน, ทอง 5 เสมอ (ตรงกับ ConfigureBankCoinsByPlayerCount)
            int colored = playerCount == 2 ? 4 : playerCount == 3 ? 5 : 7;
            s.bankCoins = new[] { colored, colored, colored, colored, colored, 5 };

            // players + playOrder
            s.players = new PlayerState[playerCount];
            s.playOrder = new int[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                s.players[i] = new PlayerState { seat = i };
                s.playOrder[i] = i;
            }

            // board: 4 ใบต่อ tier (1,2,3) — จั่ว deterministic ผ่าน GameRules.DrawCard
            var board = new List<BoardSlot>();
            if (cards != null)
                for (int tier = 1; tier <= 3; tier++)
                    for (int slot = 0; slot < 4; slot++)
                        board.Add(new BoardSlot(tier, GameRules.DrawCard(s, cards, tier)));
            s.board = board.ToArray();

            // nobles: playerCount+1 ใบ (deterministic shuffle จาก pool)
            s.nobles = PickNobles(noblePool, playerCount + 1, seed);

            return s;
        }

        // Fisher-Yates ด้วย System.Random(seed) → เลือก count ใบแรก, clone กัน mutate pool
        private static List<NobleState> PickNobles(IReadOnlyList<NobleState> pool, int count, int seed)
        {
            var result = new List<NobleState>();
            if (pool == null || pool.Count == 0) return result;

            var order = new List<int>(pool.Count);
            for (int i = 0; i < pool.Count; i++) order.Add(i);

            var rng = new System.Random(seed);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            int take = count < pool.Count ? count : pool.Count;
            for (int i = 0; i < take; i++)
            {
                NobleState src = pool[order[i]];
                result.Add(src != null
                    ? new NobleState
                    {
                        nobleId = src.nobleId,
                        requiredBonuses = (int[])src.requiredBonuses.Clone(),
                        victoryPoints = src.victoryPoints,
                        // claimed/claimedBySeat รีเซ็ตเป็นค่าเริ่มต้น (เกมใหม่)
                    }
                    : null);
            }
            return result;
        }
    }
}
