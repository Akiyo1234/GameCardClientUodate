using NUnit.Framework;
using Game.Core;

namespace Game.Core.Tests
{
    // เทสต์กฎ ApplyTakeCoins — รันใน Unity Test Runner (Window > General > Test Runner > EditMode)
    // กฎตรงกับ GameController.Bank.cs OnResourceClicked + EndTurn commit
    public class TakeCoinsTests
    {
        // state มาตรฐาน: 4 ผู้เล่น, bank [7,7,7,7,7,5], ตาของ seat 0
        private static GameState FreshState()
        {
            var players = new PlayerState[4];
            for (int i = 0; i < 4; i++) players[i] = new PlayerState { seat = i };
            return new GameState
            {
                bankCoins = new[] { 7, 7, 7, 7, 7, 5 },
                players = players,
                board = new BoardSlot[0],
                playOrder = new[] { 0, 1, 2, 3 },
                currentPlayerIndex = 0,
                winningScore = 20
            };
        }

        private static int[] C(int a, int b, int c, int d, int e, int f) => new[] { a, b, c, d, e, f };

        private static ActionResult Take(GameState s, int seat, int[] coins)
            => GameRules.ApplyAction(s, new TakeCoinsAction { seat = seat, coins = coins });

        [Test]
        public void ThreeDifferent_Succeeds_DeductsBank_AdvancesTurn()
        {
            var s = FreshState();
            var r = Take(s, 0, C(1, 1, 1, 0, 0, 0));
            Assert.IsTrue(r.ok);
            Assert.AreEqual(6, r.next.bankCoins[0]);
            Assert.AreEqual(1, r.next.players[0].coins[2]);
            Assert.AreEqual(1, r.next.currentPlayerIndex);
            Assert.AreEqual(1, r.next.totalTurnCount);
        }

        [Test]
        public void ApplyAction_DoesNotMutateOriginalState()
        {
            var s = FreshState();
            Take(s, 0, C(1, 1, 1, 0, 0, 0));
            Assert.AreEqual(7, s.bankCoins[0], "original bank must be unchanged");
            Assert.AreEqual(0, s.players[0].coins[0], "original player must be unchanged");
        }

        [Test]
        public void TwoSame_Succeeds_WhenBankAtLeast4()
        {
            var s = FreshState();
            var r = Take(s, 0, C(0, 2, 0, 0, 0, 0));
            Assert.IsTrue(r.ok);
            Assert.AreEqual(2, r.next.players[0].coins[1]);
            Assert.AreEqual(5, r.next.bankCoins[1]);
        }

        [Test]
        public void TwoSame_Fails_WhenBankBelow4()
        {
            var s = FreshState(); s.bankCoins[1] = 3;
            Assert.IsFalse(Take(s, 0, C(0, 2, 0, 0, 0, 0)).ok);
        }

        [Test]
        public void TakingGoldDirectly_Fails()
            => Assert.IsFalse(Take(FreshState(), 0, C(0, 0, 0, 0, 0, 1)).ok);

        [Test]
        public void IllegalPattern_TwoPlusOne_Fails()
            => Assert.IsFalse(Take(FreshState(), 0, C(2, 1, 0, 0, 0, 0)).ok);

        [Test]
        public void ThreeSame_Fails()
            => Assert.IsFalse(Take(FreshState(), 0, C(3, 0, 0, 0, 0, 0)).ok);

        [Test]
        public void Over10Limit_Fails()
        {
            var s = FreshState(); s.players[0].coins = C(9, 0, 0, 0, 0, 0);
            Assert.IsFalse(Take(s, 0, C(0, 2, 0, 0, 0, 0)).ok);
        }

        [Test]
        public void Exactly10_Allowed()
        {
            var s = FreshState(); s.players[0].coins = C(8, 0, 0, 0, 0, 0);
            var r = Take(s, 0, C(0, 2, 0, 0, 0, 0));
            Assert.IsTrue(r.ok);
            Assert.AreEqual(10, r.next.players[0].TotalCoins());
        }

        [Test]
        public void WrongSeat_Fails()
            => Assert.IsFalse(Take(FreshState(), 1, C(1, 1, 1, 0, 0, 0)).ok);

        [Test]
        public void EmptyColor_Fails()
        {
            var s = FreshState(); s.bankCoins[0] = 0;
            Assert.IsFalse(Take(s, 0, C(1, 0, 0, 0, 0, 0)).ok);
        }

        [Test]
        public void SingleCoin_Allowed()
        {
            var r = Take(FreshState(), 0, C(1, 0, 0, 0, 0, 0));
            Assert.IsTrue(r.ok);
            Assert.AreEqual(1, r.next.players[0].coins[0]);
        }

        [Test]
        public void EmptyTake_Fails()
            => Assert.IsFalse(Take(FreshState(), 0, C(0, 0, 0, 0, 0, 0)).ok);

        [Test]
        public void RoundWraps_OnLastSeat()
        {
            var s = FreshState(); s.currentPlayerIndex = 3;
            var r = Take(s, 3, C(1, 1, 1, 0, 0, 0));
            Assert.IsTrue(r.ok);
            Assert.AreEqual(0, r.next.currentPlayerIndex);
            Assert.AreEqual(2, r.next.currentRound);
        }

        [Test]
        public void GameOver_Fails()
        {
            var s = FreshState(); s.isGameOver = true;
            Assert.IsFalse(Take(s, 0, C(1, 1, 1, 0, 0, 0)).ok);
        }
    }
}
