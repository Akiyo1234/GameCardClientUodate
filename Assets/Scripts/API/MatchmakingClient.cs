using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MatchmakingClient : MonoBehaviour
{
    private const string WaitingStatus = "waiting";
    private const string MatchedStatus = "matched";
    private const string CancelledStatus = "cancelled";

    private const string PlayerIdPrefsKey = "MatchmakingPlayerId";
    private const string RoomIdPrefsKey = "MatchmakingRoomId";
    private const string RoomCodePrefsKey = "MatchmakingRoomCode";

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "SampleScene";
    [SerializeField] private float pollIntervalSeconds = 2f;

    private Coroutine _pollCoroutine;
    private string _currentPlayerId;
    private bool _isConnectingToFusion;

    public string CurrentRoomId => PlayerPrefs.GetString(RoomIdPrefsKey, string.Empty);
    public string CurrentRoomCode => PlayerPrefs.GetString(RoomCodePrefsKey, string.Empty);

    private void Awake()
    {
        _currentPlayerId = GetOrCreatePlayerId();
    }

    // Hook your "Find Match" UI button to this method in the Unity Inspector.
    public void FindMatch()
    {
        _currentPlayerId = GetOrCreatePlayerId();
        _isConnectingToFusion = false;
        ClearSavedMatch();

        Debug.Log($"[Matchmaking] FindMatch pressed. PlayerId={_currentPlayerId}");

        if (_pollCoroutine != null)
        {
            StopCoroutine(_pollCoroutine);
            _pollCoroutine = null;
        }

        StartCoroutine(JoinMatchmakingCoroutine());
    }

    // Hook your "Cancel Matchmaking" UI button to this method in the Unity Inspector.
    public void CancelMatchmaking()
    {
        _currentPlayerId = GetOrCreatePlayerId();
        _isConnectingToFusion = false;
        ClearSavedMatch();

        Debug.Log($"[Matchmaking] CancelMatchmaking pressed. PlayerId={_currentPlayerId}");

        if (_pollCoroutine != null)
        {
            StopCoroutine(_pollCoroutine);
            _pollCoroutine = null;
        }

        StartCoroutine(CancelMatchmakingCoroutine());
    }

    // You can also hook this to a button if you want a manual "Refresh Status" action.
    public void PollMatchStatus()
    {
        _currentPlayerId = GetOrCreatePlayerId();
        Debug.Log($"[Matchmaking] Manual poll requested. PlayerId={_currentPlayerId}");
        StartCoroutine(PollMatchStatusOnceCoroutine());
    }

    private IEnumerator JoinMatchmakingCoroutine()
    {
        var task = JoinMatchmakingAsync();
        yield return WaitForTask(task, "join matchmaking");

        if (!TryGetTaskResult(task, out var entry) || entry == null)
        {
            yield break;
        }

        HandleQueueEntry(entry);

        if (entry.Status == WaitingStatus)
        {
            RestartPolling();
        }
    }

    private IEnumerator CancelMatchmakingCoroutine()
    {
        var task = CancelMatchmakingAsync();
        yield return WaitForTask(task, "cancel matchmaking");
    }

    private IEnumerator PollMatchStatusOnceCoroutine()
    {
        var task = PollMatchStatusAsync();
        yield return WaitForTask(task, "poll match status");

        if (!TryGetTaskResult(task, out var entry) || entry == null)
        {
            yield break;
        }

        HandleQueueEntry(entry);
    }

    private IEnumerator PollLoopCoroutine()
    {
        while (true)
        {
            yield return PollMatchStatusOnceCoroutine();

            if (!string.IsNullOrEmpty(CurrentRoomCode) || _isConnectingToFusion)
            {
                _pollCoroutine = null;
                yield break;
            }

            yield return new WaitForSeconds(pollIntervalSeconds);
        }
    }

    private void RestartPolling()
    {
        if (_pollCoroutine != null)
        {
            StopCoroutine(_pollCoroutine);
        }

        _pollCoroutine = StartCoroutine(PollLoopCoroutine());
    }

    private async Task<MatchmakingQueueData> JoinMatchmakingAsync()
    {
        var client = GetSupabaseClient();
        if (client == null)
        {
            return null;
        }

        await CancelWaitingEntriesAsync(client);

        var queueEntry = new MatchmakingQueueData
        {
            PlayerId = _currentPlayerId,
            Status = WaitingStatus,
            CreatedAt = DateTime.UtcNow
        };

        var insertResponse = await client.From<MatchmakingQueueData>().Insert(queueEntry);
        var createdEntry = insertResponse.Models.FirstOrDefault() ?? queueEntry;

        Debug.Log($"[Matchmaking] Queue entry created. Id={createdEntry.Id}, PlayerId={createdEntry.PlayerId}, Status={createdEntry.Status}");

        return await TryCreateMatchAsync(client, createdEntry);
    }

    private async Task CancelMatchmakingAsync()
    {
        var client = GetSupabaseClient();
        if (client == null)
        {
            return;
        }

        await CancelWaitingEntriesAsync(client);
        Debug.Log("[Matchmaking] Matchmaking cancelled.");
    }

    private async Task<MatchmakingQueueData> PollMatchStatusAsync()
    {
        var client = GetSupabaseClient();
        if (client == null)
        {
            return null;
        }

        var latestEntry = await GetLatestQueueEntryAsync(client);
        if (latestEntry == null)
        {
            Debug.LogWarning($"[Matchmaking] No queue entry found for PlayerId={_currentPlayerId}");
            return null;
        }

        Debug.Log($"[Matchmaking] Latest queue entry. Id={latestEntry.Id}, Status={latestEntry.Status}, RoomCode={latestEntry.RoomCode}");

        if (latestEntry.Status == WaitingStatus)
        {
            return await TryCreateMatchAsync(client, latestEntry);
        }

        return latestEntry;
    }

    private async Task<MatchmakingQueueData> TryCreateMatchAsync(Supabase.Client client, MatchmakingQueueData currentEntry)
    {
        if (currentEntry == null || currentEntry.Status != WaitingStatus)
        {
            return currentEntry;
        }

        var waitingResponse = await client
            .From<MatchmakingQueueData>()
            .Where(x => x.Status == WaitingStatus)
            .Order(x => x.CreatedAt, Postgrest.Constants.Ordering.Ascending)
            .Limit(2)
            .Get();

        var waitingEntries = waitingResponse.Models
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToList();

        if (waitingEntries.Count < 2)
        {
            Debug.Log($"[Matchmaking] Waiting for more players. Current waiting count={waitingEntries.Count}");
            return await GetLatestQueueEntryAsync(client);
        }

        var firstEntry = waitingEntries[0];
        var secondEntry = waitingEntries[1];

        if (firstEntry.Id != currentEntry.Id)
        {
            Debug.Log($"[Matchmaking] Another player is first in queue. CurrentEntry={currentEntry.Id}, FirstEntry={firstEntry.Id}");
            return await GetLatestQueueEntryAsync(client);
        }

        if (string.Equals(firstEntry.PlayerId, secondEntry.PlayerId, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[Matchmaking] Duplicate waiting player detected in top queue entries.");
            return await GetLatestQueueEntryAsync(client);
        }

        var roomCode = GenerateRoomCode();
        var matchedAt = DateTime.UtcNow;

        firstEntry.Status = MatchedStatus;
        firstEntry.RoomCode = roomCode;
        firstEntry.MatchedPlayerId = secondEntry.PlayerId;
        firstEntry.MatchedAt = matchedAt;

        secondEntry.Status = MatchedStatus;
        secondEntry.RoomCode = roomCode;
        secondEntry.MatchedPlayerId = firstEntry.PlayerId;
        secondEntry.MatchedAt = matchedAt;

        await client.From<MatchmakingQueueData>().Update(firstEntry);
        await client.From<MatchmakingQueueData>().Update(secondEntry);

        Debug.Log($"[Matchmaking] Match created. RoomCode={roomCode}, PlayerA={firstEntry.PlayerId}, PlayerB={secondEntry.PlayerId}");

        return string.Equals(_currentPlayerId, firstEntry.PlayerId, StringComparison.OrdinalIgnoreCase)
            ? firstEntry
            : secondEntry;
    }

    private async Task<MatchmakingQueueData> GetLatestQueueEntryAsync(Supabase.Client client)
    {
        var response = await client
            .From<MatchmakingQueueData>()
            .Where(x => x.PlayerId == _currentPlayerId)
            .Order(x => x.CreatedAt, Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        return response.Models.FirstOrDefault();
    }

    private async Task CancelWaitingEntriesAsync(Supabase.Client client)
    {
        var response = await client
            .From<MatchmakingQueueData>()
            .Where(x => x.PlayerId == _currentPlayerId && x.Status == WaitingStatus)
            .Get();

        foreach (var entry in response.Models)
        {
            entry.Status = CancelledStatus;
            await client.From<MatchmakingQueueData>().Update(entry);
            Debug.Log($"[Matchmaking] Queue entry cancelled. Id={entry.Id}, PlayerId={entry.PlayerId}");
        }
    }

    private void HandleQueueEntry(MatchmakingQueueData entry)
    {
        if (entry == null)
        {
            Debug.LogWarning("[Matchmaking] Queue entry not found.");
            return;
        }

        Debug.Log($"[Matchmaking] Status: {entry.Status}");

        if (entry.Status != MatchedStatus || string.IsNullOrWhiteSpace(entry.RoomCode))
        {
            return;
        }

        if (_pollCoroutine != null)
        {
            StopCoroutine(_pollCoroutine);
            _pollCoroutine = null;
        }

        if (_isConnectingToFusion)
        {
            return;
        }

        _isConnectingToFusion = true;

        PlayerPrefs.SetString(RoomIdPrefsKey, entry.Id.ToString());
        PlayerPrefs.SetString(RoomCodePrefsKey, entry.RoomCode);
        PlayerPrefs.Save();

        Debug.Log($"[Matchmaking] Match found. RoomCode={entry.RoomCode}");

        if (FusionManager.Instance != null)
        {
            Debug.Log($"[Matchmaking] Connecting to Fusion with roomCode={entry.RoomCode}");
            FusionManager.Instance.StartMatchedGame(entry.RoomCode, gameSceneName);
            return;
        }

        Debug.Log($"[Matchmaking] FusionManager not found. Loading scene {gameSceneName} only.");
        SceneManager.LoadScene(gameSceneName);
    }

    private IEnumerator WaitForTask(Task task, string actionName)
    {
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
        {
            Debug.LogError($"[Matchmaking] Failed to {actionName}: {task.Exception?.GetBaseException().Message}");
        }
    }

    private static bool TryGetTaskResult<T>(Task<T> task, out T result)
    {
        result = default;

        if (task == null || task.IsFaulted || task.IsCanceled)
        {
            return false;
        }

        result = task.Result;
        return true;
    }

    private static string GenerateRoomCode()
    {
        return Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    }

    private Supabase.Client GetSupabaseClient()
    {
        var client = SupabaseManager.Instance?.Client;
        if (client == null)
        {
            Debug.LogWarning("[Matchmaking] Supabase client is not ready yet.");
        }

        return client;
    }

    private void ClearSavedMatch()
    {
        PlayerPrefs.DeleteKey(RoomIdPrefsKey);
        PlayerPrefs.DeleteKey(RoomCodePrefsKey);
        PlayerPrefs.Save();
    }

    private string GetOrCreatePlayerId()
    {
        var savedPlayerId = PlayerPrefs.GetString(PlayerIdPrefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(savedPlayerId))
        {
            return savedPlayerId;
        }

        var currentUser = SupabaseManager.Instance?.Client?.Auth?.CurrentUser;
        if (currentUser != null)
        {
            if (!string.IsNullOrWhiteSpace(currentUser.Id))
            {
                savedPlayerId = currentUser.Id;
            }
            else if (!string.IsNullOrWhiteSpace(currentUser.Email))
            {
                savedPlayerId = currentUser.Email;
            }
        }

        if (string.IsNullOrWhiteSpace(savedPlayerId))
        {
            savedPlayerId = Guid.NewGuid().ToString("N");
        }

        PlayerPrefs.SetString(PlayerIdPrefsKey, savedPlayerId);
        PlayerPrefs.Save();
        return savedPlayerId;
    }
}
