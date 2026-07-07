# ระบบ Log ระหว่างเกม → Supabase (game_logs)

> เก็บ "เหตุการณ์ในแมตช์" ขึ้น Supabase เพื่อนำไปวิเคราะห์พฤติกรรมผู้เล่นในเล่ม thesis
> ออกแบบ **generic**: `event_type` (string) + `payload` (jsonb) → เพิ่ม event ใหม่ได้โดยไม่ต้องแก้ตาราง

---

## 1. ตาราง `game_logs`

| คอลัมน์ | ชนิด | หมายเหตุ |
|---|---|---|
| `id` | bigint (identity) | PK |
| `created_at` | timestamptz | เวลาที่ DB รับ (server, default now()) |
| `user_id` | uuid | ตั้งอัตโนมัติ = `auth.uid()` (client ปลอมไม่ได้) |
| `room_id` | text | รหัสห้อง/แมตช์ (จาก `MatchmakingRoomId`; offline = "offline") |
| `session_id` | text | รหัสรอบเปิดแอป (group log ของ session เดียวกัน) |
| `event_type` | text | ชนิดเหตุการณ์ (ดูตารางด้านล่าง) |
| `payload` | jsonb | รายละเอียดเหตุการณ์ |
| `client_ts` | timestamptz | เวลาที่เหตุการณ์เกิดฝั่ง client |

**RLS:** เปิดอยู่ — insert/select ได้เฉพาะแถวที่ `user_id = auth.uid()`
การวิเคราะห์รวม (ทุกผู้เล่น) ทำผ่าน **Dashboard SQL Editor / service_role** ซึ่ง bypass RLS

สร้างตารางจาก: `supabase/migrations/20260616_game_logs.sql` (สร้างบน project แล้ว)

---

## 2. Event catalog (รอบแรก — เฉพาะแอคชั่นในเกม)

| `event_type` | เกิดเมื่อ | `payload` |
|---|---|---|
| `take_coins` | จบเทิร์นแบบหยิบเหรียญ | `seat`(int), `isBot`(bool), `coins`(int[6]), `round`(int) |
| `buy_card` | ซื้อการ์ดจากกระดาน | `seat`, `isBot`, `cardId`(str), `tier`(int), `vp`(int), `round` |
| `reserve_card` | จองการ์ด | `seat`, `isBot`, `cardId`, `tier`, `round` |
| `buy_reserved` | ซื้อการ์ดที่จองไว้ | `seat`, `isBot`, `cardId`, `vp`, `round` |
| `quiz_answer` | จบควิซ (log ทุกคนที่ตอบจริง) | `seat`(int), `questionId`(str), `correct`(bool), `timeMs`(int) |

> `coins` index: `[0]=CPU [1]=RAM [2]=Network [3]=Storage [4]=Security [5]=Gold`
> `quiz_answer` ถูก log ที่ฝั่ง authority (offline-local / online-host) ที่เดียว → ไม่ซ้ำ; seat ของ remote/บอท อยู่ในนั้นครบ

---

## 3. พฤติกรรมการส่ง (GameLogger.cs)

- **batch flush** (ไม่ insert ทีละ event): ส่งเป็นก้อนเดียวเมื่อ
  - buffer ครบ **25** events, หรือ
  - ครบ **8 วินาที**, หรือ
  - **จบเกม** (CheckWinCondition) / **แอป pause** / **ปิดแอป**
- best-effort: ถ้ายังไม่ล็อกอิน หรือ flush ล้มเหลว → ข้าม (ไม่กระทบการเล่น, ไม่ retry)
- ปิดทั้งระบบได้ด้วย `GameLogger.Enabled = false`

---

## 4. ตัวอย่าง query วิเคราะห์ (รันใน Supabase SQL Editor)

```sql
-- สัดส่วนแอคชั่นที่ผู้เล่นเลือก (หยิบ vs ซื้อ vs จอง) — เฉพาะคนจริง
select event_type, count(*) as n
from game_logs
where (payload->>'isBot')::bool = false
group by event_type order by n desc;

-- การ์ด tier ไหนถูกซื้อบ่อยสุด
select (payload->>'tier')::int as tier, count(*) as buys
from game_logs
where event_type = 'buy_card'
group by tier order by buys desc;

-- การ์ดยอดนิยม (ซื้อบ่อยสุด 10 อันดับ)
select payload->>'cardId' as card_id, count(*) as buys
from game_logs
where event_type in ('buy_card','buy_reserved')
group by card_id order by buys desc limit 10;

-- เกมเฉลี่ยจบที่รอบเท่าไหร่ (ดูจากรอบสูงสุดต่อแมตช์)
select round(avg(max_round), 1) as avg_end_round
from (select room_id, max((payload->>'round')::int) as max_round
      from game_logs group by room_id) t;

-- แอคชั่นเฉลี่ยต่อแมตช์
select round(avg(cnt), 1) as avg_actions_per_match
from (select room_id, count(*) cnt from game_logs group by room_id) t;

-- เปรียบเทียบพฤติกรรม คนจริง vs บอท
select (payload->>'isBot')::bool as is_bot, event_type, count(*) n
from game_logs group by is_bot, event_type order by is_bot, n desc;
```

---

## 5. วิธีเพิ่ม event ใหม่ (ตอนอาจารย์อยากได้ข้อมูลเพิ่ม)

ไม่ต้องแก้ตาราง แค่เรียกที่จุดที่ต้องการ:

```csharp
GameLogger.Log("quiz_answer", new GameLogger.Payload()
    .Add("seat", seat).Add("questionId", qid)
    .Add("correct", isCorrect).Add("timeMs", ms));
```

รองรับชนิด: `string`, `int`, `bool`, `int[]` (เพิ่ม overload ใน `GameLogger.Payload` ได้ถ้าต้องการชนิดอื่น)
