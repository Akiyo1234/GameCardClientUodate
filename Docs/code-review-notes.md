# Code Review Notes (2026-06-17)

รีวิวโค้ดส่วน gameplay / networking / security / state — บันทึกไว้เพื่อตามแก้

---

## 🔴 ควรปรับจริง (เชิงสถาปัตยกรรม)

### 1. Online sync = "last-writer-wins" → desync (เหรียญ/การ์ดหาย)
**ไฟล์:** `GameController.Network.cs` — `ApplyEconomySnapshot`, `ApplyBoardSnapshot`

ทุก client broadcast **state เต็มก้อน** (economy + board) แล้วฝั่งรับ **เขียนทับดิบๆ** ไม่มี version/authority คุมว่า snapshot ไหนใหม่กว่า
- อาการ: client A หยิบเหรียญ → snapshot เก่าจาก B (build ก่อน A หยิบ) ลอยมาทีหลัง → ทับเหรียญ A หาย
- = ตรงกับบั๊ก **"หยิบเหรียญแล้วไม่ได้เหรียญใน online"** ที่เจอ
- board sync ก็โมเดลเดียวกัน (RebuildTierIfChanged กันกระพริบได้ แต่ไม่กัน revert)

**วิธีแก้:** ✅ **ทำแล้ว (2026-06-19) — version guard** ที่ economy + board snapshot
- ติด `Version = totalTurnCount` (logical clock ที่ sync อยู่แล้ว) ไปกับ snapshot (FusionManager struct + payload ต่อท้าย backward-compatible)
- ฝั่งรับ (`HandleOnlineEconomyStateReceived`/`BoardStateReceived`) **ข้าม snapshot ที่ `Version < _lastApplied`** → snapshot เก่า revert ของใหม่ไม่ได้
- ปลอดภัย: topology ส่งครั้งเดียว/เครื่อง (ไม่ duplicate) → ไม่มี false-skip; ถ้า parse version พลาด → degrade เป็นพฤติกรรมเดิม
- มี log `[NetDiag] APPLY-ECON/BOARD SKIP stale ...` ให้เห็นตอนกันได้จริง
**สถานะ:** ต้องเทสต์ 2 เครื่องยืนยัน (ผมเทสต์ multiplayer เองไม่ได้). แก้สนิทระยะยาว = host-authority เต็ม (parked)

---

## 🟡 ข้อจำกัดที่ควรรู้ (ยังไม่ต้องแก้)

1. **GameLogger flush ตอน `OnApplicationQuit`** = fire-and-forget async → ปิดแอปดิบๆ HTTP อาจส่งไม่ทัน (log ก้อนท้ายตก). จบเกมปกติ (FlushNow ใน CheckWinCondition) ปลอดภัย เพราะแอปยังรัน — best-effort ยอมรับได้
2. **`[NetDiag]` logs** = ชั่วคราว spam ทุก publish/apply → **ถอดออกหลัง debug บั๊กเหรียญเสร็จ**
3. **submit-match-result: client ยังโกหกอันดับตัวเองได้** (placement=1) → จำกัดแล้วด้วย dedup (1 รางวัล/ห้องจริง/คน); แก้สนิทด้วย host-authority

---

## 🟢 จุดเล็ก (optional cleanup)

1. **client ควิซรายวันยังส่ง `p_user_id`/`p_reward_gems`** ไป RPC ที่ตอนนี้ ignore แล้ว (`PlayerDataService` SubmitDailyQuizAnswer/HasClaimed/FetchUnanswered) — ลบออกให้สะอาดได้ ไม่อันตราย
2. **`SubmitMatchResultAsync` ประกอบ JSON ด้วย string interpolation** (roomCode/roomId) — ปลอดภัยเพราะค่าเป็น alphanumeric แต่ใช้ serializer จะเป๊ะกว่า
3. **GameLogger.Enqueue อ่าน `PlayerPrefs.GetString` ทุกครั้ง** — ถูกมาก cache ได้

---

## ✅ ตรวจแล้วโอเค (ไม่มีปัญหา)

- **กฎหยิบเหรียญ** (`GameController.Bank.cs` OnResourceClicked) — 1-3 สี / 2 สีเดียว, ลิมิต 10, กองพอ — ตรงกับ Game.Core ✓
- **`MmrCalculator.cs` == สูตรใน edge function** (25/-25, 25/-5/-20, 30/10/-10/-25, Clamp max 0) — client/server ตรงกัน ✓
- **`PlayerUI.cs`** — `SpendWildcardCoins` (จ่าย black ก่อน คืนทองจริง) + `AddQuizBlackCoin` ตรงกับ Game.Core ✓
- **บอท** (`GameController.Bots.cs`) — รันเฉพาะ authority (online), เช็ค authority สดหลัง delay, กันทุกเครื่องรันชนกัน ✓
- **`QuizManager` timeout (#3)** — แก้แล้ว (~5 วิ → fallback cache/JSON) ✓
- **GameLogger buffer/flush** — guard `_flushing` กัน overlap, MaxBufferGuard กันบวม, ไม่ล็อกอิน→ข้าม, main-thread ล้วน ✓
- **Backend security** — ทุกตารางเปิด RLS, RPC ใช้ auth.uid() หมด, gem/mmr grant คุมฝั่ง server, daily-quiz RPC + submit-match-result hardened แล้ว ✓
- **action hooks (Log→DB)** — null-guard + capture ค่าก่อน Destroy/Clear ทุกจุด ✓
