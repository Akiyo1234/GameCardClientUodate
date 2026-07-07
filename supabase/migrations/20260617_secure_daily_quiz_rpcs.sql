-- ============================================================
-- ทำให้ RPC ควิซรายวันปลอดภัย (server-authoritative) — แก้ 2 ช่องโหว่:
--   #1 reward จาก client: เดิม submit เขียน reward_gems = p_reward_gems (client ส่งมา)
--      → client โกงส่ง 999999 ได้ และ client เครดิตเข้ากระเป๋าเอง
--      แก้: server กำหนดรางวัลเอง (c_reward) + เครดิต player_profiles.gems ฝั่ง server
--   #2 user จาก client: เดิมใช้ p_user_id ใน WHERE → ส่ง UUID คนอื่นได้ (griefing/อ่านของคนอื่น)
--      แก้: ใช้ auth.uid() จาก JWT แทน (คง param p_user_id ไว้เพื่อ client เดิมเรียกได้ แต่ไม่ใช้)
-- ============================================================

-- 1) submit_daily_quiz_answer — auth.uid() + reward ฝั่ง server + เครดิตกระเป๋า atomic
CREATE OR REPLACE FUNCTION public.submit_daily_quiz_answer(p_user_id uuid, p_is_correct boolean, p_reward_gems integer DEFAULT 0)
 RETURNS TABLE(success boolean, message text, reward_gems integer)
 LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'public'
AS $function$
DECLARE
  v_uid    uuid := auth.uid();
  v_today  date;
  v_claim  daily_quiz_claims%ROWTYPE;
  c_reward constant integer := 100;   -- รางวัลกำหนดฝั่ง server (ไม่เชื่อ p_reward_gems จาก client)
  v_grant  integer;
BEGIN
  IF v_uid IS NULL THEN
    RETURN QUERY SELECT false, 'ไม่ได้ล็อกอิน', 0; RETURN;
  END IF;

  v_today := (now() AT TIME ZONE 'Asia/Bangkok')::date;

  SELECT * INTO v_claim FROM daily_quiz_claims WHERE user_id = v_uid AND claim_date = v_today;
  IF NOT FOUND THEN
    RETURN QUERY SELECT false, 'ยังไม่ได้รับคำถามวันนี้', 0; RETURN;
  END IF;
  IF v_claim.answered_at IS NOT NULL THEN
    RETURN QUERY SELECT false, 'ตอบไปแล้ววันนี้', COALESCE(v_claim.reward_gems, 0); RETURN;
  END IF;

  v_grant := CASE WHEN p_is_correct THEN c_reward ELSE 0 END;

  UPDATE daily_quiz_claims
     SET is_correct = p_is_correct, answered_at = now(), reward_gems = v_grant
   WHERE user_id = v_uid AND claim_date = v_today;

  -- เครดิตกระเป๋าฝั่ง server (จุดเดียวที่ให้ gems จริง — กันโกง)
  IF v_grant > 0 THEN
    UPDATE player_profiles SET gems = COALESCE(gems, 0) + v_grant, updated_at = now() WHERE id = v_uid;
  END IF;

  RETURN QUERY SELECT true,
    CASE WHEN p_is_correct THEN 'ตอบถูก! รับรางวัล ' || v_grant || ' Gems' ELSE 'ตอบผิด ไม่ได้รางวัล' END,
    v_grant;
END;
$function$;

-- 2) get_daily_quiz_question — ใช้ auth.uid() แทน p_user_id
CREATE OR REPLACE FUNCTION public.get_daily_quiz_question(p_user_id uuid)
 RETURNS TABLE(question_id bigint, external_id text, category text, difficulty text, question text, choices jsonb, correct_index smallint, already_answered boolean, is_correct boolean, reward_gems integer)
 LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'public'
AS $function$
DECLARE
  v_uid            uuid := auth.uid();
  v_today          date;
  v_existing_claim daily_quiz_claims%ROWTYPE;
  v_question       quiz_questions%ROWTYPE;
BEGIN
  IF v_uid IS NULL THEN RETURN; END IF;
  v_today := (now() AT TIME ZONE 'Asia/Bangkok')::date;

  SELECT * INTO v_existing_claim FROM daily_quiz_claims WHERE user_id = v_uid AND claim_date = v_today;
  IF FOUND THEN
    SELECT q.* INTO v_question FROM quiz_questions q
      INNER JOIN quiz_patches p ON p.id = q.patch_id
      WHERE q.external_id = v_existing_claim.question_id AND p.is_active = true LIMIT 1;
    IF NOT FOUND THEN
      SELECT q.* INTO v_question FROM quiz_questions q WHERE q.external_id = v_existing_claim.question_id LIMIT 1;
    END IF;
    RETURN QUERY SELECT v_question.id, v_question.external_id, v_question.category, v_question.difficulty,
      v_question.question, v_question.choices, v_question.correct_index,
      (v_existing_claim.answered_at IS NOT NULL), v_existing_claim.is_correct, COALESCE(v_existing_claim.reward_gems, 0);
    RETURN;
  END IF;

  SELECT q.* INTO v_question FROM quiz_questions q
    INNER JOIN quiz_patches p ON p.id = q.patch_id
    WHERE p.is_active = true
      AND q.external_id NOT IN (SELECT c.question_id FROM daily_quiz_claims c WHERE c.user_id = v_uid AND c.question_id IS NOT NULL)
    ORDER BY random() LIMIT 1;
  IF NOT FOUND THEN
    SELECT q.* INTO v_question FROM quiz_questions q
      INNER JOIN quiz_patches p ON p.id = q.patch_id WHERE p.is_active = true ORDER BY random() LIMIT 1;
  END IF;
  IF NOT FOUND THEN RETURN; END IF;

  INSERT INTO daily_quiz_claims(user_id, claim_date, question_id)
    VALUES (v_uid, v_today, v_question.external_id) ON CONFLICT (user_id, claim_date) DO NOTHING;

  RETURN QUERY SELECT v_question.id, v_question.external_id, v_question.category, v_question.difficulty,
    v_question.question, v_question.choices, v_question.correct_index, false, NULL::boolean, 0;
END;
$function$;

-- 3) has_claimed_daily_quiz_today — auth.uid()
CREATE OR REPLACE FUNCTION public.has_claimed_daily_quiz_today(p_user_id uuid)
 RETURNS TABLE(has_claimed boolean, already_answered boolean, reward_gems integer)
 LANGUAGE sql STABLE SECURITY DEFINER SET search_path TO 'public'
AS $function$
  SELECT
    EXISTS(SELECT 1 FROM public.daily_quiz_claims WHERE user_id = auth.uid() AND claim_date = (now() AT TIME ZONE 'Asia/Bangkok')::date),
    EXISTS(SELECT 1 FROM public.daily_quiz_claims WHERE user_id = auth.uid() AND claim_date = (now() AT TIME ZONE 'Asia/Bangkok')::date AND answered_at IS NOT NULL),
    COALESCE((SELECT dqc.reward_gems FROM public.daily_quiz_claims dqc WHERE user_id = auth.uid() AND claim_date = (now() AT TIME ZONE 'Asia/Bangkok')::date), 0);
$function$;

-- 4) get_unanswered_daily_questions — auth.uid()
CREATE OR REPLACE FUNCTION public.get_unanswered_daily_questions(p_user_id uuid)
 RETURNS TABLE(id bigint, external_id text, category text, difficulty text, question text, choices jsonb, correct_index smallint)
 LANGUAGE sql STABLE SECURITY DEFINER SET search_path TO 'public'
AS $function$
    SELECT q.id, q.external_id, q.category, q.difficulty, q.question, q.choices, q.correct_index
    FROM public.quiz_questions q INNER JOIN public.quiz_patches p ON p.id = q.patch_id
    WHERE p.is_active = true AND q.external_id IS NOT NULL
      AND q.external_id NOT IN (SELECT c.question_id FROM public.daily_quiz_claims c WHERE c.user_id = auth.uid() AND c.question_id IS NOT NULL)
    ORDER BY random() LIMIT 1;
$function$;
