using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
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
    private const string TargetPlayerCountPrefsKey = "MatchmakingTargetPlayerCount";

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "SampleScene";
    [SerializeField] private float pollIntervalSeconds = 1.5f;

    [Header("Queue Safety")]
    [SerializeField] private float staleWaitingTimeoutSeconds = 300f;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI statusText;

    private Coroutine _pollCoroutine;
    private string _currentPlayerId;
    private string _currentQueueEntryId;
    private bool _isConnectingToFusion;
    private int _targetPlayerCount = 2;

    public string CurrentRoomId => PlayerPrefs.GetString(RoomIdPrefsKey, string.Empty);
    public string CurrentRoomCode => PlayerPrefs.GetString(RoomCodePrefsKey, string.Empty);

    private void Awake()
    {
        _currentPlayerId = GetOrCreatePlayerId();
        _targetPlayerCount = GetSavedTargetPlayerCount();
    }

    // Hook your "Find Match" UI button to this method in the Unity Inspector.
    public void FindMatch()
    {
        FindMatch(GetSavedTargetPlayerCount());
    }

    public void FindMatch(int targetPlayerCount)
    {
        _currentPlayerId = GetOrCreatePlayerId();
        _currentQueueEntryId = null;
        _isConnectingToFusion = false;
        _targetPlayerCount = Mathf.Clamp(targetPlayerCount, 2, 4);
        PlayerPrefs.SetInt(TargetPlayerCountPrefsKey, _targetPlayerCount);
        PlayerPrefs.Save();
        ClearSavedMatch();
        StopPolling();

        Debug.Log($"[Matchmaking] FindMatch pressed. PlayerId={_currentPlayerId}, TargetPlayers={_targetPlayerCount}");
        SetStatus($"Searching {_targetPlayerCount} players...");
        StartCoroutine(JoinMatchmakingCoroutine());
    }

    // Hook your "Cancel Matchmaking" UI button to this method in the Unity Inspector.
    public void CancelMatchmaking()
    {
        _currentPlayerId = GetOrCreatePlayerId();
        StopPolling();
        _isConnectingToFusion = false;
        ClearSavedMatch();

        Debug.Log($"[Matchmaking] CancelMatchmaking pressed. PlayerId={_currentPlayerId}");
        SetStatus("Cancelled");
        StartCoroutine(CancelMatchmakingCoroutine());
    }

    // You can also hook this to a button if you want a manual "Refresh Status" action.
    public void PollMatchStatus()
    {
        _currentPlayerId = GetOrCreatePlayerId();
        Debug.Log($"[Matchmaking] Manual poll requested. PlayerId={_currentPlayerId}");
        StartCoroutine(PollMatchStatusOnceCoroutine());
    }

    // Optional button/debug hook: force the current client to re-run the match creation attempt.
    public void TryCreateMatch()
    {
        _currentPlayerId = GetOrCreatePlayerId();
        Debug.Log($"[Matchmaking] TryCreateMatch requested. PlayerId={_currentPlayerId}");
        StartCoroutine(TryCreateMatchCoroutine());
    }

    private IEnumerator JoinMatchmakingCoroutine()
    {
        yield return WaitForSupabaseReadyCoroutine();

        var task = FindOrCreateWaitingEntryAsync();
        yield return WaitForTask(task, "join matchmaking");

        if (!TryGetTaskResult(task, out var entry) || entry == null)
        {
            yield break;
        }

        HandleQueueEntry(entry);
        if (IsWaitingEntry(entry))
        {
            RestartPolling();
        }
    }

    private IEnumerator CancelMatchmakingCoroutine()
    {
        yield return WaitForSupabaseReadyCoroutine();

        var task = CancelMatchmakingAsync();
        yield return WaitForTask(task, "cancel matchmaking");
    }

    private IEnumerator PollMatchStatusOnceCoroutine()
    {
        yield return WaitForSupabaseReadyCoroutine();

        var task = PollMatchStatusAsync();
        yield return WaitForTask(task, "poll match status");

        if (!TryGetTaskResult(task, out var entry) || entry == null)
        {
            yield break;
        }

        HandleQueueEntry(entry);
    }

    private IEnumerator TryCreateMatchCoroutine()
    {
        yield return WaitForSupabaseReadyCoroutine();

        var task = TryCreateMatchForCurrentPlayerAsync();
        yield return WaitForTask(task, "try create match");

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
        StopPolling();
        _pollCoroutine = StartCoroutine(PollLoopCoroutine());
    }

    private void StopPolling()
    {
        if (_pollCoroutine != null)
        {
            StopCoroutine(_pollCoroutine);
            _pollCoroutine = null;
        }
    }

    private async Task<MatchmakingQueueData> FindOrCreateWaitingEntryAsync()
    {
        var client = GetSupabaseClient();
        if (client == null)
        {
            return null;
        }

        await ExpireStaleWaitingEntriesAsync(client);
        await CancelMismatchedWaitingEntriesAsync(client, _targetPlayerCount);

        var existingWaitingEntry = await GetLatestWaitingQueueEntryAsync(client, _targetPlayerCount);
        if (existingWaitingEntry != null)
        {
            RememberCurrentQueueEntry(existingWaitingEntry);
            Debug.Log($"[Matchmaking] Existing waiting row found. Reusing Id={existingWaitingEntry.Id}");
            return await TryCreateMatchAsync(client, existingWaitingEntry);
        }

        var queueEntry = new MatchmakingQueueData
        {
            PlayerId = _currentPlayerId,
            Status = WaitingStatus,
            RoomCode = BuildWaitingRoomMarker(_targetPlayerCount),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            var insertResponse = await client.From<MatchmakingQueueData>().Insert(queueEntry);
            var createdEntry = insertResponse.Models.FirstOrDefault() ?? queueEntry;
            RememberCurrentQueueEntry(createdEntry);
            Debug.Log($"[Matchmaking] Queue entry created. Id={createdEntry.Id}, PlayerId={createdEntry.PlayerId}, Status={createdEntry.Status}");
            return await TryCreateMatchAsync(client, createdEntry);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Matchmaking] Insert waiting row warning: {ex.Message}");
            if (LooksLikeDuplicateQueueError(ex))
            {
                var duplicateWaitingEntry = await GetLatestWaitingQueueEntryAsync(client, _targetPlayerCount);
                if (duplicateWaitingEntry != null)
                {
                    RememberCurrentQueueEntry(duplicateWaitingEntry);
                    Debug.Log($"[Matchmaking] Duplicate waiting row detected. Reusing Id={duplicateWaitingEntry.Id}");
                    return await TryCreateMatchAsync(client, duplicateWaitingEntry);
                }
            }

            SetErrorStatus(ex.Message);
            throw;
        }
    }

    private async Task CancelMatchmakingAsync()
    {
        var client = GetSupabaseClient();
        if (client == null)
        {
            return;
        }

        var waitingEntries = await GetWaitingEntriesForCurrentPlayerAsync(client);
        foreach (var entry in waitingEntries)
        {
            entry.Status = CancelledStatus;
            entry.RoomCode = null;
            entry.MatchedAt = null;
            await client.From<MatchmakingQueueData>().Update(entry);
            Debug.Log($"[Matchmaking] Queue entry cancelled. Id={entry.Id}, PlayerId={entry.PlayerId}");
        }

        _currentQueueEntryId = null;
    }

    private async Task<MatchmakingQueueData> PollMatchStatusAsync()
    {
        var client = GetSupabaseClient();
        if (client == null)
        {
            return null;
        }

        var currentEntry = await GetCurrentSearchEntryAsync(client);
        if (currentEntry == null)
        {
            Debug.LogWarning($"[Matchmaking] No active queue entry found for PlayerId={_currentPlayerId}, QueueId={_currentQueueEntryId}");
            SetStatus("Searching...");
            return null;
        }

        Debug.Log($"[Matchmaking] Current search entry. Id={currentEntry.Id}, Status={currentEntry.Status}, RoomCode={currentEntry.RoomCode}");
        if (IsWaitingEntry(currentEntry))
        {
            return await TryCreateMatchAsync(client, currentEntry);
        }

        return currentEntry;
    }

    private async Task<MatchmakingQueueData> TryCreateMatchForCurrentPlayerAsync()
    {
        var client = GetSupabaseClient();
        if (client == null)
        {
            return null;
        }

        var waitingEntry = await GetCurrentSearchEntryAsync(client) ?? await GetLatestWaitingQueueEntryAsync(client, _targetPlayerCount);
        if (waitingEntry == null)
        {
            Debug.LogWarning($"[Matchmaking] No waiting entry available for PlayerId={_currentPlayerId}");
            return await GetLatestQueueEntryAsync(client);
        }

        RememberCurrentQueueEntry(waitingEntry);
        return await TryCreateMatchAsync(client, waitingEntry);
    }

    private async Task<MatchmakingQueueData> TryCreateMatchAsync(Supabase.Client client, MatchmakingQueueData currentEntry)
    {
        if (!IsWaitingEntry(currentEntry))
        {
            return currentEntry;
        }

        await ExpireStaleWaitingEntriesAsync(client);

        int targetPlayerCount = GetDesiredPlayerCountForEntry(currentEntry);
        var waitingEntries = await GetTopWaitingEntriesAsync(client, targetPlayerCount);
        if (waitingEntries.Count < targetPlayerCount)
        {
            Debug.Log($"[Matchmaking] Waiting for more players. Current waiting count={waitingEntries.Count}");
            return await GetLatestWaitingQueueEntryAsync(client, targetPlayerCount) ?? await GetLatestQueueEntryAsync(client);
        }

        var selectedEntries = waitingEntries.Take(targetPlayerCount).ToList();
        var firstEntry = selectedEntries[0];

        if (selectedEntries.Any(entry => !IsWaitingEntry(entry)))
        {
            Debug.LogWarning("[Matchmaking] Match creation skipped because one of the selected queue rows is no longer waiting.");
            return await GetLatestWaitingQueueEntryAsync(client, targetPlayerCount) ?? await GetLatestQueueEntryAsync(client);
        }

        if (!string.Equals(firstEntry.Id, currentEntry.Id, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[Matchmaking] Another player is first in queue. CurrentEntry={currentEntry.Id}, FirstEntry={firstEntry.Id}");
            return await GetLatestWaitingQueueEntryAsync(client, targetPlayerCount) ?? await GetLatestQueueEntryAsync(client);
        }

        if (selectedEntries
            .Select(entry => entry.PlayerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() < targetPlayerCount)
        {
            Debug.LogWarning("[Matchmaking] Duplicate waiting player detected in selected queue entries.");
            return await GetLatestWaitingQueueEntryAsync(client, targetPlayerCount) ?? await GetLatestQueueEntryAsync(client);
        }

        string roomCode = GenerateRoomCode();
        DateTime matchedAt = DateTime.UtcNow;

        foreach (var waitingEntry in selectedEntries)
        {
            waitingEntry.Status = MatchedStatus;
            waitingEntry.RoomCode = roomCode;
            waitingEntry.MatchedAt = matchedAt;
        }

        try
        {
            foreach (var waitingEntry in selectedEntries)
            {
                await client.From<MatchmakingQueueData>().Update(waitingEntry);
            }

            if (SupabaseManager.Instance != null)
            {
                await SupabaseManager.Instance.CreateRoom(roomCode, gameSceneName, targetPlayerCount);
            }

            Debug.Log($"[Matchmaking] Match created. RoomCode={roomCode}, Players={string.Join(", ", selectedEntries.Select(entry => entry.PlayerId))}, TargetPlayers={targetPlayerCount}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Matchmaking] Match update warning. Will re-poll. {ex.Message}");
            return await GetLatestQueueEntryAsync(client);
        }

        return selectedEntries.FirstOrDefault(entry => string.Equals(_currentPlayerId, entry.PlayerId, StringComparison.OrdinalIgnoreCase))
            ?? firstEntry;
    }

    private async Task<List<MatchmakingQueueData>> GetTopWaitingEntriesAsync(Supabase.Client client, int targetPlayerCount)
    {
        string waitingMarker = BuildWaitingRoomMarker(targetPlayerCount);
        var waitingResponse = await client
            .From<MatchmakingQueueData>()
            .Where(x => x.Status == WaitingStatus)
            .Where(x => x.RoomCode == waitingMarker)
            .Order(x => x.CreatedAt, Postgrest.Constants.Ordering.Ascending)
            .Limit(10)
            .Get();

        return waitingResponse.Models
            .Where(IsFreshWaitingEntry)
            .GroupBy(x => x.PlayerId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Take(targetPlayerCount)
            .ToList();
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

    private async Task<MatchmakingQueueData> GetCurrentSearchEntryAsync(Supabase.Client client)
    {
        if (string.IsNullOrWhiteSpace(_currentQueueEntryId))
        {
            return await GetLatestWaitingQueueEntryAsync(client, _targetPlayerCount);
        }

        var entry = await GetQueueEntryByIdAsync(client, _currentQueueEntryId);
        if (entry == null)
        {
            Debug.LogWarning($"[Matchmaking] Current queue row not found anymore. QueueId={_currentQueueEntryId}");
            _currentQueueEntryId = null;
            return await GetLatestWaitingQueueEntryAsync(client, _targetPlayerCount);
        }

        return entry;
    }

    private async Task<MatchmakingQueueData> GetQueueEntryByIdAsync(Supabase.Client client, string queueEntryId)
    {
        if (string.IsNullOrWhiteSpace(queueEntryId))
        {
            return null;
        }

        var response = await client
            .From<MatchmakingQueueData>()
            .Where(x => x.Id == queueEntryId)
            .Limit(1)
            .Get();

        return response.Models.FirstOrDefault();
    }

    private async Task<MatchmakingQueueData> GetLatestWaitingQueueEntryAsync(Supabase.Client client, int targetPlayerCount)
    {
        string waitingMarker = BuildWaitingRoomMarker(targetPlayerCount);
        var response = await client
            .From<MatchmakingQueueData>()
            .Where(x => x.PlayerId == _currentPlayerId)
            .Where(x => x.Status == WaitingStatus)
            .Where(x => x.RoomCode == waitingMarker)
            .Order(x => x.CreatedAt, Postgrest.Constants.Ordering.Descending)
            .Limit(1)
            .Get();

        return response.Models.FirstOrDefault(IsFreshWaitingEntry);
    }

    private async Task<List<MatchmakingQueueData>> GetWaitingEntriesForCurrentPlayerAsync(Supabase.Client client)
    {
        var response = await client
            .From<MatchmakingQueueData>()
            .Where(x => x.PlayerId == _currentPlayerId)
            .Where(x => x.Status == WaitingStatus)
            .Get();

        return response.Models.Where(IsWaitingEntry).ToList();
    }

    private void HandleQueueEntry(MatchmakingQueueData entry)
    {
        if (entry == null)
        {
            Debug.LogWarning("[Matchmaking] Queue entry not found.");
            SetErrorStatus("Queue entry not found.");
            return;
        }

        RememberCurrentQueueEntry(entry);
        Debug.Log($"[Matchmaking] Status: {entry.Status}");
        if (IsWaitingEntry(entry))
        {
            SetStatus($"Searching {GetDesiredPlayerCountForEntry(entry)} players...");
            return;
        }

        if (string.Equals(entry.Status, CancelledStatus, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Cancelled");
            return;
        }

        if (!string.Equals(entry.Status, MatchedStatus, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(entry.RoomCode))
        {
            return;
        }

        StopPolling();
        if (_isConnectingToFusion)
        {
            return;
        }

        _isConnectingToFusion = true;
        PlayerPrefs.SetString(RoomIdPrefsKey, entry.Id ?? string.Empty);
        PlayerPrefs.SetString(RoomCodePrefsKey, entry.RoomCode);
        PlayerPrefs.Save();

        SetStatus("Match found");
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

    private IEnumerator WaitForSupabaseReadyCoroutine()
    {
        var manager = SupabaseManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("[Matchmaking] SupabaseManager is missing from the scene.");
            SetErrorStatus("SupabaseManager is missing from the scene.");
            yield break;
        }

        if (manager.IsInitialized && manager.Client != null)
        {
            yield break;
        }

        Debug.Log("[Matchmaking] Waiting for Supabase client to finish initializing...");
        yield return new WaitUntil(() => SupabaseManager.Instance != null &&
                                        SupabaseManager.Instance.IsInitialized &&
                                        SupabaseManager.Instance.Client != null);
        Debug.Log("[Matchmaking] Supabase client is ready.");
    }

    private IEnumerator WaitForTask(Task task, string actionName)
    {
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
        {
            string errorMessage = task.Exception?.GetBaseException().Message ?? "Unknown error";
            SetErrorStatus(errorMessage);
            Debug.LogError($"[Matchmaking] Failed to {actionName}: {errorMessage}");
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
        return "ROOM-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    }

    private static bool IsWaitingEntry(MatchmakingQueueData entry)
    {
        return entry != null && string.Equals(entry.Status, WaitingStatus, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsFreshWaitingEntry(MatchmakingQueueData entry)
    {
        if (!IsWaitingEntry(entry))
        {
            return false;
        }

        DateTime cutoffUtc = GetWaitingCutoffUtc();
        return entry.CreatedAt >= cutoffUtc;
    }

    private DateTime GetWaitingCutoffUtc()
    {
        float timeoutSeconds = Mathf.Max(30f, staleWaitingTimeoutSeconds);
        return DateTime.UtcNow.AddSeconds(-timeoutSeconds);
    }

    private async Task ExpireStaleWaitingEntriesAsync(Supabase.Client client)
    {
        DateTime cutoffUtc = GetWaitingCutoffUtc();
        var waitingResponse = await client
            .From<MatchmakingQueueData>()
            .Where(x => x.Status == WaitingStatus)
            .Order(x => x.CreatedAt, Postgrest.Constants.Ordering.Ascending)
            .Limit(20)
            .Get();

        var staleEntries = waitingResponse.Models
            .Where(IsWaitingEntry)
            .Where(x => x.CreatedAt < cutoffUtc)
            .ToList();

        foreach (var staleEntry in staleEntries)
        {
            staleEntry.Status = CancelledStatus;
            staleEntry.RoomCode = null;
            staleEntry.MatchedAt = null;
            await client.From<MatchmakingQueueData>().Update(staleEntry);
            Debug.Log($"[Matchmaking] Expired stale waiting row. Id={staleEntry.Id}, PlayerId={staleEntry.PlayerId}, CreatedAt={staleEntry.CreatedAt:O}");
        }
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

    private void RememberCurrentQueueEntry(MatchmakingQueueData entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
        {
            return;
        }

        _currentQueueEntryId = entry.Id;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log($"[Matchmaking] UI Status => {message}");
    }

    private void SetErrorStatus(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            SetStatus("Error");
            return;
        }

        SetStatus($"Error: {errorMessage}");
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

    private static bool LooksLikeDuplicateQueueError(Exception ex)
    {
        string message = ex?.Message ?? string.Empty;
        return message.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }

    private async Task CancelMismatchedWaitingEntriesAsync(Supabase.Client client, int targetPlayerCount)
    {
        string targetMarker = BuildWaitingRoomMarker(targetPlayerCount);
        var waitingEntries = await GetWaitingEntriesForCurrentPlayerAsync(client);
        foreach (var waitingEntry in waitingEntries)
        {
            if (string.Equals(waitingEntry.RoomCode, targetMarker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            waitingEntry.Status = CancelledStatus;
            waitingEntry.RoomCode = null;
            waitingEntry.MatchedAt = null;
            await client.From<MatchmakingQueueData>().Update(waitingEntry);
        }
    }

    private int GetSavedTargetPlayerCount()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(TargetPlayerCountPrefsKey, 2), 2, 4);
    }

    private int GetDesiredPlayerCountForEntry(MatchmakingQueueData entry)
    {
        int parsedTargetCount = ParseTargetPlayerCountFromWaitingMarker(entry?.RoomCode);
        return parsedTargetCount > 0 ? parsedTargetCount : GetSavedTargetPlayerCount();
    }

    private static string BuildWaitingRoomMarker(int targetPlayerCount)
    {
        return $"QUEUE-{Mathf.Clamp(targetPlayerCount, 2, 4)}";
    }

    private static int ParseTargetPlayerCountFromWaitingMarker(string marker)
    {
        if (string.IsNullOrWhiteSpace(marker) || !marker.StartsWith("QUEUE-", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        string countText = marker.Substring("QUEUE-".Length);
        return int.TryParse(countText, out int parsedCount) ? Mathf.Clamp(parsedCount, 2, 4) : 0;
    }
}
