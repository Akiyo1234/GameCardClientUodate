-- ============================================================
-- game_logs — เก็บ "เหตุการณ์ระหว่างแมตช์" สำหรับวิเคราะห์ในเล่ม thesis
--   ออกแบบ generic: event_type (string) + payload (jsonb) → เพิ่ม event ใหม่ได้
--   โดยไม่ต้องแก้ตาราง (ปรับตามที่อาจารย์อยากได้ทีหลังได้เลย)
--
--   user_id ตั้งจาก auth.uid() อัตโนมัติ (client ปลอมไม่ได้) + RLS insert/read เฉพาะของตัวเอง
--   ผู้วิจัยดึงข้อมูลรวมผ่าน service_role (Dashboard / SQL editor) ซึ่ง bypass RLS
-- ============================================================

create table if not exists public.game_logs (
    id          bigint generated always as identity primary key,
    created_at  timestamptz not null default now(),        -- เวลาที่ DB รับ (server)
    user_id     uuid not null default auth.uid()
                references auth.users(id) on delete cascade,
    room_id     text,                                       -- รหัสห้อง/แมตช์ (จาก MatchmakingRoomId)
    session_id  text,                                       -- รหัสรอบเปิดแอป (group log ของ session เดียว)
    event_type  text not null,                              -- เช่น take_coins / buy_card / reserve_card
    payload     jsonb not null default '{}'::jsonb,         -- รายละเอียด event (seat, cardId, coins, round, ...)
    client_ts   timestamptz                                 -- เวลาที่ event เกิดฝั่ง client
);

alter table public.game_logs enable row level security;

-- insert ได้เฉพาะแถวที่ user_id = ตัวเอง (กันยัด log ในนามคนอื่น)
drop policy if exists "game_logs insert own" on public.game_logs;
create policy "game_logs insert own"
    on public.game_logs for insert to authenticated
    with check (user_id = auth.uid());

-- อ่านได้เฉพาะ log ของตัวเอง (การวิเคราะห์รวมใช้ service_role)
drop policy if exists "game_logs read own" on public.game_logs;
create policy "game_logs read own"
    on public.game_logs for select to authenticated
    using (user_id = auth.uid());

-- index สำหรับ query วิเคราะห์
create index if not exists game_logs_room_idx        on public.game_logs (room_id);
create index if not exists game_logs_event_idx       on public.game_logs (event_type);
create index if not exists game_logs_user_created_idx on public.game_logs (user_id, created_at);
