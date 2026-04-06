using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System.Collections.Generic;
using System;
using UnityEngine.SceneManagement;

public class FusionManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static FusionManager Instance { get; private set; }
    private NetworkRunner _runner;

    [Header("---- Scene Names ----")]
    public string gameSceneName = "SampleScene";

    private void Awake()
    {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        } else {
            if (Instance != this) Destroy(gameObject);
        }
    }

    // เช็คว่าเป็นเจ้าของห้อง (Master Client) หรือไม่
    public bool IsMasterClient => _runner != null && _runner.IsServer;

    public async void StartGame(GameMode mode, string roomName)
    {
        if (_runner == null) {
            _runner = gameObject.AddComponent<NetworkRunner>();
        }

        _runner.ProvideInput = true;

        var result = await _runner.StartGame(new StartGameArgs() {
            GameMode = mode,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (result.Ok) {
            Debug.Log($"[Fusion] เริ่มเกมสำเร็จ: {roomName} (Mode: {mode})");
        } else {
            Debug.LogError($"[Fusion] เริ่มเกมล้มเหลว: {result.ShutdownReason}");
        }
    }

    // ฟังก์ชันออกจากห้อง
    public void Disconnect()
    {
        if (_runner != null) {
            _runner.Shutdown();
            _runner = null;
        }
    }

    // --- INetworkRunnerCallbacks ---

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) 
    {
        Debug.Log($"[Fusion] ผู้เล่น {player} เข้าร่วมห้อง");
        
        // เมื่อเรา (Local Player) เข้าห้องสำเร็จ ให้สั่งเปลี่ยนหน้า UI
        if (player == runner.LocalPlayer) {
            if (LobbyUI.Instance != null) LobbyUI.Instance.SetViewState(true);
        }

        RefreshPlayerList(runner);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) 
    {
        Debug.Log($"[Fusion] ผู้เล่น {player} ออกจากห้อง");
        RefreshPlayerList(runner);
    }

    private void RefreshPlayerList(NetworkRunner runner)
    {
        if (LobbyUI.Instance == null) return;

        string list = "Players in Room:\n";
        foreach (var p in runner.ActivePlayers) {
            list += "- Player " + p.PlayerId + (p == runner.LocalPlayer ? " (You)" : "") + "\n";
        }
        LobbyUI.Instance.UpdatePlayerList(list);
    }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { Debug.Log($"[Fusion] ระบบชัตดาวน์: {shutdownReason}"); }
    public void OnConnectedToServer(NetworkRunner runner) { Debug.Log("[Fusion] เชื่อมต่อกับเซิร์ฟเวอร์สำเร็จ!"); }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { Debug.Log($"[Fusion] ตัดการเชื่อมต่อจากเซิร์ฟเวอร์: {reason}"); }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
