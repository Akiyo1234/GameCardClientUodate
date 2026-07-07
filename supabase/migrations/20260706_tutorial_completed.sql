-- ============================================================
-- Tutorial completion flag (ต่อบัญชี) — ใช้ตัดสินว่าจะเด้งชวนเล่นโหมดฝึกสอนหลังล็อกอินไหม
-- Client: PlayerDataService.MarkTutorialCompletedAsync() ยิง RPC นี้ตอนเล่นฝึกสอนจบครบทุก step
--         (กด Skip ไม่นับ — client ไม่เรียก)
-- ============================================================

-- 1) คอลัมน์เก็บสถานะ (default false → บัญชีเก่า/ใหม่ทุกคนถูกถามครั้งแรก)
alter table public.player_profiles
  add column if not exists tutorial_completed boolean not null default false;

-- 2) RPC ให้ client เซ็ต flag ของตัวเองเท่านั้น (auth.uid() — เซ็ตให้คนอื่นไม่ได้)
create or replace function public.mark_tutorial_completed()
returns void
language sql
security definer
set search_path = public
as $$
  update public.player_profiles
     set tutorial_completed = true, updated_at = now()
   where id = auth.uid();
$$;

grant execute on function public.mark_tutorial_completed() to authenticated;
