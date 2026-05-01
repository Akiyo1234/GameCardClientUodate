using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Globalization;

public class FusionManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static FusionManager Instance { get; private set; }
    public event Action PlayerNamesUpdated;
    public event Action ActivePlayersChanged;
    public event Action<int, int, int, int> TurnStateReceived;
    public event Action<int> QuizStartedReceived;
    public event Action<QuizAnswerSnapshot> QuizAnswerReceived;
    public event Action<List<QuizAnswerSnapshot>, List<int>> QuizResultsReceived;

    private const char PlayerNameSeparator = '|';
    private const string PlayerNameMessageType = "NAME";
    private const string TurnStateMessageType = "TURN";
    private const string QuizStartMessageType = "QUIZSTART";
    private const string QuizAnswerMessageType = "QUIZANSWER";
    private const string QuizResultMessageType = "QUIZRESULT";
    private NetworkRunner _runner;
    private NetworkSceneManagerDefault _sceneManager;
    private readonly Dictionary<int, string> _playerNames = new Dictionary<int, string>();

    public struct QuizAnswerSnapshot
    {
        public int PlayerIndex;
        public bool IsCorrect;
        public float TimeTaken;
    }

    [Header("---- Scene Names ----")]
    public string gameSceneName = "SampleScene";

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    public bool IsMasterClient => _runner != null && _runner.IsServer;
    public int ActivePlayerCount => _runner == null ? 0 : _runner.ActivePlayers.Count();

    public void StartMatchedGame(string roomCode, string sceneName = null)
    {
        if (!string.IsNullOrWhiteSpace(sceneName))
        {
            gameSceneName = sceneName;
        }

        StartGame(GameMode.AutoHostOrClient, roomCode);
    }

    public async void StartGame(GameMode mode, string roomName)
    {
        await ResetRunnerAsync();

        _runner = gameObject.AddComponent<NetworkRunner>();
        _sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
        _runner.AddCallbacks(this);
        _runner.ProvideInput = true;
        _playerNames.Clear();

        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = roomName,
            Scene = ResolveSceneRef(),
            SceneManager = _sceneManager
        });

        if (result.Ok)
        {
            Debug.Log($"[Fusion] Started session successfully: {roomName} (Mode: {mode})");
        }
        else
        {
            Debug.LogError($"[Fusion] StartGame failed: {result.ShutdownReason}");
            CleanupRunnerComponents();
        }
    }

    public void Disconnect()
    {
        _ = ResetRunnerAsync();
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[Fusion] Player joined: {player}");

        RegisterPlayerName(runner.LocalPlayer.PlayerId, GetLocalPlayerName(runner.LocalPlayer.PlayerId));

        if (runner.IsServer && player != runner.LocalPlayer)
        {
            SendKnownPlayerNamesToPlayer(player);
        }

        if (player == runner.LocalPlayer && LobbyUI.Instance != null)
        {
            LobbyUI.Instance.SetViewState(true);
        }

        if (player == runner.LocalPlayer && !runner.IsServer)
        {
            SendLocalPlayerNameToServer();
        }

        RefreshPlayerList(runner);
        NotifyActivePlayersChanged();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[Fusion] Player left: {player}");
        if (_playerNames.Remove(player.PlayerId))
        {
            NotifyPlayerNamesUpdated();
        }
        RefreshPlayerList(runner);
        NotifyActivePlayersChanged();
    }

    private void RefreshPlayerList(NetworkRunner runner)
    {
        if (LobbyUI.Instance == null)
        {
            return;
        }

        string list = "Players in Room:\n";
        foreach (var p in runner.ActivePlayers)
        {
            list += "- Player " + p.PlayerId + (p == runner.LocalPlayer ? " (You)" : string.Empty) + "\n";
        }

        LobbyUI.Instance.UpdatePlayerList(list);
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[Fusion] Runner shutdown: {shutdownReason}");
        if (runner == _runner)
        {
            CleanupRunnerComponents();
        }
        NotifyActivePlayersChanged();
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("[Fusion] Connected to server successfully.");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"[Fusion] Disconnected from server: {reason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        string payload = Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        string[] parts = payload.Split(PlayerNameSeparator);
        if (parts.Length == 0)
        {
            return;
        }

        if (string.Equals(parts[0], PlayerNameMessageType, StringComparison.Ordinal))
        {
            if (parts.Length < 3 || !int.TryParse(parts[1], out int playerId))
            {
                return;
            }

            string playerName = string.Join(PlayerNameSeparator.ToString(), parts.Skip(2));
            RegisterPlayerName(playerId, playerName);

            if (runner.IsServer)
            {
                BroadcastPlayerName(player, playerId, playerName);
            }

            return;
        }

        if (string.Equals(parts[0], TurnStateMessageType, StringComparison.Ordinal))
        {
            if (parts.Length < 5)
            {
                return;
            }

            if (!int.TryParse(parts[1], out int currentPlayerIndex) ||
                !int.TryParse(parts[2], out int currentRound) ||
                !int.TryParse(parts[3], out int totalTurnCount) ||
                !int.TryParse(parts[4], out int currentTurnDisplay))
            {
                return;
            }

            if (runner.IsServer)
            {
                TurnStateReceived?.Invoke(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);

                foreach (var activePlayer in runner.ActivePlayers)
                {
                    if (activePlayer == player || activePlayer == runner.LocalPlayer)
                    {
                        continue;
                    }

                    runner.SendReliableDataToPlayer(activePlayer, default, data.ToArray());
                }
            }
            else
            {
                TurnStateReceived?.Invoke(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);
            }

            return;
        }

        if (string.Equals(parts[0], QuizStartMessageType, StringComparison.Ordinal))
        {
            if (runner.IsServer || parts.Length < 2 || !int.TryParse(parts[1], out int questionIndex))
            {
                return;
            }

            QuizStartedReceived?.Invoke(questionIndex);
            return;
        }

        if (string.Equals(parts[0], QuizAnswerMessageType, StringComparison.Ordinal))
        {
            if (!runner.IsServer || parts.Length < 4 || !int.TryParse(parts[1], out int answerPlayerIndex))
            {
                return;
            }

            if (!TryParseBooleanFlag(parts[2], out bool isCorrect) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float timeTaken))
            {
                return;
            }

            QuizAnswerReceived?.Invoke(new QuizAnswerSnapshot
            {
                PlayerIndex = answerPlayerIndex,
                IsCorrect = isCorrect,
                TimeTaken = timeTaken
            });

            return;
        }

        if (string.Equals(parts[0], QuizResultMessageType, StringComparison.Ordinal))
        {
            if (runner.IsServer || parts.Length < 2)
            {
                return;
            }

            List<QuizAnswerSnapshot> quizAnswers = DecodeQuizAnswers(parts[1]);
            List<int> rewardGemIndices = parts.Length >= 3
                ? DecodeRewardGemIndices(parts[2])
                : new List<int>();

            QuizResultsReceived?.Invoke(quizAnswers, rewardGemIndices);
            return;
        }

        int separatorIndex = payload.IndexOf(PlayerNameSeparator);
        if (separatorIndex <= 0 || separatorIndex >= payload.Length - 1)
        {
            return;
        }

        string legacyPlayerIdText = payload.Substring(0, separatorIndex);
        string legacyPlayerName = payload.Substring(separatorIndex + 1);
        if (!int.TryParse(legacyPlayerIdText, out int legacyPlayerId))
        {
            return;
        }

        RegisterPlayerName(legacyPlayerId, legacyPlayerName);

        if (runner.IsServer)
        {
            BroadcastPlayerName(player, legacyPlayerId, legacyPlayerName);
        }
    }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    private SceneRef ResolveSceneRef()
    {
        var buildIndex = FindBuildIndexByName(gameSceneName);
        if (buildIndex >= 0)
        {
            return SceneRef.FromIndex(buildIndex);
        }

        return SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);
    }

    private static int FindBuildIndexByName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return -1;
        }

        for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            var scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            var buildSceneName = Path.GetFileNameWithoutExtension(scenePath);

            if (string.Equals(buildSceneName, sceneName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private async Task ResetRunnerAsync()
    {
        if (_runner != null)
        {
            try
            {
                await _runner.Shutdown();
            }
            catch (Exception shutdownException)
            {
                Debug.LogWarning($"[Fusion] Runner shutdown warning: {shutdownException.Message}");
            }
        }

        CleanupRunnerComponents();
    }

    private void CleanupRunnerComponents()
    {
        if (_runner != null)
        {
            Destroy(_runner);
            _runner = null;
        }

        if (_sceneManager != null)
        {
            Destroy(_sceneManager);
            _sceneManager = null;
        }
    }

    public string GetRemotePlayerName(int remoteIndex)
    {
        if (_runner == null || remoteIndex < 0)
        {
            return null;
        }

        var remotePlayers = _runner.ActivePlayers
            .Where(p => p != _runner.LocalPlayer)
            .OrderBy(p => p.PlayerId)
            .ToList();

        if (remoteIndex >= remotePlayers.Count)
        {
            return null;
        }

        var remotePlayer = remotePlayers[remoteIndex];
        return _playerNames.TryGetValue(remotePlayer.PlayerId, out string remoteName)
            ? remoteName
            : null;
    }

    public int GetLocalPlayerSeatIndex()
    {
        if (_runner == null)
        {
            return 0;
        }

        var orderedPlayers = GetOrderedActivePlayers();
        for (int i = 0; i < orderedPlayers.Count; i++)
        {
            if (orderedPlayers[i] == _runner.LocalPlayer)
            {
                return i;
            }
        }

        return 0;
    }

    public string GetPlayerNameBySeat(int seatIndex)
    {
        var orderedPlayers = GetOrderedActivePlayers();
        if (seatIndex < 0 || seatIndex >= orderedPlayers.Count)
        {
            return null;
        }

        return GetDisplayNameForPlayer(orderedPlayers[seatIndex]);
    }

    public void SendTurnState(int currentPlayerIndex, int currentRound, int totalTurnCount, int currentTurnDisplay)
    {
        if (_runner == null)
        {
            return;
        }

        byte[] payload = EncodeTurnStatePayload(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);
        if (_runner.IsServer)
        {
            foreach (var activePlayer in _runner.ActivePlayers)
            {
                if (activePlayer == _runner.LocalPlayer)
                {
                    continue;
                }

                _runner.SendReliableDataToPlayer(activePlayer, default, payload);
            }

            return;
        }

        _runner.SendReliableDataToServer(default, payload);
    }

    public void SendQuizStart(int questionIndex)
    {
        if (_runner == null || !_runner.IsServer)
        {
            return;
        }

        byte[] payload = Encoding.UTF8.GetBytes($"{QuizStartMessageType}{PlayerNameSeparator}{questionIndex}");
        foreach (var activePlayer in _runner.ActivePlayers)
        {
            if (activePlayer == _runner.LocalPlayer)
            {
                continue;
            }

            _runner.SendReliableDataToPlayer(activePlayer, default, payload);
        }
    }

    public void SendQuizAnswer(int playerIndex, bool isCorrect, float timeTaken)
    {
        if (_runner == null || _runner.IsServer)
        {
            return;
        }

        string correctnessFlag = isCorrect ? "1" : "0";
        string timeTakenText = timeTaken.ToString("0.000", CultureInfo.InvariantCulture);
        byte[] payload = Encoding.UTF8.GetBytes(
            $"{QuizAnswerMessageType}{PlayerNameSeparator}{playerIndex}{PlayerNameSeparator}{correctnessFlag}{PlayerNameSeparator}{timeTakenText}");
        _runner.SendReliableDataToServer(default, payload);
    }

    public void SendQuizResults(IEnumerable<QuizAnswerSnapshot> answers, IEnumerable<int> rewardGemIndices)
    {
        if (_runner == null || !_runner.IsServer)
        {
            return;
        }

        string answersPayload = EncodeQuizAnswers(answers);
        string rewardsPayload = EncodeRewardGemIndices(rewardGemIndices);
        byte[] payload = Encoding.UTF8.GetBytes(
            $"{QuizResultMessageType}{PlayerNameSeparator}{answersPayload}{PlayerNameSeparator}{rewardsPayload}");

        foreach (var activePlayer in _runner.ActivePlayers)
        {
            if (activePlayer == _runner.LocalPlayer)
            {
                continue;
            }

            _runner.SendReliableDataToPlayer(activePlayer, default, payload);
        }
    }

    private void SendLocalPlayerNameToServer()
    {
        if (_runner == null)
        {
            return;
        }

        string localName = GetLocalPlayerName(_runner.LocalPlayer.PlayerId);
        byte[] payload = EncodePlayerNamePayload(_runner.LocalPlayer.PlayerId, localName);
        _runner.SendReliableDataToServer(default, payload);
    }

    private void SendKnownPlayerNamesToPlayer(PlayerRef targetPlayer)
    {
        if (_runner == null || !_runner.IsServer)
        {
            return;
        }

        foreach (var pair in _playerNames)
        {
            byte[] payload = EncodePlayerNamePayload(pair.Key, pair.Value);
            _runner.SendReliableDataToPlayer(targetPlayer, default, payload);
        }
    }

    private void BroadcastPlayerName(PlayerRef sourcePlayer, int playerId, string playerName)
    {
        if (_runner == null || !_runner.IsServer)
        {
            return;
        }

        byte[] payload = EncodePlayerNamePayload(playerId, playerName);
        foreach (var activePlayer in _runner.ActivePlayers)
        {
            if (activePlayer == sourcePlayer)
            {
                continue;
            }

            _runner.SendReliableDataToPlayer(activePlayer, default, payload);
        }
    }

    private void RegisterPlayerName(int playerId, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        _playerNames[playerId] = playerName;
        NotifyPlayerNamesUpdated();
    }

    private void NotifyPlayerNamesUpdated()
    {
        PlayerNamesUpdated?.Invoke();
    }

    private void NotifyActivePlayersChanged()
    {
        ActivePlayersChanged?.Invoke();
    }

    private static byte[] EncodePlayerNamePayload(int playerId, string playerName)
    {
        string safeName = string.IsNullOrWhiteSpace(playerName) ? "Player " + playerId : playerName.Trim();
        return Encoding.UTF8.GetBytes($"{PlayerNameMessageType}{PlayerNameSeparator}{playerId}{PlayerNameSeparator}{safeName}");
    }

    private static byte[] EncodeTurnStatePayload(int currentPlayerIndex, int currentRound, int totalTurnCount, int currentTurnDisplay)
    {
        return Encoding.UTF8.GetBytes(
            $"{TurnStateMessageType}{PlayerNameSeparator}{currentPlayerIndex}{PlayerNameSeparator}{currentRound}{PlayerNameSeparator}{totalTurnCount}{PlayerNameSeparator}{currentTurnDisplay}");
    }

    private List<PlayerRef> GetOrderedActivePlayers()
    {
        if (_runner == null)
        {
            return new List<PlayerRef>();
        }

        return _runner.ActivePlayers
            .OrderBy(p => p.PlayerId)
            .ToList();
    }

    private string GetDisplayNameForPlayer(PlayerRef player)
    {
        if (_playerNames.TryGetValue(player.PlayerId, out string playerName) && !string.IsNullOrWhiteSpace(playerName))
        {
            return playerName;
        }

        return "Player " + player.PlayerId;
    }

    private static bool TryParseBooleanFlag(string value, out bool result)
    {
        if (value == "1")
        {
            result = true;
            return true;
        }

        if (value == "0")
        {
            result = false;
            return true;
        }

        return bool.TryParse(value, out result);
    }

    private static string EncodeQuizAnswers(IEnumerable<QuizAnswerSnapshot> answers)
    {
        if (answers == null)
        {
            return string.Empty;
        }

        return string.Join(";", answers.Select(answer =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2:0.000}",
                answer.PlayerIndex,
                answer.IsCorrect ? 1 : 0,
                answer.TimeTaken)));
    }

    private static List<QuizAnswerSnapshot> DecodeQuizAnswers(string payload)
    {
        var decodedAnswers = new List<QuizAnswerSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return decodedAnswers;
        }

        string[] answerEntries = payload.Split(';');
        foreach (string answerEntry in answerEntries)
        {
            if (string.IsNullOrWhiteSpace(answerEntry))
            {
                continue;
            }

            string[] answerParts = answerEntry.Split(',');
            if (answerParts.Length < 3 ||
                !int.TryParse(answerParts[0], out int playerIndex) ||
                !TryParseBooleanFlag(answerParts[1], out bool isCorrect) ||
                !float.TryParse(answerParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float timeTaken))
            {
                continue;
            }

            decodedAnswers.Add(new QuizAnswerSnapshot
            {
                PlayerIndex = playerIndex,
                IsCorrect = isCorrect,
                TimeTaken = timeTaken
            });
        }

        return decodedAnswers;
    }

    private static string EncodeRewardGemIndices(IEnumerable<int> rewardGemIndices)
    {
        if (rewardGemIndices == null)
        {
            return string.Empty;
        }

        return string.Join(",", rewardGemIndices);
    }

    private static List<int> DecodeRewardGemIndices(string payload)
    {
        var rewardGemIndices = new List<int>();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return rewardGemIndices;
        }

        string[] rewardParts = payload.Split(',');
        foreach (string rewardPart in rewardParts)
        {
            if (int.TryParse(rewardPart, out int gemIndex))
            {
                rewardGemIndices.Add(gemIndex);
            }
        }

        return rewardGemIndices;
    }

    private static string GetLocalPlayerName(int fallbackPlayerId)
    {
        if (SupabaseManager.Instance != null)
        {
            string supabaseName = SupabaseManager.Instance.GetCurrentUsername();
            if (!string.IsNullOrWhiteSpace(supabaseName))
            {
                return supabaseName;
            }
        }

        string savedName = PlayerPrefs.GetString("Username", string.Empty);
        return string.IsNullOrWhiteSpace(savedName) ? "Player " + fallbackPlayerId : savedName;
    }
}
