using System.Threading.Tasks;
using UnityEngine;
using Game.Core;

// ============================================================
// GameController — ส่วนเก็บ/กู้สถานะกระดานลง Supabase (match_sessions) เพื่อรองรับ Reconnect (ข้อ 1)
//
//   reuse ของเดิม: BuildCoreGameState() (Authority.cs) = snapshot กระดานทั้งหมดเป็น GameState
//                  RenderFromState()   (Authority.cs) = วาด GameState กลับเข้า scene
//   + BoardStateSerializer (GameState ↔ JSON) + MatchSessionService (REST → DB)
//
//   ฝั่ง SAVE (ทำแล้ว): authority สร้าง session ตอนเริ่ม → update ทุกจบเทิร์น → finished ตอนจบ
//   ฝั่ง RESTORE (พร้อมใช้): RestoreBoardStateJson() — ส่วน UI ปุ่ม "กลับเข้าเกม" + rejoin Photon = ขั้นถัดไป
//
//   ทุก call เป็น best-effort (async void + service catch เอง) → DB ล่มไม่ทำให้เกมพัง
// ============================================================
public partial class GameController
{
    private string _matchSessionId;   // id แถวใน match_sessions (online authority เท่านั้นที่ถือ)

    // เขียน session ได้เฉพาะ online + เราเป็น authority (master client)
    private bool IsPersistAuthority()
        => isOnlineMatchMode && FusionManager.Instance != null && FusionManager.Instance.IsMasterClient;

    // ── snapshot bridge (public เผื่อเรียกจากที่อื่น/เทส) ──
    public string CaptureBoardStateJson() => BoardStateSerializer.ToJson(BuildCoreGameState());

    public void RestoreBoardStateJson(string json)
    {
        GameState s = BoardStateSerializer.FromJson(json);
        if (s == null) { GameLog.Log("[MatchSession] restore ล้มเหลว: snapshot ว่าง/พัง"); return; }
        RenderFromState(s);
        GameLog.Log("[MatchSession] กู้กระดานจาก snapshot สำเร็จ");
    }

    // ── orchestration ──

    // เรียกตอนเริ่มเกม (StartInitialGameplay) — authority สร้างแถว session ก้อนแรก
    private void BeginMatchSessionIfAuthority()
    {
        if (!IsPersistAuthority()) return;
        string room = PlayerPrefs.GetString("MatchmakingRoomId", "");
        if (string.IsNullOrEmpty(room)) return;
        CreateMatchSessionAsync(room, CaptureBoardStateJson());
    }

    private async void CreateMatchSessionAsync(string room, string boardJson)
    {
        // identity binding: ดึง uid ทุกคนในห้องมาใส่ player_ids (ไม่งั้นคนอื่น reconnect ไม่ได้ — RLS)
        System.Collections.Generic.List<string> uids = null;
        if (long.TryParse(room, out long roomId))
            uids = await MatchSessionService.GetRoomPlayerUidsAsync(roomId);

        // session_name = ห้อง Photon จริง (ไว้ rejoin ห้องเดิมตอน reconnect)
        string sessionName = FusionManager.Instance != null ? FusionManager.Instance.CurrentSessionName : null;

        _matchSessionId = await MatchSessionService.CreateAsync(room, sessionName, uids, boardJson);
        GameLog.Log(string.IsNullOrEmpty(_matchSessionId)
            ? "[MatchSession] สร้าง session ไม่สำเร็จ (เน็ต/auth?)"
            : $"[MatchSession] created {_matchSessionId} room={room} players={(uids?.Count ?? 0)} session={sessionName}");
    }

    // [Fix #1] authority "รับช่วง" session: ถ้าเราเป็น authority แต่ยังไม่มี _matchSessionId
    //   (กลายเป็น authority จาก host migration → ไม่ได้เป็นคนสร้าง) → ดึง active session ของ room นี้มาถือต่อ
    //   จะได้ save/finish/abandon ต่อได้ และ updated_at เดินต่อ (กัน cron mark abandoned เกมที่ยังเล่นอยู่)
    private async Task EnsureMatchSessionIdAsync()
    {
        if (!string.IsNullOrEmpty(_matchSessionId) || !IsPersistAuthority()) return;
        string room = PlayerPrefs.GetString("MatchmakingRoomId", "");
        if (string.IsNullOrEmpty(room)) return;

        string id = await MatchSessionService.GetActiveSessionIdByRoomAsync(room);
        if (!string.IsNullOrEmpty(id))
        {
            _matchSessionId = id;
            GameLog.Log($"[MatchSession] authority รับช่วง session {id} (room={room}) หลัง host migration");
        }
    }

    // เรียกทุกจบเทิร์น (EndTurn) — authority อัปเดต board_state ก้อนล่าสุด
    private void SaveBoardStateIfAuthority()
    {
        if (!IsPersistAuthority()) return;
        SaveBoardStateAsync(CaptureBoardStateJson());
    }

    private async void SaveBoardStateAsync(string boardJson)
    {
        await EnsureMatchSessionIdAsync();                     // รับช่วงถ้าเพิ่งกลายเป็น authority
        if (string.IsNullOrEmpty(_matchSessionId)) return;
        await MatchSessionService.UpdateBoardAsync(_matchSessionId, boardJson);
    }

    // เรียกตอนจบเกม (CheckWinCondition) — บันทึก board สุดท้าย + status = finished
    private void FinishMatchSessionIfAuthority()
    {
        if (!IsPersistAuthority()) return;
        FinishMatchSessionAsync(CaptureBoardStateJson());
    }

    private async void FinishMatchSessionAsync(string boardJson)
    {
        await EnsureMatchSessionIdAsync();
        if (string.IsNullOrEmpty(_matchSessionId)) return;
        await MatchSessionService.SetFinalAsync(_matchSessionId, boardJson, "finished");
        GameLog.Log("[MatchSession] session finished");
    }

    // [Reconnect/ข้อ1] fast-path: ผู้เล่นกดออกเกมเอง + เราเป็น authority และเป็น "คนสุดท้าย" ในห้อง
    //   → ปิด session เป็น abandoned ทันที ไม่ต้องรอ cron 15 นาที
    //   ถ้ายังมีคนอื่นเหลืออยู่ → ไม่ปิด (เขาเล่นต่อได้) ; เคส crash ยังมี cron เป็น safety net
    public void AbandonMatchSessionIfLastLeaver()
    {
        if (!IsPersistAuthority()) return;
        if (FusionManager.Instance.ActivePlayerCount > 1) return;   // ยังมีผู้เล่นอื่นเชื่อมต่ออยู่
        AbandonMatchSessionAsync(CaptureBoardStateJson());
    }

    private async void AbandonMatchSessionAsync(string boardJson)
    {
        await EnsureMatchSessionIdAsync();
        if (string.IsNullOrEmpty(_matchSessionId)) return;
        await MatchSessionService.SetFinalAsync(_matchSessionId, boardJson, "abandoned");
        GameLog.Log("[MatchSession] คนสุดท้ายออกจากห้อง → session abandoned");
    }
}
