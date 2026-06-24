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

        // รอให้เกม Setup เสร็จสมบูรณ์
        yield return new WaitForSecondsRealtime(1.5f);

        // รอจนกว่า QuizManager จะเปิดหน้าควิซ
        yield return WaitUntilOrTimeout(() => QuizManager.Instance != null && QuizManager.Instance.quizPanel.activeInHierarchy, 30f, "WaitQuizPanel");
        if (tm.skipRequested) yield break;

        // ไฮไลท์ข้อที่ถูกต้อง
        QuizManager.Instance.HighlightCorrectAnswer();

        // Step 1: สอนตอบคำถาม
        RectTransform quizAnswer1 = GetCorrectAnswerButtonRect();
        tm.uiMask.FocusOn(null, "เมื่อเริ่มเกม จะมีคำถามขึ้นมาให้ตอบเพื่อแย่งลำดับเทิร์นกัน");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "โดยคนที่ตอบได้ไวสุดและถูกต้องจะได้เล่นคนแรกกับได้เหรียญพิเศษ");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "ส่วนคนที่ตอบถูกช้าลงมาก็จะได้เทิร์นถัดไปตามลำดับการตอบ");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "คนที่ตอบผิดจะอยู่ถัดมาจากคนที่ตอบถูก");
        yield return WaitUntilTap();
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(quizAnswer1, "ลองกดตอบข้อนี้ดูสิ");
        yield return WaitUntilOrTimeout(() => QuizManager.Instance.HasLocalPlayerAnswered, 120f, "WaitQuizAnswered");
        if (tm.skipRequested) yield break;

        // รอหน้าต่างควิซเปิด Result
        tm.uiMask.Hide();
        yield return WaitUntilOrTimeout(() => QuizManager.Instance.resultScreen != null && QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy, 30f, "WaitQuizResult");
        if (tm.skipRequested) yield break;

        // ไฮไลท์ปุ่ม Let's Go
        RectTransform letsGoBtn = QuizManager.Instance.resultScreen.quizActionButton.GetComponent<RectTransform>();
        tm.uiMask.FocusOn(letsGoBtn, "ยอดเยี่ยม! คุณตอบถูกและได้รับเหรียญพิเศษ กดปุ่ม Let's Go เพื่อเข้าสู่กระดานเกมเลย");
        
        yield return WaitUntilOrTimeout(() => !QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy, 60f, "WaitQuizClose");
        if (tm.skipRequested) yield break;
        tm.uiMask.Hide();

        // รอหน้าต่างควิซและ Result ปิดให้สนิท
        yield return WaitUntilOrTimeout(() => !QuizManager.Instance.quizPanel.activeInHierarchy && !QuizManager.Instance.resultScreen.quizResultPanel.activeInHierarchy, 30f, "WaitQuizClose");
        yield return new WaitForSecondsRealtime(0.5f); // รอให้แอนิเมชัน Result หายไปเผื่อไว้
        if (tm.skipRequested) yield break;

        tm.uiMask.FocusOn(null, "คุณได้เริ่มคนแรกและได้เหรียญพิเศษด้วย");
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
        tm.uiMask.FocusOn(coinBankMid, "เหรียญทรัพยากรจำเป็นสำหรับการซื้อการ์ด โดยเหรียญที่หยิบได้จะมี 5 แบบ และแบบพิเศษ 1 แบบ และสามารถหยิบได้พร้อมกัน 3 เหรียญ");
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

        // Step 6: ซื้อการ์ด
        RectTransform cardToBuy = GetTier1CardRect(0);
        CardDisplay cardDisplayToBuy = GetTier1CardDisplay(0);
        GrantCoinsForCard(cardDisplayToBuy?.data); // เสกเหรียญให้

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
        
        // หลังจากจองการ์ด เทิร์นจะเปลี่ยนอัตโนมัติ รอให้วนกลับมาตาเรา
        proxy.SetBotsEnabled(true);
        yield return WaitUntilOrTimeout(() => proxy.IsLocalPlayersTurn, 120f, "WaitPlayerTurnReserve");
        if (tm.skipRequested) yield break;

        proxy.SetBotsEnabled(false);
        proxy.ResetTurnTime();

        // --- Handle Round 5 Quiz Interruption ---
        yield return HandleQuizInterruption();
        if (tm.skipRequested) yield break;
        // ----------------------------------------

        RectTransform reservedArea = GetPlayerReservedAreaRect();
        tm.uiMask.FocusOn(reservedArea, "การ์ดที่จองจะมาอยู่ตรงนี้ และได้เหรียญพิเศษมา 1 เหรียญ โดยเหรียญพิเศษจะสามารถใช้แทนเหรียญอะไรก็ได้ในการซื้อการ์ด");
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
        else
        {
            tm.gameController.cardPreviewPopup.panel.SetActive(false); // ปิดหน้าต่างให้เลยเผื่อไม่มีปุ่ม
        }

        // Step 8: ลองให้รวบรวมเหรียญทรัพยากรอีกรอบ
        CardData reservedData = proxy.Players[0].reservedCards.Count > 0 ? proxy.Players[0].reservedCards[0] : null;
        if (reservedData != null)
        {
            if (clearBtnComp != null) clearBtnComp.interactable = true;
            string[] resNames = new string[] { "CPU", "RAM", "Network", "Storage", "Security" };
            
            bool pickedAny = false;
            
            // วนลูปสอนให้หยิบทีละเหรียญที่ขาด
            for (int i = 0; i < 5; i++)
            {
                int have = proxy.Players[0].coins[i] + proxy.Players[0].bonuses[i] + proxy.Players[0].coins[5];
                int missing = Mathf.Max(0, reservedData.costs[i] - have);
                
                while (missing > 0 && proxy.TotalPendingCoins < 3)
                {
                    Button btnComp = GetBankButtonCompByType(resNames[i]);
                    if (btnComp == null) break; 
                    
                    SetAllBankButtonsInteractable(false);
                    btnComp.interactable = true;

                    RectTransform btnR = btnComp.GetComponent<RectTransform>();
                    int currentPending = proxy.PendingCoins[i];
                    
                    if (!pickedAny)
                    {
                        tm.uiMask.FocusOn(btnR, "ลองรวบรวมเหรียญที่ขาดเพื่อมาซื้อการ์ดที่จองไว้");
                        pickedAny = true;
                    }
                    else
                    {
                        tm.uiMask.FocusOn(btnR, "และยังต้องใช้สีนี้เพิ่มอีก กดหยิบเหรียญนี้ด้วยครับ");
                    }
                    
                    yield return WaitUntilOrTimeout(() => proxy.PendingCoins[i] > currentPending, 60f, $"WaitPickCoin_{i}");
                    if (tm.skipRequested) yield break;
                    
                    missing--;
                }
            }
            SetAllBankButtonsInteractable(false); // ปิดปุ่มเหรียญทั้งหมดเมื่อหยิบเสร็จ
        }
        
        if (endTurnBtnComp != null) endTurnBtnComp.interactable = true;
        tm.uiMask.FocusOn(endTurnBtn, "กดจบเทิร์น");
        int prevIndex2 = proxy.CurrentPlayerIndex;
        yield return WaitUntilOrTimeout(() => proxy.CurrentPlayerIndex != prevIndex2, 120f, "WaitTurnEndClick2");
        if (tm.skipRequested) yield break;

        if (endTurnBtnComp != null) endTurnBtnComp.interactable = false;
        tm.uiMask.Hide();

        proxy.SetBotsEnabled(true);
        yield return WaitUntilOrTimeout(() => proxy.IsLocalPlayersTurn, 120f, "WaitPlayerTurn2");
        if (tm.skipRequested) yield break;

        proxy.SetBotsEnabled(false);
        proxy.ResetTurnTime();

        // --- Handle Quiz Interruption Before Step 9 ---
        yield return HandleQuizInterruption();
        if (tm.skipRequested) yield break;
        // ----------------------------------------------

        // Step 9: สอนซื้อการ์ดที่จอง
        // ไม่ต้องเสกเหรียญแล้ว เพราะให้ผู้เล่นเก็บเหรียญเองใน Step 8 จนพอซื้อ
        CardData reservedDataToBuy = proxy.Players[0].reservedCards.Count > 0 ? proxy.Players[0].reservedCards[0] : null;
        // แถมเหรียญให้เผื่อขาดจริงๆ กันบั๊ก
        if (reservedDataToBuy != null) GrantCoinsForCard(reservedDataToBuy);

        tm.uiMask.FocusOn(reservedArea, "ทรัพยากรครบแล้ว ลองกดที่การ์ดที่จองไว้");
        yield return WaitUntilOrTimeout(() => tm.gameController.cardPreviewPopup != null && tm.gameController.cardPreviewPopup.panel.activeInHierarchy, 120f, "WaitPreviewPopup");
        if (tm.skipRequested) yield break;
        
        tm.uiMask.FocusOn(null, "สามารถซื้อการ์ดที่จองได้ที่ตรงนี้");
        tm.uiMask.Hide();
        yield return WaitUntilOrTimeout(() => proxy.Players[0].reservedCards.Count == 0, 120f, "WaitBuyReserved");
        if (tm.skipRequested) yield break;

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
        if (tm.skipRequested) yield break;

        if (endTurnBtnComp != null) endTurnBtnComp.interactable = true;
        if (clearBtnComp != null) clearBtnComp.interactable = true;

        tm.uiMask.Hide();

        // คืนค่าเวลาเดิมก่อนจบ Tutorial (ป้องกันบั๊กถ้าหลุดออกไป)
        proxy.ExitTutorialMode();

        // จบ Tutorial โหลดหน้าเมนู
        PlayerPrefs.SetString("GameMode", "Normal"); // Reset mode
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu 1");
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
                if (btn != null) btn.GetComponent<Button>().interactable = state;
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
    
    private void GrantCoinsForCard(CardData data)
    {
        if (data == null || proxy.Players == null || proxy.Players.Length == 0) return;
        PlayerUI p = proxy.Players[0];
        if (p == null || p.coins == null || p.coins.Length < 5) return;

        p.coins[0] += Mathf.Max(0, data.costs[0] - p.coins[0]);
        p.coins[1] += Mathf.Max(0, data.costs[1] - p.coins[1]);
        p.coins[2] += Mathf.Max(0, data.costs[2] - p.coins[2]);
        p.coins[3] += Mathf.Max(0, data.costs[3] - p.coins[3]);
        p.coins[4] += Mathf.Max(0, data.costs[4] - p.coins[4]);
        
        p.UpdateUI();
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
