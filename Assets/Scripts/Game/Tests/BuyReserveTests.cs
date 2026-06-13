using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Core.Tests
{
    // เทสต์กฎ ApplyBuyCard / ApplyReserveCard — รันใน Unity Test Runner (EditMode)
    // กฎตรงกับ GameController.Cards.cs (OnCardClicked / ExecuteReserve / BuyReservedCard)
    public class BuyReserveTests
    {
        // fake card database (ไม่พึ่ง Unity)
        private class FakeDb : ICardDatabase
        {
            public readonly Dictionary<string, CardInfo> cards = new Dictionary<string, CardInfo>();
            public readonly Dictionary<int, List<string>> tiers = new Dictionary<int, List<string>>();

            public void Add(string id, int tier, int[] costs, int vp, int bonus)
            {
                cards[id] = new CardInfo { cardId = id, tier = tier, costs = costs, victoryPoints = vp, bonusType = bonus };
                if (!tiers.ContainsKey(tier)) tiers[tier] = new List<string>();
                tiers[tier].Add(id);
            }
            public bool TryGet(string cardId, out CardInfo info) => cards.TryGetValue(cardId, out info);
            public IReadOnlyList<string> GetTierCardIds(int tier)
                => tiers.TryGetValue(tier, out var l) ? l : new List<string>();
        }

        private static int[] C(int a, int b, int c, int d, int e, int f) => new[] { a, b, c, d, e, f };
        private static int[] Cost(int a, int b, int c, int d, int e) => new[] { a, b, c, d, e };

        private static FakeDb Db()
        {
            var db = new FakeDb();
            db.Add("c1", 1, Cost(1, 1, 1, 0, 0), 0, 0);
            db.Add("c2", 1, Cost(0, 0, 0, 0, 0), 1, 1);
            db.Add("c3", 1, Cost(4, 0, 0, 0, 0), 0, 2);
            db.Add("r1", 1, Cost(2, 0, 0, 0, 0), 0, 0);
            db.Add("r2", 1, Cost(2, 0, 0, 0, 0), 0, 0);
            db.Add("cwin", 3, Cost(0, 0, 0, 0, 0), 20, 0);
            db.Add("t3a", 3, Cost(5, 0, 0, 0, 0), 3, 0);
            return db;
        }

        private static GameState State(string[] boardCardIds, int[] tiersOfBoard, HashSet<string> used)
        {
            var players = new PlayerState[4];
            for (int i = 0; i < 4; i++) players[i] = new PlayerState { seat = i };
            var board = new BoardSlot[boardCardIds.Length];
            for (int i = 0; i < boardCardIds.Length; i++) board[i] = new BoardSlot(tiersOfBoard[i], boardCardIds[i]);
            return new GameState
            {
                bankCoins = C(7, 7, 7, 7, 7, 5),
                players = players,
                board = board,
                usedCardIds = used,
                playOrder = new[] { 0, 1, 2, 3 },
                currentPlayerIndex = 0,
                boardSeed = 12345,
                winningScore = 20
            };
        }

        [Test]
        public void BuyFromBoard_Succeeds_GivesBonus_PaysBank_ReplacesSlot_AdvancesTurn()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            s.players[0].coins = C(1, 1, 1, 0, 0, 0);
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(1, r.next.players[0].bonuses[0]);
            Assert.AreEqual(8, r.next.bankCoins[0]);
            Assert.AreEqual(0, r.next.players[0].coins[0]);
            Assert.IsFalse(r.next.board[0].IsEmpty);
            Assert.AreNotEqual("c1", r.next.board[0].cardId);
            Assert.AreEqual(1, r.next.currentPlayerIndex);
        }

        [Test]
        public void Buy_Unaffordable_Fails()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            Assert.IsFalse(GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c1" }, db).ok);
        }

        [Test]
        public void Buy_FreeWithBonuses()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            s.players[0].bonuses = new[] { 1, 1, 1, 0, 0 };
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(0, r.next.players[0].coins[0]);
        }

        [Test]
        public void Buy_GoldCoversShortfall_ReturnsGoldToBank()
        {
            var db = Db();
            var s = State(new[] { "c3" }, new[] { 1 }, new HashSet<string> { "c3" });
            s.players[0].coins = C(2, 0, 0, 0, 0, 2);
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c3" }, db);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(0, r.next.players[0].coins[5]);
            Assert.AreEqual(7, r.next.bankCoins[5]);
        }

        [Test]
        public void Buy_QuizBlackSpentFirst_NotReturnedToBank()
        {
            var db = Db();
            var s = State(new[] { "r1" }, new[] { 1 }, new HashSet<string> { "r1" });
            s.players[0].coins = C(0, 0, 0, 0, 0, 2);
            s.players[0].quizBlackCoins = 1;
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "r1" }, db);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(0, r.next.players[0].quizBlackCoins);
            Assert.AreEqual(6, r.next.bankCoins[5], "only real gold (1) returns to bank, not the black coin");
            Assert.AreEqual(0, r.next.players[0].coins[5]);
        }

        [Test]
        public void BuyFromReserve_RemovesFromHand_BoardUntouched()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            s.players[0].reservedCardIds.Add("c2");
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c2", fromReserve = true }, db);
            Assert.IsTrue(r.ok);
            Assert.IsFalse(r.next.players[0].reservedCardIds.Contains("c2"));
            Assert.AreEqual("c1", r.next.board[0].cardId);
        }

        [Test]
        public void Buy_UnknownCard_Fails()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            Assert.IsFalse(GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "zzz" }, db).ok);
        }

        [Test]
        public void Buy_ReachingWinningScore_EndsGame_NoAdvance()
        {
            var db = Db();
            var s = State(new[] { "cwin" }, new[] { 3 }, new HashSet<string> { "cwin" });
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "cwin" }, db);
            Assert.IsTrue(r.ok);
            Assert.IsTrue(r.next.isGameOver);
            Assert.AreEqual(0, r.next.winnerSeat);
            Assert.AreEqual(0, r.next.currentPlayerIndex, "turn must not advance after win");
        }

        [Test]
        public void Buy_ExhaustedTier_LeavesEmptySlot()
        {
            var db = Db();
            var s = State(new[] { "c2" }, new[] { 3 }, new HashSet<string> { "cwin", "t3a" });
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c2" }, db);
            Assert.IsTrue(r.ok);
            Assert.IsTrue(r.next.board[0].IsEmpty);
        }

        [Test]
        public void Reserve_AddsToHand_Gains1Gold_ReplacesSlot_AdvancesTurn()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            var r = GameRules.ApplyAction(s, new ReserveCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsTrue(r.ok);
            Assert.IsTrue(r.next.players[0].reservedCardIds.Contains("c1"));
            Assert.AreEqual(1, r.next.players[0].coins[5]);
            Assert.AreEqual(4, r.next.bankCoins[5]);
            Assert.AreNotEqual("c1", r.next.board[0].cardId);
            Assert.AreEqual(1, r.next.currentPlayerIndex);
        }

        [Test]
        public void Reserve_Full3_Fails()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            s.players[0].reservedCardIds.AddRange(new[] { "r1", "r2", "c3" });
            Assert.IsFalse(GameRules.ApplyAction(s, new ReserveCardAction { seat = 0, cardId = "c1" }, db).ok);
        }

        [Test]
        public void Reserve_NoGoldInBank_StillSucceeds_NoGoldGained()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            s.bankCoins[5] = 0;
            var r = GameRules.ApplyAction(s, new ReserveCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(0, r.next.players[0].coins[5]);
        }

        [Test]
        public void Reserve_At10Coins_NoGoldGained()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            s.players[0].coins = C(10, 0, 0, 0, 0, 0);
            var r = GameRules.ApplyAction(s, new ReserveCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsTrue(r.ok);
            Assert.AreEqual(0, r.next.players[0].coins[5]);
        }

        [Test]
        public void Buy_DoesNotMutateOriginalState()
        {
            var db = Db();
            var s = State(new[] { "c1" }, new[] { 1 }, new HashSet<string> { "c1" });
            s.players[0].coins = C(1, 1, 1, 0, 0, 0);
            GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c1" }, db);
            Assert.AreEqual(0, s.players[0].bonuses[0]);
            Assert.AreEqual("c1", s.board[0].cardId);
            Assert.AreEqual(7, s.bankCoins[0]);
        }
    }
}
