using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Game.Core;
using Game.Adapters;

// ============================================================
// GameController — ส่วนเชื่อม Game.Core (authoritative rules)
//
//   ขั้นแรกของการ wire: "shadow parity-check" หลัง feature flag useCoreValidation
//   • flag ปิด (ค่าเริ่มต้น) → เกมไม่เปลี่ยนอะไรเลย
//   • flag เปิด → ทุกครั้งที่ commit หยิบเหรียญ จะ build GameState จากสภาพจริง
//     แล้วรัน GameRules.ApplyAction เทียบผลกับ legacy → log PASS/MISMATCH
//   พิสูจน์ว่า core ให้ผลตรงกับเกมจริง โดยไม่เสี่ยงพังเกม ก่อนจะพลิกให้ core ขับจริง
// ============================================================
public partial class GameController
{
    [Header("---- Server-Authoritative (Game.Core) ----")]
    [Tooltip("เปิดเพื่อให้ Game.Core ตรวจคู่ขนานกับ logic เดิม (log parity) — ยังไม่ขับเกมจริง")]
    [SerializeField] private bool useCoreValidation = false;

    [Tooltip("เปิดเพื่อให้ Game.Core เป็นคนคำนวณผล TakeCoins จริง (เขียนผลกลับ) — turn/noble/online ยังเป็น legacy")]
    [SerializeField] private bool useCoreDrive = false;

    private ICardDatabase _coreCardDb;
    private ICardDatabase CoreCardDb => _coreCardDb ??= new CardDatabaseAdapter();

    // ── shadow parity สำหรับ action ที่มี side-effect เยอะ (ซื้อ/จอง) ──
    //   เก็บ "ผลทำนายของ core" ตอนเริ่ม action แล้วเทียบกับ "ผลจริงของ legacy" หลังจบ
    //   เทียบเฉพาะ bank/coins/score/bonus/reserved (ข้าม board เพราะ draw RNG ต่าง, ข้าม turn/noble เพราะจังหวะต่าง)
    private GameState _shadowPrediction;
    private string _shadowLabel;

    // เรียกตอน "ก่อน" legacy เริ่มแก้สถานะ (มี state เดิมอยู่)
    private void ShadowPredict(GameAction action, string label)
    {
        _shadowPrediction = null;
        _shadowLabel = label;
        GameState pre = BuildCoreGameState();
        ActionResult result = GameRules.ApplyAction(pre, action, CoreCardDb);
        if (!result.ok)
        {
            GameLog.Log($"[CoreParity][{label}] core ปฏิเสธตอนทำนาย: {result.error}");
            return;
        }
        _shadowPrediction = result.next;
    }

    // เรียกตอน "หลัง" legacy ทำ action เสร็จ (รวม EndTurn) — เทียบผล
    private void ShadowCompareAfterAction()
    {
        if (_shadowPrediction == null) return;
        GameState legacyNext = BuildCoreGameState();
        var opts = new DiffOptions { ignoreBoardCards = true, ignoreTurn = true, ignoreNobles = true };
        List<string> diffs = GameStateDiff.Diff(_shadowPrediction, legacyNext, opts);
        GameLog.Log(diffs.Count == 0
            ? $"[CoreParity][{_shadowLabel}] PASS — core ตรงกับ legacy (bank/coins/score/reserved)"
            : $"[CoreParity][{_shadowLabel}] MISMATCH: {string.Join(" | ", diffs)}");
        _shadowPrediction = null;
    }

    // สร้าง GameState (pure) จากสภาพเกมปัจจุบัน — อ่าน field ของ GameController/PlayerUI/board ตรงๆ
    private GameState BuildCoreGameState()
    {
        int playerCount = players != null ? players.Length : 0;
        var ps = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            PlayerUI pu = players[i];
            if (pu == null) { ps[i] = null; continue; }

            var reserved = new List<string>();
            if (pu.reservedCards != null)
                foreach (var c in pu.reservedCards)
                    if (c != null) reserved.Add(c.cardId);

            ps[i] = new PlayerState
            {
                seat = i,
                isBot = pu.isBot,
                score = pu.currentScore,
                coins = (int[])pu.coins.Clone(),
                quizBlackCoins = pu.quizBlackCoins,
                bonuses = (int[])pu.bonuses.Clone(),
                reservedCardIds = reserved,
            };
        }

        var board = new List<BoardSlot>();
        AppendTierSlots(board, tier1Container, 1);
        AppendTierSlots(board, tier2Container, 2);
        AppendTierSlots(board, tier3Container, 3);

        var nobles = new List<NobleState>();
        if (nobleManager != null && nobleManager.Active != null)
        {
            foreach (var nd in nobleManager.Active)
            {
                if (nd == null || nd.nobleData == null) continue;
                NobleData data = nd.nobleData;
                nobles.Add(new NobleState
                {
                    nobleId = data.nobleName,
                    requiredBonuses = (int[])data.requiredBonuses.Clone(),
                    victoryPoints = data.victoryPoints,
                });
            }
        }

        return new GameState
        {
            bankCoins = (int[])bankCoins.Clone(),
            players = ps,
            board = board.ToArray(),
            usedCardIds = new HashSet<string>(usedCardIds),
            boardSeed = boardRandomSeed,
            currentPlayerIndex = currentPlayerIndex,
            playOrder = (int[])playOrder.Clone(),
            currentRound = currentRound,
            totalTurnCount = totalTurnCount,
            winningScore = winningScore,
            nobles = nobles,
        };
    }

    private void AppendTierSlots(List<BoardSlot> board, Transform container, int tier)
    {
        if (container == null) return;
        foreach (Transform child in container)
        {
            CardDisplay cd = child.GetComponent<CardDisplay>();
            string id = (cd != null && cd.data != null) ? cd.data.cardId : null;
            board.Add(new BoardSlot(tier, id));
        }
    }

    // เทียบผล TakeCoins ของ core กับ legacy — เรียก "ก่อน" legacy commit เหรียญ
    private void ShadowValidateTakeCoins()
    {
        if (GetTotalPendingCoins() <= 0) return;

        GameState state = BuildCoreGameState();
        int seat = playOrder[currentPlayerIndex];
        var action = new TakeCoinsAction { seat = seat, coins = (int[])pendingCoins.Clone() };
        ActionResult result = GameRules.ApplyAction(state, action);

        if (!result.ok)
        {
            GameLog.Log($"[CoreParity][TakeCoins] MISMATCH: core ปฏิเสธ แต่ legacy รับ → err={result.error}");
            return;
        }

        bool ok = true;
        var sb = new StringBuilder();
        for (int i = 0; i < 6; i++)
        {
            int expectedBank = bankCoins[i] - pendingCoins[i];
            if (result.next.bankCoins[i] != expectedBank)
            { ok = false; sb.Append($" bank[{i}] core={result.next.bankCoins[i]} legacy={expectedBank};"); }

            int expectedPlayer = players[seat].coins[i] + pendingCoins[i];
            if (result.next.players[seat].coins[i] != expectedPlayer)
            { ok = false; sb.Append($" p[{i}] core={result.next.players[seat].coins[i]} legacy={expectedPlayer};"); }
        }

        GameLog.Log(ok
            ? "[CoreParity][TakeCoins] PASS — core ตรงกับ legacy"
            : $"[CoreParity][TakeCoins] MISMATCH:{sb}");
    }

    // ให้ core เป็นคนคำนวณผล TakeCoins แล้วเขียนผลกลับ (bank + เหรียญผู้เล่นปัจจุบัน)
    //   turn advance / noble / online publish ยังปล่อยให้ legacy EndTurn ทำต่อ
    //   คืน true = core จัดการแล้ว, false = core ปฏิเสธ → ให้ legacy commit แทน
    private bool DriveTakeCoinsViaCore()
    {
        GameState state = BuildCoreGameState();
        int seat = playOrder[currentPlayerIndex];
        var action = new TakeCoinsAction { seat = seat, coins = (int[])pendingCoins.Clone() };
        ActionResult result = GameRules.ApplyAction(state, action);

        if (!result.ok)
        {
            GameLog.Log($"[CoreDrive][TakeCoins] core ปฏิเสธ → fallback legacy. err={result.error}");
            return false;
        }

        RenderBankFromState(result.next);
        RenderPlayerCoinsFromState(seat, result.next.players[seat]);
        ClearPendingCoins();
        GameLog.Log("[CoreDrive][TakeCoins] applied by core (render-from-state)");
        return true;
    }

    // ── drive ซื้อการ์ด: ให้ core คำนวณ payment(เหรียญ/ทอง/black) + score(การ์ด) + bonus แล้วเขียนกลับ ──
    //   ใช้ resolveTurn:false → core ไม่แตะ noble/win/advance (ปล่อย legacy EndTurn ทำต่อ กันนับซ้ำ)
    //   ส่วนที่ยังเป็น legacy: ลบการ์ดบนกระดาน/จั่วใบใหม่ (board GameObject), reservedCards list, EndTurn
    //   คืน true = core เขียนผลแล้ว, false = core ปฏิเสธ → ให้ legacy คำนวณแทน (fallback)
    private bool DriveBuyViaCore(CardDisplay card, bool fromReserve)
    {
        if (card == null || card.data == null) return false;
        string label = fromReserve ? "BuyReserved" : "Buy";

        GameState state = BuildCoreGameState();
        int seat = playOrder[currentPlayerIndex];
        var action = new BuyCardAction { seat = seat, cardId = card.data.cardId, fromReserve = fromReserve };
        ActionResult result = GameRules.ApplyAction(state, action, CoreCardDb, resolveTurn: false);

        if (!result.ok)
        {
            GameLog.Log($"[CoreDrive][{label}] core ปฏิเสธ → fallback legacy. err={result.error}");
            return false;
        }

        // เขียนกลับเฉพาะ economy ของผู้เล่นปัจจุบัน + กองกลาง (score การ์ดรวมแล้ว, ยังไม่รวมขุนนาง)
        RenderBankFromState(result.next);
        RenderPlayerCoinsFromState(seat, result.next.players[seat]);
        GameLog.Log($"[CoreDrive][{label}] applied by core (payment/score/bonus)");
        return true;
    }

    // ── drive จองการ์ด: ให้ core จัดการ "ได้ทอง 1 (ถ้ากองมี & ถือ<10)" + กองกลาง ──
    //   resolveTurn:false; ส่วน reservedCards list + spawn GameObject + จั่วการ์ด + EndTurn ยังเป็น legacy
    private bool DriveReserveViaCore(CardDisplay card)
    {
        if (card == null || card.data == null) return false;

        GameState state = BuildCoreGameState();
        int seat = playOrder[currentPlayerIndex];
        var action = new ReserveCardAction { seat = seat, cardId = card.data.cardId };
        ActionResult result = GameRules.ApplyAction(state, action, CoreCardDb, resolveTurn: false);

        if (!result.ok)
        {
            GameLog.Log($"[CoreDrive][Reserve] core ปฏิเสธ → fallback legacy. err={result.error}");
            return false;
        }

        RenderBankFromState(result.next);
        RenderPlayerCoinsFromState(seat, result.next.players[seat]);
        GameLog.Log("[CoreDrive][Reserve] applied by core (gold/bank)");
        return true;
    }

    // ════════════════════════════════════════════════════════════════════
    // render-from-state — เขียน GameState กลับเข้า UI/scene
    //   ใช้ตอน core ขับเกม (drive) และ online sync (host broadcast → client render) ภายหลัง
    //   ก้อน R1: bank + เหรียญ/โบนัส/คะแนนผู้เล่น + turn  (กระดาน/การ์ดจอง/ขุนนาง ทำก้อนถัดไป)
    // ════════════════════════════════════════════════════════════════════

    private void RenderBankFromState(GameState s)
    {
        if (s?.bankCoins == null || bankCoins == null) return;
        for (int i = 0; i < bankCoins.Length && i < s.bankCoins.Length; i++)
            bankCoins[i] = s.bankCoins[i];
        UpdateBankUI();
    }

    // เหรียญ/โบนัส/คะแนนของผู้เล่น 1 คน (ยังไม่รวมการ์ดที่จอง — ทำก้อนถัดไป)
    private void RenderPlayerCoinsFromState(int seat, PlayerState ps)
    {
        if (ps == null || players == null || seat < 0 || seat >= players.Length) return;
        PlayerUI pu = players[seat];
        if (pu == null) return;

        if (pu.coins != null && ps.coins != null)
            for (int i = 0; i < pu.coins.Length && i < ps.coins.Length; i++) pu.coins[i] = ps.coins[i];
        pu.quizBlackCoins = ps.quizBlackCoins;
        if (pu.bonuses != null && ps.bonuses != null)
            for (int b = 0; b < pu.bonuses.Length && b < ps.bonuses.Length; b++) pu.bonuses[b] = ps.bonuses[b];
        pu.currentScore = ps.score;
        if (pu.scoreText != null) pu.scoreText.text = ps.score.ToString();
        pu.UpdateUI();
    }

    // เทิร์น (index/round + visuals) — ใช้ตอน render full state; TakeCoins drive ยังให้ legacy คุม turn เอง
    private void RenderTurnFromState(GameState s)
    {
        if (s == null) return;
        currentPlayerIndex = s.currentPlayerIndex;
        currentRound = s.currentRound;
        totalTurnCount = s.totalTurnCount;
        UpdateTurnVisuals();
        UpdateTurnCountUI();
    }

    // ── R2: กระดาน + การ์ดที่จอง ──

    // วาดกระดานจาก BoardSlot[] — reuse RebuildTierIfChanged (มี anti-flicker + ช่องว่าง) จาก Network.cs
    private void RenderBoardFromState(GameState s)
    {
        if (s == null) return;

        // sync usedCardIds (กันจั่วซ้ำ)
        if (s.usedCardIds != null)
        {
            usedCardIds.Clear();
            foreach (var id in s.usedCardIds)
                if (!string.IsNullOrEmpty(id)) usedCardIds.Add(id);
        }

        var t1 = new List<string>();
        var t2 = new List<string>();
        var t3 = new List<string>();
        if (s.board != null)
            foreach (var slot in s.board)
            {
                string id = slot.cardId ?? string.Empty; // "" = ช่องว่าง (RebuildTierIfChanged เข้าใจ)
                if (slot.tier == 1) t1.Add(id);
                else if (slot.tier == 2) t2.Add(id);
                else if (slot.tier == 3) t3.Add(id);
            }

        RebuildTierIfChanged(tier3Container, t3.ToArray());
        RebuildTierIfChanged(tier2Container, t2.ToArray());
        RebuildTierIfChanged(tier1Container, t1.ToArray());
    }

    // วาดการ์ดที่จองของผู้เล่น 1 คน (clear + respawn) — mirror การ spawn ใน ExecuteReserve
    private void RenderPlayerReservedFromState(int seat, PlayerState ps)
    {
        if (ps == null || players == null || seat < 0 || seat >= players.Length) return;
        PlayerUI pu = players[seat];
        if (pu == null || pu.reservedAreaTransform == null) return;

        foreach (Transform child in pu.reservedAreaTransform) Destroy(child.gameObject);
        pu.reservedCards.Clear();

        if (ps.reservedCardIds == null) return;
        foreach (var id in ps.reservedCardIds)
        {
            CardData data = FindCardDataById(id);
            if (data == null) continue;

            pu.reservedCards.Add(data);
            GameObject resCard = Instantiate(cardPrefab, pu.reservedAreaTransform);
            resCard.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
            CardDisplay rd = resCard.GetComponent<CardDisplay>();
            if (rd != null)
            {
                rd.LoadCardData(data);
                rd.isReserved = true;
                rd.ownerUI = pu;
            }
        }
    }

    // [TODO R3] อัปเดต display ขุนนางที่ถูก claim (score ของผู้เล่นถูก render ถูกต้องแล้วผ่าน coins/score
    //           — เหลือแค่ซ่อน NobleDisplay ที่ claimed; ต้องเพิ่ม API ให้ NobleManager claim-by-id)
    private void RenderNoblesFromState(GameState s) { /* deferred */ }

    // composer: วาด state ทั้งหมด (ใช้ตอน drive ซื้อ/จอง และ online sync)
    private void RenderFromState(GameState s)
    {
        if (s == null) return;
        RenderBankFromState(s);
        if (players != null && s.players != null)
            for (int seat = 0; seat < players.Length && seat < s.players.Length; seat++)
            {
                RenderPlayerCoinsFromState(seat, s.players[seat]);
                RenderPlayerReservedFromState(seat, s.players[seat]);
            }
        RenderBoardFromState(s);
        RenderNoblesFromState(s);
        RenderTurnFromState(s);
    }
}
