using System.Collections.Generic;

namespace Game.Core
{
    // หัวใจของ authoritative — pure function ตรวจ+apply 1 action
    //   ไม่มี UnityEngine, ไม่มี UI, ไม่มี Destroy/Instantiate → unit test ได้
    //
    // หลักการ: validate ก่อน ถ้าผ่านค่อย Clone() state แล้วแก้สำเนา คืน ActionResult.Success
    //          ถ้าไม่ผ่านคืน ActionResult.Fail(เหตุผล)
    //
    // [TODO ขั้นถัดไป] implement ทีละ action เริ่มจาก TakeCoins:
    //   - ApplyTakeCoins   ← แกะกฎจาก GameController.Bank.cs OnResourceClicked
    //   - ApplyBuyCard     ← แกะจาก GameController.Cards.cs OnCardClicked
    //   - ApplyReserveCard ← แกะจาก ExecuteReserve
    //   - ApplyAnswerQuiz
    public static class GameRules
    {
        // ApplyAction รองรับเฉพาะ "turn action" (หยิบ/ซื้อ/จอง)
        //   ควิซเป็น subsystem แยก ใช้ QuizRules (ไม่จบเทิร์น + ตอบได้ทุกคน ไม่ใช่เฉพาะคนที่ถึงตา)
        //   db จำเป็นสำหรับ Buy/Reserve (TakeCoins ไม่ใช้ → ปล่อย null ได้)
        //   resolveTurn=true (ปกติ): apply เต็ม — เช็คขุนนาง + เงื่อนไขชนะ + เลื่อนเทิร์น (online/เต็มระบบ)
        //   resolveTurn=false: apply "เฉพาะ economy/การ์ด" ของ action นี้ ไม่แตะ noble/win/advance
        //     → ใช้ตอน drive ฝั่ง Unity ที่ยังให้ legacy EndTurn คุม noble/win/turn (กันนับซ้ำ)
        public static ActionResult ApplyAction(GameState state, GameAction action, ICardDatabase db = null, bool resolveTurn = true)
        {
            if (state == null) return ActionResult.Fail("state is null");
            if (action == null) return ActionResult.Fail("action is null");
            if (state.isGameOver) return ActionResult.Fail("เกมจบแล้ว");

            switch (action)
            {
                case TakeCoinsAction a:   return RequireTurn(state, a) ?? ApplyTakeCoins(state, a, resolveTurn);
                case BuyCardAction a:     return RequireTurn(state, a) ?? ApplyBuyCard(state, a, db, resolveTurn);
                case ReserveCardAction a: return RequireTurn(state, a) ?? ApplyReserveCard(state, a, db, resolveTurn);
                default:                  return ActionResult.Fail("ApplyAction รองรับเฉพาะ turn action (ควิซใช้ QuizRules)");
            }
        }

        // เช็คว่าเป็นตาของผู้สั่งจริง — คืน null ถ้าผ่าน, คืน Fail ถ้าไม่ใช่ตาเขา
        private static ActionResult RequireTurn(GameState s, GameAction a)
            => a.seat == s.CurrentSeat ? null : ActionResult.Fail("ยังไม่ถึงตาของผู้เล่นนี้");

        // ── หยิบเหรียญ ── (แกะกฎจาก GameController.Bank.cs OnResourceClicked + EndTurn commit)
        // กติกา: หยิบ "1-3 สีต่างกัน (สีละ 1)" หรือ "2 เหรียญสีเดียวกัน (กองสีนั้นต้อง ≥ 4)"
        //        ห้ามหยิบทองตรงๆ, ถือรวมต้องไม่เกิน 10, กองกลางต้องมีพอ
        private static ActionResult ApplyTakeCoins(GameState s, TakeCoinsAction a, bool resolveTurn = true)
        {
            int[] take = a.coins;
            if (take == null || take.Length != CoinIndex.TotalCount)
                return ActionResult.Fail("รูปแบบเหรียญไม่ถูกต้อง");

            for (int i = 0; i < take.Length; i++)
                if (take[i] < 0) return ActionResult.Fail("จำนวนเหรียญติดลบไม่ได้");

            if (take[CoinIndex.Gold] > 0)
                return ActionResult.Fail("หยิบเหรียญทองโดยตรงไม่ได้ (ต้องผ่านการจองการ์ด)");

            int total = 0, distinct = 0, maxPerColor = 0;
            for (int i = 0; i < CoinIndex.ColorCount; i++)
            {
                total += take[i];
                if (take[i] > 0) distinct++;
                if (take[i] > maxPerColor) maxPerColor = take[i];
            }

            if (total == 0) return ActionResult.Fail("ต้องหยิบอย่างน้อย 1 เหรียญ");

            bool isTwoSame = (distinct == 1 && maxPerColor == 2);              // 2 เหรียญสีเดียวกัน
            bool isDifferent = (maxPerColor == 1 && total <= 3);              // 1-3 สีต่างกัน (สีละ 1)
            if (!isTwoSame && !isDifferent)
                return ActionResult.Fail("หยิบได้แค่ 'สูงสุด 3 สีต่างกัน' หรือ '2 เหรียญสีเดียวกัน'");

            for (int i = 0; i < CoinIndex.ColorCount; i++)
                if (s.bankCoins[i] < take[i])
                    return ActionResult.Fail("กองกลางมีเหรียญสีนั้นไม่พอ");

            if (isTwoSame)
                for (int i = 0; i < CoinIndex.ColorCount; i++)
                    if (take[i] == 2 && s.bankCoins[i] < 4)
                        return ActionResult.Fail("หยิบ 2 อันไม่ได้ กองกลางสีนี้เหลือไม่ถึง 4");

            PlayerState player = s.CurrentPlayer;
            if (player.TotalCoins() + total > 10)
                return ActionResult.Fail("ถือเหรียญเกิน 10 อันไม่ได้");

            // ── ผ่านกติกา: apply บนสำเนา ──
            GameState next = s.Clone();
            PlayerState np = next.CurrentPlayer;
            for (int i = 0; i < CoinIndex.TotalCount; i++)
            {
                next.bankCoins[i] -= take[i];
                np.coins[i] += take[i];
            }

            var events = new List<GameEvent> { new GameEvent { type = "CoinsTaken", seat = a.seat } };
            if (resolveTurn) AdvanceTurn(next);
            return ActionResult.Success(next, events);
        }

        // เลื่อนไปผู้เล่นถัดไป (วนรอบ → ขึ้นรอบใหม่). win/noble/quiz check ค่อยเสริมตอน migrate buy/turn
        private static void AdvanceTurn(GameState s)
        {
            s.totalTurnCount++;
            if (s.playOrder == null || s.playOrder.Length == 0) return;
            s.currentPlayerIndex++;
            if (s.currentPlayerIndex >= s.playOrder.Length)
            {
                s.currentPlayerIndex = 0;
                s.currentRound++;
            }
        }

        // ── ซื้อการ์ด ── (แกะจาก GameController.Cards.cs OnCardClicked / BuyReservedCard)
        //   ต้นทุนจริงต่อสี = max(0, costs[i] - bonuses[i]); ส่วนที่เหรียญสีไม่พอจ่ายด้วย wildcard (ทอง)
        //   ถ้าซื้อจากกระดาน → ดึงการ์ดออก + จั่วใบใหม่ลงช่องเดิม; ถ้าจากที่จอง → ลบออกจากมือ
        private static ActionResult ApplyBuyCard(GameState s, BuyCardAction a, ICardDatabase db, bool resolveTurn = true)
        {
            if (db == null) return ActionResult.Fail("ไม่มี card database");
            if (string.IsNullOrEmpty(a.cardId)) return ActionResult.Fail("ไม่ระบุการ์ด");
            if (!db.TryGet(a.cardId, out CardInfo card)) return ActionResult.Fail("ไม่รู้จักการ์ดนี้");

            PlayerState player = s.CurrentPlayer;

            // หาว่าการ์ดอยู่ที่ไหน
            int boardSlot = -1;
            if (a.fromReserve)
            {
                if (!player.reservedCardIds.Contains(a.cardId))
                    return ActionResult.Fail("การ์ดนี้ไม่ได้อยู่ในการ์ดที่คุณจองไว้");
            }
            else
            {
                for (int k = 0; k < s.board.Length; k++)
                    if (s.board[k].cardId == a.cardId) { boardSlot = k; break; }
                if (boardSlot < 0) return ActionResult.Fail("ไม่พบการ์ดนี้บนกระดาน");
            }

            // เช็คจ่ายไหวไหม (wildcard = coins[Gold] รวม black)
            int missing = 0;
            for (int i = 0; i < CoinIndex.ColorCount; i++)
            {
                int actualCost = System.Math.Max(0, card.costs[i] - player.bonuses[i]);
                if (player.coins[i] < actualCost) missing += actualCost - player.coins[i];
            }
            if (missing > player.coins[CoinIndex.Gold])
                return ActionResult.Fail("เหรียญไม่พอ (รวมส่วนลดและทองแล้ว)");

            // ── ผ่าน: apply บนสำเนา ──
            GameState next = s.Clone();
            PlayerState np = next.CurrentPlayer;
            PayForCard(next, np, card);

            np.score += card.victoryPoints;
            if (card.bonusType >= 0 && card.bonusType < CoinIndex.ColorCount)
                np.bonuses[card.bonusType]++;

            if (a.fromReserve)
                np.reservedCardIds.Remove(a.cardId);
            else
                next.board[boardSlot].cardId = DrawCard(next, db, next.board[boardSlot].tier);

            var events = new List<GameEvent> { new GameEvent { type = "CardBought", seat = a.seat, cardId = a.cardId } };

            // resolveTurn=false → คืนผลแค่ economy/score(การ์ด)/bonus/การ์ด ให้ legacy EndTurn ทำ noble/win/advance ต่อ
            if (!resolveTurn)
                return ActionResult.Success(next, events);

            // เช็คขุนนางก่อนเช็คชนะ (ตรงกับ EndTurn เดิม: CheckClaim → EvaluateWinCondition)
            // แต้มขุนนางนับรวมในเงื่อนไขชนะด้วย
            CheckAndAwardNobles(next, np, events);

            // เช็คชนะทันที (ตรงกับ EndTurn เดิม) — ถ้าถึง winningScore จบเกม ไม่เลื่อนตา
            if (np.score >= next.winningScore)
            {
                next.isGameOver = true;
                next.winnerSeat = np.seat;
                events.Add(new GameEvent { type = "GameOver", seat = np.seat });
                return ActionResult.Success(next, events);
            }

            AdvanceTurn(next);
            return ActionResult.Success(next, events);
        }

        // ── จองการ์ด ── (แกะจาก GameController.Cards.cs ExecuteReserve) — จองจากกระดานเท่านั้น
        //   เก็บได้ ≤ 3 ใบ; ได้ทอง 1 ถ้ากองทองมี & ผู้เล่นถือ < 10; ดึงออก + จั่วใบใหม่; ไม่ชนะจากการจอง
        private static ActionResult ApplyReserveCard(GameState s, ReserveCardAction a, ICardDatabase db, bool resolveTurn = true)
        {
            if (db == null) return ActionResult.Fail("ไม่มี card database");
            if (string.IsNullOrEmpty(a.cardId)) return ActionResult.Fail("ไม่ระบุการ์ด");

            PlayerState player = s.CurrentPlayer;
            if (player.reservedCardIds.Count >= 3)
                return ActionResult.Fail("จองเพิ่มไม่ได้ มีการ์ดจองเต็ม 3 ใบแล้ว");

            int boardSlot = -1;
            for (int k = 0; k < s.board.Length; k++)
                if (s.board[k].cardId == a.cardId) { boardSlot = k; break; }
            if (boardSlot < 0) return ActionResult.Fail("ไม่พบการ์ดนี้บนกระดาน");

            GameState next = s.Clone();
            PlayerState np = next.CurrentPlayer;
            np.reservedCardIds.Add(a.cardId);

            // ได้ทอง 1 (ทองจริง ไม่ใช่ quizBlack) ถ้ากองมี & ถือ < 10
            if (next.bankCoins[CoinIndex.Gold] > 0 && np.TotalCoins() < 10)
            {
                next.bankCoins[CoinIndex.Gold]--;
                np.coins[CoinIndex.Gold]++;
            }

            next.board[boardSlot].cardId = DrawCard(next, db, next.board[boardSlot].tier);

            var events = new List<GameEvent> { new GameEvent { type = "CardReserved", seat = a.seat, cardId = a.cardId } };
            if (resolveTurn) AdvanceTurn(next);
            return ActionResult.Success(next, events);
        }

        // เช็ค+มอบขุนนางทุกใบที่ผู้เล่นมีโบนัสครบ (claim ได้หลายใบในเทิร์นเดียว — ตรงกับ NobleManager.CheckClaim)
        private static void CheckAndAwardNobles(GameState s, PlayerState p, List<GameEvent> events)
        {
            if (s.nobles == null) return;
            foreach (var noble in s.nobles)
            {
                if (noble == null || noble.claimed) continue;

                bool canClaim = true;
                for (int b = 0; b < CoinIndex.ColorCount; b++)
                    if (p.bonuses[b] < noble.requiredBonuses[b]) { canClaim = false; break; }

                if (canClaim)
                {
                    noble.claimed = true;
                    noble.claimedBySeat = p.seat;
                    p.score += noble.victoryPoints;
                    events.Add(new GameEvent { type = "NobleClaimed", seat = p.seat, cardId = noble.nobleId });
                }
            }
        }

        // ── helper การจ่ายเหรียญซื้อการ์ด (ตรงกับ OnCardClicked + PlayerUI.SpendWildcardCoins) ──
        // เรียกหลังเช็คว่าจ่ายไหวแล้วเท่านั้น
        private static void PayForCard(GameState s, PlayerState p, CardInfo card)
        {
            for (int i = 0; i < CoinIndex.ColorCount; i++)
            {
                int actualCost = System.Math.Max(0, card.costs[i] - p.bonuses[i]);
                if (p.coins[i] < actualCost)
                {
                    int diff = actualCost - p.coins[i];
                    s.bankCoins[i] += p.coins[i];   // คืนเหรียญสีทั้งหมดที่มีของสีนี้
                    p.coins[i] = 0;
                    int goldReturned = SpendWildcard(p, diff); // จ่าย wildcard; black ก่อน; คืนเฉพาะทองจริง
                    s.bankCoins[CoinIndex.Gold] += goldReturned;
                }
                else
                {
                    p.coins[i] -= actualCost;
                    s.bankCoins[i] += actualCost;
                }
            }
        }

        // จ่าย wildcard (coins[Gold] รวม black) — ใช้ quizBlack ก่อน; คืน "จำนวนทองจริง" ที่ใช้ (กลับกองกลาง)
        // ตรงกับ PlayerUI.SpendWildcardCoins
        private static int SpendWildcard(PlayerState p, int amount)
        {
            if (amount <= 0) return 0;
            int avail = System.Math.Max(0, p.coins[CoinIndex.Gold]);
            int spend = System.Math.Min(amount, avail);
            int black = System.Math.Min(spend, p.quizBlackCoins);
            int gold = spend - black;
            p.quizBlackCoins -= black;
            p.coins[CoinIndex.Gold] -= spend;
            return gold;
        }

        // จั่วการ์ดแทนช่องที่ว่าง — deterministic (server-authoritative)
        //   เลือกจากกองของ tier นั้น ลบใบที่ใช้ไปแล้ว; ถ้าหมด → คืน null (ช่องว่าง)
        //   [หมายเหตุ] ใช้ seed = boardSeed + drawCounter (deterministic ใหม่ ชัดเจนกว่า formula เดิม)
        //   internal เพื่อให้ GameStateFactory ใช้ร่วม (กันเขียน logic จั่วซ้ำ)
        internal static string DrawCard(GameState s, ICardDatabase db, int tier)
        {
            var deck = db.GetTierCardIds(tier);
            if (deck == null) return null;

            var available = new List<string>();
            for (int i = 0; i < deck.Count; i++)
                if (!s.usedCardIds.Contains(deck[i])) available.Add(deck[i]);

            if (available.Count == 0) return null; // กองหมด → ช่องว่าง

            var rng = new System.Random(s.boardSeed + s.drawCounter);
            s.drawCounter++;
            string pick = available[rng.Next(available.Count)];
            s.usedCardIds.Add(pick);
            return pick;
        }
    }
}
