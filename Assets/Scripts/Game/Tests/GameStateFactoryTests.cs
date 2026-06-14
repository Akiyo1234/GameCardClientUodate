using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Core.Tests
{
    // เทสต์ GameStateFactory.NewGame — Unity Test Runner (EditMode)
    public class GameStateFactoryTests
    {
        private class FakeDb : ICardDatabase
        {
            public readonly Dictionary<string, CardInfo> cards = new Dictionary<string, CardInfo>();
            public readonly Dictionary<int, List<string>> tiers = new Dictionary<int, List<string>>();
            public void Add(string id, int tier)
            {
                cards[id] = new CardInfo { cardId = id, tier = tier, costs = new[] { 0, 0, 0, 0, 0 } };
                if (!tiers.ContainsKey(tier)) tiers[tier] = new List<string>();
                tiers[tier].Add(id);
            }
            public bool TryGet(string id, out CardInfo info) => cards.TryGetValue(id, out info);
            public IReadOnlyList<string> GetTierCardIds(int tier) => tiers.TryGetValue(tier, out var l) ? l : new List<string>();
        }

        private static FakeDb Db()
        {
            var db = new FakeDb();
            for (int t = 1; t <= 3; t++) for (int i = 0; i < 10; i++) db.Add($"t{t}_{i}", t);
            return db;
        }

        private static List<NobleState> Nobles()
        {
            var list = new List<NobleState>();
            for (int i = 0; i < 8; i++) list.Add(new NobleState { nobleId = $"n{i}", requiredBonuses = new[] { i, 0, 0, 0, 0 }, victoryPoints = 3 });
            return list;
        }

        [Test]
        public void BankSupplyByPlayerCount()
        {
            Assert.AreEqual(4, GameStateFactory.NewGame(2, Db(), Nobles(), 1).bankCoins[0]);
            Assert.AreEqual(5, GameStateFactory.NewGame(3, Db(), Nobles(), 1).bankCoins[0]);
            Assert.AreEqual(7, GameStateFactory.NewGame(4, Db(), Nobles(), 1).bankCoins[0]);
            Assert.AreEqual(5, GameStateFactory.NewGame(4, Db(), Nobles(), 1).bankCoins[5]);
        }

        [Test]
        public void BoardIs4PerTier_NoDuplicates_AllUsed()
        {
            var s = GameStateFactory.NewGame(4, Db(), Nobles(), 42);
            Assert.AreEqual(12, s.board.Length);
            int t1 = 0, t2 = 0, t3 = 0;
            var seen = new HashSet<string>();
            foreach (var slot in s.board)
            {
                if (slot.tier == 1) t1++; else if (slot.tier == 2) t2++; else if (slot.tier == 3) t3++;
                Assert.IsFalse(slot.IsEmpty);
                Assert.IsTrue(seen.Add(slot.cardId), "no duplicate");
                Assert.IsTrue(s.usedCardIds.Contains(slot.cardId));
            }
            Assert.IsTrue(t1 == 4 && t2 == 4 && t3 == 4);
            Assert.AreEqual(12, s.drawCounter);
        }

        [Test]
        public void NoblesCountIsPlayerCountPlusOne_AndPoolNotMutated()
        {
            var pool = Nobles();
            var s = GameStateFactory.NewGame(4, Db(), pool, 42);
            Assert.AreEqual(5, s.nobles.Count);
            Assert.IsFalse(s.nobles[0].claimed);
            s.nobles[0].claimed = true;
            Assert.IsFalse(pool[0].claimed, "pool template must not be mutated");
        }

        [Test]
        public void Deterministic_SameSeedSameState()
        {
            var a = GameStateFactory.NewGame(4, Db(), Nobles(), 7);
            var b = GameStateFactory.NewGame(4, Db(), Nobles(), 7);
            Assert.IsTrue(GameStateDiff.Equal(a, b));
        }

        [Test]
        public void NewGame_PassesValidator_AndRoundTripsCodec()
        {
            var s = GameStateFactory.NewGame(4, Db(), Nobles(), 42);
            var res = GameStateValidator.Validate(s, new ValidationConfig { expectedColoredPerColor = 7, expectedGoldFromBank = 5 });
            Assert.IsTrue(res.Ok, res.ToString());
            Assert.IsTrue(GameStateDiff.Equal(s, GameStateCodec.Deserialize(GameStateCodec.Serialize(s))));
        }

        [Test]
        public void EdgeCases_EmptyNoblePool_AndClampPlayerCount()
        {
            Assert.AreEqual(0, GameStateFactory.NewGame(2, Db(), new List<NobleState>(), 1).nobles.Count);
            Assert.AreEqual(4, GameStateFactory.NewGame(9, Db(), Nobles(), 1).players.Length);
            Assert.AreEqual(2, GameStateFactory.NewGame(1, Db(), Nobles(), 1).players.Length);
        }
    }
}
