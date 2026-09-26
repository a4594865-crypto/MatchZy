using System;                                       
using System.Collections.Generic;                       
using System.Collections.Frozen;
using System.IO;
using System.Threading.Tasks;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration; 
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;

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
        
        // ▼▼▼ 快取常用的 ConVar 參照 ▼▼▼
        private ConVar? _cvTvEnable = null;
        private ConVar? _cvMatchRestartDelay = null;
        // ▲▲▲ ▲▲▲ ▲▲▲
        
        // 準備階段記分板標籤計時器
        public CounterStrikeSharp.API.Modules.Timers.Timer? clanTagTimer = null;
        
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

            // 【效能優化 1】：改用背景執行緒非同步初始化資料庫，完全釋放開機主執行緒
            string moduleDir = ModuleDirectory;
            _ = Task.Run(() => {
                try {
                    database.InitializeDatabase(moduleDir);
                } catch (Exception ex) {
                    Log($"[Load] Database init failed: {ex.Message}");
                }
            });

            // This sets default config ConVars
            Server.ExecuteCommand("execifexists MatchZy/config.cfg");

            // 【效能優化 2】：在開機時快取常用的 ConVar 參照
            _cvTvEnable = ConVar.Find("tv_enable");
            _cvMatchRestartDelay = ConVar.Find("mp_match_restart_delay");

            if (!hotReload) {
                AutoStart();
            } else {
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
                
                // 完美封殺舊指令！
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

                if (isWhitelistRequired && player is { IsValid: true, IsBot: false }) {
                    
                    if (IsPlayerAdmin(player, "css_whitelist", "@css/chat")) {
                        return HookResult.Continue;
                    }

                    string wlPath = Path.Join(Server.GameDirectory + "/csgo/cfg/MatchZy/whitelist.cfg");
                    bool isAllowed = false;

                    if (File.Exists(wlPath)) {
                        var lines = File.ReadAllLines(wlPath);
                        string playerSid = player.SteamID.ToString();
                        
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
                        AddTimer(1.5f, () => {
                            if (player is { IsValid: true }) {
                                Server.ExecuteCommand($"kickid {player.UserId} \"伺 服 器 白 名 單 已 開 啟，您 不 在 白 名 單 中。\"");
                                Log($"[WHITELIST] 已踢出未授權玩家: {player.PlayerName}");
                            }
                        });
                    }
                }
                return EventPlayerConnectFullHandler(@event, info);
            });
            
           // 1. 斷線事件處理
            RegisterEventHandler<EventPlayerDisconnect>((@event, info) => {
                var player = @event.Userid;
                
                if (player is not { IsValid: true, IsBot: false }) return HookResult.Continue;
                
                int userId = (int)(player.UserId ?? -1);
                byte teamNum = player.TeamNum; 

                if (matchStartCountdownTimer != null && (teamNum == 2 || teamNum == 3))
                {
                    string playerName = string.IsNullOrEmpty(player.PlayerName) ? "未知玩家" : player.PlayerName;
                    string disconnectMsg = $"{chatPrefix} {ChatColors.White}玩 家 {ChatColors.Green}{playerName} {ChatColors.White}斷 開 連 線 請 重短 新 輸 入 {ChatColors.LightRed}.R {ChatColors.White}準 備";

                    matchStartCountdownTimer.Kill();
                    matchStartCountdownTimer = null;
                    isCountdownActive = false;
                    matchStarted = false;
                    
                    // 【Clan Tag 修復】：如果倒數期間斷線，立刻重啟標籤並清空所有人為未準備
                    clanTagTimer?.Kill();
                    clanTagTimer = AddTimer(1.0f, UpdateReadyClanTags, TimerFlags.REPEAT);
                    ClearReadyClanTags();

                    playerReadyStatus.Clear(); 
                    isShufflePending = false; 
                    OnRestartMatchCommand(null, null); 

                    Server.PrintToChatAll(disconnectMsg); 
                }

                if (!isWarmup && !matchStarted && !isPractice)
                {
                    if (userId != -1 && playerReadyStatus.ContainsKey(userId)) 
                    {
                        playerReadyStatus.Remove(userId);
                    }
                    UpdatePlayersMap();
                }

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
                if (player is not { IsValid: true } || player.IsBot || isSleep) return HookResult.Continue;

                string targetTeam = info.ArgByIndex(1); 
                int userId = (int)(player.UserId ?? -1);

                if (matchStartCountdownTimer != null || isCountdownActive)
                {
                    player.PrintToChat($"{chatPrefix} 倒 數 期 間 禁 止 切 換 隊 伍 或 觀 戰");
                    return HookResult.Stop; 
                }

                if (isSideSelectionPhase)
                {
                    player.PrintToChat($"{chatPrefix} 選 邊 期 間，禁 止 切 換 隊 伍 或 觀 戰");
                    return HookResult.Stop;
                }

                if (isWarmup) return HookResult.Continue;

                if (matchStarted)
                {
                    if (isKnifeRound) 
                    {
                        byte currentTeam = player.TeamNum;
                        if ((currentTeam == 2 || currentTeam == 3) && (targetTeam == "0" || targetTeam == "1" || targetTeam == "2" || targetTeam == "3"))
                        {
                            player.PrintToChat($"{chatPrefix} 刀 局 期 間，禁 止 互 換 隊 伍");
                            return HookResult.Stop; 
                        }
                    }

                    byte playerTeam = player.TeamNum;

                    if (playerTeam == 0 || playerTeam == 1)
                    {
                        return HookResult.Continue; 
                    }

                    if (playerTeam == 2 || playerTeam == 3)
                    {
                        player.PrintToChat($"{chatPrefix} 比 賽 已 開 始，禁 止 切 換 隊 伍");
                        return HookResult.Stop; 
                    }
                }

                return HookResult.Continue;
            });
            
            RegisterEventHandler<EventPlayerTeam>((@event, info) =>
            {
                if (matchStartCountdownTimer != null || isCountdownActive)
                {
                    @event.Silent = true; 
                    return HookResult.Changed;
                }
                return HookResult.Continue;
            }, HookMode.Pre);
            
            AddCommandListener("callvote", (player, info) =>
            {
                if (player != null && (isMatchSetup || isCountdownActive || isSideSelectionPhase || isKnifeRound || matchStarted)) 
                {
                    player.PrintToChat($"{chatPrefix} 比 賽 進 行 中 ，內 建 投 票 功 能 已 被 禁 用");
                    return HookResult.Stop; 
                }
                return HookResult.Continue; 
            });
            AddCommandListener("noclip", OnConsoleNoClip);

            AddCommandListener("css_rtv", BlockVoteInCriticalPhases);
            AddCommandListener("css_vshuffle", BlockVoteInCriticalPhases);
            AddCommandListener("css_vunshuffle", BlockVoteInCriticalPhases);
            AddCommandListener("css_slayer_vote_internal", BlockVoteInCriticalPhases);

            RegisterEventHandler<EventRoundEnd>((@event, info) =>
            {
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
                    ResetTeamDataCaches(); 

                    // 【Clan Tag】：每次換地圖重置進入熱身時，啟動記分板標籤計時器
                    clanTagTimer?.Kill();
                    clanTagTimer = AddTimer(1.0f, UpdateReadyClanTags, TimerFlags.REPEAT);

                    if (!isMatchSetup) {
                        AutoStart();
                    } else {
                        SetTeamNames(); 
                    }
                });
            });


            RegisterEventHandler<EventPlayerDeath>((@event, info) => {
                var victim = @event.Userid;
                var attacker = @event.Attacker;

                if (IsPlayerValid(victim) && IsPlayerValid(attacker))
                {
                    int victimId = (int)victim!.UserId!;
                    int attackerId = (int)attacker!.UserId!;
                    
                    if (victimId != attackerId)
                    {
                        playerKillers[victimId] = attackerId; 
                    }
                }

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

                ReadOnlySpan<char> originalMessageSpan = @event.Text.AsSpan().Trim();
                var message = originalMessageSpan.ToString().ToLower();
                int currentEventUserId = @event.Userid; 

                if (message.StartsWith(".rtv") || message.StartsWith(".vote")) {
                    
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
                        return HookResult.Handled; 
                    }
                }

                if (message == ".r" || message == ".ready") {
                    
                    if (isCountdownActive || isSideSelectionPhase) {
                        return HookResult.Handled; 
                    }

                    if (!matchStarted && readyAvailable) {
                        
                        var chatPlayer = Utilities.GetPlayerFromUserid(currentEventUserId);
                        if (chatPlayer != null && chatPlayer.IsValid) {
                            
                            int uid = (int)(chatPlayer.UserId ?? -1);

                            bool isAlreadyReady = playerReadyStatus.ContainsKey(uid) && playerReadyStatus[uid] == true;

                            if (!isAlreadyReady) {
                                
                                int currentReadyCount = GetReadyPlayersCount();

                                if ((currentReadyCount + 1) >= minimumReadyRequired) {
                                    
                                    if (isShufflePending) {
                                        playerReadyStatus[uid] = true;
                                        
                                        // 【Clan Tag】：最後一票投下準備，瞬間呼叫標籤更新並進行倒數防護
                                        UpdateReadyClanTags();

                                        ExecuteShuffleLogic();     
                                        
                                        return HookResult.Handled; 
                                    }
                                }
                            }
                        }
                    }
                }

                if (isCountdownActive && !originalMessageSpan.ToString().Contains("倒數：")) {
                    return HookResult.Handled;
                }

                int currentVersion = Api.GetVersion();
                int index = currentEventUserId + 1; 
                var playerUserId = NativeAPI.GetUseridFromIndex(index);

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

                if (commandActions != null && commandActions.TryGetValue(message, out var action)) {
                    action(player, null);
                    
                    // 【Clan Tag】：如果是玩家輸入了準備/取消準備，在動作結束後，立刻呼叫更新以零延遲反應記分板
                    if (message == ".r" || message == ".ready" || message == ".unready" || message == ".notready" || message == ".ur")
                    {
                        UpdateReadyClanTags();
                    }
                }

                if (message.StartsWith(".map"))
                {
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
            
            Console.WriteLine($"[{ModuleName} {ModuleVersion} LOADED] MatchZy by WD- (https://github.com/shobhit-pathak/)");
        } // 結束 Load 函數

        // ==========================================
        // --- 指令函數與核心修正代碼 ---
        // ==========================================

        public int GetReadyPlayersCount()
        {
            int count = 0;
            foreach (var (key, value) in playerReadyStatus)
            {
                if (value == true)
                {
                    var player = Utilities.GetPlayerFromUserid(key);
                    if (player is { IsValid: true, Connected: PlayerConnectedState.Connected } && 
                        (player.TeamNum == 2 || player.TeamNum == 3))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        // ▼▼▼ 準備標籤的函式 ▼▼▼
        private void UpdateReadyClanTags()
        {
            // 如果不在準備階段或是已經倒數開賽，就不更新
            if (!readyAvailable || matchStarted || isCountdownActive) return;

            foreach (var p in Utilities.GetPlayers())
            {
                if (p is not { IsValid: true, IsBot: false, IsHLTV: false } || !p.UserId.HasValue) 
                    continue;

                // 防呆：如果是在觀戰區(1)或是未分配陣營(0)，一律清空標籤
                if (p.TeamNum != 2 && p.TeamNum != 3)
                {
                    if (p.Clan == "Ｏ" || p.Clan == "Ｘ")
                    {
                        p.Clan = "";
                        Utilities.SetStateChanged(p, "CCSPlayerController", "m_szClan"); // 強制瞬間同步給所有客戶端
                    }
                    continue;
                }

                int uid = p.UserId.Value;
                bool isReady = playerReadyStatus.TryGetValue(uid, out var ready) && ready;

                string targetTag = isReady ? "Ｏ" : "Ｘ";
                if (p.Clan != targetTag)
                {
                    p.Clan = targetTag;
                    Utilities.SetStateChanged(p, "CCSPlayerController", "m_szClan"); // 強制瞬間同步給所有客戶端
                }
            }
        }

        private void ClearReadyClanTags()
        {
            foreach (var p in Utilities.GetPlayers())
            {
                if (p is not { IsValid: true, IsBot: false, IsHLTV: false }) 
                    continue;

                if (p.Clan == "Ｏ" || p.Clan == "Ｘ")
                {
                    p.Clan = "";
                    Utilities.SetStateChanged(p, "CCSPlayerController", "m_szClan"); // 強制瞬間同步給所有客戶端
                }
            }
        }
        // ▲▲▲ ▲▲▲ ▲▲▲

        private HookResult BlockVoteInCriticalPhases(CCSPlayerController? player, CommandInfo info)
        {
            CCSPlayerController? targetPlayer = player;
            
            if (targetPlayer == null && int.TryParse(info.GetArg(1), out int slot))
            {
                targetPlayer = Utilities.GetPlayerFromSlot(slot);
            }

            if (targetPlayer is not { IsValid: true }) return HookResult.Continue;

            if (isMatchSetup)
            {
                targetPlayer.PrintToChat($"{chatPrefix} 正 式 比 賽 (BO1/BO3) 期 間，禁 止 發 起 任 何 投 票");
                targetPlayer.PrintToCenter("正 式 比 賽 期 間 ， 禁 止 發 起 投 票");
                return HookResult.Stop; 
            }

            if (isCountdownActive || isSideSelectionPhase || isKnifeRound || matchStarted)
            {
                targetPlayer.PrintToChat($"{chatPrefix} 比 賽 進 行 中，禁 止 發 起 任 何 投 票");
                targetPlayer.PrintToCenter("比 賽 進 行 中 ， 禁 止 發 起 投 票");
                return HookResult.Stop; 
            }

            return HookResult.Continue;
        }

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

            if (isSideSelectionPhase) {
                ReplyToUserCommand(player, "選 邊 期 間，無 法 更 改 隊 伍 設 定");
                return;
            }

            if (!isWarmup) {
                ReplyToUserCommand(player, $"比 賽 已 開 始，無 法 隨 機 分 隊");
                return;
            }

            if (isCountdownActive || matchStartCountdownTimer != null) {
                ReplyToUserCommand(player, "正 在 倒 數 準 備 開 賽，無 法 開 啟 隨 機 分 隊");
                return;
            }

            isShufflePending = true;
            
            if (player != null) {
                Server.PrintToChatAll($"{chatPrefix} 管 理 員「 {ChatColors.Lime}已 開 啟 隨 機 隊 伍 分 配 {ChatColors.Default}」 將 自 動 洗 牌");
                
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false } && (p.TeamNum == 2 || p.TeamNum == 3))
                    {
                        p.PrintToCenter("已 開 啟 隨 機 隊 伍 分 配");
                    }
                }
            } else {
                Console.WriteLine("[MatchZy] 投票系統後台指令：已開啟隨機隊伍分配");
            }
        }

        [ConsoleCommand("css_unshuffle", "取消隨機分隊")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void OnUnshuffleCommand(CCSPlayerController? player, CommandInfo? command) {
            if (player != null && !IsPlayerAdmin(player)) {
                return;
            }

            if (isSideSelectionPhase) {
                ReplyToUserCommand(player, "選 邊 期 間，無 法 更 改 隊 伍 設 定");
                return;
            }

            if (isCountdownActive || matchStartCountdownTimer != null) {
                ReplyToUserCommand(player, "正 在 倒 數 準 備 開 賽，無 法 更 改 設 定");
                return;
            }

            isShufflePending = false;

            if (player != null) {
                Server.PrintToChatAll($"{chatPrefix} 管 理 員「 {ChatColors.Orange}已 取 消 隨 機 隊 伍 分 配 {ChatColors.Default}」 維 持 隊 伍 不 變");
                
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false } && (p.TeamNum == 2 || p.TeamNum == 3))
                    {
                        p.PrintToCenter("已 取 消 隨 機 隊 伍 分 配");
                    }
                }
            } else {
                Console.WriteLine("[MatchZy] 投票系統後台指令：已取消隨機隊伍分配");
            }
        }

        public void ExecuteShuffleLogic() 
        {
            ExecuteShuffleLogicWithReady(null); 
        }

        public void ExecuteShuffleLogicWithReady(CCSPlayerController? readyPlayer) 
        {
            int savedUserId = (readyPlayer is { IsValid: true }) ? (int)(readyPlayer.UserId ?? -1) : -1;

            lock (_shuffleLock)
            {
                if (!isShufflePending) return;

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

                Random rng = new();
                int n = activePlayers.Count;
                while (n > 1) 
                {
                    n--;
                    int k = rng.Next(n + 1);
                    (activePlayers[k], activePlayers[n]) = (activePlayers[n], activePlayers[k]);
                }

                int half = activePlayers.Count / 2;
                for (int i = 0; i < activePlayers.Count; i++) 
                {
                    var player = activePlayers[i];
                    if (player is not { IsValid: true }) continue;

                    CsTeam targetTeam = (i < half) ? CsTeam.CounterTerrorist : CsTeam.Terrorist;

                    if (player.TeamNum != (byte)targetTeam)
                    {
                        SwitchPlayerTeam(player, targetTeam); 
                    }
                }

                AddTimer(1.0f, () => {
                    if (matchStarted || playerReadyStatus.Count == 0) return;
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Lime}隨 機 分 隊 完 成！隊 伍 已 鎖 定");
                    Log("[Shuffle] 洗牌同步完成");
                    UpdatePlayersMap(); 
                    
                    StartMatchCountdown(); 
                });
            } 
        } 

        [ConsoleCommand("css_hp", "查詢對擊殺者的傷害統計")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnHpCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is { IsValid: true })
            {
                if (isMatchSetup) return;
                if (!matchStarted || isWarmup) return;
                ShowSinglePlayerDamage(player);
            }
        }

    } 
}
