using System.Collections;
using UnityEngine;

// ============================================================
// GameController — ส่วน Bot AI execution
//   จัดการ coroutine สั่ง BotController.ExecuteTurn เมื่อถึงเทิร์นของบอท
//   - Offline single-player: บอทเล่นทุก seat ที่เป็นบอท
//   - Online (Shared Mode): บอทเล่นแทน "ผู้เล่นที่หลุด" (seat ที่ isBot=true)
//       รันเฉพาะ Authority (id ต่ำสุดที่ยังอยู่) เท่านั้น แล้ว publish ให้ทุกคน sync
//       ถ้า authority หลุด → คนถัดไปกลายเป็น authority แล้วรับช่วงรันบอทต่อเอง (เช็ค authority สดทุกครั้ง)
// ============================================================
public partial class GameController
{
    void EnsureBotController()
    {
        // สร้าง BotController ได้ทั้ง offline และ online (online ใช้เฉพาะตอน authority รันบอทแทนคนหลุด)
        if (botController == null) botController = GetComponent<BotController>();
        if (botController == null) botController = gameObject.AddComponent<BotController>();
    }

    // online: รันบอทแทนคนหลุดได้เฉพาะ Authority (เช็คสด เผื่อ authority เปลี่ยนระหว่างเกม)
    // offline: รันได้เสมอ (ไม่มี authority)
    bool CanRunBotLocally()
    {
        if (!isOnlineMatchMode) return true;
        return FusionManager.Instance != null && FusionManager.Instance.IsMasterClient;
    }

    bool IsCurrentPlayerBot()
    {
        if (players == null || players.Length == 0) return false;
        if (playOrder == null || playOrder.Length == 0) return false;
        if (currentPlayerIndex < 0 || currentPlayerIndex >= playOrder.Length) return false;

        int activePlayerIdx = playOrder[currentPlayerIndex];
        if (activePlayerIdx < 0 || activePlayerIdx >= players.Length) return false;

        // online: seat จะ isBot=true ก็ต่อเมื่อผู้เล่นคนนั้นหลุด (UpdateDisconnectedPlayerBotStatus)
        return players[activePlayerIdx] != null && players[activePlayerIdx].isBot;
    }

    void ScheduleBotTurnIfNeeded()
    {
        if (botTurnCoroutine != null) {
            StopCoroutine(botTurnCoroutine);
            botTurnCoroutine = null;
        }

        if (isGameOver || isWaitingForContinueAfterResult || !IsCurrentPlayerBot()) return;

        // online: เฉพาะ authority เท่านั้นที่รันบอทแทนคนหลุด (กันทุกเครื่องรันบอทพร้อมกันแล้วตีกัน)
        if (!CanRunBotLocally()) return;

        botTurnCoroutine = StartCoroutine(RunBotTurnAfterDelay());
    }

    IEnumerator RunBotTurnAfterDelay()
    {
        float delay = Random.Range(botTurnDelayMin, botTurnDelayMax);
        yield return new WaitForSeconds(delay);
        botTurnCoroutine = null;

        if (isGameOver || isWaitingForContinueAfterResult || !IsCurrentPlayerBot()) yield break;

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
