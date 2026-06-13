using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Core.Tests
{
    // เทสต์ระบบขุนนาง (ผ่าน ApplyBuyCard) + ระบบควิซ (QuizRules) — Unity Test Runner (EditMode)
    public class NobleQuizTests
    {
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
            public IReadOnlyList<string> GetTierCardIds(int tier) => tiers.TryGetValue(tier, out var l) ? l : new List<string>();
        }

        private class FakeQuizDb : IQuizDatabase
        {
            public readonly Dictionary<string, int> correct = new Dictionary<string, int>();
            public bool TryGetCorrectIndex(string q, out int ci) => correct.TryGetValue(q, out ci);
        }

        private static int[] C(int a, int b, int c, int d, int e, int f) => new[] { a, b, c, d, e, f };
        private static int[] Cost(int a, int b, int c, int d, int e) => new[] { a, b, c, d, e };

        private static FakeDb Db()
        {
            var db = new FakeDb();
            db.Add("c1", 1, Cost(1, 1, 1, 0, 0), 0, 0); // bonus CPU
            db.Add("r1", 1, Cost(2, 0, 0, 0, 0), 0, 0);
            db.Add("r2", 1, Cost(2, 0, 0, 0, 0), 0, 0);
            return db;
        }

        private static GameState State(HashSet<string> used, List<NobleState> nobles)
        {
            var players = new PlayerState[4];
            for (int i = 0; i < 4; i++) players[i] = new PlayerState { seat = i };
            return new GameState
            {
                bankCoins = C(7, 7, 7, 7, 7, 5),
                players = players,
                board = new[] { new BoardSlot(1, "c1") },
                usedCardIds = used,
                nobles = nobles,
                playOrder = new[] { 0, 1, 2, 3 },
                currentPlayerIndex = 0,
                boardSeed = 1,
                winningScore = 20
            };
        }

        private static NobleState Noble(string id, int[] req, int vp)
            => new NobleState { nobleId = id, requiredBonuses = req, victoryPoints = vp };

        private static QuizAnswer QA(int seat, bool ok, float t)
            => new QuizAnswer { seat = seat, isCorrect = ok, timeTaken = t };

        // ───────── Noble ─────────

        [Test]
        public void Noble_ClaimedWhenBonusComplete_AddsVP()
        {
            var db = Db();
            var s = State(new HashSet<string> { "c1" }, new List<NobleState> { Noble("n1", new[] { 1, 0, 0, 0, 0 }, 3) });
            s.players[0].coins = C(1, 1, 1, 0, 0, 0);
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsTrue(r.ok);
            Assert.IsTrue(r.next.nobles[0].claimed);
            Assert.AreEqual(0, r.next.nobles[0].claimedBySeat);
            Assert.AreEqual(3, r.next.players[0].score);
        }

        [Test]
        public void Noble_VPCanTriggerWin()
        {
            var db = Db();
            var s = State(new HashSet<string> { "c1" }, new List<NobleState> { Noble("n1", new[] { 1, 0, 0, 0, 0 }, 20) });
            s.players[0].coins = C(1, 1, 1, 0, 0, 0);
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsTrue(r.ok);
            Assert.IsTrue(r.next.isGameOver);
            Assert.AreEqual(0, r.next.winnerSeat);
            Assert.AreEqual(0, r.next.currentPlayerIndex);
        }

        [Test]
        public void Noble_NotClaimedWhenRequirementUnmet()
        {
            var db = Db();
            var s = State(new HashSet<string> { "c1" }, new List<NobleState> { Noble("n1", new[] { 2, 0, 0, 0, 0 }, 3) });
            s.players[0].coins = C(1, 1, 1, 0, 0, 0);
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsTrue(r.ok);
            Assert.IsFalse(r.next.nobles[0].claimed);
            Assert.AreEqual(0, r.next.players[0].score);
        }

        [Test]
        public void Noble_MultipleClaimedInOneTurn()
        {
            var db = Db();
            var s = State(new HashSet<string> { "c1" },
                new List<NobleState> { Noble("n1", new[] { 1, 0, 0, 0, 0 }, 3), Noble("n2", new[] { 1, 0, 0, 0, 0 }, 3) });
            s.players[0].coins = C(1, 1, 1, 0, 0, 0);
            var r = GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsTrue(r.ok);
            Assert.IsTrue(r.next.nobles[0].claimed && r.next.nobles[1].claimed);
            Assert.AreEqual(6, r.next.players[0].score);
        }

        [Test]
        public void Noble_OriginalStateUntouched()
        {
            var db = Db();
            var s = State(new HashSet<string> { "c1" }, new List<NobleState> { Noble("n1", new[] { 1, 0, 0, 0, 0 }, 3) });
            s.players[0].coins = C(1, 1, 1, 0, 0, 0);
            GameRules.ApplyAction(s, new BuyCardAction { seat = 0, cardId = "c1" }, db);
            Assert.IsFalse(s.nobles[0].claimed);
        }

        // ───────── Quiz ─────────

        [Test]
        public void Quiz_Evaluate_CorrectAndWrong()
        {
            var qdb = new FakeQuizDb(); qdb.correct["q1"] = 2;
            var ok = QuizRules.Evaluate(new AnswerQuizAction { seat = 1, questionId = "q1", choiceIndex = 2 }, qdb, 3f);
            var no = QuizRules.Evaluate(new AnswerQuizAction { seat = 2, questionId = "q1", choiceIndex = 0 }, qdb, 1f);
            Assert.IsTrue(ok.isCorrect);
            Assert.AreEqual(1, ok.seat);
            Assert.IsFalse(no.isCorrect);
        }

        [Test]
        public void Quiz_Evaluate_UnknownQuestionIsWrong()
        {
            var qdb = new FakeQuizDb();
            var x = QuizRules.Evaluate(new AnswerQuizAction { seat = 0, questionId = "zzz", choiceIndex = 0 }, qdb, 1f);
            Assert.IsFalse(x.isCorrect);
        }

        [Test]
        public void Quiz_FastestCorrectWins()
            => Assert.AreEqual(1, QuizRules.DetermineWinner(new[] { QA(0, true, 5f), QA(1, true, 2f), QA(2, false, 1f) }, 4));

        [Test]
        public void Quiz_CorrectBeatsWrongRegardlessOfTime()
            => Assert.AreEqual(1, QuizRules.DetermineWinner(new[] { QA(0, false, 0.1f), QA(1, true, 9f) }, 4));

        [Test]
        public void Quiz_NoCorrect_NoWinner()
            => Assert.AreEqual(-1, QuizRules.DetermineWinner(new[] { QA(0, false, 1f), QA(1, false, 2f) }, 4));

        [Test]
        public void Quiz_UnansweredTreatedAsWrong()
            => Assert.AreEqual(3, QuizRules.DetermineWinner(new[] { QA(3, true, 8f) }, 4));

        [Test]
        public void Quiz_BestAnswerPerPlayerUsed()
            => Assert.AreEqual(0, QuizRules.DetermineWinner(new[] { QA(0, false, 0.5f), QA(0, true, 4f), QA(1, true, 5f) }, 4));

        [Test]
        public void Quiz_GrantBlackCoin_IncrementsWildcard_NotBank()
        {
            var s = State(new HashSet<string> { "c1" }, new List<NobleState>());
            var next = QuizRules.GrantBlackCoin(s, 2);
            Assert.AreEqual(1, next.players[2].quizBlackCoins);
            Assert.AreEqual(1, next.players[2].coins[5]);
            Assert.AreEqual(5, next.bankCoins[5], "black coin is not from bank");
            Assert.AreEqual(0, s.players[2].quizBlackCoins, "original unchanged");
        }

        [Test]
        public void Quiz_NotHandledByApplyAction()
        {
            var db = Db();
            var s = State(new HashSet<string> { "c1" }, new List<NobleState>());
            var r = GameRules.ApplyAction(s, new AnswerQuizAction { seat = 0, questionId = "q1", choiceIndex = 2 }, db);
            Assert.IsFalse(r.ok);
        }
    }
}
