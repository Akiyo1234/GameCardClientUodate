using System.Collections.Generic;
using UnityEngine;

// ============================================================
// GameController — ส่วน Card interaction & board setup
//   • OnCardClicked, BuyReservedCard       → ซื้อการ์ด (board / reserved)
//   • PromptReserveCard, ConfirmReserve,
//     CancelReserve, ExecuteReserve        → flow การจองการ์ด
//   • PopulateBoard, DrawNewCard           → แจกการ์ดลงกระดาน Tier 1/2/3
//   • LoadCardDatabase                     → โหลด CardData จาก JSON
//   • ClearContainer                       → util ใช้ทั้ง board spawn และ network resync
// ============================================================
public partial class GameController
{
    // =============================================================================
    // OnCardClicked — เมื่อกดการ์ดบนกระดาน (Short-tap)
    // -----------------------------------------------------------------------
    // Flow: ตรวจ Guard → ช่วยคำนวณต้นทุนจริง (cost - bonuses, หรือจ่ายด้วยทอง)
    // ถ้าเหรียญพอ: Destroy การ์ด → จั่วใบใหม่ลงไปแทน → ผู้เล่นได้ Score + Bonus
    // ถ้าเหรียญไม่พอ: แสดงคำเตือน
    // =============================================================================
    public void OnCardClicked(CardDisplay card)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return;
        if (IsCurrentSeatAbsent() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอต");
            return;
        }

        if (GetTotalPendingCoins() > 0) {
            ShowWarning("คุณกำลังทำ 2 แอคชั่น! กรุณากดปุ่ม Clear เหรียญออกก่อนกดซื้อการ์ด");
            return;
        }

        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        int missingCoins = 0;

        for (int i = 0; i < 5; i++) {
            int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
            if (p.coins[i] < actualCost) {
                missingCoins += (actualCost - p.coins[i]);
            }
        }

        bool canAfford = (missingCoins <= p.coins[5]);

        if (canAfford) {
            if (useCoreValidation) ShadowPredict(new Game.Core.BuyCardAction { seat = playOrder[currentPlayerIndex], cardId = card.data.cardId, fromReserve = false }, "Buy");

            // [Game.Core] ถ้าเปิด drive ให้ core คำนวณ payment/score/bonus; ถ้า core ปฏิเสธ → fallback legacy
            bool driven = useCoreDrive && DriveBuyViaCore(card, fromReserve: false);
            if (!driven) {
                for (int i = 0; i < 5; i++) {
                    int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
                    if (p.coins[i] < actualCost) {
                        int diff = actualCost - p.coins[i];
                        bankCoins[i] += p.coins[i];
                        p.coins[i] = 0;

                        int goldCoinsReturned = SpendWildcardCoinsWithoutReturningQuizBlack(p, diff);
                        bankCoins[5] += goldCoinsReturned;
                    } else {
                        p.coins[i] -= actualCost;
                        bankCoins[i] += actualCost;
                    }
                }

                p.AddScore(card.data.victoryPoints);
                p.AddBonus(card.data.bonusType);
                p.UpdateUI();
            }

            Transform parentContainer = card.transform.parent;
            int tier = (parentContainer == tier3Container) ? 3 : (parentContainer == tier2Container) ? 2 : 1;
            int slotIndex = card.transform.GetSiblingIndex(); // จำช่องเดิมไว้ก่อนดึงการ์ดออก

            // [Log→DB] บันทึกแอคชั่น "ซื้อการ์ดจากกระดาน" ก่อนการ์ดถูกทำลาย
            GameLogger.Log("buy_card", new GameLogger.Payload()
                .Add("seat", playOrder[currentPlayerIndex]).Add("isBot", p.isBot)
                .Add("cardId", card.data.cardId).Add("tier", tier)
                .Add("vp", card.data.victoryPoints).Add("round", currentRound));

            // ดึงออกจาก container ก่อน Destroy (deferred) ไม่งั้น BuildBoardSnapshot จะนับใบที่กำลังถูกลบติดไปด้วย
            card.transform.SetParent(null);
            Destroy(card.gameObject);
            DrawNewCard(tier, parentContainer, slotIndex);

            ClearWarning();
            UpdateBankUI();
            EndTurn();
            if (useCoreValidation) ShadowCompareAfterAction();
        } else {
            ShowWarning("ซื้อการ์ดไม่ได้! เหรียญของคุณไม่พอ (รวมส่วนลดและทองแล้ว)");
        }
    }

    // =============================================================================
    // PromptReserveCard / ConfirmReserve / CancelReserve / ExecuteReserve
    // Flow การจองการ์ด (Long-press หรือปุ่ม Reserve):
    // -----------------------------------------------------------------------
    //   1. PromptReserveCard  → เปิด Confirm Panel
    //   2. ConfirmReserve     → ถ้ายืนยัน → ExecuteReserve
    //   3. CancelReserve      → ปิด Panel เฉยๆ
    //   4. ExecuteReserve     → เพิ่มการ์ดลงในมือ (จำได้ ≤ 3 ใบ)
    //                         → รับทอง (Gold) 1 อัน (ถ้าเหรียญในกลางมีเหลือ)
    //                         → Destroy การ์ดจากกระดาน + จั่วใบใหม่
    // =============================================================================
    public void PromptReserveCard(CardDisplay card)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return;
        if (IsCurrentSeatAbsent() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (GetTotalPendingCoins() > 0) {
            ShowWarning("ทำ 2 แอคชั่นไม่ได้! กรุณา Clear เหรียญก่อนจองการ์ด");
            return;
        }

        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        if (p.reservedCards.Count >= 3) {
            ShowWarning("จองเพิ่มไม่ได้! คุณมีการ์ดจองในมือเต็ม 3 ใบแล้ว");
            return;
        }

        pendingReserveCard = card;
        if (confirmReservePanel != null) {
            confirmReservePanel.SetActive(true);
        }
    }

    public void ConfirmReserve()
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (IsCurrentSeatAbsent() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (confirmReservePanel != null) confirmReservePanel.SetActive(false);
        if (pendingReserveCard != null) {
            ExecuteReserve(pendingReserveCard);
            pendingReserveCard = null;
        }
    }

    public void CancelReserve()
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (IsCurrentSeatAbsent() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (confirmReservePanel != null) confirmReservePanel.SetActive(false);
        pendingReserveCard = null;
    }

    private void ExecuteReserve(CardDisplay card)
    {
        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        if (useCoreValidation) ShadowPredict(new Game.Core.ReserveCardAction { seat = playOrder[currentPlayerIndex], cardId = card.data.cardId }, "Reserve");

        // [Game.Core] ถ้าเปิด drive ให้ core จัดการ bank/ทอง; ถ้า core ปฏิเสธ → fallback legacy
        bool driven = useCoreDrive && DriveReserveViaCore(card);

        // reservedCards list (ข้อมูลการ์ดจอง) จัดการฝั่ง legacy เสมอ — core ไม่ render รายการนี้
        p.reservedCards.Add(card.data);

        if (!driven) {
            int goldIndex = 5;
            int totalPlayerCoins = GetTotalPlayerCoins(playOrder[currentPlayerIndex]); // เปลี่ยนเป็นเช็คคนเล่นตามคิว
            if (bankCoins[goldIndex] > 0 && totalPlayerCoins < 10) {
                bankCoins[goldIndex]--; p.coins[goldIndex]++; p.UpdateUI();
            } else if (bankCoins[goldIndex] <= 0) {
                ShowWarning("จองสำเร็จ! แต่ไม่ได้เหรียญพิเศษ/เหรียญดำ (กองกลางหมด)");
            } else if (totalPlayerCoins >= 10) {
                ShowWarning("จองสำเร็จ! แต่ไม่ได้เหรียญพิเศษ/เหรียญดำ (คุณถือเหรียญเต็ม 10 อันแล้ว)");
            }
        }

        if (p.reservedAreaTransform != null) {
            GameObject resCard = Instantiate(cardPrefab, p.reservedAreaTransform);
            resCard.transform.localScale = new Vector3(0.6f, 0.6f, 1f);

            CardDisplay resDisplay = resCard.GetComponent<CardDisplay>();
            resDisplay.LoadCardData(card.data);
            resDisplay.isReserved = true;
            resDisplay.ownerUI = p;
        }

        Transform parentContainer = card.transform.parent;
        int tier = (parentContainer == tier3Container) ? 3 : (parentContainer == tier2Container) ? 2 : 1;
        int slotIndex = card.transform.GetSiblingIndex(); // จำช่องเดิมไว้ก่อนดึงการ์ดออก

        // [Log→DB] บันทึกแอคชั่น "จองการ์ด"
        GameLogger.Log("reserve_card", new GameLogger.Payload()
            .Add("seat", playOrder[currentPlayerIndex]).Add("isBot", p.isBot)
            .Add("cardId", card.data.cardId).Add("tier", tier).Add("round", currentRound));

        // ดึงออกจาก container ก่อน Destroy (deferred) ไม่งั้น BuildBoardSnapshot จะนับใบที่กำลังถูกลบติดไปด้วย
        card.transform.SetParent(null);
        Destroy(card.gameObject);
        DrawNewCard(tier, parentContainer, slotIndex);

        ClearWarning();
        UpdateBankUI();
        EndTurn();
        if (useCoreValidation) ShadowCompareAfterAction();
    }

    // แตะการ์ดที่จองไว้ → เปิด popup ดูแบบใหญ่ (ซื้อจากในปุ่มของ popup)
    // ถ้ายังไม่ได้ผูก cardPreviewPopup ใน Inspector → fallback เป็นพฤติกรรมเดิม (ซื้อทันที)
    public void ShowReservedCardPreview(CardDisplay card)
    {
        if (card == null || card.data == null) return;

        if (cardPreviewPopup != null)
        {
            cardPreviewPopup.Show(card);
        }
        else
        {
            BuyReservedCard(card);
        }
    }

    public void BuyReservedCard(CardDisplay card)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return;

        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว

        if (IsCurrentSeatAbsent() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }

        if (card.ownerUI != p) {
            ShowWarning("ไม่สามารถซื้อการ์ดจองของผู้เล่นอื่นได้!");
            return;
        }

        if (GetTotalPendingCoins() > 0) {
            ShowWarning("กรุณา Clear เหรียญก่อนกดซื้อการ์ด!");
            return;
        }

        int missingCoins = 0;
        for (int i = 0; i < 5; i++) {
            int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
            if (p.coins[i] < actualCost) {
                missingCoins += (actualCost - p.coins[i]);
            }
        }

        bool canAfford = (missingCoins <= p.coins[5]);

        if (canAfford) {
            if (useCoreValidation) ShadowPredict(new Game.Core.BuyCardAction { seat = playOrder[currentPlayerIndex], cardId = card.data.cardId, fromReserve = true }, "BuyReserved");

            // [Game.Core] ถ้าเปิด drive ให้ core คำนวณ payment/score/bonus; ถ้า core ปฏิเสธ → fallback legacy
            bool driven = useCoreDrive && DriveBuyViaCore(card, fromReserve: true);
            if (!driven) {
                for (int i = 0; i < 5; i++) {
                    int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
                    if (p.coins[i] < actualCost) {
                        int diff = actualCost - p.coins[i];
                        bankCoins[i] += p.coins[i]; p.coins[i] = 0;
                        int goldCoinsReturned = SpendWildcardCoinsWithoutReturningQuizBlack(p, diff);
                        bankCoins[5] += goldCoinsReturned;
                    } else {
                        p.coins[i] -= actualCost; bankCoins[i] += actualCost;
                    }
                }

                p.AddScore(card.data.victoryPoints);
                p.AddBonus(card.data.bonusType);
            }

            // reservedCards list (ข้อมูลการ์ดจอง) จัดการฝั่ง legacy เสมอ — core ไม่ render รายการนี้
            p.reservedCards.Remove(card.data);
            p.UpdateUI();

            // [Log→DB] บันทึกแอคชั่น "ซื้อการ์ดที่จองไว้" (tier จาก CardData — การ์ดจองไม่ได้อยู่ใน tier container แล้ว)
            GameLogger.Log("buy_reserved", new GameLogger.Payload()
                .Add("seat", playOrder[currentPlayerIndex]).Add("isBot", p.isBot)
                .Add("cardId", card.data.cardId).Add("tier", card.data.tier)
                .Add("vp", card.data.victoryPoints)
                .Add("round", currentRound));

            Destroy(card.gameObject);

            ClearWarning();
            UpdateBankUI();
            EndTurn();
            if (useCoreValidation) ShadowCompareAfterAction();
        } else {
            ShowWarning("การ์ดที่คุณจองไว้ยังไม่สามารถซื้อได้ เพราะเหรียญไม่พอ!");
        }
    }

    // ───────── Board setup ─────────

    // =============================================================================
    // PopulateBoard — เริ่มต้นกระดานเกมใหม่เอี่ยม (4 ใบต่อ Tier)
    // เรียกตอน Awake เพื่อให้เห็นการ์ดทันทีที่นำเข้าเกม
    // DrawNewCard — จั่วการ์ดออกจากกอง (1 ใบ) หลังซื้อ/จอง
    //
    // ระบบ Deterministic Seed (DrawNewCard):
    //   seed = usedCardIds.Count * 1000 + tier * 97 + totalTurnCount
    //   ทุกเครื่องใช้ seed เดียวกัน → จั่วการ์ดใบเดียวกันเสมอ
    //   usedCardIds ถูก sync ผ่าน BoardState snapshot = ทุกเครื่องตรงกัน
    // =============================================================================
    void PopulateBoard()
    {
        ClearContainer(tier3Container);
        ClearContainer(tier2Container);
        ClearContainer(tier1Container);
        for (int i = 0; i < 4; i++) {
            DrawNewCard(3, tier3Container);
            DrawNewCard(2, tier2Container);
            DrawNewCard(1, tier1Container);
        }
    }

    void DrawNewCard(int tier, Transform container, int slotIndex = -1)
    {
        List<CardData> masterDeck = tier == 3 ? tier3Cards : tier == 2 ? tier2Cards : tier1Cards;
        if (masterDeck == null || masterDeck.Count == 0) return;

        // สร้าง list การ์ดที่ยังไม่เคยถูกใช้
        List<CardData> availableCards = new List<CardData>();
        foreach (var card in masterDeck)
        {
            if (!usedCardIds.Contains(card.cardId)) availableCards.Add(card);
        }

        // ถ้าหมดกองแล้ว วาง "ช่องว่าง" ไว้แทน เพื่อรักษาตำแหน่งช่อง (กัน LayoutGroup ดันการ์ดที่เหลือเลื่อน)
        if (availableCards.Count == 0)
        {
            GameLog.Log($"[GameController] กอง Tier {tier} หมดแล้ว ไม่มีการ์ดให้สุ่มเพิ่ม — วางช่องว่างแทน");
            SpawnEmptyCardSlot(container, slotIndex);
            return;
        }

        // เลือกการ์ด 1 ใบจาก "กองของ tier นี้เท่านั้น" (availableCards มาจาก masterDeck ของ tier) → สุ่มในเทียร์ตัวเองเสมอ
        CardData selectedCard;
        if (isOnlineMatchMode)
        {
            // [Online] Synchronized Seed: ทุกเครื่องใช้ seed เดียวกัน → สุ่มได้การ์ดใบเดียวกันตอน pre-populate
            // boardRandomSeed (มาจากชื่อห้อง) = ฐานสุ่มประจำแมตช์ → ต่างกันทุกแมตช์ แต่ตรงกันทุกเครื่อง
            // ส่วนที่เหลืออิงจำนวนการ์ดที่ใช้ไปแล้ว/tier/เทิร์น ซึ่ง sync ผ่าน BoardState snapshot (host reconcile ตามมา)
            int deterministicSeed = boardRandomSeed + (usedCardIds.Count * 1000) + (tier * 97) + totalTurnCount;
            Random.State originalState = Random.state;
            Random.InitState(deterministicSeed);

            selectedCard = availableCards[Random.Range(0, availableCards.Count)];

            // คืนค่า Random state เดิมเพื่อไม่ให้กระทบการสุ่มอื่นๆ ในเกม
            Random.state = originalState;
        }
        else
        {
            // [Offline] สุ่มจริงจากกองของ tier นี้ → กระดานต่างกันทุกเกม (Random ถูก re-seed ใน Awake)
            selectedCard = availableCards[Random.Range(0, availableCards.Count)];
        }

        usedCardIds.Add(selectedCard.cardId); // บันทึกว่าใบนี้ถูกใช้แล้ว
        GameObject newCardObj = Instantiate(cardPrefab, container);
        newCardObj.GetComponent<CardDisplay>()?.LoadCardData(selectedCard);

        // วางการ์ดใหม่ที่ช่องเดิมของใบที่หายไป (ถ้าระบุมา) แทนการต่อท้าย
        if (slotIndex >= 0)
        {
            newCardObj.transform.SetSiblingIndex(slotIndex);
        }
    }

    /// <summary>โหลดข้อมูลการ์ดจาก cards_database.json อัตโนมัติ</summary>
    void LoadCardDatabase()
    {
        CardDatabaseLoader.EnsureLoaded();
        tier1Cards = CardDatabaseLoader.Tier1Cards;
        tier2Cards = CardDatabaseLoader.Tier2Cards;
        tier3Cards = CardDatabaseLoader.Tier3Cards;
        GameLog.Log($"[GameController] โหลดการ์ดจาก JSON สำเร็จ! T1:{tier1Cards.Count} T2:{tier2Cards.Count} T3:{tier3Cards.Count}");
    }

    /// <summary>
    /// base seed สุ่มกระดานสำหรับโหมดออนไลน์ — derive จากชื่อห้อง (Photon session)
    /// ทุกเครื่องในแมตช์เดียวกันได้ค่าเท่ากัน (กระดานตรงกัน) แต่คนละแมตช์ได้คนละค่า (กระดานไม่ซ้ำ)
    /// ใช้ hash แบบ deterministic เอง — ห้ามใช้ string.GetHashCode() เพราะ .NET สุ่มค่าต่างกันข้าม process/เครื่อง → จะ desync
    /// fallback = 0 ถ้ายังไม่มีชื่อห้อง (พฤติกรรมเดิม; BoardState snapshot reconcile ให้ตรงกันอยู่ดี)
    /// </summary>
    private int GetOnlineBoardSeed()
    {
        string sessionName = FusionManager.Instance != null ? FusionManager.Instance.CurrentSessionName : null;
        if (string.IsNullOrEmpty(sessionName)) return 0;

        unchecked
        {
            int hash = 17;
            foreach (char c in sessionName) hash = (hash * 31) + c;
            return hash & 0x7fffffff; // บังคับเป็นค่าบวก (กัน seed ติดลบ)
        }
    }

    // วาง "ช่องว่าง" (placeholder มองไม่เห็น กดไม่ได้) เพื่อรักษาตำแหน่ง slot ตอนกองการ์ดหมด
    // → การ์ดที่เหลือไม่เลื่อน และ board snapshot ยังเก็บตำแหน่งช่องว่างได้ (data == null → string.Empty)
    GameObject SpawnEmptyCardSlot(Transform container, int slotIndex = -1)
    {
        if (container == null || cardPrefab == null) return null;

        GameObject slot = Instantiate(cardPrefab, container);
        slot.name = "EmptyCardSlot";

        // ไม่มีข้อมูล → กดซื้อไม่ได้ (OnShortTap และบอทเช็ค data == null อยู่แล้ว)
        CardDisplay cd = slot.GetComponent<CardDisplay>();
        if (cd != null) cd.data = null;

        // ซ่อนทั้งใบแต่ "คงขนาดช่องไว้" ให้ HorizontalLayoutGroup → การ์ดที่เหลือไม่เลื่อน/ไม่ชนกัน
        // ใช้ CanvasGroup (alpha 0) แทนการปิด Image.enabled เพราะ Image ที่ถูก disable จะไม่ contribute layout size
        //   (ChildControlWidth อาจยุบช่องเหลือ 0) — CanvasGroup ซ่อนทุก graphic ในตัว + กันคลิกทะลุทั้งใบ โดยไม่แตะ layout
        CanvasGroup cg = slot.GetComponent<CanvasGroup>();
        if (cg == null) cg = slot.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false; // กันคลิก/กดค้าง (คุม CardDisplay + CardLongPress ในตัว)
        cg.interactable = false;

        if (slotIndex >= 0) slot.transform.SetSiblingIndex(slotIndex);
        return slot;
    }

    // ───────── Container util (used by board spawn + network resync) ─────────

    void ClearContainer(Transform c) {
        if (c == null) return;
        // เก็บลูกทั้งหมดก่อน แล้วค่อย detach+Destroy เพื่อไม่ให้ใบที่กำลังถูกลบ (deferred)
        // ยังถูกนับว่าอยู่ใน container ตอน BuildBoardSnapshot/rebuild ในเฟรมเดียวกัน
        List<Transform> children = new List<Transform>();
        foreach (Transform child in c) children.Add(child);
        foreach (Transform child in children) {
            child.SetParent(null);
            Destroy(child.gameObject);
        }
    }
}
