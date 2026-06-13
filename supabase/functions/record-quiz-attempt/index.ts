// ============================================================
// Edge Function: record-quiz-attempt  (server-authoritative)
// บันทึกว่าผู้เล่น "ตอบควิซรายวันแล้ว" โดย *ไม่ให้รางวัล* — ใช้ตอนตอบผิด/หมดเวลา
// คู่กับ grant-quiz-reward (ตอบถูก = ให้รางวัล + ใส่ claim row)
// เป้าหมาย: ทำให้ลิมิต "1 ครั้ง/วัน" นับรวมการตอบผิดด้วย กันเปิดเข้ามาตอบใหม่จนกว่าจะถูก
// deploy: supabase functions deploy record-quiz-attempt   (verify-jwt = เปิด)
// ============================================================
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY")!;
const SERVICE_ROLE = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const CORS = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "authorization, apikey, content-type",
    "Access-Control-Allow-Methods": "POST, OPTIONS",
};
const json = (b: unknown, s = 200) =>
    new Response(JSON.stringify(b), { status: s, headers: { ...CORS, "Content-Type": "application/json" } });

Deno.serve(async (req) => {
    if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });
    if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

    try {
        const authHeader = req.headers.get("Authorization") ?? "";
        const userClient = createClient(SUPABASE_URL, ANON_KEY, {
            global: { headers: { Authorization: authHeader } },
        });
        const { data: { user } } = await userClient.auth.getUser();
        if (!user) return json({ error: "ไม่ได้เข้าสู่ระบบ" }, 401);

        const db = createClient(SUPABASE_URL, SERVICE_ROLE);
        // ใช้เวลาไทย (UTC+7) ให้ "วันใหม่" รีเซ็ตเที่ยงคืนไทย — ตรงกับ grant-quiz-reward
        const today = new Date(Date.now() + 7 * 60 * 60 * 1000).toISOString().slice(0, 10); // YYYY-MM-DD (Asia/Bangkok)

        // ใส่ claim row ของวันนี้แบบไม่ให้รางวัล — ถ้ามีอยู่แล้ว (23505) ถือว่าสำเร็จเช่นกัน
        const { error } = await db
            .from("daily_quiz_claims")
            .insert({ user_id: user.id, claim_date: today });
        if (error && error.code !== "23505") throw error; // 23505 = unique_violation = บันทึกไว้แล้ว

        return json({ ok: true });
    } catch (e) {
        console.error(e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ" }, 500);
    }
});
