using System.Collections.Generic;

namespace Game.Core
{
    // คำตอบควิซของผู้เล่น 1 คน (หลัง authority ตรวจถูก/ผิดแล้ว)
    public struct QuizAnswer
    {
        public int seat;
        public bool isCorrect;
        public float timeTaken; // วินาทีที่ใช้ตอบ (น้อย = เร็ว = ดีกว่า)
    }

    // แหล่งเฉลยควิซ — authority ใช้ตรวจถูก/ผิด (client เชื่อไม่ได้)
    public interface IQuizDatabase
    {
        bool TryGetCorrectIndex(string questionId, out int correctIndex);
    }

    // ระบบควิซ — แยกจาก turn loop (ตอบได้ทุกคน, ไม่จบเทิร์น)
    //   flow: host เก็บ AnswerQuizAction ของทุกคนในรอบ → Evaluate เป็น QuizAnswer
    //         → DetermineWinner หาผู้ชนะ → GrantBlackCoin ให้ผู้ชนะ
    //   กฎตรงกับ QuizManager.BuildRankedPlayers + DetermineRewardGemIndices + ApplyRewardGemIndices
    public static class QuizRules
    {
        // ตรวจถูก/ผิดฝั่ง authority (จากเฉลยใน db) แล้วแปลงเป็น QuizAnswer
        public static QuizAnswer Evaluate(AnswerQuizAction a, IQuizDatabase db, float timeTaken)
        {
            bool correct = db != null
                           && db.TryGetCorrectIndex(a.questionId, out int correctIndex)
                           && a.choiceIndex == correctIndex;
            return new QuizAnswer { seat = a.seat, isCorrect = correct, timeTaken = timeTaken };
        }

        // หาผู้ชนะรอบควิซ: อันดับ 1 = ถูก(desc) แล้วเวลาน้อย(asc)
        //   คืน seat ผู้ชนะ ถ้าอันดับ 1 ตอบถูก; ถ้าไม่มีใครถูกเลย คืน -1 (ไม่มีรางวัล)
        //   ผู้เล่นที่ไม่ได้ตอบ ถือว่าผิด + เวลาแย่สุด (ตรงกับ BuildRankedPlayers)
        public static int DetermineWinner(IEnumerable<QuizAnswer> answers, int totalPlayers)
        {
            if (totalPlayers <= 0) return -1;

            var best = new QuizAnswer[totalPlayers];
            var has = new bool[totalPlayers];

            if (answers != null)
            {
                foreach (var ans in answers)
                {
                    if (ans.seat < 0 || ans.seat >= totalPlayers) continue;
                    if (!has[ans.seat] || Beats(ans, best[ans.seat]))
                    {
                        best[ans.seat] = ans;
                        has[ans.seat] = true;
                    }
                }
            }

            int winner = -1;
            QuizAnswer winnerAns = default;
            bool found = false;
            for (int s = 0; s < totalPlayers; s++)
            {
                QuizAnswer cur = has[s]
                    ? best[s]
                    : new QuizAnswer { seat = s, isCorrect = false, timeTaken = float.MaxValue };

                if (!found || Beats(cur, winnerAns))
                {
                    winnerAns = cur;
                    winner = s;
                    found = true;
                }
            }

            return (found && winnerAns.isCorrect) ? winner : -1;
        }

        // a ดีกว่า b ไหม: ถูกชนะผิด; ถ้าเท่ากันดูเวลาน้อยกว่า
        private static bool Beats(QuizAnswer a, QuizAnswer b)
        {
            if (a.isCorrect != b.isCorrect) return a.isCorrect;
            return a.timeTaken < b.timeTaken;
        }

        // มอบเหรียญดำ (wildcard จากควิซ) ให้ผู้ชนะ — quizBlackCoins++ และ coins[Gold]++
        //   (เหรียญดำไม่ได้มาจากกองกลาง → ไม่แตะ bankCoins; ตรงกับ PlayerUI.AddQuizBlackCoin)
        public static GameState GrantBlackCoin(GameState s, int seat)
        {
            if (s == null || seat < 0 || s.players == null || seat >= s.players.Length) return s;
            GameState next = s.Clone();
            next.players[seat].quizBlackCoins++;
            next.players[seat].coins[CoinIndex.Gold]++;
            return next;
        }
    }
}
