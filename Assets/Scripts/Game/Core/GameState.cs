using System;
using System.Collections.Generic;

namespace Game.Core
{
    // สถานะแมตช์ทั้งหมด — source of truth เดียวที่ authority (host/server) ถือ
    // C# ล้วน ไม่ผูก Unity → รันบน Fusion host (C#) หรือพอร์ตไป backend ได้
    //
    // แกะมาจาก field ใน GameController.cs:
    //   bankCoins[6], players(PlayerUI[]), board(scene graph), usedCardIds,
    //   boardRandomSeed, currentPlayerIndex, playOrder[], currentRound,
    //   totalTurnCount, winningScore, isGameOver
    [Serializable]
    public class GameState
    {
        // ── Bank (กองกลาง) ──
        public int[] bankCoins = new int[CoinIndex.TotalCount]; // [6]

        // ── ผู้เล่น (ตาม seat 0..3) ──
        public PlayerState[] players;

        // ── กระดาน ──
        public BoardSlot[] board;                 // 12 ช่อง (3 tier × 4)
        public HashSet<string> usedCardIds = new HashSet<string>();
        public int boardSeed;                     // base seed ประจำแมตช์ (deterministic draw)
        public int drawCounter;                   // ตัวนับการจั่ว → ใช้ derive seed แต่ละครั้ง (แทน totalTurnCount เดิม)

        // ── ขุนนาง ── (แกะจาก NobleManager) — ใบที่ claimed=true ถือว่าถูกหยิบไปแล้ว
        public List<NobleState> nobles = new List<NobleState>();

        // ── เทิร์น ──
        public int currentPlayerIndex;            // index ใน playOrder
        public int[] playOrder = { 0, 1, 2, 3 };
        public int currentRound = 1;
        public int totalTurnCount;
        public int winningScore = 20;

        // ── จบเกม ──
        public bool isGameOver;
        public int winnerSeat = -1;

        // seat ของผู้เล่นที่ถึงตาเล่นตอนนี้
        public int CurrentSeat => playOrder[currentPlayerIndex];

        public PlayerState CurrentPlayer => players[CurrentSeat];

        // สำเนา deep copy — rules engine คืน state ใหม่โดยไม่แก้ของเดิม
        public GameState Clone()
        {
            var clonePlayers = new PlayerState[players.Length];
            for (int i = 0; i < players.Length; i++)
                clonePlayers[i] = players[i]?.Clone();

            var cloneNobles = new List<NobleState>();
            if (nobles != null)
                foreach (var n in nobles) cloneNobles.Add(n?.Clone());

            return new GameState
            {
                nobles = cloneNobles,
                bankCoins = (int[])bankCoins.Clone(),
                players = clonePlayers,
                board = (BoardSlot[])board.Clone(),
                usedCardIds = new HashSet<string>(usedCardIds),
                boardSeed = boardSeed,
                drawCounter = drawCounter,
                currentPlayerIndex = currentPlayerIndex,
                playOrder = (int[])playOrder.Clone(),
                currentRound = currentRound,
                totalTurnCount = totalTurnCount,
                winningScore = winningScore,
                isGameOver = isGameOver,
                winnerSeat = winnerSeat
            };
        }
    }
}
