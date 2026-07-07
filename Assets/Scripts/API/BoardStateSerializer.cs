using System;
using Game.Core;

// ============================================================
// BoardStateSerializer — แปลง GameState (สถานะกระดานทั้งหมด) ↔ JSON string
//   สำหรับเก็บลง Supabase ตาราง match_sessions.board_state (jsonb) เพื่อรองรับ Reconnect
//
//   หลักการ: ไม่เขียน serializer ใหม่ — reuse GameStateCodec (binary, deterministic,
//   ผ่านเทส round-trip แล้ว) แล้วห่อ bytes เป็น base64 ใน JSON envelope
//   → ได้ jsonb ที่ valid + กู้คืนได้ครบ 100% (bank/players/board/deck-seed/turn/noble)
//
//   หมายเหตุ: base64 มีแต่ [A-Za-z0-9+/=] ไม่มี quote/backslash → ดึงค่าออกแบบ string ตรงๆ ได้ปลอดภัย
// ============================================================
public static class BoardStateSerializer
{
    private const int EnvelopeVersion = 1;

    // GameState → JSON envelope string (เก็บลง board_state)
    public static string ToJson(GameState state)
    {
        if (state == null) return null;
        byte[] bytes = GameStateCodec.Serialize(state);
        string b64 = Convert.ToBase64String(bytes);
        return "{\"v\":" + EnvelopeVersion + ",\"codec\":\"" + b64 + "\"}";
    }

    // JSON envelope string → GameState (คืน null ถ้า parse/decode ไม่ได้)
    public static GameState FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            string b64 = ExtractCodec(json);
            if (string.IsNullOrEmpty(b64)) return null;
            byte[] bytes = Convert.FromBase64String(b64);
            return GameStateCodec.Deserialize(bytes);
        }
        catch
        {
            return null;
        }
    }

    // ดึงค่า "codec":"..." จาก envelope แบบไม่พึ่ง JSON lib (รองรับทั้งแถวเดี่ยวและ row จาก REST array)
    private static string ExtractCodec(string json)
    {
        const string key = "\"codec\":\"";
        int start = json.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return null;
        start += key.Length;
        int end = json.IndexOf('"', start);
        if (end < 0) return null;
        return json.Substring(start, end - start);
    }
}
