-- ============================================================
-- Matchmaking cleanup — กันห้อง/คิวค้างสะสม (rooms ที่ค้าง status='waiting' ตลอดกาล)
--
-- สาเหตุ: claim_matchmaking_room insert rooms ด้วย status='waiting' แล้วไม่เคยปิด/ลบ
--         + matchmaking_queue สะสมแถว waiting/cancelled/matched ที่ไม่ถูกเก็บกวาด
-- วิธีแก้: ตั้ง pg_cron เก็บกวาดเป็นรอบ (ไม่ต้องรอให้มีคนค้นหาเพื่อ trigger lazy cleanup)
--
-- หมายเหตุ: ห้องที่มีผลแมตช์ผูกอยู่ "mark เป็น closed" ไม่ใช่ลบ
--           (match_results.room_id อ้าง rooms แบบ on delete cascade → ลบแล้วประวัติหาย)
--
-- รันใน Supabase SQL Editor (ต้องเปิด extension pg_cron ใน Dashboard → Database → Extensions ก่อน
--   หรือปล่อยให้บรรทัด create extension ด้านล่างเปิดให้ ถ้าโปรเจกต์อนุญาต)
-- รันซ้ำได้ (idempotent)
-- ============================================================

create extension if not exists pg_cron;

-- ── (A) เคลียร์ของค้างที่มีอยู่ตอนนี้ทันที ──
update public.rooms
set status = 'closed'
where status = 'waiting'
  and created_at < timezone('utc', now()) - interval '30 minutes';

-- ปิดห้องที่ค้าง playing (เกมไม่จบปกติ/คนออกหมดกลางเกม) — created_at เกิน 1 ชม.
--   (rooms ไม่มี updated_at → ใช้ created_at; 1 ชม. ยาวกว่าเกมจริงมาก กันปิดเกมที่ยังเล่นอยู่)
update public.rooms
set status = 'finished'
where status = 'playing'
  and created_at < timezone('utc', now()) - interval '1 hour';

update public.matchmaking_queue
set status = 'cancelled',
    room_code = null,
    room_id = null,
    matched_at = null
where status = 'waiting'
  and created_at < timezone('utc', now()) - interval '5 minutes';

delete from public.matchmaking_queue
where status in ('cancelled', 'matched')
  and created_at < timezone('utc', now()) - interval '1 day';

-- ปิดเกมที่ "active แต่ไม่มีความเคลื่อนไหว" (คนออกหมด/เกมตาย) — updated_at นิ่งเกิน 15 นาที
update public.match_sessions
set status = 'abandoned'
where status = 'active'
  and updated_at < timezone('utc', now()) - interval '15 minutes';

-- ── (C) ฟังก์ชันเก็บกวาด (รวม logic ไว้ที่เดียว ให้ cron เรียก) ──
create or replace function public.cleanup_matchmaking()
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_now timestamptz := timezone('utc', now());
begin
    -- ปิดห้องที่ค้าง waiting เกิน 30 นาที (mark ไม่ลบ → กันประวัติแมตช์หาย)
    update public.rooms
    set status = 'closed'
    where status = 'waiting'
      and created_at < v_now - interval '30 minutes';

    -- ปิดห้องที่ค้าง playing เกิน 1 ชม. (เกมไม่จบปกติ/คนออกหมดกลางเกม)
    update public.rooms
    set status = 'finished'
    where status = 'playing'
      and created_at < v_now - interval '1 hour';

    -- ยกเลิกแถวคิวที่ค้าง waiting เกิน 5 นาที (ผู้เล่นปิดแอประหว่างค้นหา ฯลฯ)
    update public.matchmaking_queue
    set status = 'cancelled',
        room_code = null,
        room_id = null,
        matched_at = null
    where status = 'waiting'
      and created_at < v_now - interval '5 minutes';

    -- ลบแถวคิวที่ตายแล้วเกิน 1 วัน (กันตารางบวม)
    delete from public.matchmaking_queue
    where status in ('cancelled', 'matched')
      and created_at < v_now - interval '1 day';

    -- ปิดเกมที่ active แต่ updated_at นิ่งเกิน 15 นาที (คนออกหมด/เกมตาย)
    --   ตั้งให้ยาวกว่า Photon PlayerTTL พอควร เพื่อให้ reconnect มีโอกาสก่อนโดนปิด
    update public.match_sessions
    set status = 'abandoned'
    where status = 'active'
      and updated_at < v_now - interval '15 minutes';
end;
$$;

grant execute on function public.cleanup_matchmaking() to service_role;

-- ── ตั้ง cron ทุก 15 นาที (idempotent: ลบ job เดิมก่อนถ้ามี) ──
do $$
begin
    perform cron.unschedule('mm-cleanup');
exception
    when others then null;  -- ยังไม่เคยตั้ง → ข้าม
end $$;

select cron.schedule(
    'mm-cleanup',
    '*/15 * * * *',
    $$ select public.cleanup_matchmaking(); $$
);
