using System;
using System.Threading.Tasks;

// ============================================================
// ReconnectService — ตรวจ "มีเกมค้างให้กลับเข้าไหม" (ฝั่ง detection ของ Reconnect/ข้อ 1)
//
//   เรียกตอน Login/หน้าเมนู: ถาม Supabase ว่าผู้เล่นนี้มี match_sessions ที่ status='active'
//   ค้างอยู่ไหม (RLS กรองให้เห็นเฉพาะที่ตัวเองเป็นสมาชิก → ต้องมี identity binding player_ids)
//   ถ้ามี → คืนข้อมูลพอจะ rejoin: sessionName (ห้อง Photon เดิม) + board_state (สำหรับกู้ถ้าห้องตายแล้ว)
//
//   ส่วนนี้เป็น logic ล้วน เทสได้ (อ่าน DB) — การ rejoin Photon จริง + ปุ่ม UI = ขั้นถัดไป (ต้องเทสเครื่อง)
// ============================================================
public static class ReconnectService
{
    public class ActiveSession
    {
        public string sessionId;     // match_sessions.id
        public string roomCode;      // = MatchmakingRoomId เดิม (numeric)
        public string sessionName;   // ห้อง Photon ที่จะ rejoin (StartGameCoroutine ใช้ตัวนี้)
        public string boardRaw;      // raw row JSON — ส่งเข้า GameController.RestoreBoardStateJson() ได้ตรงๆ
                                     // (FromJson หา "codec" ในก้อนนี้เจอเอง)
    }

    // คืน session ที่ค้างอยู่ หรือ null ถ้าไม่มี — เรียกตอนเข้าเมนูหลังล็อกอิน
    public static async Task<ActiveSession> CheckAsync()
    {
        string row = await MatchSessionService.GetActiveSessionAsync();
        if (string.IsNullOrEmpty(row)) return null;

        var s = new ActiveSession
        {
            sessionId   = StringValue(row, "id"),
            roomCode    = StringValue(row, "room_code"),
            sessionName = StringValue(row, "session_name"),
            boardRaw    = row,
        };
        // ต้องมีห้อง Photon ให้กลับเข้าถึงจะ rejoin ได้
        return string.IsNullOrEmpty(s.sessionName) ? null : s;
    }

    // ดึงค่า "key":"value" (string) จาก JSON แบบไม่พึ่ง lib — ใช้กับ field ระดับบนที่เป็น text
    private static string StringValue(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        string needle = "\"" + key + "\":\"";
        int s = json.IndexOf(needle, StringComparison.Ordinal);
        if (s < 0) return null;
        s += needle.Length;
        int e = json.IndexOf('"', s);
        return e < 0 ? null : json.Substring(s, e - s);
    }
}
