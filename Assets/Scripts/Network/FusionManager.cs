using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Globalization;

// =============================================================================
// FusionManager — หัวใจหลักของระบบ Multiplayer (Photon Fusion)
// -----------------------------------------------------------------------
// หน้าที่:
//   1. สร้าง/เข้าร่วมห้องผ่าน Photon Fusion (Host/Client/AutoHostOrClient)
//   2. ส่งข้อมูลผ่าน SendReliableDataToPlayer (text-based protocol)
//   3. รับข้อมูลใน OnReliableDataReceived และ route ไปยัง events ที่ถูกต้อง
//   4. อัปเดต LobbyUI เมื่อผู้เล่นเข้า/ออกห้อง
//   5. Sync สถานะห้อง (waiting/playing/finished) ไปยัง Supabase
// -----------------------------------------------------------------------
// Network Message Protocol (ใช้ | เป็นตัวคั่น field):
//   NAME|playerId|playerName       → ส่งชื่อผู้เล่น
//   TURN|playerIdx|round|total|disp → สะสถานะเทิร์น
//   ECON|bankCoins|playerData       → เศรษฐกิจ (bank, เหรียญ, คะแนน)
//   BOARD|t1|t2|t3|used            → การ์ดบนกระดาน
//   QUIZSTART|questionIndex         → หาก Host เริ่มควิซ
//   QUIZANSWER|playerIdx|bool|time  → Client ส่งคำตอบ
//   QUIZRESULT|answers|rewardIndices→ Host ประกาศผล
//   QUIZREQ|──                      → Client ขอให้เริ่มควิซ
//   STATEREQ|──                     → Late-joiner ขอ Full State
// -----------------------------------------------------------------------
// Pattern: Singleton + DontDestroyOnLoad (ผ่าน Awake)
// =============================================================================
public class FusionManager : MonoBehaviour, INetworkRunnerCallbacks
{
    // Singleton instance และ Events ที่ GameController subscribe ไว้
    public static FusionManager Instance { get; private set; }
    public event Action PlayerNamesUpdated;       // เมื่อรับชื่อผู้เล่นคนใดก็ตาม
    public event Action ActivePlayersChanged;     // เมื่อคนเข้า/ออกห้อง
    public event Action<int, int, int, int> TurnStateReceived;  // ชุด Turn State (player, round, total, display)
    public event Action<int> QuizStartedReceived;               // เมื่อรับคำสั่งเริ่มควิซจาก Host
    public event Action<QuizAnswerSnapshot> QuizAnswerReceived; // Host รับคำตอบจาก Client
    public event Action<List<QuizAnswerSnapshot>, List<int>> QuizResultsReceived; // ผลควิซจาก Host
    public event Action<EconomyStateSnapshot> EconomyStateReceived;  // สถานะเศรษฐกิจ
    public event Action<BoardStateSnapshot> BoardStateReceived;      // สถานะกระดาน
    public event Action QuizStartRequested;       // Client ขอเริ่มควิซ
    // late-joiner ขอ full state จาก host — ส่ง playerId ของคนที่ขอ เพื่อให้ host ตอบกลับเฉพาะคนนั้น
    public event Action<int> FullStateRequested;
    // [NEW] เมื่อรับข้อมูล characterIndex ของผู้เล่นคนอื่น (playerId, characterIndex)
    public event Action<int, int> PlayerCharacterReceived;

    private const char PlayerNameSeparator = '|';
    private const string PlayerNameMessageType = "NAME";
    private const string TurnStateMessageType = "TURN";
    private const string QuizStartMessageType = "QUIZSTART";
    private const string QuizAnswerMessageType = "QUIZANSWER";
    private const string QuizResultMessageType = "QUIZRESULT";
    private const string EconomyStateMessageType = "ECON";
    private const string BoardStateMessageType = "BOARD";
    private const string QuizRequestMessageType = "QUIZREQ";
    private const string StateRequestMessageType = "STATEREQ";
    private const string CharacterMessageType = "CHAR"; // [NEW] sync avatar
    private NetworkRunner _runner;
    private NetworkSceneManagerDefault _sceneManager;
    private readonly Dictionary<int, string> _playerNames = new Dictionary<int, string>();
    // [Shared Mode · Step 5] Stable seat map: index = seat, value = PlayerId (เรียงตามลำดับเข้าห้อง = id จากน้อยไปมาก)
    //   ตรึงครั้งเดียว — "ไม่ลบ" เมื่อมีคนออก เพื่อให้ seat ของคนที่เหลือคงที่ (กันบั๊ก seat เลื่อนสวมรอยคนที่ออก)
    private readonly List<int> _seatOrder = new List<int>();
    private readonly Dictionary<int, int> _playerCharacters = new Dictionary<int, int>(); // [NEW] playerId -> characterIndex
    private bool _hasPendingQuizStart;
    private int _pendingQuizStartIndex = -1;
    // [NEW] ติดตามว่าเกมเริ่มไปแล้วหรือยัง เพื่อใช้ตัดสินว่าควร kick กลับ MainMenu เมื่อคนออกกลางเกม
    public bool IsGameInProgress { get; set; } = false;

    public struct QuizAnswerSnapshot
    {
        public int PlayerIndex;
        public bool IsCorrect;
        public float TimeTaken;
    }

    public struct EconomyPlayerSnapshot
    {
        public int Score;
        public int[] Coins;
        public int[] Bonuses;
        public int QuizBlackCoins;
        public string[] ReservedCardIds;
    }

    public struct EconomyStateSnapshot
    {
        public int[] BankCoins;
        public EconomyPlayerSnapshot[] Players;
    }

    // สถานะการ์ดบนกระดาน (face-up market) สำหรับ sync ออนไลน์
    // แต่ละ tier เก็บ cardId ตามลำดับช่อง (string.Empty = ช่องว่าง)
    // UsedCardIds = cardId ทั้งหมดที่ถูกจั่วออกจากกอง (กันการ์ดซ้ำ/เพี้ยนข้ามเครื่อง)
    public struct BoardStateSnapshot
    {
        public string[] Tier1CardIds;
        public string[] Tier2CardIds;
        public string[] Tier3CardIds;
        public string[] UsedCardIds;
    }

    [Header("---- Scene Names ----")]
    public string gameSceneName = "SampleScene";

    [Header("---- Photon ----")]
    // บังคับ Photon region ให้ทุกแพลตฟอร์มต่อที่เดียวกัน (asia, jp, sg, us, ...) — เว้นว่าง = ใช้ best region ตาม ping
    // จำเป็นมาก: ไม่งั้น PC กับมือถืออาจต่อคนละ region → host สร้างห้องที่นึง client หาอีกที่นึง → เข้าห้องไม่ได้
    [SerializeField] private string fixedPhotonRegion = "asia";
    // เปิด Photon log ละเอียด (region/operation) ลง logcat เพื่อดีบัก — เปิดเฉพาะตอนต้องไล่ปัญหา network
    [SerializeField] private bool verbosePhotonLog = false;

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

    // [Shared Mode · Step 2] authority = ผู้เล่น id ต่ำสุด (ใน Host mode = host เดิม → behavior ไม่เปลี่ยน)
    public bool IsMasterClient => _runner != null && _runner.IsRunning && _runner.LocalPlayer == AuthorityPlayer;
    public NetworkRunner Runner => _runner;
    public int ActivePlayerCount => _runner == null ? 0 : _runner.ActivePlayers.Count();
    // ชื่อห้อง/เซสชัน Photon — เท่ากันทุกเครื่องในแมตช์เดียวกัน (ใช้ทำ seed สุ่มกระดานให้ตรงกันข้ามเครื่อง)
    public string CurrentSessionName => _runner != null && _runner.SessionInfo != null ? _runner.SessionInfo.Name : null;

    // ─── [Shared Mode Migration · Step 1] Authority helpers ───────────────────
    // นิยาม "Authority" = ผู้เล่นที่ PlayerId ต่ำสุดในห้อง (= seat 0 = Photon shared master client โดยพฤตินัย)
    // ใช้แทนแนวคิด Host/IsServer ที่ใช้ไม่ได้ใน Shared Mode (ซึ่งไม่มี server peer)
    // ทุกเครื่องคำนวณค่าเดียวกันเสมอ → ได้ host-migration อัตโนมัติ (authority ออก → คนถัดไป id ต่ำสุดรับช่วงเอง)
    //
    // หมายเหตุ: ยังไม่มีใครเรียกใน Step 1 — เพิ่มไว้เฉยๆ ไม่กระทบ behavior เดิม (จะเอาไปใช้ใน Step 2)

    // PlayerRef ของ authority ปัจจุบัน (default ถ้ายังไม่มีผู้เล่น/runner ยังไม่พร้อม)
    public PlayerRef AuthorityPlayer
    {
        get
        {
            if (_runner == null)
            {
                return default;
            }

            PlayerRef authority = default;
            bool found = false;
            foreach (var player in _runner.ActivePlayers)
            {
                if (!found || player.PlayerId < authority.PlayerId)
                {
                    authority = player;
                    found = true;
                }
            }

            return authority;
        }
    }

    // local player เป็น authority ของห้องนี้ไหม (เวอร์ชันรับ runner จาก callback)
    private bool IsLocalAuthority(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning)
        {
            return false;
        }

        PlayerRef authority = default;
        bool found = false;
        foreach (var player in runner.ActivePlayers)
        {
            if (!found || player.PlayerId < authority.PlayerId)
            {
                authority = player;
                found = true;
            }
        }

        return found && runner.LocalPlayer == authority;
    }

    // ส่งข้อมูลไปยัง authority ของห้อง (แทน SendReliableDataToServer ที่ใช้ไม่ได้ใน Shared Mode)
    // ถ้า local เป็น authority เองอยู่แล้ว → ไม่ต้องส่ง (caller ควรจัดการ state ตัวเองโดยตรง)
    private void SendToAuthority(byte[] payload)
    {
        if (_runner == null || payload == null)
        {
            return;
        }

        PlayerRef authority = AuthorityPlayer;
        if (authority == _runner.LocalPlayer)
        {
            return;
        }

        _runner.SendReliableDataToPlayer(authority, default, payload);
    }

    // =============================================================================
    // StartMatchedGame — เริ่มเกมหลัง Matchmaking
    // -----------------------------------------------------------------------
    // [Shared Mode · Step 3] ทุกเครื่องเข้าด้วย GameMode.Shared + SessionName เดียวกัน
    //   Photon สร้างห้องให้คนแรก แล้วคนถัดมา join ห้องเดิมอัตโนมัติ → ไม่มี host/client race
    //   พารามิเตอร์ isHost เก็บไว้เพื่อความเข้ากันได้กับ caller เดิม แต่ไม่ใช้แล้ว (Shared ไม่มี host election)
    // =============================================================================
    public void StartMatchedGame(string roomCode, string sceneName = null, Action<string> onFail = null, bool? isHost = null)
    {
        // [FIX] ระบุว่าเป็นโหมดออนไลน์
        PlayerPrefs.SetString("GameMode", "Online");
        PlayerPrefs.Save();

        // ถ้าไม่มี sceneName (Lobby manual) → เข้าห้อง Shared ค้างไว้ในฉาก lobby เพื่อรอคนเข้าร่วม
        if (string.IsNullOrEmpty(sceneName))
        {
            StartGameCoroutine(GameMode.Shared, roomCode, null);
            return;
        }

        // [FIX-ANDROID] Auto-Match ใช้ Coroutine retry — ทุก call อยู่บน Main Thread
        StartCoroutine(StartMatchedGameCoroutine(roomCode, sceneName, onFail, isHost));
    }

    private IEnumerator StartMatchedGameCoroutine(string roomCode, string sceneName, Action<string> onFail, bool? isHost)
    {
        const int maxRetries = 24;
        const float retryDelaySeconds = 2.5f;
        string lastFailReason = "Unknown";

        // [Shared Mode · Step 3] ไม่มี host election แล้ว — ทุกเครื่องเข้าด้วย GameMode.Shared
        //   คนแรกที่เข้าจะสร้างห้อง คนถัดมา join ห้องเดิมให้เอง (Photon จัดการ) → ไม่ต้องรอ/ไม่ race
        //   _ = isHost; // เก็บพารามิเตอร์ไว้เพื่อ compat แต่ไม่ใช้
        _ = isHost;
        GameMode targetMode = GameMode.Shared;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                GameLog.Log($"[Fusion] Auto-Match retry {attempt}/{maxRetries} for room {roomCode}");
                yield return new WaitForSeconds(retryDelaySeconds);
            }

            // ใช้ StartGameCoroutine (ซึ่งจัดการทุกอย่างบน main thread)
            bool? result = null;
            yield return StartGameCoroutineInternal(targetMode, roomCode, sceneName, ok => result = ok, reason => lastFailReason = reason);

            if (result == true)
            {
                GameLog.Log($"[Fusion] Auto-Match OK: room={roomCode}, isMaster={IsMasterClient}");
                yield break;
            }

            GameLog.Log($"[Fusion] Auto-Match attempt {attempt} failed. Will retry...");
        }

        string errorMsg = $"Failed to join room '{roomCode}' after {maxRetries} retries. Last Error: {lastFailReason}";
        Debug.LogWarning($"[Fusion] Auto-Match: {errorMsg}");
        onFail?.Invoke(errorMsg);
    }

    public void LoadGameScene()
    {
        if (_runner != null && IsLocalAuthority(_runner))
        {
            string sceneToLoad = string.IsNullOrEmpty(gameSceneName) ? "SampleScene" : gameSceneName;
            _runner.LoadScene(ResolveSceneRef(sceneToLoad), UnityEngine.SceneManagement.LoadSceneMode.Single);

            // snapshot ผู้เล่นที่อยู่จริงตอนเกมเริ่ม + อัปเดต status='playing' ในครั้งเดียว
            SetRoomStatus("playing", _runner.ActivePlayers.Count());
        }
    }

    // host-only helper สำหรับอัปเดตสถานะห้องใน Supabase (waiting → playing → finished)
    public void SetRoomStatus(string status, int? playerCount = null)
    {
        if (_runner == null || !IsLocalAuthority(_runner)) return;
        string roomCode = _runner.SessionInfo?.Name;
        if (string.IsNullOrEmpty(roomCode)) return;
        if (SupabaseManager.Instance == null || !SupabaseManager.Instance.IsInitialized) return;

        _ = PlayerDataService.CreateRoomAsync(roomCode, playerCount: playerCount, status: status);
    }

    // ── Public entry: เรียกจาก LobbyUI / อื่นๆ ──
    // ยังคง signature เดิม (async Task) ไว้ เพื่อไม่ให้โค้ดที่ fire-and-forget ด้วย _ = ... พัง
    // แต่ภายในเปลี่ยนไปใช้ Coroutine เพื่อรับประกัน Main Thread safety บน Android
    public async Task StartGame(GameMode mode, string roomName, string sceneToLoad = null)
    {
        // [FIX] ระบุว่าเป็นโหมดออนไลน์เสมอเมื่อมีการเริ่ม Network
        PlayerPrefs.SetString("GameMode", "Online");
        PlayerPrefs.Save();

        // เรียก Coroutine ผ่าน helper ที่ block async จนกว่า coroutine จบ
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        StartCoroutine(StartGameCoroutineWithCallback(mode, roomName, sceneToLoad, tcs));
        await tcs.Task;
    }

    // helper: เพื่อให้ async callers รอ coroutine ให้จบได้
    private IEnumerator StartGameCoroutineWithCallback(GameMode mode, string roomName, string sceneToLoad, System.Threading.Tasks.TaskCompletionSource<bool> tcs)
    {
        yield return StartGameCoroutineInternal(mode, roomName, sceneToLoad, ok =>
        {
            tcs.TrySetResult(ok);
        });
    }

    // ── Public entry: Coroutine version ──
    public Coroutine StartGameCoroutine(GameMode mode, string roomName, string sceneToLoad = null)
    {
        // [FIX] ตั้ง GameMode = Online เหมือนกับ StartGame() และ StartMatchedGame()
        // ถ้าไม่ตั้งตรงนี้ → GameController.IsMatchedOnlineSession() จะคืนค่า false → เล่นกับ Bot แทน
        PlayerPrefs.SetString("GameMode", "Online");
        PlayerPrefs.Save();
        return StartCoroutine(StartGameCoroutineInternal(mode, roomName, sceneToLoad, null));
    }

    // ── Public entry: Coroutine version พร้อม callback ผลลัพธ์ (ใช้ใน JoinRoomWithRetryCoroutine) ──
    public Coroutine StartGameCoroutineWithResult(GameMode mode, string roomName, Action<bool> onComplete, string sceneToLoad = null, bool allowSessionCreation = true)
    {
        PlayerPrefs.SetString("GameMode", "Online");
        PlayerPrefs.Save();
        return StartCoroutine(StartGameCoroutineInternal(mode, roomName, sceneToLoad, onComplete, null, allowSessionCreation));
    }


    // ──────────────────────────────────────────────────────────────────
    //  Core: ทุก network join/create ผ่านที่นี่ — 100% Main Thread
    // ──────────────────────────────────────────────────────────────────
    // allowSessionCreation: true = สร้างห้องได้ถ้ายังไม่มี (Create/Matchmaking)
    //                       false = ต้องมีห้องอยู่จริงเท่านั้น (Join) — ถ้าไม่มีจะ error แทนการสร้างห้องเดี่ยว
    //   [Shared Mode · Step 6] กันบั๊ก "ต่างคนต่างสร้างห้องตัวเอง" เมื่อรหัสห้อง/region ไม่ตรง
    private IEnumerator StartGameCoroutineInternal(GameMode mode, string roomName, string sceneToLoad, Action<bool> onComplete, Action<string> onFailReason = null, bool allowSessionCreation = true)
    {
        // บังคับ region ให้ตรงกันทุกเครื่องก่อนต่อ Photon (กัน PC กับมือถือไปอยู่คนละ region แล้วหาห้องกันไม่เจอ)
        ApplyFixedPhotonRegion();

        // Reset runner ก่อน
        yield return ResetRunnerCoroutine();

        // สร้าง Runner ใหม่บน Main Thread
        _runner = gameObject.AddComponent<NetworkRunner>();
        _sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
        _runner.AddCallbacks(this);
        _runner.ProvideInput = true;
        _playerNames.Clear();
        _seatOrder.Clear();
        _hasPendingQuizStart = false;
        _pendingQuizStartIndex = -1;

        // ระบุฉากปลายทาง
        SceneRef targetScene;
        if (!string.IsNullOrEmpty(sceneToLoad))
        {
            targetScene = ResolveSceneRef(sceneToLoad);
        }
        else
        {
            targetScene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        GameLog.Log($"[Fusion] StartGame → room='{roomName}', mode={mode}, allowCreate={allowSessionCreation}, region='{(Fusion.Photon.Realtime.PhotonAppSettings.TryGetGlobal(out var ps) && ps.AppSettings != null ? ps.AppSettings.FixedRegion : "?")}'");

        // เรียก Fusion StartGame (async) แล้ว poll รอผลลัพธ์บน main thread
        var fusionStartTask = _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = roomName,
            Scene = targetScene,
            SceneManager = _sceneManager,
            // [Shared Mode · Step 6] Join (allowCreate=false) → ถ้าห้องไม่มีจริงจะ fail ไม่สร้างห้องเดี่ยว
            EnableClientSessionCreation = allowSessionCreation
        });

        // poll ทุก frame จนกว่า task จะเสร็จ (max 25 วินาที)
        float elapsed = 0f;
        while (!fusionStartTask.IsCompleted && elapsed < 25f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        bool taskOk = false;
        string failReason = "";

        if (!fusionStartTask.IsCompleted)
        {
            failReason = "StartGame timed out after 25s for room: " + roomName;
            Debug.LogWarning($"[Fusion] {failReason}");
            CleanupRunnerComponents();
            onComplete?.Invoke(false);
            onFailReason?.Invoke(failReason);
            yield break;
        }

        if (fusionStartTask.IsFaulted)
        {
            failReason = fusionStartTask.Exception?.GetBaseException().Message ?? "Unknown Task Exception";
        }
        else if (fusionStartTask.IsCompletedSuccessfully && fusionStartTask.Result.Ok)
        {
            taskOk = true;
        }
        else if (fusionStartTask.IsCompletedSuccessfully)
        {
            var startResult = fusionStartTask.Result;
            failReason = $"{startResult.ShutdownReason} | msg='{startResult.ErrorMessage}'";
            // log แบบเต็ม — ErrorMessage มักมีเหตุผลจริงจาก Photon (เช่น config/version mismatch, plugin error)
            Debug.LogWarning($"[Fusion] StartGame result detail: mode={mode}, room={roomName}, reason={startResult.ShutdownReason}, msg='{startResult.ErrorMessage}', stack={startResult.StackTrace}");
        }
        else
        {
            failReason = "Task Canceled or Failed";
        }

        if (taskOk)
        {
            GameLog.Log($"[Fusion] Started session successfully: {roomName} (Mode: {mode})");

            if (_runner != null && IsLocalAuthority(_runner) && SupabaseManager.Instance != null && SupabaseManager.Instance.IsInitialized)
            {
                _ = PlayerDataService.CreateRoomAsync(roomName, roomName, 1);
            }

            // Lobby UI update
            if (LobbyUI.Instance != null)
            {
                LobbyUI.Instance.SetViewState(true);
                if (LobbyUI.Instance.roomNameText != null)
                {
                    LobbyUI.Instance.roomNameText.text = "Room Code : " + roomName;
                }
            }

            onComplete?.Invoke(true);
        }
        else
        {
            Debug.LogWarning($"[Fusion] StartGame failed: {failReason}");
            CleanupRunnerComponents();
            onComplete?.Invoke(false);
            onFailReason?.Invoke(failReason);
        }
    }


    public void Disconnect()
    {
        StartCoroutine(ResetRunnerCoroutine());
    }

    // บังคับ Photon FixedRegion ในโค้ดก่อน StartGame ทุกครั้ง — ไม่พึ่งค่าใน asset อย่างเดียว
    // (Editor ที่เปิดค้างตอน git pull อาจยังถือ PhotonAppSettings เก่าที่ไม่มี region → ต่อ best-region แทน → คนละ region กับมือถือ)
    private void ApplyFixedPhotonRegion()
    {
        if (!Fusion.Photon.Realtime.PhotonAppSettings.TryGetGlobal(out var photonSettings) || photonSettings.AppSettings == null)
        {
            Debug.LogWarning("[Fusion] Cannot access PhotonAppSettings.Global to force region.");
            return;
        }

        // เปิด Photon log ละเอียด → เห็น region ที่ต่อจริง + return code ของ JoinRoom (เปิดเฉพาะตอนดีบัก)
        if (verbosePhotonLog)
        {
            photonSettings.AppSettings.NetworkLogging = ExitGames.Client.Photon.DebugLevel.INFO;
        }

        if (string.IsNullOrWhiteSpace(fixedPhotonRegion))
        {
            return; // เว้นว่าง = ใช้ best region ตาม ping
        }

        string target = fixedPhotonRegion.Trim();
        if (!string.Equals(photonSettings.AppSettings.FixedRegion, target, StringComparison.OrdinalIgnoreCase))
        {
            GameLog.Log($"[Fusion] Forcing Photon FixedRegion '{photonSettings.AppSettings.FixedRegion}' -> '{target}'");
            photonSettings.AppSettings.FixedRegion = target;
        }
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        GameLog.Log($"[Fusion] Player joined: {player}");

        RegisterPlayerName(runner.LocalPlayer.PlayerId, GetLocalPlayerName(runner.LocalPlayer.PlayerId));

        if (IsLocalAuthority(runner) && player != runner.LocalPlayer)
        {
            SendKnownPlayerNamesToPlayer(player); // รวม characterIndex แล้ว (ใน SendKnownPlayerNamesToPlayer)
        }

        if (player == runner.LocalPlayer && LobbyUI.Instance != null)
        {
            LobbyUI.Instance.SetViewState(true);
        }

        if (player == runner.LocalPlayer && !IsLocalAuthority(runner))
        {
            SendLocalPlayerNameToServer();
            // [NEW] ส่ง characterIndex ของตัวเองไปหา Host เพื่อ sync รูป avatar
            int myCharIndex = UnityEngine.PlayerPrefs.GetInt("SelectedCharacter", 0);
            SendLocalCharacterToServer(myCharIndex);
        }

        if (IsLocalAuthority(runner) && player == runner.LocalPlayer)
        {
            // [NEW] Authority ส่ง characterIndex ของตัวเองให้ทุกคนที่อยู่ในห้องแล้ว
            int myCharIndex = UnityEngine.PlayerPrefs.GetInt("SelectedCharacter", 0);
            BroadcastLocalCharacter(myCharIndex);
        }

        RefreshSeatOrder(runner); // ตรึง seat ของผู้เล่นใหม่ (ก่อน refresh UI)
        RefreshPlayerList(runner);
        NotifyActivePlayersChanged();
        // ไม่ sync player_count ขึ้น DB ทุกครั้ง — รอ snapshot ตอน LoadGameScene
        // (lobby UI อ่านจาก Fusion ตรงอยู่แล้ว, DB เก็บไว้เป็น "บันทึกแมตช์")
    }

    // [Shared Mode · Step 5/6] เพิ่ม PlayerId ใหม่เข้า seat map โดย "ไม่ลบ" ของเดิม แล้ว Sort ตาม id เสมอ
    //   → seat = อันดับ PlayerId (global) เหมือนกันทุกเครื่อง (กัน callback มาคนละจังหวะแล้ว seat ไม่ตรงกัน)
    //   → คงที่ตลอดแมตช์ แม้มีคนออกกลางเกม (id คนออกยังอยู่ใน list → คนที่เหลือไม่เลื่อน)
    private void RefreshSeatOrder(NetworkRunner runner)
    {
        if (runner == null)
        {
            return;
        }

        bool added = false;
        foreach (var p in runner.ActivePlayers)
        {
            if (!_seatOrder.Contains(p.PlayerId))
            {
                _seatOrder.Add(p.PlayerId);
                added = true;
            }
        }

        // เรียงตาม PlayerId เสมอ → seat เท่ากันทุกเครื่อง (ids เพิ่มขึ้นเรื่อยๆ → ของเดิมไม่สลับตำแหน่ง)
        if (added)
        {
            _seatOrder.Sort();
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        GameLog.Log($"[Fusion] Player left: {player}");
        _playerNames.Remove(player.PlayerId);
        _playerCharacters.Remove(player.PlayerId);
        NotifyPlayerNamesUpdated();

        // [FIX] ถ้าเกมเริ่มไปแล้วและมีคนออกกลางเกม → พาทุกคนกลับ MainMenu แทนการค้าง
        if (IsGameInProgress)
        {
            GameLog.Log("[Fusion] Player left mid-game → kicking all players back to MainMenu.");
            StartCoroutine(KickAllToMainMenuCoroutine());
            return;
        }

        RefreshPlayerList(runner);
        NotifyActivePlayersChanged();
    }

    private IEnumerator KickAllToMainMenuCoroutine()
    {
        // รอ 1 frame เพื่อให้ event อื่นๆ ทำงานเสร็จก่อน
        yield return null;
        IsGameInProgress = false;
        PlayerPrefs.DeleteKey("GameMode");
        PlayerPrefs.DeleteKey("MatchmakingRoomCode");
        PlayerPrefs.Save();

        // รอให้ Runner ปิดและล้างข้อมูลเรียบร้อยก่อนย้าย Scene เพื่อป้องกัน Error (too many commands in package) จากข้อมูลที่ค้าง
        yield return ResetRunnerCoroutine();

        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu 1");
    }

    private void RefreshPlayerList(NetworkRunner runner)
    {
        string list = "Players in Room:\n";
        foreach (var p in runner.ActivePlayers)
        {
            // [FIX] ดึงชื่อจริงจาก _playerNames dictionary แทน PlayerId ตัวเลข
            string displayName;
            if (_playerNames.TryGetValue(p.PlayerId, out string realName) && !string.IsNullOrWhiteSpace(realName))
            {
                displayName = realName;
            }
            else
            {
                displayName = "Player " + p.PlayerId; // fallback ถ้ายังไม่ได้รับชื่อ
            }
            bool isLocal = (p == runner.LocalPlayer);
            list += "- " + displayName + (isLocal ? " (You)" : string.Empty) + "\n";
        }

        if (LobbyUI.Instance != null)
        {
            LobbyUI.Instance.UpdatePlayerList(list, runner.ActivePlayers.Count(), IsLocalAuthority(runner));
        }

    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        GameLog.Log($"[Fusion] Runner shutdown: {shutdownReason}");
        if (runner == _runner)
        {
            CleanupRunnerComponents();
        }
        NotifyActivePlayersChanged();
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        GameLog.Log("[Fusion] Connected to server successfully.");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        GameLog.Log($"[Fusion] Disconnected from server: {reason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        // log ระดับ connection — ถ้า fail ที่นี่ = ต่อ game server ไม่ติด (คนละเรื่องกับ join room)
        Debug.LogWarning($"[Fusion] OnConnectFailed: addr={remoteAddress}, reason={reason}");
    }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    // =============================================================================
    // OnReliableDataReceived — รับข้อมูล Network และ Route ไปยัง Event ที่ถูกต้อง
    // -----------------------------------------------------------------------
    // การ Route ข้อมูล (เช็ค payload.Split('|')[0]):
    //   NAME       → เพิ่ม/อัปเดตชื่อผู้เล่น และ broadcast ต่อ (ถ้าเป็น Host)
    //   TURN       → อัปเดต turn state + relay (ถ้าเป็น Host)
    //   QUIZSTART  → Client เริ่มควิซตามคำสั่ง
    //   QUIZREQ    → Host รับคำขอเริ่มควิซ
    //   STATEREQ   → Host รับคำขอ Full State (late-joiner)
    //   QUIZANSWER → Host รับคำตอบจาก Client
    //   QUIZRESULT → Client รับผลควิซจาก Host
    //   ECON       → อัปเดตเศรษฐกิจ + relay
    //   BOARD      → อัปเดตกระดาน + relay
    // =============================================================================
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

            if (IsLocalAuthority(runner))
            {
                BroadcastPlayerName(player, playerId, playerName);
            }

            return;
        }

        // [NEW] CHAR|playerId|characterIndex → sync avatar
        if (string.Equals(parts[0], CharacterMessageType, StringComparison.Ordinal))
        {
            if (parts.Length < 3 || !int.TryParse(parts[1], out int charPlayerId) || !int.TryParse(parts[2], out int charIndex))
            {
                return;
            }

            _playerCharacters[charPlayerId] = charIndex;
            PlayerCharacterReceived?.Invoke(charPlayerId, charIndex);

            if (IsLocalAuthority(runner))
            {
                // Broadcast ต่อให้ทุกคน
                byte[] rawData = data.ToArray();
                foreach (var activePlayer in runner.ActivePlayers)
                {
                    if (activePlayer == player) continue;
                    runner.SendReliableDataToPlayer(activePlayer, default, rawData);
                }
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

            if (IsLocalAuthority(runner))
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
            if (IsLocalAuthority(runner) || parts.Length < 2 || !int.TryParse(parts[1], out int questionIndex))
            {
                return;
            }

            _hasPendingQuizStart = true;
            _pendingQuizStartIndex = questionIndex;
            QuizStartedReceived?.Invoke(questionIndex);
            return;
        }

        if (string.Equals(parts[0], QuizRequestMessageType, StringComparison.Ordinal))
        {
            // เฉพาะ host เท่านั้นที่ตอบสนองคำขอเริ่มควิซ (client เป็นคนส่งมา)
            if (IsLocalAuthority(runner))
            {
                QuizStartRequested?.Invoke();
            }

            return;
        }

        if (string.Equals(parts[0], StateRequestMessageType, StringComparison.Ordinal))
        {
            // เฉพาะ host เท่านั้นที่ตอบสนองคำขอ full state (late-joiner เป็นคนส่งมา)
            // ส่ง playerId ของคนขอไปด้วย เพื่อให้ host ตอบกลับเฉพาะคนนั้น (ไม่รีเซ็ต timer คนอื่น)
            if (IsLocalAuthority(runner))
            {
                FullStateRequested?.Invoke(player.PlayerId);
            }

            return;
        }

        if (string.Equals(parts[0], QuizAnswerMessageType, StringComparison.Ordinal))
        {
            if (!IsLocalAuthority(runner) || parts.Length < 4 || !int.TryParse(parts[1], out int answerPlayerIndex))
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
            if (IsLocalAuthority(runner) || parts.Length < 2)
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

        if (string.Equals(parts[0], EconomyStateMessageType, StringComparison.Ordinal))
        {
            if (parts.Length < 3)
            {
                return;
            }

            EconomyStateSnapshot snapshot = DecodeEconomyState(parts[1], parts[2]);
            if (IsLocalAuthority(runner))
            {
                EconomyStateReceived?.Invoke(snapshot);

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
                EconomyStateReceived?.Invoke(snapshot);
            }

            return;
        }

        if (string.Equals(parts[0], BoardStateMessageType, StringComparison.Ordinal))
        {
            if (parts.Length < 5)
            {
                return;
            }

            BoardStateSnapshot boardSnapshot = new BoardStateSnapshot
            {
                Tier1CardIds = DecodeStringArray(parts[1]),
                Tier2CardIds = DecodeStringArray(parts[2]),
                Tier3CardIds = DecodeStringArray(parts[3]),
                UsedCardIds = DecodeStringArray(parts[4])
            };

            BoardStateReceived?.Invoke(boardSnapshot);

            if (IsLocalAuthority(runner))
            {
                foreach (var activePlayer in runner.ActivePlayers)
                {
                    if (activePlayer == player || activePlayer == runner.LocalPlayer)
                    {
                        continue;
                    }

                    runner.SendReliableDataToPlayer(activePlayer, default, data.ToArray());
                }
            }

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

        if (IsLocalAuthority(runner))
        {
            BroadcastPlayerName(player, legacyPlayerId, legacyPlayerName);
        }
    }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    private SceneRef ResolveSceneRef(string sceneName = null)
    {
        string targetScene = string.IsNullOrEmpty(sceneName) ? gameSceneName : sceneName;
        var buildIndex = FindBuildIndexByName(targetScene);
        if (buildIndex >= 0)
        {
            return SceneRef.FromIndex(buildIndex);
        }

        return SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
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

    private IEnumerator ResetRunnerCoroutine()
    {
        if (_runner != null)
        {
            var shutdownTask = _runner.Shutdown();
            
            // Poll for shutdown to complete
            float elapsed = 0f;
            while (!shutdownTask.IsCompleted && elapsed < 5f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (shutdownTask.IsFaulted)
            {
                Debug.LogWarning($"[Fusion] Runner shutdown warning: {shutdownTask.Exception?.GetBaseException().Message}");
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

    // [Shared Mode · Step 5] seat อิงจาก stable map (_seatOrder) ไม่ใช่รายชื่อ active สดๆ
    //   → seat ของ local คงที่ตลอดแมตช์ แม้ผู้เล่น id ต่ำกว่าออกไป (เดิมจะเลื่อนไปสวม seat คนที่ออก)
    public int GetLocalPlayerSeatIndex()
    {
        if (_runner == null)
        {
            return 0;
        }

        int seat = _seatOrder.IndexOf(_runner.LocalPlayer.PlayerId);
        return seat >= 0 ? seat : 0;
    }

    public string GetPlayerNameBySeat(int seatIndex)
    {
        if (seatIndex < 0 || seatIndex >= _seatOrder.Count)
        {
            return null;
        }

        // คืน null ถ้ายังไม่รู้ชื่อ (เช่น คนออกไปแล้ว) → caller จะคงชื่อเดิมบน UI ไว้ ไม่เขียนทับเป็น "Player X"
        return _playerNames.TryGetValue(_seatOrder[seatIndex], out string name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }

    // PlayerId ที่ถูก assign ให้ seat นี้ (-1 ถ้า seat เกินช่วง) — ใช้เช็คสถานะการเชื่อมต่อของ seat
    public int GetPlayerIdBySeat(int seatIndex)
    {
        return (seatIndex >= 0 && seatIndex < _seatOrder.Count) ? _seatOrder[seatIndex] : -1;
    }

    // playerId นี้ยังเชื่อมต่ออยู่ในห้องไหม
    public bool IsPlayerConnected(int playerId)
    {
        if (_runner == null || playerId < 0)
        {
            return false;
        }

        foreach (var p in _runner.ActivePlayers)
        {
            if (p.PlayerId == playerId)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetPlayerCharacterBySeat(int seatIndex, out int characterIndex)
    {
        characterIndex = 0;
        // [Shared Mode] ใช้ stable seat map (_seatOrder) แทน live ordering → avatar ไม่ map ผิด seat ตอนมีคนออก
        int playerId = GetPlayerIdBySeat(seatIndex);
        if (playerId < 0)
        {
            return false;
        }

        return TryGetPlayerCharacter(playerId, out characterIndex);
    }

    // [NEW] หา seat index (0-based) จาก playerId — ใช้ตอน sync avatar (อิง stable seat map)
    public int GetSeatIndexForPlayerId(int playerId)
    {
        return _seatOrder.IndexOf(playerId);
    }



    public void SendTurnState(int currentPlayerIndex, int currentRound, int totalTurnCount, int currentTurnDisplay)
    {
        if (_runner == null)
        {
            return;
        }

        byte[] payload = EncodeTurnStatePayload(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);
        if (IsLocalAuthority(_runner))
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

        SendToAuthority(payload);
    }

    public void SendBoardState(BoardStateSnapshot snapshot)
    {
        if (_runner == null)
        {
            return;
        }

        byte[] payload = BuildBoardPayload(snapshot);

        if (IsLocalAuthority(_runner))
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

        SendToAuthority(payload);
    }

    // client ขอให้ host เริ่มควิซ (เมื่อ client เป็นคนจบเทิร์นที่ถึงรอบควิซ)
    public void RequestQuizStart()
    {
        if (_runner == null)
        {
            return;
        }

        byte[] payload = Encoding.UTF8.GetBytes(QuizRequestMessageType);

        if (IsLocalAuthority(_runner))
        {
            // host เรียกเองได้โดยตรง ไม่ต้องส่งผ่าน network
            QuizStartRequested?.Invoke();
            return;
        }

        SendToAuthority(payload);
    }

    // client (late-joiner) ขอ full state ปัจจุบันจาก host
    public void RequestFullState()
    {
        if (_runner == null || IsLocalAuthority(_runner))
        {
            return; // host มี state ครบอยู่แล้ว ไม่ต้องขอ
        }

        byte[] payload = Encoding.UTF8.GetBytes(StateRequestMessageType);
        SendToAuthority(payload);
    }

    // host ตอบกลับ full state เฉพาะ player ที่ขอ (ส่งเจาะจง ไม่ broadcast — กันรีเซ็ต timer คนที่กำลังเล่นอยู่)
    public void SendBoardStateToPlayer(int playerId, BoardStateSnapshot snapshot)
    {
        if (_runner == null || !IsLocalAuthority(_runner) || !TryGetPlayerRef(playerId, out PlayerRef target))
        {
            return;
        }

        _runner.SendReliableDataToPlayer(target, default, BuildBoardPayload(snapshot));
    }

    public void SendEconomyStateToPlayer(int playerId, EconomyStateSnapshot snapshot)
    {
        if (_runner == null || !IsLocalAuthority(_runner) || !TryGetPlayerRef(playerId, out PlayerRef target))
        {
            return;
        }

        _runner.SendReliableDataToPlayer(target, default, BuildEconomyPayload(snapshot));
    }

    public void SendTurnStateToPlayer(int playerId, int currentPlayerIndex, int currentRound, int totalTurnCount, int currentTurnDisplay)
    {
        if (_runner == null || !IsLocalAuthority(_runner) || !TryGetPlayerRef(playerId, out PlayerRef target))
        {
            return;
        }

        _runner.SendReliableDataToPlayer(target, default,
            EncodeTurnStatePayload(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay));
    }

    private bool TryGetPlayerRef(int playerId, out PlayerRef result)
    {
        if (_runner != null)
        {
            foreach (var activePlayer in _runner.ActivePlayers)
            {
                if (activePlayer.PlayerId == playerId)
                {
                    result = activePlayer;
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    private static byte[] BuildBoardPayload(BoardStateSnapshot snapshot)
    {
        return Encoding.UTF8.GetBytes(string.Join(
            PlayerNameSeparator.ToString(),
            BoardStateMessageType,
            EncodeStringArray(snapshot.Tier1CardIds),
            EncodeStringArray(snapshot.Tier2CardIds),
            EncodeStringArray(snapshot.Tier3CardIds),
            EncodeStringArray(snapshot.UsedCardIds)));
    }

    private static byte[] BuildEconomyPayload(EconomyStateSnapshot snapshot)
    {
        string bankPayload = EncodeIntArray(snapshot.BankCoins);
        string playersPayload = EncodeEconomyPlayers(snapshot.Players);
        return Encoding.UTF8.GetBytes(
            $"{EconomyStateMessageType}{PlayerNameSeparator}{bankPayload}{PlayerNameSeparator}{playersPayload}");
    }

    public void SendQuizStart(int questionIndex)
    {
        if (_runner == null || !IsLocalAuthority(_runner))
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
        if (_runner == null || IsLocalAuthority(_runner))
        {
            return;
        }

        string correctnessFlag = isCorrect ? "1" : "0";
        string timeTakenText = timeTaken.ToString("0.000", CultureInfo.InvariantCulture);
        byte[] payload = Encoding.UTF8.GetBytes(
            $"{QuizAnswerMessageType}{PlayerNameSeparator}{playerIndex}{PlayerNameSeparator}{correctnessFlag}{PlayerNameSeparator}{timeTakenText}");
        SendToAuthority(payload);
    }

    public void SendQuizResults(IEnumerable<QuizAnswerSnapshot> answers, IEnumerable<int> rewardGemIndices)
    {
        if (_runner == null || !IsLocalAuthority(_runner))
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

    public void SendEconomyState(EconomyStateSnapshot snapshot)
    {
        if (_runner == null)
        {
            return;
        }

        byte[] payload = BuildEconomyPayload(snapshot);

        if (IsLocalAuthority(_runner))
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

        SendToAuthority(payload);
    }

    public bool TryConsumePendingQuizStart(out int questionIndex)
    {
        if (_hasPendingQuizStart)
        {
            questionIndex = _pendingQuizStartIndex;
            _hasPendingQuizStart = false;
            _pendingQuizStartIndex = -1;
            return true;
        }

        questionIndex = -1;
        return false;
    }

    private void SendLocalPlayerNameToServer()
    {
        if (_runner == null)
        {
            return;
        }

        string localName = GetLocalPlayerName(_runner.LocalPlayer.PlayerId);
        byte[] payload = EncodePlayerNamePayload(_runner.LocalPlayer.PlayerId, localName);
        SendToAuthority(payload);
    }

    // [NEW] ผู้เล่น local ส่ง characterIndex ไปหา Server/Host
    public void SendLocalCharacterToServer(int characterIndex)
    {
        if (_runner == null) return;
        int localId = _runner.LocalPlayer.PlayerId;
        _playerCharacters[localId] = characterIndex;
        byte[] payload = Encoding.UTF8.GetBytes($"{CharacterMessageType}{PlayerNameSeparator}{localId}{PlayerNameSeparator}{characterIndex}");
        SendToAuthority(payload);
    }

    // [NEW] Host ส่ง characterIndex ปัจจุบันของตัวเองไปหา client ที่เพิ่งเข้า
    public void BroadcastLocalCharacter(int characterIndex)
    {
        if (_runner == null || !IsLocalAuthority(_runner)) return;
        int localId = _runner.LocalPlayer.PlayerId;
        _playerCharacters[localId] = characterIndex;
        byte[] payload = Encoding.UTF8.GetBytes($"{CharacterMessageType}{PlayerNameSeparator}{localId}{PlayerNameSeparator}{characterIndex}");
        foreach (var p in _runner.ActivePlayers)
        {
            if (p == _runner.LocalPlayer) continue;
            _runner.SendReliableDataToPlayer(p, default, payload);
        }
    }

    // [NEW] ดึง characterIndex จาก dictionary (ใช้ใน GameController ตอนตั้งค่า remote avatar)
    public bool TryGetPlayerCharacter(int playerId, out int characterIndex)
    {
        return _playerCharacters.TryGetValue(playerId, out characterIndex);
    }

    private void SendKnownPlayerNamesToPlayer(PlayerRef targetPlayer)
    {
        if (_runner == null || !IsLocalAuthority(_runner))
        {
            return;
        }

        foreach (var pair in _playerNames)
        {
            byte[] payload = EncodePlayerNamePayload(pair.Key, pair.Value);
            _runner.SendReliableDataToPlayer(targetPlayer, default, payload);
        }

        // [NEW] ส่ง characterIndex ที่รู้จักให้คนใหม่ด้วย
        foreach (var pair in _playerCharacters)
        {
            byte[] charPayload = Encoding.UTF8.GetBytes($"{CharacterMessageType}{PlayerNameSeparator}{pair.Key}{PlayerNameSeparator}{pair.Value}");
            _runner.SendReliableDataToPlayer(targetPlayer, default, charPayload);
        }
    }

    private void BroadcastPlayerName(PlayerRef sourcePlayer, int playerId, string playerName)
    {
        if (_runner == null || !IsLocalAuthority(_runner))
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
        // [FIX] refresh lobby player list ทันทีที่ได้ชื่อใหม่ (ไม่รอให้มีคนเข้า/ออกก่อน)
        if (_runner != null && _runner.IsRunning && LobbyUI.Instance != null)
        {
            RefreshPlayerList(_runner);
        }
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

    private static string EncodeEconomyPlayers(IEnumerable<EconomyPlayerSnapshot> players)
    {
        if (players == null)
        {
            return string.Empty;
        }

        return string.Join(";", players.Select(player =>
            $"{player.Score}~{EncodeIntArray(player.Coins)}~{EncodeIntArray(player.Bonuses)}~{player.QuizBlackCoins}~{EncodeStringArray(player.ReservedCardIds)}"));
    }

    private static EconomyStateSnapshot DecodeEconomyState(string bankPayload, string playersPayload)
    {
        var snapshot = new EconomyStateSnapshot
        {
            BankCoins = DecodeIntArray(bankPayload),
            Players = System.Array.Empty<EconomyPlayerSnapshot>()
        };

        if (string.IsNullOrWhiteSpace(playersPayload))
        {
            return snapshot;
        }

        string[] playerEntries = playersPayload.Split(';');
        var players = new List<EconomyPlayerSnapshot>(playerEntries.Length);
        foreach (string playerEntry in playerEntries)
        {
            if (string.IsNullOrWhiteSpace(playerEntry))
            {
                continue;
            }

            string[] parts = playerEntry.Split('~');
            if (parts.Length < 3 || !int.TryParse(parts[0], out int score))
            {
                continue;
            }

            int quizBlackCoins = 0;
            if (parts.Length >= 4)
            {
                int.TryParse(parts[3], out quizBlackCoins);
            }

            string[] reservedCards = System.Array.Empty<string>();
            if (parts.Length >= 5)
            {
                reservedCards = DecodeStringArray(parts[4]);
            }

            players.Add(new EconomyPlayerSnapshot
            {
                Score = score,
                Coins = DecodeIntArray(parts[1]),
                Bonuses = DecodeIntArray(parts[2]),
                QuizBlackCoins = quizBlackCoins,
                ReservedCardIds = reservedCards
            });
        }

        snapshot.Players = players.ToArray();
        return snapshot;
    }

    private static string EncodeIntArray(IEnumerable<int> values)
    {
        if (values == null)
        {
            return string.Empty;
        }

        return string.Join(",", values);
    }

    private static int[] DecodeIntArray(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return System.Array.Empty<int>();
        }

        string[] parts = payload.Split(',');
        int[] values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            int.TryParse(parts[i], out values[i]);
        }

        return values;
    }

    // cardId ไม่มี ',' หรือ '|' อยู่แล้ว ใช้ '-' แทนช่องว่าง
    private const string EmptyCardSlotToken = "-";

    private static string EncodeStringArray(IEnumerable<string> values)
    {
        if (values == null)
        {
            return string.Empty;
        }

        return string.Join(",", values.Select(v => string.IsNullOrEmpty(v) ? EmptyCardSlotToken : v));
    }

    private static string[] DecodeStringArray(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return System.Array.Empty<string>();
        }

        string[] parts = payload.Split(',');
        string[] values = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            values[i] = parts[i] == EmptyCardSlotToken ? string.Empty : parts[i];
        }

        return values;
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
