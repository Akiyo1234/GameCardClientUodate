using System.Collections.Generic;
using Game.Core;

// ============================================================
// Adapters — สะพานเชื่อม "ข้อมูลฝั่ง Unity" (CardData/QuizQuestion) เข้ากับ Game.Core
//   • อยู่ใน Assembly-CSharp (อ้าง Game.Core ได้เพราะ autoReferenced)
//   • Game.Core ยังคงปลอด UnityEngine — การพึ่ง Unity อยู่ที่ฝั่งนี้เท่านั้น
// ============================================================
namespace Game.Adapters
{
    // ครอบ CardDatabaseLoader → ICardDatabase (ใช้กับ GameRules.ApplyBuyCard/ReserveCard/DrawCard)
    //   prebuild dictionary ครั้งเดียว: id → CardInfo, และรายชื่อ id ต่อ tier (เรียงตามลำดับใน JSON = คงที่)
    public class CardDatabaseAdapter : ICardDatabase
    {
        private readonly Dictionary<string, CardInfo> byId = new Dictionary<string, CardInfo>();
        private readonly Dictionary<int, List<string>> tierIds = new Dictionary<int, List<string>>
        {
            { 1, new List<string>() },
            { 2, new List<string>() },
            { 3, new List<string>() },
        };

        public CardDatabaseAdapter()
        {
            CardDatabaseLoader.EnsureLoaded();
            foreach (var c in CardDatabaseLoader.AllCards)
            {
                if (c == null || string.IsNullOrEmpty(c.cardId)) continue;

                byId[c.cardId] = new CardInfo
                {
                    cardId = c.cardId,
                    tier = c.tier,
                    costs = (int[])c.costs.Clone(), // clone กัน GameRules แตะ array เดิมของ CardData
                    victoryPoints = c.victoryPoints,
                    bonusType = c.bonusType,
                };

                if (tierIds.TryGetValue(c.tier, out var list)) list.Add(c.cardId);
            }
        }

        public bool TryGet(string cardId, out CardInfo info)
            => byId.TryGetValue(cardId ?? string.Empty, out info);

        public IReadOnlyList<string> GetTierCardIds(int tier)
            => tierIds.TryGetValue(tier, out var list) ? list : new List<string>();
    }

    // ครอบ List<QuizManager.QuizQuestion> → IQuizDatabase (ใช้กับ QuizRules.Evaluate)
    //   เก็บเฉลย id → correctChoiceIndex เพื่อให้ authority ตรวจถูก/ผิดเอง (client เชื่อไม่ได้)
    public class QuizDatabaseAdapter : IQuizDatabase
    {
        private readonly Dictionary<string, int> correctById = new Dictionary<string, int>();

        public QuizDatabaseAdapter(IEnumerable<QuizManager.QuizQuestion> questions)
        {
            if (questions == null) return;
            foreach (var q in questions)
            {
                if (q == null || string.IsNullOrEmpty(q.id)) continue;
                correctById[q.id] = q.correctChoiceIndex;
            }
        }

        public bool TryGetCorrectIndex(string questionId, out int correctIndex)
            => correctById.TryGetValue(questionId ?? string.Empty, out correctIndex);
    }
}
