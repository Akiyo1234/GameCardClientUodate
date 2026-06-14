using System.Collections.Generic;

namespace Game.Core
{
    // ตั้งค่าการตรวจ — บาง invariant ต้องรู้ context (จำนวนเหรียญเริ่มต้น ฯลฯ) จึงเปิด/ปิดได้
    public class ValidationConfig
    {
        // จำนวนเหรียญสีต่อสีตอนเริ่มเกม (2คน=4, 3คน=5, 4คน=7) — null = ข้ามเช็ค conservation สี
        public int? expectedColoredPerColor = null;
        // จำนวนทองที่มาจากกองกลางทั้งหมด (ปกติ 5) — null = ข้ามเช็ค conservation ทอง
        // หมายเหตุ: quizBlackCoins ถูก "mint" ไม่ได้มาจากกอง → ไม่นับใน conservation นี้
        public int? expectedGoldFromBank = null;
        // เพดานถือเหรียญ — null = ข้ามเช็ค (เผื่อ quizBlackCoins ดันเกิน 10 ได้โดยชอบ)
        public int? maxCoinsPerPlayer = 10;
        public int maxReserved = 3;
    }

    public class ValidationResult
    {
        public readonly List<string> violations = new List<string>();
        public bool Ok => violations.Count == 0;
        private void Add(string v) => violations.Add(v);
        internal void Violation(string v) => Add(v);
        public override string ToString() => Ok ? "VALID" : string.Join("; ", violations);
    }

    // ตรวจ GameState ว่าเป็นสถานะที่ "ถูกต้องตามกติกา" หรือไม่
    //   ใช้ฝั่ง authority: ตรวจหลัง apply action (จับ bug), หรือตรวจ state ที่ client ส่งมา (กันโกง/desync)
    //   ไม่แก้ state — แค่รายงาน violations
    public static class GameStateValidator
    {
        public static ValidationResult Validate(GameState s, ValidationConfig config = null)
        {
            config = config ?? new ValidationConfig();
            var r = new ValidationResult();

            if (s == null) { r.Violation("state เป็น null"); return r; }

            // ── bank ──
            bool bankOk = s.bankCoins != null && s.bankCoins.Length == CoinIndex.TotalCount;
            if (!bankOk) r.Violation("bankCoins length ไม่ถูกต้อง");
            else
                for (int i = 0; i < s.bankCoins.Length; i++)
                    if (s.bankCoins[i] < 0) r.Violation($"bank[{i}] ติดลบ ({s.bankCoins[i]})");

            // ── players ──
            if (s.players == null) { r.Violation("players เป็น null"); return r; }

            for (int seat = 0; seat < s.players.Length; seat++)
            {
                PlayerState p = s.players[seat];
                if (p == null) continue;

                bool coinsOk = p.coins != null && p.coins.Length == CoinIndex.TotalCount;
                if (!coinsOk) r.Violation($"p{seat} coins length ไม่ถูกต้อง");
                else
                {
                    for (int i = 0; i < p.coins.Length; i++)
                        if (p.coins[i] < 0) r.Violation($"p{seat} coins[{i}] ติดลบ");

                    if (p.quizBlackCoins > p.coins[CoinIndex.Gold])
                        r.Violation($"p{seat} quizBlackCoins({p.quizBlackCoins}) > gold({p.coins[CoinIndex.Gold]})");

                    if (config.maxCoinsPerPlayer.HasValue && p.TotalCoins() > config.maxCoinsPerPlayer.Value)
                        r.Violation($"p{seat} ถือ {p.TotalCoins()} เกิน {config.maxCoinsPerPlayer.Value}");
                }

                if (p.bonuses == null || p.bonuses.Length != CoinIndex.ColorCount)
                    r.Violation($"p{seat} bonuses length ไม่ถูกต้อง");
                else
                    for (int b = 0; b < p.bonuses.Length; b++)
                        if (p.bonuses[b] < 0) r.Violation($"p{seat} bonuses[{b}] ติดลบ");

                if (p.score < 0) r.Violation($"p{seat} score ติดลบ");
                if (p.quizBlackCoins < 0) r.Violation($"p{seat} quizBlackCoins ติดลบ");

                if (p.reservedCardIds == null)
                    r.Violation($"p{seat} reservedCardIds เป็น null");
                else if (p.reservedCardIds.Count > config.maxReserved)
                    r.Violation($"p{seat} จอง {p.reservedCardIds.Count} เกิน {config.maxReserved}");
            }

            // ── turn ──
            if (s.playOrder == null || s.playOrder.Length == 0)
                r.Violation("playOrder ว่าง");
            else
            {
                if (s.currentPlayerIndex < 0 || s.currentPlayerIndex >= s.playOrder.Length)
                    r.Violation($"currentPlayerIndex {s.currentPlayerIndex} นอกช่วง");
                for (int i = 0; i < s.playOrder.Length; i++)
                    if (s.playOrder[i] < 0 || s.playOrder[i] >= s.players.Length)
                        r.Violation($"playOrder[{i}]={s.playOrder[i]} ไม่ใช่ seat ที่ถูกต้อง");
            }

            // ── การ์ดต้องไม่ซ้ำ (board + reserved ทุกคน) และต้องอยู่ใน usedCardIds ──
            var seen = new HashSet<string>();
            if (s.board != null)
            {
                foreach (var slot in s.board)
                {
                    if (slot.tier < 1 || slot.tier > 3) r.Violation($"board slot tier ผิด ({slot.tier})");
                    if (slot.IsEmpty) continue;
                    if (!seen.Add(slot.cardId)) r.Violation($"การ์ดซ้ำบนกระดาน: {slot.cardId}");
                    if (s.usedCardIds != null && !s.usedCardIds.Contains(slot.cardId))
                        r.Violation($"การ์ดบนกระดานไม่อยู่ใน usedCardIds: {slot.cardId}");
                }
            }
            foreach (var p in s.players)
            {
                if (p?.reservedCardIds == null) continue;
                foreach (var id in p.reservedCardIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!seen.Add(id)) r.Violation($"การ์ดซ้ำ (จอง): {id}");
                    if (s.usedCardIds != null && !s.usedCardIds.Contains(id))
                        r.Violation($"การ์ดที่จองไม่อยู่ใน usedCardIds: {id}");
                }
            }

            // ── coin conservation (ถ้ารู้ supply เริ่มต้น) ──
            if (bankOk && config.expectedColoredPerColor.HasValue)
            {
                int exp = config.expectedColoredPerColor.Value;
                for (int i = 0; i < CoinIndex.ColorCount; i++)
                {
                    int total = s.bankCoins[i];
                    foreach (var p in s.players)
                        if (p?.coins != null && p.coins.Length == CoinIndex.TotalCount) total += p.coins[i];
                    if (total != exp) r.Violation($"สี {i} ไม่ conserve: รวม {total} != {exp}");
                }
            }
            if (bankOk && config.expectedGoldFromBank.HasValue)
            {
                int exp = config.expectedGoldFromBank.Value;
                int total = s.bankCoins[CoinIndex.Gold];
                foreach (var p in s.players)
                    if (p?.coins != null && p.coins.Length == CoinIndex.TotalCount)
                        total += p.coins[CoinIndex.Gold] - p.quizBlackCoins; // นับเฉพาะทองจริง (ไม่รวม black ที่ mint)
                if (total != exp) r.Violation($"ทองไม่ conserve: รวม {total} != {exp}");
            }

            return r;
        }
    }
}
