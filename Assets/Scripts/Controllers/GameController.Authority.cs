using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Game.Core;

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

        for (int i = 0; i < 6; i++)
        {
            bankCoins[i] = result.next.bankCoins[i];
            players[seat].coins[i] = result.next.players[seat].coins[i];
        }
        players[seat].UpdateUI();
        ClearPendingCoins();
        GameLog.Log("[CoreDrive][TakeCoins] applied by core");
        return true;
    }
}
