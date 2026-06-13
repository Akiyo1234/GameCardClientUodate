using System.Collections.Generic;

namespace Game.Core
{
    // ผลของการ apply 1 action
    //   ok=false  → action ผิดกติกา (error อธิบายเหตุผล), state ไม่เปลี่ยน
    //   ok=true   → next คือ state ใหม่, events คือสิ่งที่เกิด (ให้ presentation เล่นเอฟเฟกต์/เสียง)
    public class ActionResult
    {
        public bool ok;
        public string error;
        public GameState next;
        public List<GameEvent> events = new List<GameEvent>();

        public static ActionResult Fail(string error) => new ActionResult { ok = false, error = error };

        public static ActionResult Success(GameState next, List<GameEvent> events = null) =>
            new ActionResult { ok = true, next = next, events = events ?? new List<GameEvent>() };
    }

    // เหตุการณ์ที่เกิดจาก action (เช่น "ซื้อการ์ดสำเร็จ", "ได้ขุนนาง", "จบเกม")
    // presentation ใช้ trigger แอนิเมชัน/เสียง — รายละเอียดค่อยเสริมตอนทำ vertical slice
    public class GameEvent
    {
        public string type;   // เช่น "CoinsTaken", "CardBought", "CardReserved", "GameOver"
        public int seat;
        public string cardId; // ถ้าเกี่ยวกับการ์ด
    }
}
