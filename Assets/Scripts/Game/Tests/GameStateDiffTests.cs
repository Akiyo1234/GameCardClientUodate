using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Core.Tests
{
    // เทสต์ GameStateDiff — เทียบ state (parity core vs legacy / จับ desync) — Unity Test Runner (EditMode)
    public class GameStateDiffTests
    {
        private static int[] C(int a, int b, int c, int d, int e, int f) => new[] { a, b, c, d, e, f };

        private static GameState S()
        {
            var players = new PlayerState[4];
            for (int i = 0; i < 4; i++) players[i] = new PlayerState { seat = i };
            return new GameState
            {
                bankCoins = C(7, 7, 7, 7, 7, 5),
                players = players,
                board = new[] { new BoardSlot(1, "a"), new BoardSlot(2, "b") },
                usedCardIds = new HashSet<string> { "a", "b" },
                playOrder = new[] { 0, 1, 2, 3 },
                currentPlayerIndex = 0,
                nobles = new List<NobleState> { new NobleState { nobleId = "n1", victoryPoints = 3 } },
                winningScore = 20,
            };
        }

        [Test] public void CloneEqualsOriginal()
            => Assert.IsTrue(GameStateDiff.Equal(S(), S().Clone()));

        [Test] public void BankDiffDetected()
        {
            var a = S(); var b = S().Clone(); b.bankCoins[0] = 6;
            var d = GameStateDiff.Diff(a, b);
            Assert.AreEqual(1, d.Count);
            StringAssert.Contains("bank[0]", d[0]);
        }

        [Test] public void PlayerCoinDiffDetected()
        { var a = S(); var b = S().Clone(); b.players[1].coins[2] = 5; Assert.IsFalse(GameStateDiff.Equal(a, b)); }

        [Test] public void ScoreDiffDetected()
        { var a = S(); var b = S().Clone(); b.players[0].score = 4; Assert.IsFalse(GameStateDiff.Equal(a, b)); }

        [Test] public void ReservedDiffDetected()
        { var a = S(); var b = S().Clone(); b.players[0].reservedCardIds.Add("x"); Assert.IsFalse(GameStateDiff.Equal(a, b)); }

        [Test] public void BoardDiffDetected()
        { var a = S(); var b = S().Clone(); b.board[0] = new BoardSlot(1, "zzz"); Assert.IsFalse(GameStateDiff.Equal(a, b)); }

        [Test] public void BoardDiffIgnoredWithFlag()
        {
            var a = S(); var b = S().Clone(); b.board[0] = new BoardSlot(1, "zzz");
            Assert.IsTrue(GameStateDiff.Equal(a, b, new DiffOptions { ignoreBoardCards = true }));
        }

        [Test] public void TurnDiffDetectedAndIgnorable()
        {
            var a = S(); var b = S().Clone(); b.currentPlayerIndex = 2; b.totalTurnCount = 9;
            Assert.IsFalse(GameStateDiff.Equal(a, b));
            Assert.IsTrue(GameStateDiff.Equal(a, b, new DiffOptions { ignoreTurn = true }));
        }

        [Test] public void NobleDiffDetectedAndIgnorable()
        {
            var a = S(); var b = S().Clone(); b.nobles[0].claimed = true;
            Assert.IsFalse(GameStateDiff.Equal(a, b));
            Assert.IsTrue(GameStateDiff.Equal(a, b, new DiffOptions { ignoreNobles = true }));
        }

        [Test] public void MultipleDiffsReported()
        {
            var a = S(); var b = S().Clone();
            b.bankCoins[0] = 6; b.players[0].score = 1; b.currentPlayerIndex = 3;
            Assert.GreaterOrEqual(GameStateDiff.Diff(a, b).Count, 3);
        }

        [Test] public void BuyStyleParity_IgnoreBoardAndTurn_StillCatchesCoinDiff()
        {
            var a = S(); var b = S().Clone();
            b.board[0] = new BoardSlot(1, "draw_diff");
            b.currentPlayerIndex = 1; b.totalTurnCount = 1;
            var opts = new DiffOptions { ignoreBoardCards = true, ignoreTurn = true };
            Assert.IsTrue(GameStateDiff.Equal(a, b, opts));
            b.players[0].coins[0] = 3;
            Assert.IsFalse(GameStateDiff.Equal(a, b, opts));
        }
    }
}
