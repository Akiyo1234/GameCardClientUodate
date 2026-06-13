# Server-Authoritative Architecture — Design (Step 1)

> สถานะ: **ดราฟต์ดีไซน์ + skeleton** (ยังไม่ wire เข้าเกมจริง เกมเดิมรันได้ปกติ)
> เป้าหมาย: ย้ายเกมจาก peer-authoritative (Fusion Shared Mode) → **authoritative**
> โดยเริ่มจากการแยก "กติกาเกม" ออกจาก Unity/UI ให้เป็น Game Core ที่ทดสอบได้

---

## ทำไมตอนนี้ server คุมเกมไม่ได้

State เกมกระจายอยู่ใน 3 ที่ที่ผูกกับ Unity:

1. **State ผู้เล่น** ฝังใน `PlayerUI` (MonoBehaviour): `coins[6]`, `bonuses[5]`,
   `currentScore`, `reservedCards`, `quizBlackCoins`
2. **State กระดาน** ฝังใน scene graph: การ์ดบนกระดาน = GameObject ใน
   `tier1/2/3Container` (state = ตัว object เอง) ← แกะยากสุด
3. **กติกาปนกับ UI**: validation เรียก `ShowWarning()`, `Destroy()`,
   `Instantiate()`, `UpdateUI()` สลับกันหมด

ข่าวดี: card database เป็น JSON อยู่แล้ว (`CardDatabaseLoader` → `cards_database.json`)
→ server โหลดไฟล์เดียวกันได้ ไม่ต้องพึ่ง ScriptableObject

---

## สถาปัตยกรรมเป้าหมาย — แยก 3 ชั้น

```
Presentation (MonoBehaviour)  PlayerUI, CardDisplay = "จอ" อย่างเดียว
                              render จาก state, แปลคลิก → Action
Authority (host/server)       เรียก ApplyAction() ตรวจ+อัปเดต state กลาง
Game Core (C# ล้วน)           GameState + Action + Rules   ← สร้างใหม่ (Assets/Scripts/Game/Core)
```

flow ปลายทาง:
```
click → build GameAction → (ส่งไป host) → host: ApplyAction(state, action)
      → broadcast newState → ทุก client RenderFrom(newState)
```

---

## โมเดลข้อมูล (ดู Assets/Scripts/Game/Core/)

- `GameState`  — สถานะแมตช์ทั้งหมด (bank, players, board, turn, nobles)
- `PlayerState`— ต่อ seat: coins[6], bonuses[5], score, reservedCardIds, quizBlackCoins
- `BoardSlot`  — 1 ช่องกระดาน: tier + cardId (string, ว่าง = ช่องว่าง)
- `GameAction` — TakeCoins / BuyCard / ReserveCard / AnswerQuiz
- `ActionResult` — ผลของการ apply: ok / error / next state / events
- `GameRules`  — `ApplyAction()` (pure, ยังไม่ implement)

**กุญแจ:** เก็บการ์ดเป็น `cardId` (string) อ้างไปที่ JSON database เดียวกันทั้งสองฝั่ง
ไม่เก็บ `CardData`/GameObject

`pendingCoins[]` (เหรียญเลือกค้าง) ไม่อยู่ใน state กลาง — เป็น UI staging ฝั่ง client
พอกดยืนยันค่อยยิงเป็น `TakeCoinsAction` ก้อนเดียว (atomic)

---

## แผนแกะโค้ด (logic เดิม → Game Core)

| Game Core | แกะมาจาก |
|---|---|
| กฎหยิบเหรียญ | `GameController.Bank.cs` `OnResourceClicked` (เอา validation ทิ้ง UI) |
| กฎซื้อ/จ่ายเหรียญ | `GameController.Cards.cs` `OnCardClicked` (loop `max(0,cost-bonus)` + จ่ายทอง) |
| กฎจอง | `ExecuteReserve` (≤3 ใบ, +ทอง 1) |
| จั่วการ์ด deterministic | `DrawNewCard` (ย้าย RNG/seed เข้า state) |
| เงื่อนไขชนะ + จบตา | `GameController.Turns.cs` `EndTurn` |
| ขุนนาง | `NobleManager.cs` |
| state ผู้เล่น | `PlayerUI.cs` fields |

---

## Roadmap

1. ✅ **แยก Game Core เป็น C# ล้วน** — state + action + rules (Assets/Scripts/Game/Core)
2. ✅ **กฎทุก action (pure + เทสต์ผ่าน 68 เคส ผ่าน dotnet):**
   - `TakeCoins` (18), `BuyCard` board/reserve + `ReserveCard` (30), Noble + Quiz (20)
   - เงื่อนไขชนะ (รวมแต้มขุนนาง), จั่ว deterministic, immutability
   - ควิซแยกเป็น `QuizRules` (round resolution: ผู้ชนะ = ถูก+เร็วสุด)
3. ⏳ **[ถัดไป] สร้าง adapter ฝั่ง Unity** — `ICardDatabase`/`IQuizDatabase` ครอบ `CardDatabaseLoader`/quiz DB
4. ⏳ **Wire เข้าเกมจริง** — UI สร้าง Action → host เรียก `ApplyAction`/`QuizRules` → render จาก state
5. ⏳ Reconnect/resume ตามมาเอง (state รวมศูนย์)
6. ⏳ (ถ้ามีเวลา) host เป็น headless dedicated server จริง

หมายเหตุ: เทสต์อยู่ใน Assets/Scripts/Game/Tests (NUnit, EditMode) — รันใน Unity Test Runner ได้
ตรวจกฎด้วย dotnet (นอก Unity) ทุกครั้งที่แก้ core ก็ได้เช่นกัน

---

## ข้อควรระวัง / การตัดสินใจ

- **งานหนักสุด = แกะ board ออกจาก scene graph** ทำ `BoardSlot[]` เป็น source of truth
  แล้วให้ scene render ตาม — เริ่มจาก `BuildBoardSnapshot` (Network.cs) ที่ทำคล้ายกันอยู่แล้ว
- **Game Core ต้องปลอด `UnityEngine` 100%** (ใช้ `System.Random` ไม่ใช่ `UnityEngine.Random`,
  เลี่ยง `Mathf`) → รันได้ทั้ง Fusion host (C#) และพอร์ตไป Supabase (TS) ได้ ไม่ปิดทางเลือก
- ที่ยังอยู่ใน MonoBehaviour: `ShowWarning`, แอนิเมชัน, เสียง, layout, spawn GameObject
  → กลายเป็นตัว render จาก state

---

## ดัชนีเหรียญ/โบนัส (อ้างอิงจากโค้ดเดิม)

```
coins index:   0=CPU 1=RAM 2=Network 3=Storage 4=Security 5=Gold(wildcard)
bonuses index: 0=CPU 1=RAM 2=Network 3=Storage 4=Security  (ไม่มี Gold)
```
