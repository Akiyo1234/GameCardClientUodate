using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Core.Tests
{
    // เทสต์ serialization (GameState/GameAction ↔ bytes) — Unity Test Runner (EditMode)
    public class CodecTests
    {
        private static int[] C(int a, int b, int c, int d, int e, int f) => new[] { a, b, c, d, e, f };

        private static GameState Rich()
        {
            var players = new PlayerState[4];
            for (int i = 0; i < 4; i++) players[i] = new PlayerState { seat = i };
            players[0].coins = C(1, 2, 0, 0, 3, 2);
            players[0].quizBlackCoins = 1;
            players[0].bonuses = new[] { 1, 0, 2, 0, 0 };
            players[0].score = 7;
            players[0].reservedCardIds.AddRange(new[] { "r1", "r2" });
            players[2] = null;
            return new GameState
            {
                bankCoins = C(4, 5, 6, 7, 3, 4),
                players = players,
                board = new[] { new BoardSlot(1, "a"), new BoardSlot(1, null), new BoardSlot(2, "b"), new BoardSlot(3, "c") },
                usedCardIds = new HashSet<string> { "a", "b", "c", "r1", "r2" },
                boardSeed = 999,
                drawCounter = 12,
                playOrder = new[] { 0, 1, 3 },
                currentPlayerIndex = 1,
                currentRound = 3,
                totalTurnCount = 8,
                winningScore = 15,
                isGameOver = true,
                winnerSeat = 1,
                nobles = new List<NobleState>
                {
                    new NobleState { nobleId = "n1", requiredBonuses = new[]{1,0,0,0,0}, victoryPoints = 3, claimed = true, claimedBySeat = 0 },
                    new NobleState { nobleId = "n2", requiredBonuses = new[]{0,2,2,0,0}, victoryPoints = 3 },
                },
            };
        }

        [Test]
        public void GameState_RoundTrip_PreservesEverything()
        {
            var orig = Rich();
            var back = GameStateCodec.Deserialize(GameStateCodec.Serialize(orig));

            Assert.IsTrue(GameStateDiff.Equal(orig, back), "diff-equal");
            Assert.AreEqual(999, back.boardSeed);
            Assert.AreEqual(12, back.drawCounter);
            Assert.AreEqual(15, back.winningScore);
            Assert.AreEqual(5, back.usedCardIds.Count);
            Assert.IsTrue(back.usedCardIds.Contains("r1"));
            Assert.IsNull(back.players[2]);
            Assert.IsTrue(back.board[1].IsEmpty);
            Assert.AreEqual(1, back.board[1].tier);
            Assert.AreEqual(2, back.players[0].reservedCardIds.Count);
            Assert.IsTrue(back.nobles[0].claimed);
            Assert.AreEqual(0, back.nobles[0].claimedBySeat);
            Assert.IsFalse(back.nobles[1].claimed);
            Assert.IsTrue(back.isGameOver);
            Assert.AreEqual(1, back.winnerSeat);
        }

        [Test]
        public void GameState_ReSerialize_IsStable()
        {
            var a = GameStateCodec.Deserialize(GameStateCodec.Serialize(Rich()));
            var b = GameStateCodec.Deserialize(GameStateCodec.Serialize(a));
            Assert.IsTrue(GameStateDiff.Equal(a, b));
        }

        [Test]
        public void Action_TakeCoins_RoundTrip()
        {
            var a = new TakeCoinsAction { seat = 2, coins = C(1, 1, 1, 0, 0, 0) };
            var b = (TakeCoinsAction)GameActionCodec.Deserialize(GameActionCodec.Serialize(a));
            Assert.AreEqual(2, b.seat);
            Assert.AreEqual(1, b.coins[0]);
            Assert.AreEqual(0, b.coins[3]);
        }

        [Test]
        public void Action_BuyCard_RoundTrip()
        {
            var a = new BuyCardAction { seat = 1, cardId = "cpu_x", fromReserve = true };
            var b = (BuyCardAction)GameActionCodec.Deserialize(GameActionCodec.Serialize(a));
            Assert.AreEqual(1, b.seat);
            Assert.AreEqual("cpu_x", b.cardId);
            Assert.IsTrue(b.fromReserve);
        }

        [Test]
        public void Action_ReserveCard_RoundTrip()
        {
            var a = new ReserveCardAction { seat = 3, cardId = "ram_y" };
            var b = (ReserveCardAction)GameActionCodec.Deserialize(GameActionCodec.Serialize(a));
            Assert.AreEqual(3, b.seat);
            Assert.AreEqual("ram_y", b.cardId);
        }

        [Test]
        public void Action_AnswerQuiz_RoundTrip()
        {
            var a = new AnswerQuizAction { seat = 0, questionId = "q42", choiceIndex = 2 };
            var b = (AnswerQuizAction)GameActionCodec.Deserialize(GameActionCodec.Serialize(a));
            Assert.AreEqual(0, b.seat);
            Assert.AreEqual("q42", b.questionId);
            Assert.AreEqual(2, b.choiceIndex);
        }

        [Test]
        public void Action_DecodedFromWire_AppliesViaRules()
        {
            var players = new PlayerState[4];
            for (int i = 0; i < 4; i++) players[i] = new PlayerState { seat = i };
            var s = new GameState
            {
                bankCoins = C(7, 7, 7, 7, 7, 5),
                players = players,
                board = new BoardSlot[0],
                playOrder = new[] { 0, 1, 2, 3 },
                currentPlayerIndex = 0,
                winningScore = 20,
            };
            byte[] wire = GameActionCodec.Serialize(new TakeCoinsAction { seat = 0, coins = C(1, 1, 1, 0, 0, 0) });
            var result = GameRules.ApplyAction(s, GameActionCodec.Deserialize(wire));
            Assert.IsTrue(result.ok);
            Assert.AreEqual(1, result.next.players[0].coins[0]);
        }
    }
}
