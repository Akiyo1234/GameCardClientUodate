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
2. ✅ **กฎทุก action** — TakeCoins, BuyCard(board/reserve), ReserveCard, Noble, Quiz(QuizRules)
3. ✅ **เครื่องมือ authority ครบ (pure, ~142 assertions ผ่าน dotnet / 70+ tests ใน Unity):**
   - `GameStateValidator` — ตรวจสถานะเป็นไปไม่ได้ (กันโกง/desync)
   - `GameStateDiff` — เทียบ state (parity/desync) มี ignore options
   - `GameStateCodec` / `GameActionCodec` — state/action ↔ byte[] (binary, สำหรับ online/DB)
   - `GameStateFactory.NewGame` — สร้าง state เริ่มต้น deterministic
4. ✅ **adapter ฝั่ง Unity** — `CardDatabaseAdapter`, `QuizDatabaseAdapter`
5. ✅ **wire บางส่วน (หลัง flag, ยืนยันในเกมแล้ว):**
   - shadow parity ทุก action (useCoreValidation) — PASS ในเกมจริง
   - DRIVE `TakeCoins` (useCoreDrive) — core คำนวณเหรียญจริง
   - render-from-state helpers (bank/players/board/reserved/turn)
6. ✅ **DRIVE ซื้อ/จอง (useCoreDrive)** — core คำนวณ payment/score(การ์ด)/bonus/ทองจอง จริง
   - กุญแจ: `GameRules.ApplyAction(..., resolveTurn:false)` → apply เฉพาะ economy ของ action
     **ไม่แตะ noble/win/advance** จึงไม่ต้อง refactor `EndTurn` (เลี่ยงนับซ้ำ): legacy EndTurn
     ยังคุม noble(CheckClaim)/win/advance/quiz/bot/publish เหมือนเดิม
   - แบ่งงาน: **core = เลขเงิน/แต้มการ์ด/โบนัส** (ส่วนกันโกง), **legacy = GameObject การ์ด
     (destroy/draw) + reservedCards list + turn flow**. มี fallback: core ปฏิเสธ → legacy คำนวณแทน
   - verified pure logic ด้วย dotnet (resolveTurn true/false ทั้ง buy/reserve/takecoins)
7. ⏳ **Online host-authority** — host เป็นเจ้าของ GameState จริง
8. ⏳ Reconnect/resume (state รวมศูนย์ + codec persist), headless server

---

## Online integration plan (ก้าวถัดไป — เครื่องมือ pure พร้อมหมดแล้ว)

```
[client]  คลิก → สร้าง GameAction → GameActionCodec.Serialize → ส่ง RPC ไป host
[host]    รับ bytes → GameActionCodec.Deserialize → GameRules.ApplyAction(state, action, cardDb)
          → (option) GameStateValidator.Validate กันโกง → GameStateCodec.Serialize(next)
          → broadcast bytes ให้ทุก client
[ทุกเครื่อง] รับ → GameStateCodec.Deserialize → RenderFromState(next)  (จอแสดงผลตาม state)
```

- host สร้าง state เริ่มต้นด้วย `GameStateFactory.NewGame(...)` ตอนเกมเริ่ม
- ส่วนที่ต้องเขียนใหม่ฝั่ง Unity: RPC transport (Fusion `byte[]`) + ตัวเลือก host
  (player authority = PlayerId ต่ำสุด หรือ headless server) + จุดเรียก RenderFromState
- ของพร้อมแล้ว: ApplyAction (มี resolveTurn), codec, factory, validator,
  **RenderFromState ครบ** (bank/players/board/reserved/turn/**nobles**), DRIVE take/buy/reserve
  - noble render = `NobleManager.ClaimByName` ซ่อน visual ใบ claimed โดยไม่บวกคะแนนซ้ำ
    (คะแนนมาจาก PlayerState.score), idempotent เรียกซ้ำได้
  - **RPC transport พร้อมแล้ว (inert)** ใน FusionManager:
    - `SendGameAction(byte[])` client→authority (base64 ของ GameActionCodec ห่อใน payload `GACT|…`)
    - `BroadcastGameState(byte[])` authority→clients (`GSTATE|…`)
    - events `GameActionReceived(senderId, bytes)` / `GameStateReceived(bytes)` — ยังไม่มีใคร subscribe
- ค้าง (ต้องเทสต์ 2 เครื่องจริง): wire GameController เข้า transport หลัง flag `useOnlineAuthority`:
  - client (ไม่ใช่ authority): intercept action → `GameActionCodec.Serialize` → `SendGameAction`
  - authority: subscribe `GameActionReceived` → `BuildCoreGameState` → `ApplyAction` (resolveTurn:true)
    → `RenderFromState` (ของตัวเอง) + `GameStateCodec.Serialize` → `BroadcastGameState`
  - client: subscribe `GameStateReceived` → `GameStateCodec.Deserialize` → `RenderFromState`
  - board replacement ออนไลน์ต้อง deterministic ผ่าน core (เลิกใช้ legacy RNG path)

หมายเหตุ: เทสต์อยู่ใน Assets/Scripts/Game/Tests (NUnit, EditMode) — รันใน Unity Test Runner ได้
ตรวจกฎด้วย dotnet ก็ได้ (csproj ชั่วคราว `<Compile Include=".../Game/Core/*.cs"/>` แล้ว `dotnet run`)

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
