using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.Core
{
    // แปลง GameState ↔ byte[] (binary, deterministic) — สำหรับ online: host broadcast state ให้ clients
    //   ใช้ System.IO ล้วน (ไม่พึ่ง UnityEngine) → ส่งผ่าน Fusion RPC (byte[]) หรือเก็บลง DB ได้
    public static class GameStateCodec
    {
        private const byte Version = 1;

        public static byte[] Serialize(GameState s)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(Version);

            WriteIntArray(w, s.bankCoins);

            int pn = s.players?.Length ?? 0;
            w.Write(pn);
            for (int i = 0; i < pn; i++)
            {
                PlayerState p = s.players[i];
                w.Write(p != null);
                if (p == null) continue;
                w.Write(p.seat);
                w.Write(p.isBot);
                w.Write(p.score);
                WriteIntArray(w, p.coins);
                w.Write(p.quizBlackCoins);
                WriteIntArray(w, p.bonuses);
                WriteStringList(w, p.reservedCardIds);
            }

            int bn = s.board?.Length ?? 0;
            w.Write(bn);
            for (int i = 0; i < bn; i++)
            {
                w.Write(s.board[i].tier);
                w.Write(s.board[i].cardId ?? "");
            }

            int un = s.usedCardIds?.Count ?? 0;
            w.Write(un);
            if (s.usedCardIds != null) foreach (var id in s.usedCardIds) w.Write(id ?? "");

            w.Write(s.boardSeed);
            w.Write(s.drawCounter);
            w.Write(s.currentPlayerIndex);
            WriteIntArray(w, s.playOrder);
            w.Write(s.currentRound);
            w.Write(s.totalTurnCount);
            w.Write(s.winningScore);
            w.Write(s.isGameOver);
            w.Write(s.winnerSeat);

            int nn = s.nobles?.Count ?? 0;
            w.Write(nn);
            for (int i = 0; i < nn; i++)
            {
                NobleState nb = s.nobles[i];
                w.Write(nb != null);
                if (nb == null) continue;
                w.Write(nb.nobleId ?? "");
                WriteIntArray(w, nb.requiredBonuses);
                w.Write(nb.victoryPoints);
                w.Write(nb.claimed);
                w.Write(nb.claimedBySeat);
            }

            w.Flush();
            return ms.ToArray();
        }

        public static GameState Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms, Encoding.UTF8);
            r.ReadByte(); // version (reserved สำหรับ migration ภายหลัง)

            var s = new GameState();
            s.bankCoins = ReadIntArray(r);

            int pn = r.ReadInt32();
            s.players = new PlayerState[pn];
            for (int i = 0; i < pn; i++)
            {
                if (!r.ReadBoolean()) { s.players[i] = null; continue; }
                s.players[i] = new PlayerState
                {
                    seat = r.ReadInt32(),
                    isBot = r.ReadBoolean(),
                    score = r.ReadInt32(),
                    coins = ReadIntArray(r),
                    quizBlackCoins = r.ReadInt32(),
                    bonuses = ReadIntArray(r),
                    reservedCardIds = ReadStringList(r),
                };
            }

            int bn = r.ReadInt32();
            s.board = new BoardSlot[bn];
            for (int i = 0; i < bn; i++)
            {
                int tier = r.ReadInt32();
                string id = r.ReadString();
                s.board[i] = new BoardSlot(tier, string.IsNullOrEmpty(id) ? null : id);
            }

            int un = r.ReadInt32();
            s.usedCardIds = new HashSet<string>();
            for (int i = 0; i < un; i++)
            {
                string id = r.ReadString();
                if (!string.IsNullOrEmpty(id)) s.usedCardIds.Add(id);
            }

            s.boardSeed = r.ReadInt32();
            s.drawCounter = r.ReadInt32();
            s.currentPlayerIndex = r.ReadInt32();
            s.playOrder = ReadIntArray(r);
            s.currentRound = r.ReadInt32();
            s.totalTurnCount = r.ReadInt32();
            s.winningScore = r.ReadInt32();
            s.isGameOver = r.ReadBoolean();
            s.winnerSeat = r.ReadInt32();

            int nn = r.ReadInt32();
            s.nobles = new List<NobleState>(nn);
            for (int i = 0; i < nn; i++)
            {
                if (!r.ReadBoolean()) { s.nobles.Add(null); continue; }
                s.nobles.Add(new NobleState
                {
                    nobleId = r.ReadString(),
                    requiredBonuses = ReadIntArray(r),
                    victoryPoints = r.ReadInt32(),
                    claimed = r.ReadBoolean(),
                    claimedBySeat = r.ReadInt32(),
                });
            }

            return s;
        }

        private static void WriteIntArray(BinaryWriter w, int[] a)
        {
            int n = a?.Length ?? 0;
            w.Write(n);
            for (int i = 0; i < n; i++) w.Write(a[i]);
        }

        private static int[] ReadIntArray(BinaryReader r)
        {
            int n = r.ReadInt32();
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = r.ReadInt32();
            return a;
        }

        private static void WriteStringList(BinaryWriter w, List<string> list)
        {
            int n = list?.Count ?? 0;
            w.Write(n);
            for (int i = 0; i < n; i++) w.Write(list[i] ?? "");
        }

        private static List<string> ReadStringList(BinaryReader r)
        {
            int n = r.ReadInt32();
            var list = new List<string>(n);
            for (int i = 0; i < n; i++) list.Add(r.ReadString());
            return list;
        }
    }
}
