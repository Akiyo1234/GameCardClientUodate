-- ============================================================
-- match_results — กันฟาร์มรางวัลของ submit-match-result
--   ผูกผลแมตช์กับ "ห้องจริงที่จบแล้ว" (rooms.id) + UNIQUE(user_id, room_id)
--   → ผู้เล่น 1 คนรับรางวัลต่อ 1 ห้องได้ครั้งเดียว (เรียก loop ซ้ำไม่ได้)
--   insert เฉพาะ service_role (edge function) — client เขียนเองไม่ได้
-- ============================================================
create table if not exists public.match_results (
    id            bigint generated always as identity primary key,
    user_id       uuid not null references auth.users(id) on delete cascade,
    room_id       bigint not null references public.rooms(id) on delete cascade,
    placement     integer not null,
    total_players integer not null,
    mmr_delta     integer not null,
    gems_awarded  integer not null,
    created_at    timestamptz not null default now(),
    unique (user_id, room_id)   -- กันรับซ้ำในห้องเดียวกัน
);

alter table public.match_results enable row level security;

-- อ่านได้เฉพาะผลของตัวเอง; ไม่มี policy insert/update → มีแต่ service_role (edge fn) ที่เขียนได้
drop policy if exists "match_results read own" on public.match_results;
create policy "match_results read own"
    on public.match_results for select to authenticated
    using (user_id = auth.uid());

create index if not exists match_results_room_idx on public.match_results (room_id);
