using System;                                       
using System.Collections.Generic;                       
using System.Collections.Frozen;
using System.IO;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration; 
using CounterStrikeSharp.API.Modules.Events;


namespace MatchZy
{
    [MinimumApiVersion(227)]
    public partial class MatchZy : BasePlugin
    {
        
        public override string ModuleName => "MatchZy";

        public override string ModuleVersion => "0.8.15";

        public override string ModuleAuthor => "WD- (https://github.com/shobhit-pathak/)";

        public override string ModuleDescription => "A plugin for running and managing CS2 practice/pugs/scrims/matches!";
        public CounterStrikeSharp.API.Modules.Timers.Timer? matchStartCountdownTimer = null;
        public int countdownRemaining = 7;

        public string chatPrefix = $"[{ChatColors.Green}MatchZy{ChatColors.Default}]";
        public string adminChatPrefix = $"[{ChatColors.Red}ADMIN{ChatColors.Default}]";

        // Plugin start phase data
        public bool isPractice = false;
        public bool isSleep = false;
        public bool readyAvailable = false;
        public bool matchStarted = false;
        public bool isWarmup = false;
        public bool isCountdownActive = false; 
        public bool isShufflePending = false; 
        public bool isAutoResetTimerActive = false;
        public bool isKnifeRound = false;
        public bool isSideSelectionPhase = false;
        public bool isMatchLive = false;
        public long liveMatchId = -1;
        public int autoStartMode = 1;
        private static readonly object _shuffleLock = new();
        public bool mapReloadRequired = false;

        // Pause Data
        public bool isPaused = false;
        // 【.NET 10 升級】：使用 Target-typed new
        public Dictionary<string, object> unpauseData = new() {
            { "ct", false },
            { "t", false },
            { "pauseTeam", "" }
        };

        bool isPauseCommandForTactical = false;

        // Knife Data
        public int knifeWinner = 0;
        public string knifeWinnerName = "";

        // Players Data (including admins)
        public int connectedPlayers = 0;
        private Dictionary<int, bool> playerReadyStatus = new();
        private Dictionary<int, CCSPlayerController> playerData = new();

        // Admin Data
        private Dictionary<string, string> loadedAdmins = new();

        // 廣告防禦黑名單 (Ad Blacklist)
        public string[] adBlacklist = [];

        // Timers
        public CounterStrikeSharp.API.Modules.Timers.Timer? unreadyPlayerMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? sideSelectionMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? pausedStateTimer = null;

        // Each message is kept in chat display for ~13 seconds, hence setting default chat timer to 13 seconds.
        // Configurable using matchzy_chat_messages_timer_delay <seconds>
        public int chatTimerDelay = 13;

        // Game Config
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2; // Number of ready players required start the match. If set to 0, all connected players have to ready-up to start the match.
        public bool isWhitelistRequired = false;
        public bool isSaveNadesAsGlobalEnabled = false;

        public bool isPlayOutEnabled = false;

        public bool playerHasTakenDamage = false;

        // 【.NET 10 升級】：轉換為 FrozenDictionary，讓指令查詢速度物理封頂
        public FrozenDictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;

        // SQLite/MySQL Database 
        private Database database = new();
    
        public override void Load(bool hotReload) {
            
            LoadAdmins();
            LoadAdBlacklist(); // 載入廣告黑名單設定檔

            database.InitializeDatabase(ModuleDirectory);

            // This sets default config ConVars
            Server.ExecuteCommand("execifexists MatchZy/config.cfg");

            if (!hotReload) {
                AutoStart();
            } else {
                // Pluign should not be reloaded while a match is live (this would messup with the match flags which were set)
                // Only hot-reload the plugin if you are testing something and don't want to restart the server time and again.
                UpdatePlayersMap();
                AutoStart();
            }

            // 【.NET 10 升級】：字典初始化後呼叫 ToFrozenDictionary 永久鎖定效能
            commandActions = new Dictionary<string, Action<CCSPlayerController?, CommandInfo?>> {
                { ".ready", OnPlayerReady },
                { ".r", OnPlayerReady },
                { ".forceready", OnForceReadyCommandCommand },
                { ".unready", OnPlayerUnReady },
                { ".notready", OnPlayerUnReady },
                { ".ur", OnPlayerUnReady },
                { ".stay", OnTeamStay },
                { ".switch", OnTeamSwitch },
                { ".swap", OnTeamSwitch },
                
                // ▼▼▼ 雙軌暫停系統專屬指令註冊 ▼▼▼
                { ".tech", (player, command) => TechPause(player, command) },
                { ".p", (player, command) => TacPause(player, command) },
                { ".pause", (player, command) => TacPause(player, command) },
                
                // 註冊新的專屬解除指令
                { ".unt", (player, command) => { if (player != null) HandleUntCommand(player); } },
                { ".unp", (player, command) => { if (player != null) HandleUnpCommand(player); } },
                
                // 完美封殺舊指令！只要打 .up 或 .unpause，直接跳出中文防呆提示，不觸發任何暫停解除
                { ".up", (player, command) => { if (player != null) player.PrintToChat($"{chatPrefix} {ChatColors.Default}請確認目前暫停類型，並輸入 {ChatColors.Green}.unt{ChatColors.Default} (技術) 或 {ChatColors.Green}.unp{ChatColors.Default} (戰術) 來解除！"); } },
                { ".unpause", (player, command) => { if (player != null) player.PrintToChat($"{chatPrefix} {ChatColors.Default}請確認目前暫停類型，並輸入 {ChatColors.Green}.unt{ChatColors.Default} (技術) 或 {ChatColors.Green}.unp{ChatColors.Default} (戰術) 來解除！"); } },
                // ▲▲▲ 雙軌暫停系統註冊結束 ▲▲▲

                { ".forcepause", OnForcePauseCommand },
                { ".fp", OnForcePauseCommand },
                { ".forceunpause", OnForceUnpauseCommand },
                { ".fup", OnForceUnpauseCommand },
                { ".tac", OnTacCommand },
                { ".roundknife", OnKnifeCommand },
                { ".rk", OnKnifeCommand },
                { ".playout", OnPlayoutCommand },
                { ".start", OnStartCommand },
                { ".force", OnStartCommand },
                { ".forcestart", OnStartCommand },
                { ".skipveto", OnSkipVetoCommand },
                { ".sv", OnSkipVetoCommand },
                { ".restart", OnRestartMatchCommand },
                { ".rr", OnRestartMatchCommand },
                { ".endmatch", OnEndMatchCommand },
                { ".forceend", OnEndMatchCommand },
                { ".reloadmap", OnMapReloadCommand },
                { ".settings", OnMatchSettingsCommand },
                { ".whitelist", OnWLCommand },
                { ".globalnades", OnSaveNadesAsGlobalCommand },
                { ".reload_admins", OnReloadAdmins },
                { ".tactics", OnPracCommand },
                { ".prac", OnPracCommand },
                { ".showspawns", OnShowSpawnsCommand },
                { ".hidespawns", OnHideSpawnsCommand },
                { ".dryrun", OnDryRunCommand },
                { ".dry", OnDryRunCommand },
                { ".noflash", OnNoFlashCommand },
                { ".noblind", OnNoFlashCommand },
                { ".break", OnBreakCommand },
                { ".bot", OnBotCommand },
                { ".cbot", OnCrouchBotCommand },
                { ".crouchbot", OnCrouchBotCommand },
                { ".boost", OnBoostBotCommand },
                { ".crouchboost", OnCrouchBoostBotCommand },
                { ".nobots", OnNoBotsCommand },
                { ".solid", OnSolidCommand },
                { ".impacts", OnImpactsCommand },
                { ".traj", OnTrajCommand },
                { ".pip", OnTrajCommand },
                { ".god", OnGodCommand },
                { ".ff", OnFastForwardCommand },
                { ".fastforward", OnFastForwardCommand },
                { ".clear", OnClearCommand },
                { ".match", OnMatchCommand },
                { ".uncoach", OnUnCoachCommand },
                { ".exitprac", OnMatchCommand },
                { ".stop", OnStopCommand },
                { ".help", OnHelpCommand },
                { ".t", OnTCommand },
                { ".ct", OnCTCommand },
                { ".spec", OnSpecCommand },
                { ".fas", OnFASCommand },
                { ".watchme", OnFASCommand },
                { ".last", OnLastCommand },
                { ".throw", OnRethrowCommand },
                { ".rethrow", OnRethrowCommand },
                { ".rt", OnRethrowCommand },
                { ".throwsmoke", OnRethrowSmokeCommand },
                { ".rethrowsmoke", OnRethrowSmokeCommand },
                { ".thrownade", OnRethrowGrenadeCommand },
                { ".rethrownade", OnRethrowGrenadeCommand },
                { ".rethrowgrenade", OnRethrowGrenadeCommand },
                { ".throwgrenade", OnRethrowGrenadeCommand },
                { ".rethrowflash", OnRethrowFlashCommand },
                { ".throwflash", OnRethrowFlashCommand },
                { ".rethrowdecoy", OnRethrowDecoyCommand },
                { ".throwdecoy", OnRethrowDecoyCommand },
                { ".throwmolotov", OnRethrowMolotovCommand },
                { ".rethrowmolotov", OnRethrowMolotovCommand },
                { ".timer", OnTimerCommand },
                { ".lastindex", OnLastIndexCommand },
                { ".bestspawn", OnBestSpawnCommand },
                { ".worstspawn", OnWorstSpawnCommand },
                { ".bestctspawn", OnBestCTSpawnCommand },
                { ".worstctspawn", OnWorstCTSpawnCommand },
                { ".besttspawn", OnBestTSpawnCommand },
                { ".worsttspawn", OnWorstTSpawnCommand },
                { ".savepos", OnSavePosCommand},
                { ".shuffle", OnShuffleCommand },
                { ".unshuffle", OnUnshuffleCommand },
                { ".loadpos", OnLoadPosCommand},
                { ".hp", OnHpCommand }
            }.ToFrozenDictionary();

            // 1. 強力白名單修正：直接檢查 whitelist.cfg 檔案
            RegisterEventHandler<EventPlayerConnectFull>((@event, info) => {
                var player = @event.Userid;

                // ▼▼▼ 新增：廣告防禦門神 (進場名稱秒踢與永久封鎖) ▼▼▼
                if (player is { IsValid: true, IsBot: false } && !string.IsNullOrEmpty(player.PlayerName) && adBlacklist.Length > 0)
                {
                    bool isAdName = false;
                    foreach (var ad in adBlacklist)
                    {
                        if (player.PlayerName.Contains(ad, StringComparison.OrdinalIgnoreCase))
                        {
                            isAdName = true;
                            break;
                        }
                    }

                    if (isAdName)
                    {
                        Log($"[廣告防禦] 偵測到違規名稱，進場秒 Ban: {player.PlayerName} (SteamID: {player.SteamID})");
                        Server.ExecuteCommand($"css_ban #{player.UserId} 0 \"廣告機器人封鎖\"");
                        Server.ExecuteCommand($"kickid {player.UserId} \"Ban_Ads\""); // 雙重保險：瞬間斷開連線
                        return HookResult.Continue;
                    }
                }
                // ▲▲▲ 新增結束 ▲▲▲

                // 只有開啟 .whitelist 指令時才檢查
                // 【.NET 10 升級】：現代化模式匹配
                if (isWhitelistRequired && player is { IsValid: true, IsBot: false }) {
                    
                    // 管理員豁免
                    if (IsPlayerAdmin(player, "css_whitelist", "@css/chat")) {
                        return HookResult.Continue;
                    }

                    // 直接檢查 cfg 檔案內容
                    string wlPath = Path.Join(Server.GameDirectory + "/csgo/cfg/MatchZy/whitelist.cfg");
                    bool isAllowed = false;

                    if (File.Exists(wlPath)) {
                        var lines = File.ReadAllLines(wlPath);
                        string playerSid = player.SteamID.ToString();
                        
                        // 【優化替換 1】：移除 .Any()，改用高效能 foreach + Span 零分配比對
                        foreach (var line in lines)
                        {
                            if (line.AsSpan().Trim().SequenceEqual(playerSid))
                            {
                                isAllowed = true;
                                break;
                            }
                        }
                    }

                    if (!isAllowed) {
                        // 延遲踢除，確保訊息發送
                        AddTimer(1.5f, () => {
                            if (player is { IsValid: true }) {
                                Server.ExecuteCommand($"kickid {player.UserId} \"伺 服 器 白 名 單 已 開 啟，您 不 在 白 名 單 中。\"");
                                Log($"[WHITELIST] 已踢出未授權玩家: {player.PlayerName}");
                            }
                        });
                    }
                }
                // 呼叫原生處理程序
                return EventPlayerConnectFullHandler(@event, info);
            });
            
           // 1. 斷線事件處理：整合「倒數中止」與「刀場斷線自動移除名單」
            RegisterEventHandler<EventPlayerDisconnect>((@event, info) => {
                var player = @event.Userid;
                
                //  1：排除空指標、無效實體與機器人 (Bot 離開不影響比賽，直接跳出)
                if (player is not { IsValid: true, IsBot: false }) return HookResult.Continue;
                
                int userId = (int)(player.UserId ?? -1);
                byte teamNum = player.TeamNum; 

                // --- A. 倒數期間斷線：中止倒數 ---
                //  2：只有 CT(3) 或 T(2) 的「正式玩家」斷線，才需要中止倒數！觀戰者離開不干擾開賽。
                if (matchStartCountdownTimer != null && (teamNum == 2 || teamNum == 3))
                {
                    // 安全獲取名字
                    string playerName = string.IsNullOrEmpty(player.PlayerName) ? "未知玩家" : player.PlayerName;
                    string disconnectMsg = $"{chatPrefix} {ChatColors.White}玩 家 {ChatColors.Green}{playerName} {ChatColors.White}斷 開 連 線 請 重 新 輸 入 {ChatColors.LightRed}.R {ChatColors.White}準 備";

                    // 立即停止計時器並關閉所有外掛倒數狀態
                    matchStartCountdownTimer.Kill();
                    matchStartCountdownTimer = null;
                    isCountdownActive = false;
                    matchStarted = false;

                    // 物理重置我們自訂的字典與洗牌預約
                    playerReadyStatus.Clear(); 
                    isShufflePending = false; 
                    OnRestartMatchCommand(null, null); 

                    // 發送訊息到聊天框
                    Server.PrintToChatAll(disconnectMsg); 
                }

                // --- B. 關鍵補強：刀場/選邊期間斷線，靜默移除名單以防止邏輯鎖死 ---
                if (!isWarmup && !matchStarted && !isPractice)
                {
                    if (userId != -1 && playerReadyStatus.ContainsKey(userId)) 
                    {
                        // 僅進行數值移除，不發送任何訊息或 Log
                        playerReadyStatus.Remove(userId);
                    }

                    // 更新地圖玩家緩存，確保剩下的玩家指令（如 .stay / .switch）能被正確計算
                    UpdatePlayersMap();
                }

                // 呼叫原本可能定義在其他檔案的處理程序
                return EventPlayerDisconnectHandler(@event, info);
            });
            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerGivenC4>(EventPlayerGivenC4);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);

// 2. 鐵腕版：倒數期間絕對禁止換隊與觀戰
AddCommandListener("jointeam", (player, info) =>
{
    // 【.NET 10 升級】：模式匹配
    if (player is not { IsValid: true } || player.IsBot || isSleep) return HookResult.Continue;

    string targetTeam = info.ArgByIndex(1); 
    int userId = (int)(player.UserId ?? -1);

    // --- 核心邏輯：只要正在倒數中，管你在不在熱身，通通不准換隊 ---
    if (matchStartCountdownTimer != null || isCountdownActive)
    {
        player.PrintToChat($"{chatPrefix} 倒 數 期 間 禁 止 切 換 隊 伍 或 觀 戰");
        return HookResult.Stop; 
    }

    // 刀局結束後的「等待選邊期間」，絕對禁止換隊與觀戰！
    if (isSideSelectionPhase)
    {
        player.PrintToChat($"{chatPrefix} 選 邊 期 間，禁 止 切 換 隊 伍 或 觀 戰");
        return HookResult.Stop;
    }

    // 1. 如果是熱身階段（且沒在倒數，也不是在選邊），允許自由換隊、自由去觀戰
    if (isWarmup) return HookResult.Continue;

    // 2. 比賽正式開始後（包含刀局與正賽）
    if (matchStarted)
    {
        // 【關鍵差別點一：刀局期間全面封鎖】
        if (isKnifeRound) 
        {
            byte currentTeam = player.TeamNum;
            if ((currentTeam == 2 || currentTeam == 3) && (targetTeam == "0" || targetTeam == "1" || targetTeam == "2" || targetTeam == "3"))
            {
                player.PrintToChat($"{chatPrefix} 刀 局 期 間，禁 止 互 換 隊 伍");
                return HookResult.Stop; 
            }
        }

        // 【關鍵差別點二：LIVE正賽期間（非刀局），保護補位機制，但鎖死場上選手】
        byte playerTeam = player.TeamNum;

        // 情況 A：如果你目前是「觀 spectator (1)」或「剛連線未分配 (0)」
        // 允許你自由選擇隊伍 (包含點選自動選擇 0、加入 T 2、加入 CT 3) 來補位！
        if (playerTeam == 0 || playerTeam == 1)
        {
            return HookResult.Continue; 
        }

        // 情況 B：如果你目前已經是場上的「T (2)」或「CT (3)」選手
        // 絕對禁止你使用任何指令 (包含 0 自動、1 觀戰、2 T、3 CT) 逃跑或換隊！
        if (playerTeam == 2 || playerTeam == 3)
        {
            player.PrintToChat($"{chatPrefix} 比 賽 已 開 始，禁 止 切 換 隊 伍");
            return HookResult.Stop; 
        }
    }

    return HookResult.Continue;
});
            // --- 修正版：攔截倒數期間的所有隊伍變動廣播 ---
            RegisterEventHandler<EventPlayerTeam>((@event, info) =>
            {
                // 只要計時器正在跑，或者開關是開啟的，就絕對靜音
                if (matchStartCountdownTimer != null || isCountdownActive)
                {
                    @event.Silent = true; 
                    return HookResult.Changed;
                }
                return HookResult.Continue;
            }, HookMode.Pre);
            
            // 徹底禁用 ESC 投票系統
            AddCommandListener("callvote", (player, info) =>
            {
                // 完美防禦：正賽與刀局也全面禁用 ESC 換圖
                if (player != null && (isMatchSetup || isCountdownActive || isSideSelectionPhase || isKnifeRound || matchStarted)) 
                {
                    player.PrintToChat($"{chatPrefix} 比 賽 進 行 中 ，內 建 投 票 功 能 已 被 禁 用");
                    return HookResult.Stop; 
                }
                return HookResult.Continue; 
            });
            AddCommandListener("noclip", OnConsoleNoClip);

            // 徹底封死控制台發起的跨外掛投票指令 (加入 css_slayer_vote_internal 防護)
            AddCommandListener("css_rtv", BlockVoteInCriticalPhases);
            AddCommandListener("css_vshuffle", BlockVoteInCriticalPhases);
            AddCommandListener("css_vunshuffle", BlockVoteInCriticalPhases);
            AddCommandListener("css_slayer_vote_internal", BlockVoteInCriticalPhases);
            // 這邊結束 

            RegisterEventHandler<EventRoundEnd>((@event, info) =>
            {
                // 原有的 RoundEnd 邏輯...
                if (!isKnifeRound) return HookResult.Continue;

                DetermineKnifeWinner();
                @event.Winner = knifeWinner;
                int finalEvent = 10;
                if (knifeWinner == 3) {
                    finalEvent = 8;
                } else if (knifeWinner == 2) {
                    finalEvent = 9;
                }
                @event.Reason = finalEvent;
                isSideSelectionPhase = true;
                isKnifeRound = false;
                StartAfterKnifeWarmup();

                return HookResult.Changed;
            }, HookMode.Pre);

           RegisterEventHandler<EventRoundEnd>((@event, info) => {
                try 
                {
                    if (isDryRun)
                    {
                        StartPracticeMode();
                        isDryRun = false;
                        return HookResult.Continue;
                    }
                    if (!isMatchLive) return HookResult.Continue;
                    HandlePostRoundEndEvent(@event);
                    return HookResult.Continue;
                }
                catch (Exception e)
                {
                    Log($"[EventRoundEnd FATAL] An error occurred: {e.Message}");
                    return HookResult.Continue;
                }

            }, HookMode.Post);


RegisterListener<Listeners.OnMapStart>(mapName => {
    AddTimer(1.0f, () => {
        // 核心修正：清理緩存，但不手動指定 CT/T
        ResetTeamDataCaches(); 

        if (!isMatchSetup) {
            // 一般路人局：自動啟動
            AutoStart();
        } else {
            // 正式比賽 (JSON)：僅刷新隊名
            SetTeamNames(); 
        }
    });
});


            RegisterEventHandler<EventPlayerDeath>((@event, info) => {
                var victim = @event.Userid;
                var attacker = @event.Attacker;

                // 1. 記錄是誰殺了這名玩家 (寫入死亡筆記本)
                if (IsPlayerValid(victim) && IsPlayerValid(attacker))
                {
                    int victimId = (int)victim!.UserId!;
                    int attackerId = (int)attacker!.UserId!;
                    
                    // 排除自殺，記錄擊殺者 ID
                    if (victimId != attackerId)
                    {
                        playerKillers[victimId] = attackerId; 
                    }
                }

                // 2. 保留原本熱身階段發錢的邏輯
                if (!isWarmup) return HookResult.Continue;
                if (!IsPlayerValid(victim)) return HookResult.Continue;
                if (victim!.InGameMoneyServices != null) victim.InGameMoneyServices.Account = 16000;
                return HookResult.Continue;
            });

            AddCommandListener("noclip", OnConsoleNoClip);

            
            RegisterEventHandler<EventPlayerHurt>((@event, info) =>
            {
                CCSPlayerController? attacker = @event.Attacker;
                CCSPlayerController? victim = @event.Userid;

                if (!IsPlayerValid(attacker) || !IsPlayerValid(victim)) return HookResult.Continue;

                if (isPractice && victim!.IsBot)
                {
                    int damage = @event.DmgHealth;
                    int postDamageHealth = @event.Health;
                    PrintToPlayerChat(attacker!, Localizer["matchzy.pracc.damage", damage, victim.PlayerName, postDamageHealth]);
                    return HookResult.Continue;
                }

                if (!attacker!.IsValid || attacker.IsBot && !(@event.DmgHealth > 0 || @event.DmgArmor > 0))
                    return HookResult.Continue;
                if (matchStarted && victim!.TeamNum != attacker.TeamNum) 
                {
                    int targetId = (int)victim.UserId!;
                    UpdatePlayerDamageInfo(@event, targetId);
                    if (attacker != victim) playerHasTakenDamage = true;
                }

                return HookResult.Continue;
            });

           RegisterEventHandler<EventPlayerChat>((@event, info) => {

    // --- [第一步修正] 頂端攔截邏輯：隱藏開賽指令與倒數期間雜訊 ---
    // 【.NET 10 升級】：改用 ReadOnlySpan 進行零垃圾 (0 GC) 切片與比對，保留原本邏輯
    ReadOnlySpan<char> originalMessageSpan = @event.Text.AsSpan().Trim();
    var message = originalMessageSpan.ToString().ToLower();
    int currentEventUserId = @event.Userid; 

    // ▼▼▼ 新增：廣告防禦門神 (文字黑洞吞噬與永久封鎖) ▼▼▼
    if (adBlacklist.Length > 0)
    {
        bool isSpam = false;
        foreach (var ad in adBlacklist)
        {
            if (message.Contains(ad, StringComparison.OrdinalIgnoreCase))
            {
                isSpam = true;
                break;
            }
        }

        if (isSpam)
        {
            Log($"[廣告防禦] 攔截到洗頻訊息並直接吞掉: {@event.Text}");
            var badPlayer = Utilities.GetPlayerFromUserid(currentEventUserId);
            if (badPlayer is { IsValid: true }) {
                Server.ExecuteCommand($"css_ban #{player.UserId} 0 \"廣告機器人封鎖\"");
                Server.ExecuteCommand($"kickid {badPlayer.UserId} \"Ban_Ads\""); // 雙重保險：瞬間斷開連線
            }
            return HookResult.Handled; 
        }
    }
    // ▲▲▲ 新增結束 ▲▲▲

    // =========================================================================
    // 【跨外掛防禦網】：針對 SLAYER_PanoramaVote 的 RTV 與 Vote 指令攔截
    //  =========================================================================
    if (message.StartsWith(".rtv") || message.StartsWith(".vote")) {
        
        // 完美防禦：加入 isKnifeRound 與 matchStarted，徹底禁止正賽呼叫投票面板！
        if (isMatchSetup || isCountdownActive || isSideSelectionPhase || isKnifeRound || matchStarted) {
            
            var chatPlayer = Utilities.GetPlayerFromUserid(currentEventUserId);
            if (chatPlayer != null && chatPlayer.IsValid) {
                if (isMatchSetup) {
                    chatPlayer.PrintToChat($"{chatPrefix} 正 式 比 賽 (BO1/BO3) 期 間，禁 止 發 起 任 何 投 票");
                    chatPlayer.PrintToCenter("正 式 比 賽 期 間 ， 禁 止 發 起 投 票");
                } else {
                    chatPlayer.PrintToChat($"{chatPrefix} 比 賽 進 行 中 ，禁 止 發 起 任 何 投 票");
                    chatPlayer.PrintToCenter("比 賽 進 行 中 ， 禁 止 發 起 投 票");
                }
            }
            // 回傳 Handled 直接把這句話吃掉，投票外掛根本收不到這個指令
            return HookResult.Handled; 
        }
    }

   // =========================================================================
    // 分流：防盲目觸發、精準驗證第 10 票、手動寫入紀錄
    // =========================================================================
    if (message == ".r" || message == ".ready") {
        
        //  倒數中「或者卡在選邊時」，絕對禁止任何準備或洗牌邏輯！
        if (isCountdownActive || isSideSelectionPhase) {
            return HookResult.Handled; // 直接吃掉指令，不給任何反應
        }

        if (!matchStarted && readyAvailable) {
            
            // 取出發言的玩家
            var chatPlayer = Utilities.GetPlayerFromUserid(currentEventUserId);
            if (chatPlayer != null && chatPlayer.IsValid) {
                
                int uid = (int)(chatPlayer.UserId ?? -1);

                // 2. 身分驗證：檢查他是不是「已經準備過」了？
                bool isAlreadyReady = playerReadyStatus.ContainsKey(uid) && playerReadyStatus[uid] == true;

                // 3. 只有「還沒準備的玩家」投下的票才算數！(防亂刷 .r)
                if (!isAlreadyReady) {
                    
                    int currentReadyCount = GetReadyPlayersCount();

                    // 4. 檢查：加上他這神聖的一票後，是不是剛好滿門檻？
                    if ((currentReadyCount + 1) >= minimumReadyRequired) {
                        
                        if (isShufflePending) {
                            // 決定性的最後一票！
                            
                            // 5. 手動幫他把紀錄寫進去！
                            playerReadyStatus[uid] = true;

                            // 啟動洗牌與秒開
                            ExecuteShuffleLogic();     
                            
                            // 吃掉指令，不讓它往下走去干擾原生系統
                            return HookResult.Handled; 
                        }
                    }
                }
            }
        }
    }

    // 2. 如果倒數已經在跑，擋掉所有一般發話（維持你原本完美的發話管理）
    if (isCountdownActive && !originalMessageSpan.ToString().Contains("倒數：")) {
        return HookResult.Handled;
    }
    // --- [第一步結束] ---

    int currentVersion = Api.GetVersion();
    int index = currentEventUserId + 1; // 這裡也同步改用安全變數
    var playerUserId = NativeAPI.GetUseridFromIndex(index);

    // 【優化替換 2】：移除 .Split 與 string.Join，改用內建 Span 高效能切片完美無損分割
    int spaceIndex = originalMessageSpan.IndexOf(' ');
    string messageCommand;
    string messageCommandArg;

    if (spaceIndex == -1) {
        messageCommand = originalMessageSpan.ToString();
        messageCommandArg = string.Empty;
    } else {
        messageCommand = originalMessageSpan[..spaceIndex].ToString();
        messageCommandArg = originalMessageSpan[(spaceIndex + 1)..].ToString();
    }

    CCSPlayerController? player = null;
    if (playerData.TryGetValue(playerUserId, out CCSPlayerController? value)) {
        player = value;
    }

    if (player == null) {
        UpdatePlayersMap();
        player = playerData[playerUserId];
    }

    // Handling player commands
    if (commandActions != null && commandActions.TryGetValue(message, out var action)) {
        action(player, null);
    }

    if (message.StartsWith(".map"))
    {
        // 把 isSideSelectionPhase 一併加入鎖定條件
        if (isMatchSetup || isSideSelectionPhase)
        {
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Orange}正 式 比 賽 或 選 邊 期 間{ChatColors.Default}，禁止更換！");
            return HookResult.Continue;
        }
        HandleMapChangeCommand(player, messageCommandArg);
    }

    if (message.StartsWith(".restore"))
    {
        HandleRestoreCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".asay"))
    {
        if (IsPlayerAdmin(player, "css_asay", "@css/chat"))
        {
            if (messageCommandArg != "")
            {
                Server.PrintToChatAll($"{adminChatPrefix} {messageCommandArg}");
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", ".asay <message>"]);
            }
        }
        else
        {
            SendPlayerNotAdminMessage(player);
        }
    }
    if (message.StartsWith(".savenade") || message.StartsWith(".sn"))
    {
        HandleSaveNadeCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".delnade") || message.StartsWith(".dn"))
    {
        HandleDeleteNadeCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".deletenade"))
    {
        HandleDeleteNadeCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".importnade") || message.StartsWith(".in"))
    {
        HandleImportNadeCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".listnades") || message.StartsWith(".lin"))
    {
        HandleListNadesCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".loadnade") || message.StartsWith(".ln"))
    {
        HandleLoadNadeCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".spawn"))
    {
        HandleSpawnCommand(player, messageCommandArg, player.TeamNum, "spawn");
    }
    if (message.StartsWith(".ctspawn") || message.StartsWith(".cts"))
    {
        HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
    }
    if (message.StartsWith(".tspawn") || message.StartsWith(".ts"))
    {
        HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.Terrorist, "tspawn");
    }
    if (message.StartsWith(".team1"))
    {
        HandleTeamNameChangeCommand(player, messageCommandArg, 1);
    }
    if (message.StartsWith(".team2"))
    {
        HandleTeamNameChangeCommand(player, messageCommandArg, 2);
    }
    if (message.StartsWith(".rcon"))
    {
        if (IsPlayerAdmin(player, "css_rcon", "@css/rcon"))
        {
            Server.ExecuteCommand(messageCommandArg);
            ReplyToUserCommand(player, "Command sent successfully!");
        }
        else
        {
            SendPlayerNotAdminMessage(player);
        }
    }
    if (message.StartsWith(".coach"))
    {
        HandleCoachCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".ban"))
    {
        HandeMapBanCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".pick"))
    {
        HandeMapPickCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".back"))
    {
        HandleBackCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".delay"))
    {
        HandleDelayCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".throwindex"))
    {
        HandleThrowIndexCommand(player, messageCommandArg);
    }
    if (message.StartsWith(".throwidx"))
    {
        HandleThrowIndexCommand(player, messageCommandArg);
    }

    return HookResult.Continue;
});
            RegisterEventHandler<EventPlayerBlind>((@event, info) =>
            {
                CCSPlayerController? player = @event.Userid;
                CCSPlayerController? attacker = @event.Attacker;
                if (!isPractice) return HookResult.Continue;

                if (!IsPlayerValid(player) || !IsPlayerValid(attacker)) return HookResult.Continue;

                if (attacker!.IsValid)
                {
                    double roundedBlindDuration = Math.Round(@event.BlindDuration, 2);
                    PrintToPlayerChat(attacker, Localizer["matchzy.pracc.blind", player!.PlayerName, roundedBlindDuration]);
                }
                var userId = player!.UserId;
                if (userId != null && noFlashList.Contains((int)userId))
                {
                    Server.NextFrame(() => KillFlashEffect(player));
                }

                return HookResult.Continue;
            });

            RegisterEventHandler<EventSmokegrenadeDetonate>(EventSmokegrenadeDetonateHandler);
            RegisterEventHandler<EventFlashbangDetonate>(EventFlashbangDetonateHandler);
            RegisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            RegisterEventHandler<EventMolotovDetonate>(EventMolotovDetonateHandler);
            RegisterEventHandler<EventDecoyStarted>(EventDecoyDetonateHandler);
            
            // ============================
            Console.WriteLine($"[{ModuleName} {ModuleVersion} LOADED] MatchZy by WD- (https://github.com/shobhit-pathak/)");
        } // 結束 Load 函數

        // ==========================================
        // --- 指令函數與核心修正代碼 ---
        // ==========================================

// --- 核心修正：重新定義人數統計邏輯，完全排除觀戰者與離線玩家 ---
        public int GetReadyPlayersCount()
        {
            int count = 0;
            // 【.NET 10 升級】：字典解構，0二次查詢成本
            foreach (var (key, value) in playerReadyStatus)
            {
                if (value == true)
                {
                    var player = Utilities.GetPlayerFromUserid(key);
                    // 超強防護網：必須「IsValid 且在線 (PlayerConnected) 且在 T/CT 隊上」才算人數
                    if (player is { IsValid: true, Connected: PlayerConnectedState.Connected } && 
                        (player.TeamNum == 2 || player.TeamNum == 3))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        // 專門用來擋控制台跨外掛投票的共用函數
        private HookResult BlockVoteInCriticalPhases(CCSPlayerController? player, CommandInfo info)
        {
            CCSPlayerController? targetPlayer = player;
            
            // 如果是伺服器代理轉發的指令 (player 會是 null)，我們從第一個隱藏參數把玩家 Slot 抓出來
            if (targetPlayer == null && int.TryParse(info.GetArg(1), out int slot))
            {
                targetPlayer = Utilities.GetPlayerFromSlot(slot);
            }

            if (targetPlayer is not { IsValid: true }) return HookResult.Continue;

            // 1. 【新增防護】：如果是 BO1/BO3 正式比賽，無論如何全面禁止投票！
            if (isMatchSetup)
            {
                targetPlayer.PrintToChat($"{chatPrefix} 正 式 比 賽 (BO1/BO3) 期 間，禁 止 發 起 任 何 投 票");
                targetPlayer.PrintToCenter("正 式 比 賽 期 間 ， 禁 止 發 起 投 票");
                return HookResult.Stop; 
            }

            // 2. 【原有防護】：一般路人局的「倒數/選邊/刀局/正賽」期間，禁止干擾
            // 完美防禦：加入刀局與正賽鎖定
            if (isCountdownActive || isSideSelectionPhase || isKnifeRound || matchStarted)
            {
                targetPlayer.PrintToChat($"{chatPrefix} 比 賽 進 行 中，禁 止 發 起 任 何 投 票");
                targetPlayer.PrintToCenter("比 賽 進 行 中 ， 禁 止 發 起 投 票");
                return HookResult.Stop; 
            }

            return HookResult.Continue;
        }

        // 這邊結束
[ConsoleCommand("css_shuffle", "預約隨機分隊")]
[CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
public void OnShuffleCommand(CCSPlayerController? player, CommandInfo? command) {
    if (player != null && !IsPlayerAdmin(player)) {
        return;
    }

    if (isMatchSetup) { 
        ReplyToUserCommand(player, "比 賽 已 開 始，無 法 隨 機 分 隊");
        return;
    }

    // 選邊期間絕對禁止洗牌，保護勝方權益！
    if (isSideSelectionPhase) {
        ReplyToUserCommand(player, "選 邊 期 間，無 法 更 改 隊 伍 設 定");
        return;
    }

    if (!isWarmup) {
        ReplyToUserCommand(player, $"比 賽 已 開 始，無 法 隨 機 分 隊");
        return;
    }

    // 【加入這一段防護】
    if (isCountdownActive || matchStartCountdownTimer != null) {
        ReplyToUserCommand(player, "正 在 倒 數 準 備 開 賽，無 法 開 啟 隨 機 分 隊");
        return;
    }

    isShufflePending = true;
    
    // 完美修正：把廣播包起來，判斷是誰下達的指令！
    if (player != null) {
        // 1. 真人管理員手動輸入 ➔ 聊天室廣播給大家聽
        Server.PrintToChatAll($"{chatPrefix} 管 理 員「 {ChatColors.Lime}已 開 啟 隨 機 隊 伍 分 配 {ChatColors.Default}」 將 自 動 洗 牌");
        
        // 2. ★ 修正：使用 PrintToCenter 來顯示畫面下方提示 ★
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is { IsValid: true, IsBot: false } && (p.TeamNum == 2 || p.TeamNum == 3))
            {
                p.PrintToCenter("已 開 啟 隨 機 隊 伍 分 配");
            }
        }
    } else {
        // 投票系統後台觸發 (player 為 null) ➔ 保持安靜，只在後台留紀錄
        Console.WriteLine("[MatchZy] 投票系統後台指令：已開啟隨機隊伍分配");
    }
}

[ConsoleCommand("css_unshuffle", "取消隨機分隊")]
[CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
public void OnUnshuffleCommand(CCSPlayerController? player, CommandInfo? command) {
    if (player != null && !IsPlayerAdmin(player)) {
        return;
    }

    // 選邊期間也絕對禁止取消洗牌！
    if (isSideSelectionPhase) {
        ReplyToUserCommand(player, "選 邊 期 間，無 法 更 改 隊 伍 設 定");
        return;
    }

    // 【加入這一段防護】
    if (isCountdownActive || matchStartCountdownTimer != null) {
        ReplyToUserCommand(player, "正 在 倒 數 準 備 開 賽，無 法 更 改 設 定");
        return;
    }

    isShufflePending = false;

    // 完美修正：把廣播包起來，判斷是誰下達的指令！
    if (player != null) {
        // 1. 聊天室廣播給大家聽
        Server.PrintToChatAll($"{chatPrefix} 管 理 員「 {ChatColors.Orange}已 取 消 隨 機 隊 伍 分 配 {ChatColors.Default}」 維 持 隊 伍 不 變");
        
        // 2. ★ 修正：使用 PrintToCenter 來顯示畫面下方提示 ★
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is { IsValid: true, IsBot: false } && (p.TeamNum == 2 || p.TeamNum == 3))
            {
                p.PrintToCenter("已 取 消 隨 機 隊 伍 分 配");
            }
        }
    } else {
        // 投票系統後台觸發 (player 為 null) ➔ 保持安靜，只在後台留紀錄
        Console.WriteLine("[MatchZy] 投票系統後台指令：已取消隨機隊伍分配");
    }
}
        // =========================================================================
        // 專門照顧 Utility.cs#L267 與其他檔案呼叫的舊名字（不帶參數版）
        // =========================================================================
        public void ExecuteShuffleLogic() 
        {
            // 直接轉發給你原版這份寫得最好、帶參數的版本，傳入 null 作為安全回退
            ExecuteShuffleLogicWithReady(null); 
        }

        // =========================================================================
        // 同步動態洗牌分隊 + 官方原生隊名穩定版 (不自訂隊名，絕不崩潰)
        // =========================================================================
       public void ExecuteShuffleLogicWithReady(CCSPlayerController? readyPlayer) 
{
    int savedUserId = (readyPlayer is { IsValid: true }) ? (int)(readyPlayer.UserId ?? -1) : -1;

    lock (_shuffleLock)
    {
        if (!isShufflePending) return;

        // 【優化替換 3】：移除 LINQ .Where().ToList()，改用集合表達式 []
        List<CCSPlayerController> activePlayers = [];
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is { IsValid: true, IsBot: false } && (p.TeamNum == 2 || p.TeamNum == 3))
            {
                activePlayers.Add(p);
            }
        }

        if (activePlayers.Count < 2) 
        {
            Log("[Shuffle] 選手人數不足，無法執行隨機分隊。");
            isShufflePending = false; 
            
            var originalPlayer = Utilities.GetPlayerFromUserid(savedUserId);
            if (originalPlayer is { IsValid: true }) OnPlayerReady(originalPlayer, null);
            return;
        }

        // Fisher-Yates 洗牌演算法
        Random rng = new();
        int n = activePlayers.Count;
        while (n > 1) 
        {
            n--;
            int k = rng.Next(n + 1);
            (activePlayers[k], activePlayers[n]) = (activePlayers[n], activePlayers[k]);
        }

        // 【型態安全修正】：設定 CsTeam 列舉 + CommitSuicide() 自殺換隊機制
        int half = activePlayers.Count / 2;
        for (int i = 0; i < activePlayers.Count; i++) 
        {
            var player = activePlayers[i];
            if (player is not { IsValid: true }) continue;

            // 1. 直接宣告為標準的 CsTeam 列舉型態，絕不使用 int 混淆
            CsTeam targetTeam = (i < half) ? CsTeam.CounterTerrorist : CsTeam.Terrorist;

            // 2. 判斷目前的隊伍是否與目標隊伍不符 (注意：比對時將 targetTeam 轉成 int 比對 TeamNum)
            if (player.TeamNum != (byte)targetTeam)
            {
                // 3. 【核心修正】改用 MatchZy 內建的 SwitchPlayerTeam
                // 強制在下一個伺服器影格瞬間刷新玩家陣營，徹底消滅 1 秒後的抓名時間差
                SwitchPlayerTeam(player, targetTeam); 
            }
        }

      // 延遲 0.2 秒：讓 CS2 底層引擎完成非同步網絡封包對齊
                AddTimer(1.0f, () => {
                    // 如果剛才有人斷線（導致準備名單被清空為0人），或者比賽已經開了，立刻退出
                    if (matchStarted || playerReadyStatus.Count == 0) return;
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Lime}隨 機 分 隊 完 成！隊 伍 已 鎖 定");
                    Log("[Shuffle] 洗牌同步完成");
                    // 在執行完所有的 ChangeTeam 指令之後
                    UpdatePlayersMap(); // 刷新 MatchZy 全域玩家隊伍分佈圖快取
                    
                    // 【核心修正點】：不要在這裡秒開，也不要把標記關掉！
                    // 直接去呼叫倒數方法，讓 StartMatchCountdown 內部的 isShufflePending 防護盾去決定秒開、不重生
                    StartMatchCountdown(); 
                });
            } //  結束 lock (_shuffleLock)
        } //  結束 ExecuteShuffleLogicWithReady 方法

        [ConsoleCommand("css_hp", "查詢對擊殺者的傷害統計")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnHpCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is { IsValid: true })
            {
                // 核心防護：如果是 BO1/BO3 正式比賽，直接安靜結束，不顯示任何訊息
                if (isMatchSetup) return;

                // 熱身階段或比賽尚未開始，也不顯示
                if (!matchStarted || isWarmup) return;

                // 呼叫我們在 DamageInfo_2.cs 寫好的單人查詢邏輯
                ShowSinglePlayerDamage(player);
            }
        }

        // =========================================================================
        // 載入廣告黑名單 (Ad Blacklist Loader)
        // =========================================================================
        private void LoadAdBlacklist()
        {
            string fileName = "MatchZy/ad_blacklist.txt";
            string filePath = Path.Join(Server.GameDirectory + "/csgo/cfg", fileName);

            if (File.Exists(filePath))
            {
                try
                {
                    var lines = File.ReadAllLines(filePath);
                    List<string> validLines = [];
                    foreach (var line in lines)
                    {
                        // 【.NET 10 升級】：Span 零分配切片與驗證
                        var trimmed = line.AsSpan().Trim();
                        if (!trimmed.IsEmpty && !trimmed.StartsWith("//"))
                        {
                            validLines.Add(trimmed.ToString());
                        }
                    }
                    // 【.NET 10 升級】：集合表達式轉換
                    adBlacklist = [.. validLines];
                    Log($"[LoadAdBlacklist] 成功載入 {adBlacklist.Length} 筆廣告黑名單。");
                }
                catch (Exception e)
                {
                    Log($"[LoadAdBlacklist FATAL] 讀取黑名單時發生錯誤: {e.Message}");
                }
            }
            else
            {
                Log("[LoadAdBlacklist] 黑名單檔案不存在，建立預設檔案。");
                try
                {
                    string? directoryPath = Path.GetDirectoryName(filePath);
                    if (directoryPath is not null && !Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                    // 預設寫入常見廣告，加上教學註解
                    File.WriteAllLines(filePath, [
                        "// 在下方加入要封鎖的廣告網址或關鍵字 (一行一個)", 
                        "// 系統會自動忽略 // 開頭的註解與空白行",
                        "cs2commends", 
                        "cs2commends.com"
                    ]);
                    adBlacklist = ["cs2commends", "cs2commends.com"];
                }
                catch (Exception e)
                {
                    Log($"[LoadAdBlacklist FATAL] 建立黑名單檔案時發生錯誤: {e.Message}");
                }
            }
        }

    } // 結束 class MatchZy
} //  結束 namespace MatchZy
