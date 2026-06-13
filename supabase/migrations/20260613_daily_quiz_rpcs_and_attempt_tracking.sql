-- ════════════════════════════════════════════════════════════════════════════
-- 20260613_daily_quiz_rpcs_and_attempt_tracking.sql
--
-- จุดประสงค์:
--   1) นำ "ซอร์สจริง" ของ RPC ฝั่ง DB ที่ก่อนหน้านี้สร้างตรงใน Supabase Dashboard
--      (ไม่เคยถูกเก็บในเรปอ) กลับเข้ามา version control:
--        • has_claimed_daily_quiz_today(p_user_id uuid) -> boolean
--        • get_unanswered_daily_questions(p_user_id uuid) -> setof rows
--   2) แก้ schema drift: ตาราง daily_quiz_claims บน server จริงมีคอลัมน์
--      question_id (text, nullable) แต่ SCHEMA.sql ในเรปอไม่มี — เพิ่มให้ตรง
--
-- ที่มา: reverse-engineer จาก project "GameCardDatabase" (uwspzhwvjpkcjpoqgkhp)
--        ผ่าน pg_get_functiondef / information_schema เมื่อ 2026-06-13
--        ทุกคำสั่งเขียนแบบ idempotent (รันซ้ำได้ ไม่พัง)
-- ════════════════════════════════════════════════════════════════════════════

-- ── 1) schema drift: เพิ่มคอลัมน์ question_id ที่มีอยู่จริงบน server ──────────────
-- get_unanswered_daily_questions ใช้คอลัมน์นี้กรองข้อที่ผู้เล่นเคยตอบแล้ว
alter table public.daily_quiz_claims
    add column if not exists question_id text;

-- ── 2) RPC: เช็คว่าวันนี้ (เวลาไทย) รับ/ตอบควิซไปแล้วหรือยัง ───────────────────
create or replace function public.has_claimed_daily_quiz_today(p_user_id uuid)
 returns boolean
 language sql
 security definer
as $function$
  select exists (
    select 1 from public.daily_quiz_claims
    where user_id = p_user_id
      and claim_date = (now() at time zone 'UTC' at time zone 'Asia/Bangkok')::date
  );
$function$;

-- ── 3) RPC: สุ่มคำถาม 1 ข้อที่ผู้เล่นยังไม่เคยตอบ จาก patch ที่ active ───────────
-- หมายเหตุ: ตัวกรอง "ยังไม่เคยตอบ" อิงคอลัมน์ daily_quiz_claims.question_id
-- ซึ่งปัจจุบัน edge function (grant-quiz-reward / record-quiz-attempt) ยังไม่ได้
-- บันทึก question_id ลงไป → ตัวกรองจึงเป็น no-op และคืนข้อแบบสุ่มไปก่อน
-- (ถ้าต้องการให้กรองข้อซ้ำจริง ต้องให้ edge function insert question_id ด้วย)
create or replace function public.get_unanswered_daily_questions(p_user_id uuid)
 returns table(id bigint, external_id text, category text, difficulty text, question text, choices jsonb, correct_index smallint)
 language sql
 stable security definer
 set search_path to 'public'
as $function$
    select
        q.id,
        q.external_id,
        q.category,
        q.difficulty,
        q.question,
        q.choices,
        q.correct_index
    from public.quiz_questions q
    inner join public.quiz_patches p on p.id = q.patch_id
    where p.is_active = true
      and q.external_id is not null
      and q.external_id not in (
          select c.question_id
          from public.daily_quiz_claims c
          where c.user_id = p_user_id
            and c.question_id is not null
      )
    order by random()
    limit 1;
$function$;
