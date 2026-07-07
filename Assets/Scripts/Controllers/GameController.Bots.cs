using System.Collections;
using UnityEngine;

// ============================================================
// GameController — ส่วน Bot AI execution
//   จัดการ coroutine สั่ง BotController.ExecuteTurn เมื่อถึงเทิร์นของบอท
//   - Offline single-player: บอทเล่นทุก seat ที่เป็นบอท
//   - Online (Shared Mode): **ไม่มีบอทเล่นแทนคนหลุดแล้ว** (เอาออก 2026-06-26)
//       คนหลุด → seat ถูก mark isDisconnected (UpdateDisconnectedPlayerStatus) เพื่อบล็อก input + ใช้ตรวจ reconnect
//       (แยกจาก isBot ที่เป็นบอท AI offline — ดู PlayerUI.IsAbsent = isBot || isDisconnected)
//       แต่ "ไม่รันบอท" → เทิร์นของ seat ที่หลุดถูกข้ามด้วย Turn Timer (Update→ForceEndTurn) แทน
//       (ตรงดีไซน์ reconnect: ข้ามเทิร์น ไม่ใช่บอทเล่นจริง — กัน state เพี้ยนตอนเจ้าตัวกลับมา)
// ============================================================
public partial class GameController
{
    void EnsureBotController()
    {
        // สร้าง BotController ได้ทั้ง offline และ online (online ใช้เฉพาะตอน authority รันบอทแทนคนหลุด)
        if (botController == null) botController = GetComponent<BotController>();
        if (botController == null) botController = gameObject.AddComponent<BotController>();
    }

    // offline: รันบอทได้เสมอ (single-player มีบอทจริง)
    // online: ไม่รันบอทแล้ว — คนหลุดให้ Turn Timer ข้ามเทิร์นแทน (ไม่ให้บอทเล่นแทนคนหลุด)
    bool CanRunBotLocally()
    {
        return !isOnlineMatchMode;
    }

    // seat ที่กำลังเล่นอยู่ "ไม่มีคนจริงคุม" หรือไม่ (offline=บอท AI, online=คนหลุด)
    //   → ใช้บล็อก input ของ seat นั้น + ให้บอทเล่น (offline) / ข้ามเทิร์นด้วย timer (online)
    bool IsCurrentSeatAbsent()
    {
        if (players == null || players.Length == 0) return false;
        if (playOrder == null || playOrder.Length == 0) return false;
        if (currentPlayerIndex < 0 || currentPlayerIndex >= playOrder.Length) return false;

        int activePlayerIdx = playOrder[currentPlayerIndex];
        if (activePlayerIdx < 0 || activePlayerIdx >= players.Length) return false;

        return players[activePlayerIdx] != null && players[activePlayerIdx].IsAbsent;
    }

    void ScheduleBotTurnIfNeeded()
    {
        if (botTurnCoroutine != null) {
            StopCoroutine(botTurnCoroutine);
            botTurnCoroutine = null;
        }

        if (isGameOver || isWaitingForContinueAfterResult || !IsCurrentSeatAbsent()) return;

        // online: ไม่รันบอทเลย (CanRunBotLocally=false) — คนหลุดให้ Turn Timer ข้ามเทิร์นแทน
        //   offline เท่านั้นที่รันบอทจริง
        if (!CanRunBotLocally()) return;

        botTurnCoroutine = StartCoroutine(RunBotTurnAfterDelay());
    }

    IEnumerator RunBotTurnAfterDelay()
    {
        bool isTutorial = UnityEngine.Object.FindAnyObjectByType<TutorialManager>() != null;
        float minDelay = isTutorial ? tutorialBotTurnDelayMin : botTurnDelayMin;
        float maxDelay = isTutorial ? tutorialBotTurnDelayMax : botTurnDelayMax;
        float delay = Random.Range(minDelay, maxDelay);
        yield return new WaitForSeconds(delay);
        botTurnCoroutine = null;

        if (isGameOver || isWaitingForContinueAfterResult || !IsCurrentSeatAbsent()) yield break;

        // เช็ค authority ซ้ำ "สด" หลัง delay — เผื่อ authority หลุดระหว่างนี้แล้วเราไม่ใช่ authority แล้ว
        // (ถ้าเราเพิ่งกลายเป็น authority ก็จะผ่าน → รับช่วงรันบอทต่อ)
        if (!CanRunBotLocally()) yield break;

        EnsureBotController();
        if (botController == null) yield break;

        isExecutingBotTurn = true;
        try
        {
            botController.ExecuteTurn(playOrder[currentPlayerIndex]);
        }
        finally
        {
            isExecutingBotTurn = false;
        }
    }
}
