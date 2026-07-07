using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// ============================================================
// MatchSessionService — เก็บ/กู้ "สถานะกระดาน" ลง Supabase ตาราง match_sessions (รองรับ Reconnect)
//
//   ใครเขียน: เฉพาะ authority (online master client) — create ตอนเริ่ม, update ทุกจบเทิร์น, finished ตอนจบ
//   ใครอ่าน: ผู้เล่นที่เป็นสมาชิก (RLS: auth.uid() = any(player_ids)) ใช้ตอนเช็ค Reconnect
//
//   ใช้ REST ตรง (เหมือน GameLogger) — ไม่พึ่ง model class ของ Supabase SDK
//   best-effort: ทุกเมธอด catch เอง คืน false/null ถ้าล้มเหลว (ไม่ทำให้เกมพัง)
// ============================================================
public static class MatchSessionService
{
    private static readonly HttpClient _http = new HttpClient();

    private static bool Auth(out string token, out string uid)
    {
        token = null; uid = null;
        var sb = SupabaseManager.Instance?.Client;
        token = sb?.Auth?.CurrentSession?.AccessToken;
        uid = sb?.Auth?.CurrentUser?.Id;
        return !string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(uid);
    }

    private static HttpRequestMessage Req(HttpMethod m, string pathAndQuery, string token, string prefer = null)
    {
        var req = new HttpRequestMessage(m, $"{SupabaseConfig.Url}/rest/v1/{pathAndQuery}");
        req.Headers.TryAddWithoutValidation("apikey", SupabaseConfig.AnonKey);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        if (prefer != null) req.Headers.TryAddWithoutValidation("Prefer", prefer);
        return req;
    }

    // ดึง uid ของผู้เล่นทุกคนในห้อง (จาก matchmaking_queue ผ่าน RPC) — สำหรับ identity binding
    //   ต้องใส่ครบใน player_ids ไม่งั้นคนอื่น reconnect ไม่ได้ (RLS อ่านได้เฉพาะสมาชิก)
    public static async Task<List<string>> GetRoomPlayerUidsAsync(long roomId)
    {
        var list = new List<string>();
        if (!Auth(out string token, out _)) return list;
        try
        {
            using var req = Req(HttpMethod.Post, "rpc/get_room_player_uids", token);
            req.Content = new StringContent("{\"p_room_id\":" + roomId + "}", Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return list;
            ExtractQuotedTokens(await resp.Content.ReadAsStringAsync(), list);
            return list;
        }
        catch { return list; }
    }

    // สร้างแถว session ใหม่ → คืน id (uuid) หรือ null ถ้าล้มเหลว
    //   playerIds = uid ทุกคนในห้อง (จะถูกเติม uid ของ authority เองให้เสมอ — RLS insert ต้องผ่าน)
    public static async Task<string> CreateAsync(string roomCode, string sessionName, List<string> playerIds, string boardStateJson)
    {
        if (!Auth(out string token, out string uid)) return null;
        try
        {
            var ids = new List<string>();
            if (playerIds != null) foreach (var p in playerIds) if (!string.IsNullOrEmpty(p) && !ids.Contains(p)) ids.Add(p);
            if (!ids.Contains(uid)) ids.Add(uid);   // authority ต้องอยู่ใน player_ids เสมอ

            var arr = new StringBuilder("[");
            for (int i = 0; i < ids.Count; i++) { if (i > 0) arr.Append(','); arr.Append(JsonStr(ids[i])); }
            arr.Append(']');

            string body = "{"
                + "\"room_code\":" + JsonStr(roomCode) + ","
                + "\"session_name\":" + JsonStr(sessionName) + ","
                + "\"player_ids\":" + arr + ","
                + "\"status\":\"active\","
                + "\"board_state\":" + (string.IsNullOrEmpty(boardStateJson) ? "null" : boardStateJson)
                + "}";
            using var req = Req(HttpMethod.Post, "match_sessions", token, "return=representation");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return ExtractFirstId(await resp.Content.ReadAsStringAsync());
        }
        catch { return null; }
    }

    // อัปเดต board_state (เรียกทุกจบเทิร์น)
    public static async Task<bool> UpdateBoardAsync(string sessionId, string boardStateJson)
    {
        if (string.IsNullOrEmpty(sessionId) || !Auth(out string token, out _)) return false;
        try
        {
            string body = "{\"board_state\":" + (string.IsNullOrEmpty(boardStateJson) ? "null" : boardStateJson) + "}";
            using var req = Req(new HttpMethod("PATCH"), $"match_sessions?id=eq.{sessionId}", token, "return=minimal");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // จบเกม: บันทึก board สุดท้าย + เปลี่ยน status เป็น finished (หรือ abandoned)
    public static async Task<bool> SetFinalAsync(string sessionId, string boardStateJson, string status)
    {
        if (string.IsNullOrEmpty(sessionId) || !Auth(out string token, out _)) return false;
        try
        {
            string body = "{"
                + (string.IsNullOrEmpty(boardStateJson) ? "" : "\"board_state\":" + boardStateJson + ",")
                + "\"status\":" + JsonStr(status)
                + "}";
            using var req = Req(new HttpMethod("PATCH"), $"match_sessions?id=eq.{sessionId}", token, "return=minimal");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // หา active session ที่ผู้เล่นนี้เป็นสมาชิก (RLS กรองให้) → คืน raw row JSON หรือ null
    //   ใช้ตอน Login เพื่อโชว์ปุ่ม "กลับเข้าเกม" (ขั้นถัดไป: parse board_state + room_code ออกมา rejoin)
    public static async Task<string> GetActiveSessionAsync()
    {
        if (!Auth(out string token, out _)) return null;
        try
        {
            using var req = Req(HttpMethod.Get,
                "match_sessions?status=eq.active&order=updated_at.desc&limit=1", token);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            string txt = (await resp.Content.ReadAsStringAsync())?.Trim();
            return (string.IsNullOrEmpty(txt) || txt == "[]") ? null : txt;
        }
        catch { return null; }
    }

    // ดึง id ของ active session ตาม room (ใช้ตอน authority "รับช่วง" session หลัง host migration)
    //   RLS อ่านได้เฉพาะถ้า uid เราอยู่ใน player_ids ของแถวนั้น (CreateAsync ใส่ทุกคนในห้องไว้แล้ว)
    public static async Task<string> GetActiveSessionIdByRoomAsync(string roomCode)
    {
        if (string.IsNullOrEmpty(roomCode) || !Auth(out string token, out _)) return null;
        try
        {
            using var req = Req(HttpMethod.Get,
                $"match_sessions?room_code=eq.{Uri.EscapeDataString(roomCode)}&status=eq.active&order=updated_at.desc&limit=1&select=id",
                token);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return ExtractFirstId(await resp.Content.ReadAsStringAsync());
        }
        catch { return null; }
    }

    // ── helpers ──
    private static string ExtractFirstId(string arrayJson)
    {
        if (string.IsNullOrEmpty(arrayJson)) return null;
        const string key = "\"id\":\"";
        int i = arrayJson.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return null;
        i += key.Length;
        int e = arrayJson.IndexOf('"', i);
        return e < 0 ? null : arrayJson.Substring(i, e - i);
    }

    // ดึงทุก "..." จาก JSON array (uuid ไม่มี escape → ปลอดภัย) เช่น ["a","b"] → [a,b]
    private static void ExtractQuotedTokens(string json, List<string> outList)
    {
        if (string.IsNullOrEmpty(json)) return;
        int i = 0;
        while (i < json.Length)
        {
            int s = json.IndexOf('"', i);
            if (s < 0) break;
            int e = json.IndexOf('"', s + 1);
            if (e < 0) break;
            string tok = json.Substring(s + 1, e - s - 1);
            if (!string.IsNullOrEmpty(tok)) outList.Add(tok);
            i = e + 1;
        }
    }

    private static string JsonStr(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
