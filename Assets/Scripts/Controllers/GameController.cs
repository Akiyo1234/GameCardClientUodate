using UnityEngine;
using System.Collections.Generic;

public class GameController : MonoBehaviour
{
    [Header("---- Board & Prefabs ----")]
    public Transform tier3Container; public Transform tier2Container; public Transform tier1Container;
    public GameObject cardPrefab; public GameObject resourcePrefab; public Transform resourceBankContainer;

    [Header("---- Card Database (Decks) ----")]
    public List<CardData> tier3Cards; 
    public List<CardData> tier2Cards; 
    public List<CardData> tier1Cards;

    [Header("---- Player Management ----")]
    public PlayerUI[] players;
    public int currentPlayerIndex = 0;

    [Header("---- Pending Actions ----")]
    public int[] pendingCoins = new int[6]; 
    private List<ResourceButton> bankButtons = new List<ResourceButton>();

    [Header("---- Turn Timer ----")]
    public float turnDuration = 30f; 
    private float currentTurnTime;

    [Header("---- Game Rules ----")]
    public int winningScore = 15; 
    public int currentRound = 1;  
    private bool isGameOver = false; 

    void Start()
    {
        if (cardPrefab == null) return;
        PopulateBoard(); 
        SpawnResourceBank(); 
        SetupPlayers();
        Debug.Log($"\n========== เริ่มเกม: รอบที่ {currentRound} ==========\n"); 
        ResetTimer(); 
        UpdateTurnVisuals();
    }

    void Update()
    {
        if (isGameOver) return; 

        if (currentTurnTime > 0)
        {
            currentTurnTime -= Time.deltaTime;
            float fillAmount = currentTurnTime / turnDuration;
            players[currentPlayerIndex].UpdateTimerBar(fillAmount);

            if (currentTurnTime <= 0)
            {
                Debug.Log($"[ผู้เล่น {currentPlayerIndex + 1}] หมดเวลา! บังคับจบเทิร์น");
                ClearPendingCoins(); 
                EndTurn(); 
            }
        }
    }

    public void ClearPendingCoins()
    {
        System.Array.Clear(pendingCoins, 0, 6); 
        foreach (ResourceButton btn in bankButtons) if (btn != null) btn.UpdatePendingUI(0);
    }

    public void OnResourceClicked(ResourceButton clickedBtn)
    {
        if (isGameOver) return; 

        int index = GetResourceIndex(clickedBtn.resourceType);
        if (index == 5) return; 

        int totalPicked = 0;
        for (int i = 0; i < 5; i++) totalPicked += pendingCoins[i];

        bool hasDoublePick = false;
        for (int i = 0; i < 5; i++) if (pendingCoins[i] >= 2) hasDoublePick = true;
        
        if (hasDoublePick || totalPicked >= 3) return;

        if (pendingCoins[index] > 0) {
            if (totalPicked == 1) pendingCoins[index]++;
        } else {
            pendingCoins[index]++;
        }

        clickedBtn.UpdatePendingUI(pendingCoins[index]);
    }

    public void OnCardClicked(CardDisplay card)
    {
        if (isGameOver) return; 

        PlayerUI p = players[currentPlayerIndex];
        
        // --- อัปเกรด: ระบบคำนวณเหรียญแบบ Splendor (ใช้ทองแทนได้) ---
        int missingCoins = 0; // ตัวแปรนับว่า "ขาดเหรียญสีกี่อัน"
        
        for (int i = 0; i < 5; i++) { // เช็คแค่ 5 สีหลัก (Index 0 ถึง 4)
            if (p.coins[i] < card.data.costs[i]) {
                missingCoins += (card.data.costs[i] - p.coins[i]);
            }
        }

        // เช็คว่าเหรียญทอง (Index 5) มีพอจ่ายส่วนที่ขาดไหม?
        bool canAfford = (missingCoins <= p.coins[5]);

        if (canAfford) {
            // --- ขั้นตอนการจ่ายเหรียญ ---
            for (int i = 0; i < 5; i++) {
                if (p.coins[i] < card.data.costs[i]) {
                    int diff = card.data.costs[i] - p.coins[i];
                    p.coins[i] = 0; // จ่ายสีนั้นจนเกลี้ยงกระเป๋า
                    p.coins[5] -= diff; // เอาเหรียญทองจ่ายแทนส่วนที่ขาด
                } else {
                    p.coins[i] -= card.data.costs[i]; // จ่ายด้วยเหรียญสีตามปกติ
                }
            }
            p.UpdateUI();
            
            // รับคะแนน
            p.AddScore(card.data.victoryPoints); 

            // ตรวจสอบ Tier เพื่อเติมการ์ด
            Transform parentContainer = card.transform.parent;
            int tier = 0;
            if (parentContainer == tier3Container) tier = 3;
            else if (parentContainer == tier2Container) tier = 2;
            else if (parentContainer == tier1Container) tier = 1;

            Destroy(card.gameObject); 

            if (tier > 0) {
                DrawNewCard(tier, parentContainer);
            }

            EndTurn(); 
        } 
        else 
        {
            // เพิ่มการแจ้งเตือนใน Console จะได้รู้ว่าทำไมกดไม่ได้!
            Debug.LogWarning($"⚠️ ซื้อการ์ดไม่ได้! ขาดอีก {missingCoins - p.coins[5]} เหรียญ (รวมทองแล้วก็ยังไม่พอ)");
        }
    }
    public void EndTurn()
    {
        if (isGameOver) return;

        players[currentPlayerIndex].ReceiveCoins(pendingCoins);
        ClearPendingCoins(); 
        
        currentPlayerIndex++;

        if (currentPlayerIndex >= players.Length)
        {
            currentPlayerIndex = 0; 
            CheckWinCondition(); 

            if (!isGameOver)
            {
                currentRound++; 
                Debug.Log($"\n========== เริ่มรอบที่ {currentRound} ==========\n");
            }
        }

        if (!isGameOver)
        {
            ResetTimer(); 
            UpdateTurnVisuals();
        }
    }

    void CheckWinCondition()
    {
        PlayerUI winner = null;
        int highestScore = 0;

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].currentScore >= winningScore && players[i].currentScore > highestScore)
            {
                highestScore = players[i].currentScore;
                winner = players[i];
            }
        }

        if (winner != null)
        {
            isGameOver = true;
            Debug.Log($"\n🏆 เกมจบแล้ว! ผู้ชนะคือ {winner.nameText.text} ด้วยคะแนน {highestScore} แต้ม! 🏆\n");
            foreach (var p in players) p.UpdateTimerBar(0); 
        }
    }

    // ==========================================
    // ระบบกองการ์ด (สุ่มแบบไม่อนุญาตให้ซ้ำกับบนกระดาน)
    // ==========================================

    void PopulateBoard() 
    {
        ClearContainer(tier3Container); ClearContainer(tier2Container); ClearContainer(tier1Container);
        
        for(int i = 0; i < 4; i++) { 
            DrawNewCard(3, tier3Container);
            DrawNewCard(2, tier2Container);
            DrawNewCard(1, tier1Container);
        }
    }

    void DrawNewCard(int tier, Transform container) 
    {
        List<CardData> masterDeck = null;
        if (tier == 3) masterDeck = tier3Cards;
        else if (tier == 2) masterDeck = tier2Cards;
        else if (tier == 1) masterDeck = tier1Cards;

        if (masterDeck == null || masterDeck.Count == 0) return;

        // 1. ตรวจสอบว่าตอนนี้มีการ์ดหน้าตาแบบไหนอยู่บนบอร์ดบ้าง (เก็บข้อมูลไว้)
        List<CardData> cardsOnBoard = new List<CardData>();
        foreach (Transform child in container)
        {
            CardDisplay display = child.GetComponent<CardDisplay>();
            if (display != null && display.data != null)
            {
                cardsOnBoard.Add(display.data);
            }
        }

        // 2. คัดเลือกเฉพาะการ์ดที่ยัง "ไม่อยู่บนบอร์ด" เท่านั้น
        List<CardData> availableCards = new List<CardData>();
        foreach (CardData card in masterDeck)
        {
            if (!cardsOnBoard.Contains(card))
            {
                availableCards.Add(card);
            }
        }

        // กันเหนียว: ถ้าบังเอิญการ์ดทั้งหมดที่มีมันน้อยกว่า 4 ใบ มันจะไม่มีให้สุ่ม จึงยอมให้ซ้ำได้
        if (availableCards.Count == 0) 
        {
            availableCards.AddRange(masterDeck);
        }

        // 3. สุ่มจากการ์ดที่ไม่ซ้ำ
        int randomIndex = Random.Range(0, availableCards.Count);
        CardData selectedCard = availableCards[randomIndex]; 

        // 4. สร้างการ์ดใบใหม่ลงบอร์ด
        GameObject newCardObj = Instantiate(cardPrefab, container);
        newCardObj.GetComponent<CardDisplay>()?.LoadCardData(selectedCard);
    }

    void ResetTimer() { currentTurnTime = turnDuration; }

    void UpdateTurnVisuals() {
        for (int i = 0; i < players.Length; i++)
            if (players[i] != null) players[i].SetActiveTurn(i == currentPlayerIndex);
    }

    int GetResourceIndex(string type) {
        if (type == "CPU") return 0; if (type == "RAM") return 1; if (type == "Network") return 2;
        if (type == "Storage") return 3; if (type == "Security") return 4; return 5;
    }

    void SpawnResourceBank() {
        ClearContainer(resourceBankContainer); bankButtons.Clear(); 
        string[] resNames = { "CPU", "RAM", "Network", "Storage", "Security", "Wildcard" };
        foreach (string res in resNames) {
            GameObject obj = Instantiate(resourcePrefab, resourceBankContainer);
            ResourceButton btn = obj.GetComponent<ResourceButton>();
            if (btn != null) { btn.Setup(res); bankButtons.Add(btn); }
        }
    }

    void SetupPlayers() {
        if (players == null) return;
        for (int i = 0; i < players.Length; i++)
            if (players[i] != null) players[i].SetupPlayer("Player " + (i + 1));
    }

    void ClearContainer(Transform c) { 
        if (c == null) return; 
        foreach (Transform child in c) Destroy(child.gameObject); 
    }
}