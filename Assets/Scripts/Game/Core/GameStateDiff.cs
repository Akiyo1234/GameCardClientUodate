using System.Collections.Generic;

namespace Game.Core
{
    // ตัวเลือกการเทียบ — ข้าม field ที่อาจต่างกันโดยชอบได้
    public class DiffOptions
    {
        public bool ignoreBoardCards = false; // ข้ามการ์ดบนกระดาน (เช่น draw RNG ของ core vs legacy ต่างกัน)
        public bool ignoreTurn = false;       // ข้าม currentPlayerIndex/round/totalTurnCount/playOrder/gameOver
        public bool ignoreNobles = false;
    }

    // เทียบ GameState 2 ตัว แล้วรายงานจุดต่าง — ใช้เช็ค parity (core vs legacy) และจับ desync (online 2 เครื่อง)
    public static class GameStateDiff
    {
        public static bool Equal(GameState a, GameState b, DiffOptions opts = null)
            => Diff(a, b, opts).Count == 0;

        public static List<string> Diff(GameState a, GameState b, DiffOptions opts = null)
        {
            opts = opts ?? new DiffOptions();
            var d = new List<string>();
            if (a == null || b == null) { d.Add("state เป็น null"); return d; }

            // ── bank ──
            CompareIntArray(d, "bank", a.bankCoins, b.bankCoins);

            // ── players ──
            int an = a.players?.Length ?? 0;
            int bn = b.players?.Length ?? 0;
            if (an != bn) d.Add($"players length {an}!={bn}");
            else
            {
                for (int seat = 0; seat < an; seat++)
                {
                    PlayerState pa = a.players[seat], pb = b.players[seat];
                    if (pa == null && pb == null) continue;
                    if (pa == null || pb == null) { d.Add($"p{seat} null mismatch"); continue; }

                    CompareIntArray(d, $"p{seat}.coins", pa.coins, pb.coins);
                    CompareIntArray(d, $"p{seat}.bonuses", pa.bonuses, pb.bonuses);
                    if (pa.score != pb.score) d.Add($"p{seat}.score {pa.score}!={pb.score}");
                    if (pa.quizBlackCoins != pb.quizBlackCoins) d.Add($"p{seat}.quizBlack {pa.quizBlackCoins}!={pb.quizBlackCoins}");
                    CompareStringList(d, $"p{seat}.reserved", pa.reservedCardIds, pb.reservedCardIds);
                }
            }

            // ── board ──
            if (!opts.ignoreBoardCards)
            {
                int abn = a.board?.Length ?? 0;
                int bbn = b.board?.Length ?? 0;
                if (abn != bbn) d.Add($"board length {abn}!={bbn}");
                else
                    for (int i = 0; i < abn; i++)
                    {
                        if (a.board[i].tier != b.board[i].tier) d.Add($"board[{i}].tier {a.board[i].tier}!={b.board[i].tier}");
                        if (a.board[i].cardId != b.board[i].cardId) d.Add($"board[{i}].card {a.board[i].cardId}!={b.board[i].cardId}");
                    }
            }

            // ── turn ──
            if (!opts.ignoreTurn)
            {
                if (a.currentPlayerIndex != b.currentPlayerIndex) d.Add($"currentPlayerIndex {a.currentPlayerIndex}!={b.currentPlayerIndex}");
                if (a.currentRound != b.currentRound) d.Add($"currentRound {a.currentRound}!={b.currentRound}");
                if (a.totalTurnCount != b.totalTurnCount) d.Add($"totalTurnCount {a.totalTurnCount}!={b.totalTurnCount}");
                if (a.isGameOver != b.isGameOver) d.Add($"isGameOver {a.isGameOver}!={b.isGameOver}");
                if (a.winnerSeat != b.winnerSeat) d.Add($"winnerSeat {a.winnerSeat}!={b.winnerSeat}");
                CompareIntArray(d, "playOrder", a.playOrder, b.playOrder);
            }

            // ── nobles ──
            if (!opts.ignoreNobles)
            {
                int ann = a.nobles?.Count ?? 0;
                int bnn = b.nobles?.Count ?? 0;
                if (ann != bnn) d.Add($"nobles count {ann}!={bnn}");
                else
                    for (int i = 0; i < ann; i++)
                    {
                        NobleState na = a.nobles[i], nb = b.nobles[i];
                        if (na == null || nb == null) { if (na != nb) d.Add($"noble[{i}] null mismatch"); continue; }
                        if (na.nobleId != nb.nobleId) d.Add($"noble[{i}].id {na.nobleId}!={nb.nobleId}");
                        if (na.claimed != nb.claimed) d.Add($"noble[{i}].claimed {na.claimed}!={nb.claimed}");
                        if (na.claimedBySeat != nb.claimedBySeat) d.Add($"noble[{i}].by {na.claimedBySeat}!={nb.claimedBySeat}");
                    }
            }

            return d;
        }

        private static void CompareIntArray(List<string> d, string label, int[] a, int[] b)
        {
            int an = a?.Length ?? 0, bn = b?.Length ?? 0;
            if (an != bn) { d.Add($"{label} length {an}!={bn}"); return; }
            for (int i = 0; i < an; i++)
                if (a[i] != b[i]) d.Add($"{label}[{i}] {a[i]}!={b[i]}");
        }

        private static void CompareStringList(List<string> d, string label, List<string> a, List<string> b)
        {
            int an = a?.Count ?? 0, bn = b?.Count ?? 0;
            if (an != bn) { d.Add($"{label} count {an}!={bn}"); return; }
            for (int i = 0; i < an; i++)
                if (a[i] != b[i]) d.Add($"{label}[{i}] {a[i]}!={b[i]}");
        }
    }
}
