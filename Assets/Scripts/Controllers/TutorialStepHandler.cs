using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class TutorialStepHandler
{
    private TutorialManager tm;
    private TutorialGameProxy proxy;

    public TutorialStepHandler(TutorialManager tutorialManager)
    {
        tm = tutorialManager;
        proxy = new TutorialGameProxy();
    }

    public IEnumerator ExecuteTutorial()
    {
        // ยืดเวลาเทิร์นเพื่อไม่ให้ข้ามเทิร์นระหว่างรอผู้เล่นทำตามสอน
        proxy.EnterTutorialMode(tm.gameController);

        // try/finally ครอบทั้งหมด: ไม่ว่าจะจบแบบปกติ, กด Skip, หรือ coroutine โดนหยุดกลางคัน (scene ปิด/object ถูกทำลาย)
        // cleanup ท้ายนี้จะรันเสมอ กัน bug ค้าง turn timer, บอทถูกปิด, หรือ GameMode ค้างเป็น "Tutorial"
        try
        {
            yield return ExecuteTutorialSteps();
        }
        finally
        {
            if (tm.endTurnButton != null) tm.endTurnButton.interactable = true;
            if (tm.clearButton != null) tm.clearButton.interactable = true;
            if (tm.uiMask != null) tm.uiMask.Hide();
            if (QuizManager.Instance != null) QuizManager.Instance.timerFrozen = false;

            proxy.ExitTutorialMode();

            PlayerPrefs.SetString("GameMode", "Normal"); // Reset mode
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu 1");
        }
    }

    private IEnumerator ExecuteTutorialSteps()
    {
        // รอให้เกม Setup เสร็จสมบูรณ์
        yield return new WaitForSecondsRealtime(1.5f);

        // รอจนกว่า QuizManager จะเปิดหน้าควิซ
        yield return WaitUntilOrTimeout(() => QuizManager.Instance != null && QuizManager.Instance.quizPanel.activeInHierarchy, 30f, "WaitQuizPanel");
        if (tm.skipRequested) yield break;

        // ไฮไลท์ข้อที่ถูกต้อง
        QuizManager.Instance.HighlightCorrectAnswer();

        // หยุดนาฬิกาควิซจริงไว้ก่อน กันหมดเวลาระหว่างที่ผู้เล่นยังอ่านคำอธิบายไม่จบ (ยังไม่ถึงขั้นให้กดตอบ)
        QuizManager.Instance.timerFrozen = true;

        // Step 1: สอนตอบคำถาม
        RectTransform quizAnswer1 = GetCorrectAnswerButtonRect();
        tm.uiMask.FocusOn(null, "เมื่อเริ่มเกม จะมีคำถามขึ้นมาให้ตอบเพื่อแย่งลำดับเทิร์นกัน");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "โดยคนที่ตอบได้ไวสุดและถูกต้องจะได้เล่นคนแรกกับได้เหรียญพิเศษ/เหรียญดำ");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "ส่วนคนที่ตอบถูกช้าลงมาก็จะได้เทิร์นถัดไปตามลำดับการตอบ");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "คนที่ตอบผิดจะอยู่ถัดมาจากคนที่ตอบถูก");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        // อธิบายจบแล้ว ถึงขั้นให้กดตอบจริง ค่อยปล่อยนาฬิกาให้นับต่อ
        QuizManager.Instance.timerFrozen = false;

        tm.uiMask.FocusOn(quizAnswer1, "ลองกดตอบข้อนี้ดูสิ");
        yield return WaitUntilOrTimeout(() => QuizManager.Instance.HasLocalPlayerAnswered, 120f, "WaitQuizAnswered");
        if (tm.skipRequested) yield break;

        // รอหน้าต่างควิซเปิด Result
        tm.uiMask.Hide();
        yield return WaitUntilOrTimeout(() => QuizManager.Instance.resultScreen != null && QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy, 30f, "WaitQuizResult");
        if (tm.skipRequested) yield break;

        // ไฮไลท์ปุ่ม Let's Go
        RectTransform letsGoBtn = QuizManager.Instance.resultScreen.quizActionButton.GetComponent<RectTransform>();
        tm.uiMask.FocusOn(letsGoBtn, "ยอดเยี่ยม! คุณตอบถูกและได้รับเหรียญพิเศษ/เหรียญดำ กดปุ่ม Let's Go เพื่อเข้าสู่กระดานเกมเลย");
        
        yield return WaitUntilOrTimeout(() => !QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy, 60f, "WaitQuizClose");
        if (tm.skipRequested) yield break;
        tm.uiMask.Hide();

        // รอหน้าต่างควิซและ Result ปิดให้สนิท
        yield return WaitUntilOrTimeout(() => !QuizManager.Instance.quizPanel.activeInHierarchy && !QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy, 30f, "WaitQuizClose");
        yield return new WaitForSecondsRealtime(0.5f); // รอให้แอนิเมชัน Result หายไปเผื่อไว้
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "คุณได้เริ่มคนแรกและได้เหรียญพิเศษ/เหรียญดำด้วย โดยเหรียญพิเศษ/เหรียญดำสามารถใช้แทนเหรียญอะไรก็ได้ในการซื้อการ์ด");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        // Get buttons to control their interactable state
        Button endTurnBtnComp = tm.endTurnButton;
        Button clearBtnComp = tm.clearButton;

        if (endTurnBtnComp != null) endTurnBtnComp.interactable = false;
        if (clearBtnComp != null) clearBtnComp.interactable = false;

        // Step 2: สอนหยิบ 3 เหรียญ
        SetAllBankButtonsInteractable(false); // ปิดปุ่มเหรียญทั้งหมดก่อน
        RectTransform coinBankMid = GetCoinBankRect();
        tm.uiMask.FocusOn(coinBankMid, "เหรียญทรัพยากรจำเป็นสำหรับการซื้อการ์ด โดยเหรียญที่หยิบได้จะมี 5 แบบ และสามารถหยิบได้พร้อมกัน 3 เหรียญ ยกเว้นเหรียญพิเศษ/เหรียญดำไม่สามารถหยิบได้");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(coinBankMid, "ลองหยิบเหรียญทรัพยากรอย่างละเหรียญจากกองกลาง");
        SetAllBankButtonsInteractable(true); // เปิดให้กด
        
        yield return WaitUntilOrTimeout(() => proxy.TotalPendingCoins > 0, 120f, "WaitPickCoin1");
        yield return WaitUntilOrTimeout(() => proxy.TotalPendingCoins == 3, 120f, "WaitPickCoin3");
        if (tm.skipRequested) yield break;

        // Step 3: ยกเลิกเหรียญ
        SetAllBankButtonsInteractable(false); // ปิดปุ่มเหรียญไม่ให้กดเพิ่ม
        if (clearBtnComp != null) clearBtnComp.interactable = true; // เปิดให้กดยกเลิก
        RectTransform clearBtn = clearBtnComp != null ? clearBtnComp.GetComponent<RectTransform>() : null;
        tm.uiMask.FocusOn(clearBtn, "หากกดหยิบเหรียญผิดอัน สามารถกดยกเลิกได้ที่ปุ่มนี้");
        
        yield return WaitUntilOrTimeout(() => proxy.TotalPendingCoins == 0, 120f, "WaitClearCoins");
        if (tm.skipRequested) yield break;
        if (clearBtnComp != null) clearBtnComp.interactable = false; // ปิดกลับ

        // Step 4: หยิบ 2 เหรียญสีเดียวกัน
        RectTransform ramBtn = GetBankButtonByType("RAM");
        tm.uiMask.FocusOn(ramBtn, "และถ้าเหรียญแต่ละแบบ มีเท่ากับหรือมากกว่า 4 เหรียญ จะสามารถหยิบเหรียญแบบเดียวกันได้ 2 เหรียญ แต่จะหยิบได้แค่ 2 เหรียญเท่านั้น");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(ramBtn, "ลองหยิบเหรียญ RAM 2 เหรียญสิ");
        Button ramBtnComp = GetBankButtonCompByType("RAM");
        if (ramBtnComp != null) ramBtnComp.interactable = true; // เปิดแค่ปุ่ม RAM เท่านั้น
        
        // รอให้กดหยิบก่อน
        yield return WaitUntilOrTimeout(() => proxy.PendingCoins[1] > 0, 120f, "WaitPickRAM1");
        yield return WaitUntilOrTimeout(() => proxy.PendingCoins[1] == 2, 120f, "WaitPickRAM2");
        if (tm.skipRequested) yield break;
        
        SetAllBankButtonsInteractable(false); // ปิดกลับเมื่อหยิบเสร็จ

        // Step 5: จบเทิร์น
        if (endTurnBtnComp != null) endTurnBtnComp.interactable = true; // เปิดให้กดจบเทิร์น
        RectTransform endTurnBtn = endTurnBtnComp != null ? endTurnBtnComp.GetComponent<RectTransform>() : null;
        tm.uiMask.FocusOn(endTurnBtn, "กดจบเทิร์นเพื่อส่งต่อให้ผู้เล่นคนถัดไป");
        
        int prevIndex1 = proxy.CurrentPlayerIndex;
        yield return WaitUntilOrTimeout(() => proxy.CurrentPlayerIndex != prevIndex1, 120f, "WaitTurnEndClick1");
        if (tm.skipRequested) yield break;

        if (endTurnBtnComp != null) endTurnBtnComp.interactable = false; // ปิดระหว่างรอเทิร์น
        tm.uiMask.Hide(); // ซ่อนหน้ากากเพื่อให้เห็นบอทเล่น

        // เปิดบอทกลับมาเพื่อให้มันเล่นเทิร์นของตัวเองได้
        proxy.SetBotsEnabled(true);

        // รอให้วนกลับมาเป็นเทิร์นผู้เล่น
        yield return WaitUntilOrTimeout(() => proxy.IsLocalPlayersTurn, 120f, "WaitPlayerTurn");
        if (tm.skipRequested) yield break;

        proxy.SetBotsEnabled(false);
        proxy.ResetTurnTime();

        // Step 6: ซื้อการ์ด — เก็บเหรียญเองทีละเทิร์นตามปกติจนพอ แล้วค่อยซื้อ (ไม่เสกให้)
        //   ไม่ล็อกใบไว้: คำนวนใหม่ทุกเทิร์นว่าใบไหน "ใกล้ครบสุด" (ขาดเหรียญน้อยสุด) แล้วเล็งเก็บใบนั้น
        System.Func<CardData> step6Target = () =>
        {
            PlayerUI pt = (proxy.Players != null && proxy.Players.Length > 0) ? proxy.Players[0] : null;
            CardDisplay c = GetClosestAffordableTier1CardDisplay(pt);
            return c != null ? c.data : null;
        };

        yield return CollectOverTurnsUntilAffordable(step6Target,
            "ก่อนซื้อ ต้องมีทรัพยากรให้พอ ลองหยิบเหรียญสีนี้จากกองกลาง",
            "หยิบเหรียญสีนี้เพิ่มอีกให้พอซื้อ");
        if (tm.skipRequested) yield break;

        // ทรัพยากรพอแล้ว → เล็งใบที่ใกล้ครบสุด ณ ตอนนี้ (ปกติคือใบที่เพิ่งเก็บครบ) เพื่อซื้อ
        PlayerUI pBuy = (proxy.Players != null && proxy.Players.Length > 0) ? proxy.Players[0] : null;
        CardDisplay cardDisplayToBuy = GetClosestAffordableTier1CardDisplay(pBuy);
        if (cardDisplayToBuy == null) cardDisplayToBuy = GetTier1CardDisplay(0);
        RectTransform cardToBuy = cardDisplayToBuy != null ? cardDisplayToBuy.GetComponent<RectTransform>() : GetTier1CardRect(0);

        tm.uiMask.FocusOn(cardToBuy, "การซื้อการ์ดต้องกด 1 ครั้งที่การ์ดที่ต้องการซื้อ");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(cardToBuy, "ลองซื้อการ์ดใบนี้ดู ใช้ทรัพยากรจากที่เก็บมา");
        
        yield return WaitUntilOrTimeout(() => {
            if (tm.gameController.confirmReservePanel != null && tm.gameController.confirmReservePanel.activeInHierarchy)
            {
                tm.gameController.confirmReservePanel.SetActive(false);
                tm.uiMask.FocusOn(cardToBuy, "กรุณากดคลิกธรรมดา 1 ครั้งเพื่อซื้อการ์ดครับ ไม่ต้องกดค้าง");
            }
            return cardDisplayToBuy == null || cardDisplayToBuy.gameObject == null || !cardDisplayToBuy.gameObject.activeInHierarchy;
        }, 120f, "WaitBuyCard");
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "หลังจากการซื้อการ์ดแล้ว การ์ดแต่ละสีที่ซื้อมาจะสามารถลดการใช้ทรัพยากรของสีนั้นๆในการซื้อการ์ดใบอื่นได้");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.Hide();

        // หลังจากซื้อการ์ด เทิร์นจะเปลี่ยนอัตโนมัติ รอให้วนกลับมาตาเรา
        proxy.SetBotsEnabled(true);
        yield return WaitUntilOrTimeout(() => proxy.IsLocalPlayersTurn, 120f, "WaitPlayerTurnBuy");
        if (tm.skipRequested) yield break;

        proxy.SetBotsEnabled(false);
        proxy.ResetTurnTime();

        // Step 7: จองการ์ด
        RectTransform cardToReserve = null;
        int minCost = 999;
        if (tm.gameController != null && tm.gameController.tier1Container != null)
        {
            CardDisplay[] boardCards = tm.gameController.tier1Container.GetComponentsInChildren<CardDisplay>();
            foreach (var card in boardCards)
            {
                if (card != null && card.gameObject.activeInHierarchy && card.data != null)
                {
                    int cost = 0;
                    foreach(int c in card.data.costs) cost += c;
                    if (cost < minCost)
                    {
                        minCost = cost;
                        cardToReserve = card.GetComponent<RectTransform>();
                    }
                }
            }
        }
        if (cardToReserve == null) cardToReserve = GetTier1CardRect(1);
        int initialReserved = proxy.Players[0].reservedCards.Count;
        
        tm.uiMask.FocusOn(cardToReserve, "การจองการ์ดต้องกดค้างที่การ์ดที่ต้องการซื้อ");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(cardToReserve, "ลองจองการ์ดใบนี้ดู");

        // รอจน Popup จองโผล่ขึ้นมา
        yield return WaitUntilOrTimeout(() => tm.gameController.confirmReservePanel != null && tm.gameController.confirmReservePanel.activeInHierarchy, 120f, "WaitReservePopup");
        if (tm.skipRequested) yield break;

        UnityEngine.UI.Button[] popupButtons = tm.gameController.confirmReservePanel.GetComponentsInChildren<UnityEngine.UI.Button>();
        RectTransform confirmBtnRect = null;
        foreach (var btn in popupButtons)
        {
            if (btn.name.ToLower().Contains("confirm") || btn.name.ToLower().Contains("ok") || btn.name.ToLower().Contains("yes") || btn.name.ToLower().Contains("ตกลง"))
            {
                confirmBtnRect = btn.GetComponent<RectTransform>();
                break;
            }
        }
        if (confirmBtnRect == null && popupButtons.Length > 0) confirmBtnRect = popupButtons[0].GetComponent<RectTransform>();

        if (confirmBtnRect != null)
        {
            tm.uiMask.FocusOn(confirmBtnRect, "กดปุ่มตกลงเพื่อยืนยันการจอง");
        }
        else
        {
            tm.uiMask.Hide(); // ซ่อน Mask ให้กดปุ่มใน Popup ได้ถ้าหาปุ่มไม่เจอ
        }

        yield return WaitUntilOrTimeout(() => proxy.Players[0].reservedCards.Count > initialReserved, 120f, "WaitReserveConfirm");
        if (tm.skipRequested) yield break;

        tm.uiMask.Hide();
        
        // หลังจากจองการ์ด เทิร์นจะเปลี่ยนอัตโนมัติ รอให้วนกลับมาตาเรา — หรือหลุดออกทันทีถ้าควิซโผล่ก่อน
        // (ระหว่างควิซ IsLocalPlayersTurn ยังไม่ true จนกว่าจะตอบเสร็จ — ถ้ารอเฉยๆ จะพลาดจังหวะสอนตอบ)
        proxy.SetBotsEnabled(true);
        yield return WaitUntilOrTimeout(
            () => proxy.IsLocalPlayersTurn || (QuizManager.Instance != null && QuizManager.Instance.IsQuizActive),
            120f, "WaitPlayerTurnReserve");
        if (tm.skipRequested) yield break;

        // --- ควิซครบรอบโผล่พอดี → สอนตอบก่อน แล้วค่อยรอถึงตาเราจริง ---
        yield return HandleQuizInterruption();
        if (tm.skipRequested) yield break;
        yield return WaitUntilOrTimeout(() => proxy.IsLocalPlayersTurn, 60f, "WaitPlayerTurnAfterReserveQuiz");
        if (tm.skipRequested) yield break;
        // ----------------------------------------

        proxy.SetBotsEnabled(false);
        proxy.ResetTurnTime();

        RectTransform reservedArea = GetPlayerReservedAreaRect();
        tm.uiMask.FocusOn(reservedArea, "การ์ดที่จองจะมาอยู่ตรงนี้ และได้เหรียญพิเศษ/เหรียญดำมา 1 เหรียญ");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        // Step 7.5: สอนดูการ์ดที่จอง
        tm.uiMask.FocusOn(reservedArea, "กดคลิกสั้นๆ (ไม่ต้องกดค้าง) ที่พื้นที่จองเพื่อดูรายละเอียดของการ์ดที่จองไว้ได้เลย");
        yield return WaitUntilOrTimeout(() => tm.gameController.cardPreviewPopup != null && tm.gameController.cardPreviewPopup.panel.activeInHierarchy, 120f, "WaitPreviewPopupReserve");
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "หน้าต่างนี้จะบอกคุณว่าการ์ดที่จองไว้ต้องใช้ทรัพยากรอะไรบ้าง และสามารถซื้อได้จากหน้านี้");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        if (tm.gameController.cardPreviewPopup != null && tm.gameController.cardPreviewPopup.closeButton != null)
        {
            RectTransform closeBtnRect = tm.gameController.cardPreviewPopup.closeButton.GetComponent<RectTransform>();
            tm.uiMask.FocusOn(closeBtnRect, "กดที่ปุ่มกากบาทเพื่อปิดหน้าต่างนี้ไปก่อน");
            yield return WaitUntilOrTimeout(() => !tm.gameController.cardPreviewPopup.panel.activeInHierarchy, 60f, "WaitClosePreviewPopup");
            if (tm.skipRequested) yield break;
        }
        else if (tm.gameController.cardPreviewPopup != null)
        {
            tm.gameController.cardPreviewPopup.panel.SetActive(false); // ปิดหน้าต่างให้เลยเผื่อไม่มีปุ่ม
        }

        // Step 8: เก็บเหรียญเองทีละเทิร์นตามปกติ จนพอซื้อการ์ดที่จองไว้ (หยิบ 3/เทิร์น จบเทิร์น บอทเล่นสลับ วนจนพอ)
        //   การ์ดที่จองอยู่ในมือ ไม่มีใครแย่งได้ — วนเก็บเหรียญได้อย่างปลอดภัย
        CardData reservedData = proxy.Players[0].reservedCards.Count > 0 ? proxy.Players[0].reservedCards[0] : null;
        if (reservedData != null)
        {
            yield return CollectOverTurnsUntilAffordable(
                () => (proxy.Players != null && proxy.Players.Length > 0 && proxy.Players[0].reservedCards.Count > 0)
                        ? proxy.Players[0].reservedCards[0] : null,
                "ลองรวบรวมเหรียญที่ขาดเพื่อมาซื้อการ์ดที่จองไว้",
                "และยังต้องใช้สีนี้เพิ่มอีก กดหยิบเหรียญนี้ด้วยครับ");
            if (tm.skipRequested) yield break;
        }

        // Step 9: สอนซื้อการ์ดที่จอง (เหรียญที่ผู้เล่นเก็บเองใน Step 8 พอซื้อแล้ว — ไม่เสกให้)
        tm.uiMask.FocusOn(reservedArea, "ทรัพยากรครบแล้ว ลองกดที่การ์ดที่จองไว้");
        yield return WaitUntilOrTimeout(() => tm.gameController.cardPreviewPopup != null && tm.gameController.cardPreviewPopup.panel.activeInHierarchy, 120f, "WaitPreviewPopup");
        if (tm.skipRequested) yield break;
        
        // ป็อปอัปเปิดแล้ว → ชี้ไปที่ปุ่ม Buy ให้ผู้เล่นกดซื้อการ์ดที่จองเอง (ปุ่มอื่นถูกหน้ากากบังไว้)
        Button buyBtnComp = tm.gameController.cardPreviewPopup != null ? tm.gameController.cardPreviewPopup.buyButton : null;
        if (buyBtnComp != null)
            tm.uiMask.FocusOn(buyBtnComp.GetComponent<RectTransform>(), "กดปุ่ม Buy เพื่อซื้อการ์ดที่จองไว้ ด้วยเหรียญที่เก็บมา");
        else
        {
            // ไม่มี reference ปุ่ม Buy → ถอดหน้ากากให้ผู้เล่นกดซื้อเองในป็อปอัป
            tm.uiMask.FocusOn(null, "สามารถซื้อการ์ดที่จองได้ที่ตรงนี้");
            tm.uiMask.Hide();
        }
        yield return WaitUntilOrTimeout(() => proxy.Players[0].reservedCards.Count == 0, 120f, "WaitBuyReserved");
        if (tm.skipRequested) yield break;
        tm.uiMask.Hide(); // ซื้อเสร็จ → ถอดหน้ากากให้เห็นบอทเล่นเทิร์นถัดไป

        // รอจนบอทเล่นครบตาและกลับมาตาเรา
        proxy.SetBotsEnabled(true);
        yield return WaitUntilOrTimeout(() => proxy.IsLocalPlayersTurn, 120f, "WaitPlayerTurnBuyReserved");
        if (tm.skipRequested) yield break;

        proxy.SetBotsEnabled(false);
        proxy.ResetTurnTime();

        // Step 10: วิธีชนะเกม
        tm.uiMask.FocusOn(null, "คุณสามารถชนะเกมนี้ได้โดยที่รวบรวมดาวให้ครบ 20 ดวงก่อนผู้เล่นคนอื่น โดยดาวจะได้มา 2 วิธี");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "วิธีที่ 1 จากการซื้อการ์ดโดยการ์ดแต่ละใบจะมีดาวบอกอยู่ด้านขวาบนของการ์ด");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "วิธีที่ 2 จะได้จากการซื้อการ์ดแต่ละสีตามที่กำหนดได้ครบก่อนผู้เล่นคนอื่นจะได้ดาวตามที่กำหนดไว้ไป");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "ถ้าเข้าใจแล้วไปลองเล่นเกมจริงกันเลย หรือสามารถไปกดอ่านวิธีการเล่นได้ตรงหน้าเมนู");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break; // กด Skip ที่ step สุดท้าย = ไม่นับว่าผ่าน

        // เล่นจบครบทุก step จริง (ไม่ได้กด Skip) → บันทึกสถานะ "ผ่านฝึกสอน" ต่อบัญชี (fire-and-forget)
        //   เขียน local ทันทีก่อน await → finally จะรีโหลดเมนู แต่เมนูอ่าน flag ผ่านแล้ว จะไม่เด้งฝึกสอนซ้ำ
        _ = PlayerDataService.MarkTutorialCompletedAsync();
        // จบ Tutorial ตามปกติ - cleanup (คืนค่า turn timer, เปิดบอท, reset GameMode, กลับเมนู) ทำใน finally ของ ExecuteTutorial()
    }

    // Helper functions สำหรับหา RectTransform ปุ่มต่างๆ
    private RectTransform GetCorrectAnswerButtonRect() 
    { 
        if (QuizManager.Instance != null && QuizManager.Instance.answerButtons != null && QuizManager.Instance.answerButtons.Length > 0)
        {
            int idx = QuizManager.Instance.GetCorrectChoiceIndex();
            if (idx >= 0 && idx < QuizManager.Instance.answerButtons.Length)
            {
                return QuizManager.Instance.answerButtons[idx].GetComponent<RectTransform>();
            }
        }
        return null; 
    }
    
    private RectTransform GetCoinBankRect() 
    { 
        if (tm.gameController != null && tm.gameController.resourceBankContainer != null)
        {
            return tm.gameController.resourceBankContainer.GetComponent<RectTransform>();
        }
        return null; 
    }

    private RectTransform GetBankButtonByType(string resourceType)
    {
        if (tm.gameController != null && tm.gameController.bankButtons != null)
        {
            foreach (var btn in tm.gameController.bankButtons)
            {
                if (btn != null && btn.resourceType == resourceType)
                    return btn.GetComponent<RectTransform>();
            }
        }
        return null;
    }

    private Button GetBankButtonCompByType(string resourceType)
    {
        if (tm.gameController != null && tm.gameController.bankButtons != null)
        {
            foreach (var btn in tm.gameController.bankButtons)
            {
                if (btn != null && btn.resourceType == resourceType)
                    return btn.GetComponent<Button>();
            }
        }
        return null;
    }

    private void SetAllBankButtonsInteractable(bool state)
    {
        if (tm.gameController != null && tm.gameController.bankButtons != null)
        {
            foreach (var btn in tm.gameController.bankButtons)
            {
                if (btn == null) continue;
                Button btnComp = btn.GetComponent<Button>();
                if (btnComp != null) btnComp.interactable = state;
            }
        }
    }

    private RectTransform GetTier1CardRect(int index)
    {
        if (tm.gameController != null && tm.gameController.tier1Container != null && tm.gameController.tier1Container.childCount > index)
        {
            return tm.gameController.tier1Container.GetChild(index).GetComponent<RectTransform>();
        }
        return null;
    }

    private CardDisplay GetTier1CardDisplay(int index)
    {
        if (tm.gameController != null && tm.gameController.tier1Container != null && tm.gameController.tier1Container.childCount > index)
        {
            return tm.gameController.tier1Container.GetChild(index).GetComponent<CardDisplay>();
        }
        return null;
    }

    private RectTransform GetPlayerReservedAreaRect()
    {
        if (proxy.Players != null && proxy.Players.Length > 0 && proxy.Players[0] != null)
        {
            return proxy.Players[0].reservedAreaTransform != null ? proxy.Players[0].reservedAreaTransform.GetComponent<RectTransform>() : null;
        }
        return null;
    }
    
    // การ์ด tier-1 ที่ "ต้นทุนรวมน้อยสุด" บนกระดาน (ใช้เล็งใบที่ผู้เล่นเก็บเหรียญซื้อไหวง่ายสุด)
    private CardDisplay GetCheapestTier1CardDisplay()
    {
        if (tm.gameController == null || tm.gameController.tier1Container == null) return null;
        CardDisplay cheapest = null;
        int minCost = int.MaxValue;
        foreach (var card in tm.gameController.tier1Container.GetComponentsInChildren<CardDisplay>())
        {
            if (card == null || !card.gameObject.activeInHierarchy || card.data == null) continue;
            int cost = 0;
            foreach (int c in card.data.costs) cost += c;
            if (cost < minCost) { minCost = cost; cheapest = card; }
        }
        return cheapest;
    }

    // การ์ด tier-1 บนกระดานที่ "ใกล้ซื้อได้มากสุด" สำหรับผู้เล่น p (ขาดเหรียญจริงน้อยสุด ณ ตอนนี้)
    //   คำนวนใหม่ทุกเทิร์น — ผู้เล่นเก็บเหรียญไปเรื่อยๆ ใบที่ขาดน้อยสุดจะเปลี่ยนได้ จึงเล็งใบที่ใกล้ครบสุดเสมอ
    //   ไม่ล็อกใบไว้: ถ้าใบเดิมโดนบอทซื้อไป หรือมีใบอื่นใกล้ครบกว่า ก็สลับไปเล็งใบนั้นแทน
    private CardDisplay GetClosestAffordableTier1CardDisplay(PlayerUI p)
    {
        if (tm.gameController == null || tm.gameController.tier1Container == null) return null;
        if (p == null || p.coins == null || p.coins.Length < 6 || p.bonuses == null) return GetCheapestTier1CardDisplay();
        CardDisplay best = null;
        int bestShort = int.MaxValue, bestCost = int.MaxValue;
        foreach (var card in tm.gameController.tier1Container.GetComponentsInChildren<CardDisplay>())
        {
            if (card == null || !card.gameObject.activeInHierarchy || card.data == null) continue;
            int missing = 0, totalCost = 0;
            for (int i = 0; i < 5; i++)
            {
                int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
                if (p.coins[i] < actualCost) missing += actualCost - p.coins[i];
                totalCost += card.data.costs[i];
            }
            int shortfall = Mathf.Max(0, missing - p.coins[5]); // เหรียญที่ยังต้องหยิบจริง (หลังใช้เหรียญพิเศษ/เหรียญดำครอบ)
            // เลือกใบขาดน้อยสุด; เท่ากันเลือกใบต้นทุนรวมถูกกว่า (เก็บครบเร็วกว่า)
            if (shortfall < bestShort || (shortfall == bestShort && totalCost < bestCost))
            {
                bestShort = shortfall; bestCost = totalCost; best = card;
            }
        }
        return best;
    }

    // เช็คว่าผู้เล่นซื้อการ์ด c ไหวไหม (หัก bonus ส่วนลด + ใช้เหรียญพิเศษ/เหรียญดำครอบส่วนที่ขาด)
    // ตรรกะตรงกับ GameController.OnCardClicked
    private bool CanAffordCard(PlayerUI p, CardData c)
    {
        if (p == null || c == null || p.coins == null || p.coins.Length < 6) return false;
        int missing = 0;
        for (int i = 0; i < 5; i++)
        {
            int actualCost = Mathf.Max(0, c.costs[i] - p.bonuses[i]);
            if (p.coins[i] < actualCost) missing += actualCost - p.coins[i];
        }
        return missing <= p.coins[5];
    }

    // [โหมดสอน] พาผู้เล่นกดหยิบเหรียญจากกองกลางเพื่อสะสมไปซื้อการ์ด target — สอนให้ใช้โควตาต่อเทิร์นให้คุ้ม
    //   - เก็บให้เต็ม 3 เหรียญต่อเทิร์นเสมอ แม้พอซื้อแล้วก็เติมสีอื่นให้ครบโควตา (หยุดเมื่อครบ 3 / ถือครบ 10 / กองกลางหมด)
    //   - ลำดับเลือกสี: (1) สีที่ขาดสำหรับการ์ด (ไล่ 3 สีต่างกัน) → (2) fallback 2 เหรียญสีเดิมตอนเหลือขาดสีเดียว+ยังไม่พอซื้อ → (3) เติมสีอื่นให้ครบโควตา
    //   - หัก bonus ส่วนลด และเลี่ยงคอมโบผิดกติกา (หยิบ 2 เหรียญสีเดียวได้เฉพาะตอนยังไม่แตะสีอื่น + กองกลาง ≥4)
    //   - เปิดให้กดได้ทีละสีที่เลือก กันผู้เล่นหยิบผิด
    private IEnumerator GuideCollectForCard(CardData target, string firstHint, string moreHint)
    {
        if (target == null) yield break;
        PlayerUI p = (proxy.Players != null && proxy.Players.Length > 0) ? proxy.Players[0] : null;
        if (p == null) yield break;
        string[] resNames = { "CPU", "RAM", "Network", "Storage", "Security" };
        bool pickedAny = false;

        while (!tm.skipRequested)
        {
            // เหรียญที่ยังขาดของแต่ละสี (หัก bonus + นับ pending ที่กำลังหยิบด้วย) + จำนวนเหรียญที่ถืออยู่
            int[] lack = new int[5];
            int missingTotal = 0, heldTotal = 0;
            for (int i = 0; i < 5; i++)
            {
                int actualCost = Mathf.Max(0, target.costs[i] - p.bonuses[i]);
                lack[i] = Mathf.Max(0, actualCost - (p.coins[i] + proxy.PendingCoins[i]));
                missingTotal += lack[i];
            }
            for (int i = 0; i < 6; i++) heldTotal += p.coins[i];

            if (proxy.TotalPendingCoins >= 3) yield break;  // ใช้โควตาครบ 3 เหรียญแล้ว → จบเทิร์น
            // ลงแอคชั่น 'หยิบ 2 เหรียญสีเดียว' ไปแล้ว = จบแอคชั่นหยิบรอบนี้ (กติกาห้ามหยิบต่อ)
            for (int i = 0; i < 5; i++) if (proxy.PendingCoins[i] >= 2) yield break;
            // ถือเหรียญครบ 10 อันแล้ว หยิบเพิ่มไม่ได้ตามกติกา → ออกไปเก็บเข้ากระเป๋า
            if (heldTotal + proxy.TotalPendingCoins >= 10) yield break;

            bool affordable = missingTotal <= p.coins[5]; // เหรียญพิเศษ/เหรียญดำครอบส่วนที่เหลือได้ → พอซื้อแล้ว

            // เลือกสีที่จะหยิบ — เก็บให้เต็มโควตา 3 เหรียญต่อเทิร์นเสมอ (สอนให้ใช้โควตาให้คุ้ม):
            //   1) สีที่ยังขาดสำหรับการ์ดเป้าหมาย และยังไม่ได้แตะเทิร์นนี้ (ไล่ให้ครบ 3 สีต่างกัน)
            int pick = -1;
            bool needForCard = false;
            for (int i = 0; i < 5; i++)
            {
                if (lack[i] <= 0 || proxy.PendingCoins[i] != 0) continue;
                if (tm.gameController.bankCoins[i] > 0) { pick = i; needForCard = true; break; }
            }

            //   2) ยังไม่พอซื้อ + เหลือขาดแค่สีเดียว → fallback หยิบ 2 เหรียญสีเดิม (กองกลางสีนั้น ≥4 ตามกติกา)
            if (pick < 0 && !affordable && proxy.TotalPendingCoins == 1)
            {
                for (int i = 0; i < 5; i++)
                    if (proxy.PendingCoins[i] == 1 && lack[i] > 0 && tm.gameController.bankCoins[i] >= 4) { pick = i; needForCard = true; break; }
            }

            //   3) พอซื้อแล้ว/ไม่มีสีที่ขาดให้หยิบ แต่ยังไม่ครบโควตา 3 เหรียญ → เติมด้วยสีอื่นที่ยังหยิบได้
            //      (สอนให้ใช้โควตาต่อเทิร์นให้คุ้ม ไม่เหลือทิ้งไว้)
            if (pick < 0)
            {
                for (int i = 0; i < 5; i++)
                {
                    if (proxy.PendingCoins[i] != 0) continue;
                    if (tm.gameController.bankCoins[i] > 0) { pick = i; break; }
                }
            }

            if (pick < 0) yield break; // ไม่มีสีที่หยิบได้แล้ว → ออกไปเก็บเข้ากระเป๋า/จองการ์ด

            Button btnComp = GetBankButtonCompByType(resNames[pick]);
            if (btnComp == null) yield break;
            SetAllBankButtonsInteractable(false);
            btnComp.interactable = true;

            RectTransform btnR = btnComp.GetComponent<RectTransform>();
            int before = proxy.PendingCoins[pick];
            // สีที่ขาดจริง = ใช้ hint เรื่องซื้อการ์ด; สีที่เติมให้เต็มโควตา = ใช้ hint สอนใช้โควตาให้คุ้ม
            string hint = needForCard ? (pickedAny ? moreHint : firstHint)
                                      : "เก็บให้ครบ 3 เหรียญ ใช้โควตาต่อเทิร์นให้คุ้ม (กดหยิบเหรียญสีนี้เพิ่ม)";
            tm.uiMask.FocusOn(btnR, hint);
            pickedAny = true;

            yield return WaitUntilOrTimeout(() => proxy.PendingCoins[pick] > before || tm.skipRequested, 60f, "WaitCollectCoin");
            if (tm.skipRequested) yield break;
            if (proxy.PendingCoins[pick] <= before) yield break; // หยิบไม่สำเร็จ/หมดเวลา → ออกกันลูปค้าง
        }
    }

    // [โหมดสอน] พาผู้เล่นเก็บเหรียญ "ทีละเทิร์นตามปกติ" จนซื้อการ์ดไหว
    //   วนรอบละ: หยิบ ≤3 เหรียญที่ขาด → กดจบเทิร์น (เก็บเข้ากระเป๋าจริง) → บอทเล่นสลับ → รอวนกลับมา → เช็คควิซ
    //   targetFn: การ์ดเป้าหมาย ณ ตอนนั้น (การ์ดบนกระดานอาจโดนบอทซื้อไป → เล็งใบใหม่ได้)
    private IEnumerator CollectOverTurnsUntilAffordable(System.Func<CardData> targetFn, string firstHint, string moreHint)
    {
        Button endTurnBtnComp = tm.endTurnButton;
        Button clearBtnComp = tm.clearButton;
        RectTransform endTurnBtn = endTurnBtnComp != null ? endTurnBtnComp.GetComponent<RectTransform>() : null;

        int safety = 0;
        while (!tm.skipRequested && safety++ < 12)
        {
            // ควิซโบนัสอาจโผล่พอดีตอนเข้ามา/เพิ่งถึงตาเรา (ทั้งกรณีเก็บยังไม่ครบ และเก็บครบแล้ว) → สอนตอบให้จบก่อนเสมอ
            yield return HandleQuizInterruption();
            if (tm.skipRequested) yield break;

            PlayerUI p = (proxy.Players != null && proxy.Players.Length > 0) ? proxy.Players[0] : null;
            CardData target = targetFn != null ? targetFn() : null;
            if (p == null || target == null) yield break;
            if (CanAffordCard(p, target)) yield break; // ทรัพยากรพอซื้อแล้ว

            // หยิบเหรียญที่ขาดในเทิร์นนี้
            if (clearBtnComp != null) clearBtnComp.interactable = true;
            yield return GuideCollectForCard(target, firstHint, moreHint);
            if (tm.skipRequested) yield break;
            SetAllBankButtonsInteractable(false);
            if (clearBtnComp != null) clearBtnComp.interactable = false;

            if (proxy.TotalPendingCoins > 0)
            {
                // หยิบเหรียญได้ → กดจบเทิร์นเพื่อเก็บเข้ากระเป๋า
                if (endTurnBtnComp != null) endTurnBtnComp.interactable = true;
                tm.uiMask.FocusOn(endTurnBtn, "กดจบเทิร์นเพื่อเก็บเหรียญไว้");
                int prevIdx = proxy.CurrentPlayerIndex;
                yield return WaitUntilOrTimeout(() => proxy.CurrentPlayerIndex != prevIdx, 120f, "WaitCollectEndTurn");
                if (tm.skipRequested) yield break;
                if (endTurnBtnComp != null) endTurnBtnComp.interactable = false;
            }
            else if (CanReserveForWildcard(p))
            {
                // เหรียญสีที่ต้องการในกองกลางหมด → จองการ์ดเอาเหรียญพิเศษ/เหรียญดำมาใช้แทน (จองแล้วจบเทิร์นเอง)
                yield return GuideReserveForWildcard();
                if (tm.skipRequested) yield break;
            }
            else
            {
                yield break; // หยิบก็ไม่ได้ จองก็ไม่ได้ (มือเต็ม/กองกลางไม่มีเหรียญพิเศษ) → ออกกันค้าง
            }

            // บอทเล่นสลับตามปกติ — แต่เผื่อครบ 5 รอบมีควิซโบนัสโผล่ระหว่างเก็บเหรียญ ต้องสอนตอบก่อน
            tm.uiMask.Hide();
            proxy.SetBotsEnabled(true);
            // รอกลับมาตาเรา หรือหลุดออกทันทีถ้าควิซโผล่ก่อน
            //   (ระหว่างควิซ IsLocalPlayersTurn ยังไม่ true จนกว่าจะตอบเสร็จ — ถ้ารอเฉยๆ จะพลาดจังหวะสอนตอบ)
            yield return WaitUntilOrTimeout(
                () => proxy.IsLocalPlayersTurn || (QuizManager.Instance != null && QuizManager.Instance.IsQuizActive),
                120f, "WaitBackToPlayerCollect");
            if (tm.skipRequested) yield break;

            // ควิซโผล่ → สอนตอบก่อน (โหมดสอนตอบถูกเสมอ ได้เล่นคนแรก) แล้วรอจนถึงตาเราจริงก่อนวนเก็บเหรียญต่อ
            yield return HandleQuizInterruption();
            if (tm.skipRequested) yield break;
            yield return WaitUntilOrTimeout(() => proxy.IsLocalPlayersTurn, 60f, "WaitPlayerTurnAfterQuiz");
            if (tm.skipRequested) yield break;

            proxy.SetBotsEnabled(false);
            proxy.ResetTurnTime();
        }
    }

    // การ์ด tier-1 บนกระดานที่ "ต้นทุนรวมมากสุด" (ใช้เป็นใบสำหรับจองเอาเหรียญพิเศษ/เหรียญดำ — เก็บใบถูกไว้ซื้อ)
    private CardDisplay GetReservableTier1Card()
    {
        if (tm.gameController == null || tm.gameController.tier1Container == null) return null;
        CardDisplay best = null;
        int maxCost = -1;
        foreach (var card in tm.gameController.tier1Container.GetComponentsInChildren<CardDisplay>())
        {
            if (card == null || !card.gameObject.activeInHierarchy || card.data == null) continue;
            int cost = 0;
            foreach (int c in card.data.costs) cost += c;
            if (cost > maxCost) { maxCost = cost; best = card; }
        }
        return best;
    }

    // จองการ์ดเพื่อเอาเหรียญพิเศษ/เหรียญดำได้ไหม: มือยังไม่เต็ม 3 ใบ + กองกลางยังมีเหรียญพิเศษ + ถือเหรียญ <10 + มีการ์ดให้จอง
    private bool CanReserveForWildcard(PlayerUI p)
    {
        if (p == null || tm.gameController == null) return false;
        if (p.reservedCards.Count >= 3) return false;
        if (tm.gameController.bankCoins == null || tm.gameController.bankCoins[5] <= 0) return false;
        int total = 0;
        for (int i = 0; i < 6 && p.coins != null && i < p.coins.Length; i++) total += p.coins[i];
        if (total >= 10) return false;
        return GetReservableTier1Card() != null;
    }

    // [โหมดสอน] พาผู้เล่นจองการ์ด (กดค้าง → ยืนยัน) เพื่อรับเหรียญพิเศษ/เหรียญดำมาใช้แทนสีที่ขาด — จองแล้วจบเทิร์นเอง
    private IEnumerator GuideReserveForWildcard()
    {
        CardDisplay toReserve = GetReservableTier1Card();
        if (toReserve == null) yield break;
        RectTransform rt = toReserve.GetComponent<RectTransform>();
        int before = (proxy.Players != null && proxy.Players.Length > 0) ? proxy.Players[0].reservedCards.Count : 0;

        tm.uiMask.FocusOn(rt, "เหรียญสีที่ต้องการในกองกลางหมดแล้ว! ลองจองการ์ดใบนี้ (กดค้างที่การ์ด) เพื่อรับเหรียญพิเศษ/เหรียญดำมาใช้แทน");

        // รอ popup ยืนยันการจองโผล่
        yield return WaitUntilOrTimeout(() => tm.gameController.confirmReservePanel != null && tm.gameController.confirmReservePanel.activeInHierarchy, 120f, "WaitReserveWildcardPopup");
        if (tm.skipRequested) yield break;

        // ไฮไลต์ปุ่มยืนยัน
        UnityEngine.UI.Button[] popupButtons = tm.gameController.confirmReservePanel.GetComponentsInChildren<UnityEngine.UI.Button>();
        RectTransform confirmRt = null;
        foreach (var btn in popupButtons)
        {
            string n = btn.name.ToLower();
            if (n.Contains("confirm") || n.Contains("ok") || n.Contains("yes") || n.Contains("ตกลง"))
            {
                confirmRt = btn.GetComponent<RectTransform>();
                break;
            }
        }
        if (confirmRt == null && popupButtons.Length > 0) confirmRt = popupButtons[0].GetComponent<RectTransform>();
        if (confirmRt != null) tm.uiMask.FocusOn(confirmRt, "กดตกลงเพื่อยืนยันการจอง จะได้เหรียญพิเศษ/เหรียญดำมา 1 เหรียญ");
        else tm.uiMask.Hide();

        // รอจนจองสำเร็จ (reservedCards เพิ่มขึ้น = ได้เหรียญพิเศษ/เหรียญดำ + จบเทิร์นเอง)
        yield return WaitUntilOrTimeout(() => (proxy.Players != null && proxy.Players.Length > 0 && proxy.Players[0].reservedCards.Count > before), 120f, "WaitReserveWildcardConfirm");
    }

    private IEnumerator WaitUntilOrTimeout(System.Func<bool> condition, float timeout, string label)
    {
        float elapsed = 0f;
        while (!condition() && elapsed < timeout && !tm.skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (elapsed >= timeout && !tm.skipRequested)
            Debug.LogWarning($"[Tutorial] TIMEOUT at: {label}");
    }

    private IEnumerator WaitUntilClickOnRect(RectTransform rect, float timeout = 60f)
    {
        if (rect == null) {
            yield return new WaitForSecondsRealtime(2f); // Fallback ถ้าระบุปุ่มผิด
            yield break;
        }

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : (canvas != null ? canvas.worldCamera : Camera.main);

        bool clicked = false;
        float elapsed = 0f;
        
        while (!clicked && elapsed < timeout && !tm.skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            
            if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
            {
                Vector2 screenPos = Input.GetMouseButtonDown(0) ? (Vector2)Input.mousePosition : Input.GetTouch(0).position;
                Vector2 localPos;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPos, cam, out localPos))
                {
                    if (rect.rect.Contains(localPos))
                    {
                        clicked = true;
                    }
                }
            }
            yield return null;
        }
        
        if (elapsed >= timeout && !tm.skipRequested)
            Debug.LogWarning($"[Tutorial] WaitUntilClickOnRect timeout on {rect.name}");
    }

    private IEnumerator WaitUntilTap()
    {
        yield return new WaitForSecondsRealtime(0.5f);
        yield return new WaitUntil(() => (!Input.GetMouseButton(0) && Input.touchCount == 0) || tm.skipRequested);
        yield return new WaitUntil(() => Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) || tm.skipRequested);
    }

    private IEnumerator HandleQuizInterruption()
    {
        if (QuizManager.Instance != null && QuizManager.Instance.IsQuizActive)
        {
            int correctIdx = QuizManager.Instance.GetCorrectChoiceIndex();
            if (correctIdx >= 0 && correctIdx < QuizManager.Instance.answerButtons.Length)
            {
                RectTransform correctBtnRect = QuizManager.Instance.answerButtons[correctIdx].GetComponent<RectTransform>();
                tm.uiMask.FocusOn(correctBtnRect, "อ๊ะ! มีกระดานคำถามโบนัสโผล่มาพอดี ไม่ต้องห่วงเพราะในโหมดนี้คุณจะตอบถูกเสมอและได้เล่นเป็นคนแรก ลองกดปุ่มคำตอบดูสิ");
            }
            yield return WaitUntilOrTimeout(() => !QuizManager.Instance.IsQuizActive, 120f, "WaitQuizFinish");
            if (tm.skipRequested) yield break;

            // รอให้หน้าต่างสรุปผลโผล่มา
            yield return WaitUntilOrTimeout(() => QuizManager.Instance.resultScreen != null && QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy, 30f, "WaitQuizResult");
            if (tm.skipRequested) yield break;

            if (QuizManager.Instance.resultScreen != null && QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy)
            {
                RectTransform letsGoBtn = QuizManager.Instance.resultScreen.quizActionButton.GetComponent<RectTransform>();
                tm.uiMask.FocusOn(letsGoBtn, "ยอดเยี่ยม! กดปุ่ม Let's Go เพื่อเรียนรู้วิธีการเล่นในสเต็ปถัดไปต่อเลย");
                yield return WaitUntilOrTimeout(() => !QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy, 60f, "WaitQuizClose");
            }

            tm.uiMask.Hide();
            // รอให้ควิซปิดจริงๆ
            yield return WaitUntilOrTimeout(() => !QuizManager.Instance.quizPanel.activeInHierarchy && !QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy, 30f, "WaitQuizClose2");
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }
}
