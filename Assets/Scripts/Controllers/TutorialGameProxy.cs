using UnityEngine;

public class TutorialGameProxy
{
    private GameController gc;
    private float originalTurnDuration;
    private BotController[] cachedBots;

    public void EnterTutorialMode(GameController gc)
    {
        this.gc = gc;
        originalTurnDuration = gc.turnDuration;
        gc.turnDuration = 99999f;
        gc.currentTurnTime = 99999f;
        
        // Cache และปิดบอทในที่เดียว
        cachedBots = Object.FindObjectsByType<BotController>(FindObjectsSortMode.None);
        SetBotsEnabled(false);
    }

    public void SetBotsEnabled(bool enabled)
    {
        if (cachedBots == null) return;
        foreach (var b in cachedBots) 
        {
            if (b != null) b.enabled = enabled;
        }
    }

    public void ResetTurnTime()
    {
        if (gc != null)
        {
            gc.turnDuration = 99999f;
            gc.currentTurnTime = 99999f;
        }
    }

    public void ExitTutorialMode()
    {
        if (gc != null)
        {
            gc.turnDuration = originalTurnDuration;
        }
        SetBotsEnabled(true);
    }
    
    // Convenience properties
    public int CurrentPlayerIndex => gc != null ? gc.currentPlayerIndex : 0;
    public bool IsLocalPlayersTurn => gc != null ? gc.IsLocalPlayersTurn() : false;
    public int[] PendingCoins => gc != null ? gc.pendingCoins : new int[6];
    public int TotalPendingCoins => gc != null ? gc.GetTotalPendingCoins() : 0;
    public PlayerUI[] Players => gc != null ? gc.players : null;
}
