using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

namespace Game.Core.Tests
{
    // เทสต์ GameStateValidator — ตรวจ "สถานะที่เป็นไปไม่ได้" (กันโกง/จับ desync) — Unity Test Runner (EditMode)
    public class GameStateValidatorTests
    {
        private static int[] C(int a, int b, int c, int d, int e, int f) => new[] { a, b, c, d, e, f };

        private static GameState Valid()
        {
            var players = new PlayerState[4];
            for (int i = 0; i < 4; i++) players[i] = new PlayerState { seat = i };
            return new GameState
            {
                bankCoins = C(7, 7, 7, 7, 7, 5),
                players = players,
                board = new BoardSlot[0],
                usedCardIds = new HashSet<string>(),
                playOrder = new[] { 0, 1, 2, 3 },
                currentPlayerIndex = 0,
                winningScore = 20,
            };
        }

        private static ValidationConfig Cfg()
            => new ValidationConfig { expectedColoredPerColor = 7, expectedGoldFromBank = 5 };

        [Test] public void CleanStateIsValid()
            => Assert.IsTrue(GameStateValidator.Validate(Valid(), Cfg()).Ok);

        [Test] public void NegativeBankDetected()
        { var s = Valid(); s.bankCoins[0] = -1; Assert.IsFalse(GameStateValidator.Validate(s).Ok); }

        [Test] public void NegativePlayerCoinDetected()
        { var s = Valid(); s.players[1].coins[2] = -3; Assert.IsFalse(GameStateValidator.Validate(s).Ok); }

        [Test] public void QuizBlackExceedingGoldDetected()
        { var s = Valid(); s.players[0].quizBlackCoins = 2; s.players[0].coins[5] = 1; Assert.IsFalse(GameStateValidator.Validate(s).Ok); }

        [Test] public void Over10Detected()
        {
            var s = Valid(); s.players[0].coins = C(6, 6, 0, 0, 0, 0);
            Assert.IsFalse(GameStateValidator.Validate(s, new ValidationConfig { maxCoinsPerPlayer = 10 }).Ok);
        }

        [Test] public void QuizBlackOver10AllowedWhenCapDisabled()
        {
            var s = Valid();
            s.players[0].coins = C(0, 0, 0, 0, 0, 11); s.players[0].quizBlackCoins = 11;
            Assert.IsTrue(GameStateValidator.Validate(s, new ValidationConfig { maxCoinsPerPlayer = null }).Ok);
        }

        [Test] public void Over3ReservedDetected()
        {
            var s = Valid();
            s.players[0].reservedCardIds.AddRange(new[] { "a", "b", "c", "d" });
            s.usedCardIds = new HashSet<string> { "a", "b", "c", "d" };
            Assert.IsFalse(GameStateValidator.Validate(s).Ok);
        }

        [Test] public void BadCurrentPlayerIndexDetected()
        { var s = Valid(); s.currentPlayerIndex = 9; Assert.IsFalse(GameStateValidator.Validate(s).Ok); }

        [Test] public void BadPlayOrderSeatDetected()
        { var s = Valid(); s.playOrder = new[] { 0, 1, 2, 99 }; Assert.IsFalse(GameStateValidator.Validate(s).Ok); }

        [Test] public void DuplicateBoardCardDetected()
        {
            var s = Valid();
            s.board = new[] { new BoardSlot(1, "x"), new BoardSlot(1, "x") };
            s.usedCardIds = new HashSet<string> { "x" };
            Assert.IsFalse(GameStateValidator.Validate(s).Ok);
        }

        [Test] public void BoardCardNotInUsedDetected()
        {
            var s = Valid();
            s.board = new[] { new BoardSlot(1, "y") };
            s.usedCardIds = new HashSet<string>();
            Assert.IsFalse(GameStateValidator.Validate(s).Ok);
        }

        [Test] public void CardOnBoardAndReservedDetected()
        {
            var s = Valid();
            s.board = new[] { new BoardSlot(1, "z") };
            s.players[2].reservedCardIds.Add("z");
            s.usedCardIds = new HashSet<string> { "z" };
            Assert.IsFalse(GameStateValidator.Validate(s).Ok);
        }

        [Test] public void ColorConservationBreakDetected()
        { var s = Valid(); s.bankCoins[0] = 6; Assert.IsFalse(GameStateValidator.Validate(s, Cfg()).Ok); }

        [Test] public void MovedCoinsStillConserve()
        {
            var s = Valid(); s.bankCoins[0] = 5; s.players[0].coins[0] = 2;
            Assert.IsTrue(GameStateValidator.Validate(s, Cfg()).Ok);
        }

        [Test] public void MintedQuizBlackKeepsGoldConservation()
        {
            var s = Valid(); s.players[0].coins[5] = 2; s.players[0].quizBlackCoins = 2;
            Assert.IsTrue(GameStateValidator.Validate(s, Cfg()).Ok);
        }

        [Test] public void RealGoldOverSupplyDetected()
        {
            var s = Valid(); s.players[0].coins[5] = 2; // ทองจริง mint ไม่ได้
            Assert.IsFalse(GameStateValidator.Validate(s, Cfg()).Ok);
        }
    }
}
