using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class QuizManager : MonoBehaviour
{
    public static QuizManager Instance { get; private set; }

    // โครงสร้างข้อมูลสำหรับ 1 คำถาม
    [System.Serializable]
    public class QuizQuestion
    {
        [TextArea(2, 4)]
        public string questionText;
        public string[] choices = new string[4]; // ช้อยส์ 4 ข้อ
        [Tooltip("กรอก 0, 1, 2 หรือ 3")]
        public int correctChoiceIndex; // เฉลยว่าข้อไหนถูก
    }

    [System.Serializable]
    public class PlayerAnswer
    {
        public int playerIndex; 
        public bool isCorrect;  
        public float timeTaken; 
    }

    public GameController gameController;

    [Header("---- คลังคำถาม (Quiz Database) ----")]
    public List<QuizQuestion> questionDatabase;
    private QuizQuestion currentQuestion;

    [Header("---- UI หน้าต่างคำถาม ----")]
    public GameObject quizPanel;
    public TextMeshProUGUI questionText;
    public TextMeshProUGUI timerText;
    public Button[] answerButtons; // ใส่ปุ่มทั้ง 4 ปุ่ม
    public TextMeshProUGUI[] answerChoiceTexts; // ใส่ Text ที่อยู่ในปุ่มทั้ง 4 อัน
    public TextMeshProUGUI rewardText; // [NEW] แสดงข้อความรางวัล
    public ResultScreenUI resultScreen; // หน้าต่างสรุปผลอเนกประสงค์

    [Header("---- ตั้งค่าเวลา ----")]
    public float timeLimit = 10f; // เวลาตอบ
    private float currentTime;
    private bool isQuizActive = false;

    private List<PlayerAnswer> currentAnswers = new List<PlayerAnswer>();

    public bool IsQuizActive => isQuizActive;

    void Awake()
    {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // ปิดหน้าต่างควิซไว้ก่อนตอนเริ่มเกมเสมอ (ใส่ใน Awake การันตีว่าทำงานแน่นอน)
        if (quizPanel != null) quizPanel.SetActive(false);
    }

    void Update()
    {
        // นับเวลาถอยหลังตอนที่เปิดหน้าควิซ
    if (isQuizActive)
        {
            currentTime -= Time.deltaTime;
            
            // ปรับให้โชว์แค่ตัวเลขเพียวๆ
            if (timerText != null) timerText.text = Mathf.CeilToInt(currentTime).ToString();

            if (currentTime <= 0)
            {
                ForceEndQuiz(); // หมดเวลาปุ๊บ สรุปผลทันที
            }
        }
    }
    // =====================================
    // ระบบควบคุมควิซ
    // =====================================

    // ฟังก์ชันนี้เอาไว้เรียกใช้ตอนจะให้หน้าต่างคำถามเด้งขึ้นมา
    public void StartQuiz()
    {
        if (questionDatabase.Count == 0) 
        {
            Debug.LogError("ยังไม่ได้ใส่คำถามใน Database!");
            return;
        }

        // 1. สุ่มคำถาม 1 ข้อจากคลัง
        int rand = Random.Range(0, questionDatabase.Count);
        currentQuestion = questionDatabase[rand];

        // 2. จัดหน้าตา UI
        questionText.text = currentQuestion.questionText;
        for (int i = 0; i < 4; i++)
        {
            answerChoiceTexts[i].text = currentQuestion.choices[i];
            answerButtons[i].interactable = true; // เปิดให้กดได้
        }

        currentAnswers.Clear();
        currentTime = timeLimit;
        isQuizActive = true;
        if (gameController != null) gameController.SetGameplayInputLocked(true);
        if (quizPanel != null) quizPanel.SetActive(true);
        
        if (rewardText != null) {
            rewardText.text = "รางวัลอันดับ 1: สุ่มไอเทม 3 ชิ้น (RAM, CPU, Security ฯลฯ)";
        }

        Debug.Log($"<color=white>เปิดควิซ: {currentQuestion.questionText}</color>");
    }

    // ฟังก์ชันรับคำตอบจากปุ่มบนจอ (เราคือ Player 1 = Index 0)
    public void OnClickAnswer(int choiceIndex)
    {
        if (!isQuizActive) return;

        int myIndex = (gameController != null) ? gameController.playOrder[gameController.currentPlayerIndex] : 0;
        float timeUsed = timeLimit - currentTime;
        bool correct = (choiceIndex == currentQuestion.correctChoiceIndex);

        Debug.Log($"<color=yellow>[Quiz] คุณตอบข้อ: {choiceIndex} | เฉลยคือ: {currentQuestion.correctChoiceIndex} | ผลลัพธ์: {(correct ? "ถูก" : "ผิด")}</color>");

        // บันทึกคำตอบของเรา
        currentAnswers.Add(new PlayerAnswer { 
            playerIndex = myIndex, 
            isCorrect = correct, 
            timeTaken = timeUsed 
        });

        // ปิดปุ่ม เพื่อไม่ให้กดซ้ำ
        foreach(var btn in answerButtons) btn.interactable = false;

        // [Restore] เริ่มกระบวนการสรุปผลทันที (หรือรอสักครู่เพื่อความตื่นเต้น)
        StartCoroutine(WaitAndFinishQuiz());
    }

    private System.Collections.IEnumerator WaitAndFinishQuiz()
    {
        // รอ 1-2 วินาทีให้ผู้เล่นเห็นสิ่งที่ตัวเองกด
        yield return new WaitForSeconds(1.0f);
        
        // จำลองบอทตอบ (ถ้าต้องการความรวดเร็วให้ใช้ค่าสั้นๆ)
        SimulateOtherPlayers(gameController != null ? gameController.playOrder[gameController.currentPlayerIndex] : 0);
        
        ForceEndQuiz();
    }


    private void SimulateOtherPlayers(int excludeIndex)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i == excludeIndex) continue; // ข้ามตัวเราเอง

            // บอทแต่ละตัวจะมีความฉลาดและเร็วต่างกัน
            float botDifficulty = Random.Range(0.4f, 0.8f);
            float botSpeed = Random.Range(1.0f, timeLimit);

            currentAnswers.Add(new PlayerAnswer {
                playerIndex = i,
                isCorrect = (Random.value < botDifficulty), 
                timeTaken = botSpeed
            });
        }
    }

    private void ForceEndQuiz()
    {
        isQuizActive = false;
        if (gameController != null) gameController.SetGameplayInputLocked(false);
        if (quizPanel != null) quizPanel.SetActive(false);

        if (currentAnswers.Count == 0)
        {
            Debug.Log("<color=red>หมดเวลา! ไม่มีใครตอบเลย</color>");
            return;
        }

        ProcessQuizResults(currentAnswers);
    }

    // =====================================
    // ระบบคำนวณและแจกของ (คงเดิมเป๊ะๆ)
    // =====================================
    public void ProcessQuizResults(List<PlayerAnswer> answers)
    {
        int totalPlayers = GetTotalPlayersForQuiz();
        if (totalPlayers <= 0)
        {
            Debug.LogWarning("[Quiz] No players available to rank.");
            return;
        }

        var normalizedAnswers = answers
            .Where(a => a != null)
            .GroupBy(a => a.playerIndex)
            .Select(group => group
                .OrderByDescending(a => a.isCorrect)
                .ThenBy(a => a.timeTaken)
                .First())
            .ToList();

        for (int i = 0; i < totalPlayers; i++)
        {
            bool alreadyAnswered = normalizedAnswers.Any(a => a.playerIndex == i);
            if (alreadyAnswered)
            {
                continue;
            }

            normalizedAnswers.Add(new PlayerAnswer
            {
                playerIndex = i,
                isCorrect = false,
                timeTaken = timeLimit + 999f
            });
        }

        var rankedPlayers = normalizedAnswers
            .OrderByDescending(a => a.isCorrect)
            .ThenBy(a => a.timeTaken)
            .ToList();

        Debug.Log("\n<color=yellow>=== สรุปผลการตอบคำถาม ===</color>");
        for (int i = 0; i < rankedPlayers.Count; i++)
        {
            var p = rankedPlayers[i];
            Debug.Log($"อันดับ {i + 1}: ผู้เล่น {p.playerIndex + 1}  ตอบถูก: {p.isCorrect}  เวลา: {p.timeTaken:F2} s");
        }

        var winner = rankedPlayers[0];
        bool hasWinner = winner.isCorrect;

        if (hasWinner)
        {
            string rewardMsg = GiveRandomGems(winner.playerIndex, 3);
            Debug.Log($"<color=green>🎉 ผู้เล่น {winner.playerIndex + 1} ชนะควิซ! {rewardMsg}</color>");
            
            if (rewardText != null) {
                string pName = gameController.players[winner.playerIndex].nameText.text;
                rewardText.text = $"ผู้ชนะ: {pName}\n{rewardMsg}";
            }
        }
        else
        {
            Debug.Log("<color=red>ไม่มีใครตอบถูกเลย! อดรางวัลทั้งหมด</color>");
            if (rewardText != null) rewardText.text = "ไม่มีใครตอบถูกเลย!";
        }

        int[] newTurnOrder = new int[totalPlayers];
        for (int i = 0; i < totalPlayers; i++)
        {
            newTurnOrder[i] = rankedPlayers[i].playerIndex;
        }

        if (resultScreen != null)
        {
            gameController.SetWaitingForContinueAfterResult(true);
        }

        gameController.ApplyNewTurnOrder(newTurnOrder);

        // โชว์หน้าสรุปผลตอนได้รับลำดับการเล่นใหม่ พร้อมบอกให้จุดพลุถ้ามีคนชนะ!
        if (resultScreen != null)
        {
            List<string> rankings = new List<string>();
            foreach (var p in rankedPlayers)
            {
                string pName = gameController.players[p.playerIndex].nameText.text;
                if (string.IsNullOrEmpty(pName) || pName.Trim() == "") {
                    pName = "Player " + (p.playerIndex + 1);
                }
                string status = p.isCorrect ? "ถูก" : "ผิด";
                rankings.Add($"{pName} ({status} - {p.timeTaken:F2}s)");
            }

            resultScreen.ShowResults("สรุปลำดับการเล่นรอบนี้", rankings, false, hasWinner);
        }
    }

    private int GetTotalPlayersForQuiz()
    {
        if (gameController != null && gameController.players != null && gameController.players.Length > 0)
        {
            return gameController.players.Length;
        }

        return 4;
    }

    private string GiveRandomGems(int playerIndex, int amount)
    {
        if (gameController == null) return "ล้มเหลว";
        PlayerUI winnerUI = gameController.players[playerIndex];

        Dictionary<string, int> receivedGems = new Dictionary<string, int>();

        for (int i = 0; i < amount; i++)
        {
            int randomGem = Random.Range(0, 5); 
            if (gameController.bankCoins[randomGem] > 0)
            {
                gameController.bankCoins[randomGem]--;
                winnerUI.coins[randomGem]++;

                string gemName = GetGemName(randomGem);
                if (receivedGems.ContainsKey(gemName)) receivedGems[gemName]++;
                else receivedGems[gemName] = 1;
            }
        }

        winnerUI.UpdateUI();
        gameController.UpdateBankUI();

        // สร้างข้อความสรุป: "ได้รับ RAM 1 / CPU 2"
        List<string> parts = new List<string>();
        foreach (var pair in receivedGems) {
            parts.Add($"{pair.Key} {pair.Value}");
        }
        
        return "ได้รับ " + string.Join(" / ", parts);
    }

    private string GetGemName(int index) {
        switch(index) {
            case 0: return "CPU";
            case 1: return "RAM";
            case 2: return "Network";
            case 3: return "Storage";
            case 4: return "Security";
            default: return "Item";
        }
    }
}
