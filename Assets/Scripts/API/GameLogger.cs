using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// ============================================================
// GameLogger — เก็บ "เหตุการณ์ในแมตช์" แล้วส่งขึ้น Supabase ตาราง game_logs
//   (สำหรับวิเคราะห์ข้อมูลในเล่ม thesis)
//
//   หลักการ: ห้าม insert ทีละ event (ยิง request ถี่เกิน) → เก็บลง buffer แล้ว
//   "batch flush" เป็นก้อนเดียว เมื่อ:
//     • buffer ครบ BatchSize
//     • ครบ FlushInterval วินาที (ตั้งเวลา)
//     • จบเกม / แอปถูก pause / ปิด (เรียก FlushNow)
//
//   ใช้งาน (จากที่ไหนก็ได้ ไม่ต้องผูก Inspector — สร้าง GameObject เองครั้งแรก):
//     GameLogger.Log("buy_card", new GameLogger.Payload()
//         .Add("seat", seat).Add("cardId", id).Add("tier", tier).Add("vp", vp));
//
//   user_id ถูกตั้งโดย DB (auth.uid) — client ส่งแค่ event/payload/room/session/เวลา
//   ถ้ายังไม่ล็อกอิน → ข้าม (insert ไม่ผ่าน RLS) ; best-effort: flush ล้มเหลว = ทิ้งก้อนนั้น
// ============================================================
public class GameLogger : MonoBehaviour
{
    // เปิด/ปิดทั้งระบบ (ถ้าอยากปิดชั่วคราวตอน dev ก็ตั้ง false)
    public static bool Enabled = true;

    private const int BatchSize = 25;            // ครบเท่านี้ flush ทันที
    private const float FlushInterval = 8f;      // หรือ flush ทุกๆ กี่วินาที
    private const int MaxBufferGuard = 500;      // กัน buffer โตไม่จำกัดถ้า offline ยาวๆ

    private static GameLogger _instance;
    private static bool _quitting;

    // auto-create singleton — เรียก GameLogger.Log() ได้เลยโดยไม่ต้องวางในซีน
    public static GameLogger Instance
    {
        get
        {
            if (_instance == null && !_quitting)
            {
                var go = new GameObject("GameLogger");
                _instance = go.AddComponent<GameLogger>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private readonly List<string> _buffer = new List<string>();  // เก็บ row JSON ที่ประกอบแล้ว
    private string _sessionId;
    private string _matchId = "";   // id ประจำ "แมตช์" (roll ใหม่ทุก game_start) — ใช้ group แทน room_id ที่อาจซ้ำ
    private bool _flushing;
    private bool _warnedNoLogin;    // เตือน "ยังไม่ล็อกอิน → ไม่ได้ log" แค่ครั้งเดียว กัน log ท่วม

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        _sessionId = Guid.NewGuid().ToString("N");  // id ประจำรอบเปิดแอป
        StartCoroutine(FlushTimer());
    }

    private void OnApplicationPause(bool pause) { if (pause) FlushNow(); }
    private void OnApplicationQuit() { _quitting = true; FlushNow(); }

    // ── API หลัก ──
    public static void Log(string eventType, Payload payload = null)
    {
        if (!Enabled || string.IsNullOrEmpty(eventType)) return;
        Instance?.Enqueue(eventType, payload);
    }

    // เริ่ม "แมตช์ใหม่" — roll match_id ใหม่ (เรียกตอน game_start)
    //   1 app-run เล่นได้หลายแมตช์ → session_id เดียวกันแต่ match_id ต่างกัน
    //   ใช้ group วิเคราะห์แทน room_id (เลข matchmaking ที่อาจถูกใช้ซ้ำ)
    public static void BeginMatch()
    {
        if (Instance != null) Instance._matchId = Guid.NewGuid().ToString("N");
    }

    // บังคับส่งทันที (เรียกตอนจบเกม)
    public static void FlushNow()
    {
        if (_instance != null && _instance._buffer.Count > 0)
            _ = _instance.FlushAsync();
    }

    private void Enqueue(string eventType, Payload payload)
    {
        string roomId = PlayerPrefs.GetString("MatchmakingRoomId", "offline");
        // InvariantCulture: กันปฏิทินพุทธ (locale ไทย) ทำให้ปีเป็น พ.ศ. (+543) — DB ต้องเก็บ ค.ศ./UTC เสมอ
        string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        string payloadJson = payload != null ? payload.Build() : "{}";

        // ประกอบ row JSON (payload เป็น object jsonb ดิบ ไม่ใช่ string)
        string row = "{"
            + "\"event_type\":" + JsonStr(eventType) + ","
            + "\"room_id\":" + JsonStr(roomId) + ","
            + "\"match_id\":" + JsonStr(_matchId) + ","
            + "\"session_id\":" + JsonStr(_sessionId) + ","
            + "\"client_ts\":" + JsonStr(ts) + ","
            + "\"payload\":" + payloadJson
            + "}";

        _buffer.Add(row);
        if (_buffer.Count > MaxBufferGuard) _buffer.RemoveAt(0); // กันบวม (ทิ้งเก่าสุด)
        if (_buffer.Count >= BatchSize) _ = FlushAsync();
    }

    private IEnumerator FlushTimer()
    {
        var wait = new WaitForSeconds(FlushInterval);
        while (true)
        {
            yield return wait;
            if (_buffer.Count > 0) _ = FlushAsync();
        }
    }

    // ส่ง buffer ทั้งก้อนไป REST /rest/v1/game_logs (insert array 1 request)
    private async Task FlushAsync()
    {
        if (_flushing || _buffer.Count == 0) return;

        var sb = SupabaseManager.Instance?.Client;
        string token = sb?.Auth?.CurrentSession?.AccessToken;
        if (string.IsNullOrEmpty(token))
        {
            // ยังไม่ล็อกอิน → insert ไม่ได้ (best-effort). เตือนครั้งเดียว กัน tester เก็บข้อมูลแล้วได้ 0 แถวแบบเงียบๆ
            if (!_warnedNoLogin)
            {
                _warnedNoLogin = true;
                Debug.LogWarning($"[GameLogger] ยังไม่ได้ล็อกอิน — log {_buffer.Count} event จะไม่ถูกบันทึกลง DB (จะเงียบจนกว่าจะล็อกอิน)");
            }
            return;
        }

        _flushing = true;

        // ตัดก้อนที่จะส่งออกจาก buffer ก่อน (เผื่อมี event ใหม่เข้ามาระหว่างส่ง)
        var batch = new List<string>(_buffer);
        _buffer.Clear();

        try
        {
            string body = "[" + string.Join(",", batch) + "]";
            string url = $"{SupabaseConfig.Url}/rest/v1/game_logs";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("apikey", SupabaseConfig.AnonKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync();
                Debug.LogWarning($"[GameLogger] flush {batch.Count} logs ล้มเหลว ({(int)resp.StatusCode}): {err}");
                // best-effort: ทิ้งก้อนนี้ (ไม่ re-queue เพื่อกัน retry storm / buffer บวม)
            }
            else
            {
                GameLog.Log($"[GameLogger] flushed {batch.Count} logs");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameLogger] flush error: {e.Message}");
        }
        finally
        {
            _flushing = false;
        }
    }

    private static readonly HttpClient _http = new HttpClient();

    // ── JSON helpers ──
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

    // ── ตัวประกอบ payload (jsonb) แบบ fluent — รองรับ string/int/bool/int[] ──
    public class Payload
    {
        private readonly StringBuilder _sb = new StringBuilder("{");
        private bool _first = true;
        private void Sep() { if (!_first) _sb.Append(','); _first = false; }

        public Payload Add(string key, string val)
        { Sep(); _sb.Append(JsonStr(key)).Append(':').Append(JsonStr(val)); return this; }

        public Payload Add(string key, int val)
        { Sep(); _sb.Append(JsonStr(key)).Append(':').Append(val); return this; }

        public Payload Add(string key, bool val)
        { Sep(); _sb.Append(JsonStr(key)).Append(':').Append(val ? "true" : "false"); return this; }

        public Payload Add(string key, int[] arr)
        {
            Sep(); _sb.Append(JsonStr(key)).Append(":[");
            if (arr != null)
                for (int i = 0; i < arr.Length; i++) { if (i > 0) _sb.Append(','); _sb.Append(arr[i]); }
            _sb.Append(']');
            return this;
        }

        internal string Build() => _sb.ToString() + "}";
    }
}
