using System;                                       
using System.Collections.Generic;                       
using System.Linq;                                      
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
        public bool isCountdownActive = false; // 全域開關
        public bool isShufflePending = false; // 預約隨機分隊標記
        public bool isKnifeRound = false;
        public bool isSideSelectionPhase = false;
        public bool isMatchLive = false;
        public long liveMatchId = -1;
        public int autoStartMode = 1;
        
        public bool mapReloadRequired = false;

        // Pause Data
        public bool isPaused = false;
        public Dictionary<string, object> unpauseData = new Dictionary<string, object> {
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
        private Dictionary<int, bool> playerReadyStatus = new Dictionary<int, bool>();
        private Dictionary<int, CCSPlayerController> playerData = new Dictionary<int, CCSPlayerController>();

        // Admin Data
        private Dictionary<string, string> loadedAdmins = new Dictionary<string, string>();

        // Timers
        public CounterStrikeSharp.API.Modules.Timers.Timer? unreadyPlayerMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? sideSelectionMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? pausedStateTimer = null;

        // Each message is kept in chat display for ~13 seconds, hence setting default chat timer to 13 seconds.
        public int chatTimerDelay = 13;

        // Game Config
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2; // Number of ready players required start the match.
        public bool isWhitelistRequired = false;
        public bool isSaveNadesAsGlobalEnabled = false;

        public bool isPlayOutEnabled = false;

        public bool playerHasTakenDamage = false;

        // User command - action map
        public Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;

        // SQLite/MySQL Database 
        private Database database = new();

        // 執行緒安全保護鎖，防止並發洗牌衝突
        private static readonly object _shuffleLock = new object();
    
        public override void Load(bool hotReload) {
            
            LoadAdmins();

            database.InitializeDatabase(ModuleDirectory);

            // This sets default config ConVars
            Server.ExecuteCommand("execifexists MatchZy/config.cfg");

            if (!hotReload) {
                AutoStart();
            } else {
                UpdatePlayersMap();
                AutoStart();
            }

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
                { ".tech", OnTechCommand },
                { ".p", OnPauseCommand },
                { ".pause", OnPauseCommand },
                { ".unpause", OnUnpauseCommand },
                { ".up", OnUnpauseCommand },
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
                { ".loadpos", OnLoadPosCommand}
            };

            // 1. 強力白名單修正：直接檢查 whitelist.cfg 檔案
            RegisterEventHandler<EventPlayerConnectFull>((@event, info) => {
                var player = @event.Userid;

                if (isWhitelistRequired && player != null && player.IsValid && !player.IsBot) {
                    if (IsPlayerAdmin(player, "css_whitelist", "@css/chat")) {
                        return HookResult.Continue;
                    }

                    string wlPath = Path.Join(Server.GameDirectory + "/csgo/cfg/MatchZy/whitelist.cfg");
                    bool isAllowed = false;

                    if (File.Exists(wlPath)) {
                        var lines = File.ReadAllLines(wlPath);
                        string playerSid = player.SteamID.ToString();
                        isAllowed = lines.Any(line => line.Trim() == playerSid);
                    }

                    if (!isAllowed) {
                        AddTimer(1.5f, () => {
                            if (player != null && player.IsValid) {
                                Server.ExecuteCommand($"kickid {player.UserId} \"伺 服 器 白 名 單 已 開 啟，您 不 在 白 名 單 中。\"");
                                Log($"[WHITELIST] 已踢出未授權玩家: {player.PlayerName}");
                            }
                        });
                    }
                }
                return EventPlayerConnectFullHandler(@event, info);
            });
            
            // 2. 斷線事件處理：整合「倒數中止」與「狀態清空」
            RegisterEventHandler<EventPlayerDisconnect>((@event, info) => {
                var player = @event.Userid;
                if (player == null) return HookResult.Continue;
                int userId = (int)(player.UserId ?? -1);

                if (matchStartCountdownTimer != null || isCountdownActive)
                {
                    string disconnectMsg = $"{chatPrefix} {ChatColors.White}玩 家 {ChatColors.Green}{player.PlayerName} {ChatColors.White}斷 開 連 線 請 重 新 輸 入 {ChatColors.LightRed}.R {ChatColors.White}準 備";

                    if (matchStartCountdownTimer != null)
                    {
                        matchStartCountdownTimer.Kill();
                        matchStartCountdownTimer = null;
                    }
                    isCountdownActive = false;
                    matchStarted = false;

                    playerReadyStatus.Clear(); 
                    isShufflePending = false; 
                    countdownRemaining = 7;   
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

            // 3. 鐵腕版：倒數期間絕對禁止換隊與觀戰
            AddCommandListener("jointeam", (player, info) =>
            {
                if (player == null || player.IsBot || isSleep) return HookResult.Continue;

                string targetTeam = info.ArgByIndex(1); 

                if (matchStartCountdownTimer != null || isCountdownActive)
                {
                    player.PrintToChat($"{chatPrefix} 倒 數 期 間 禁 止 切 換 隊 伍 或 觀 戰");
                    return HookResult.Stop; 
                }

                if (isWarmup) return HookResult.Continue;

                if (matchStarted)
                {
                    if (isKnifeRound)
                    {
                        byte currentTeam = player.TeamNum;
                        if ((currentTeam == 2 || currentTeam == 3) && (targetTeam == "1" || targetTeam == "2" || targetTeam == "3"))
                        {
                            player.PrintToChat($"{chatPrefix} 刀 局 期 間，禁 止 互 換 隊 伍");
                            return HookResult.Stop; 
                        }
                    }

                    if (targetTeam == "1") return HookResult.Continue;

                    byte playerTeam = player.TeamNum;
                    if ((targetTeam == "2" || targetTeam == "3") && (playerTeam == 2 || playerTeam == 3))
                    {
                        player.PrintToChat($"{chatPrefix} 比 賽 已 開 始，禁 止 互 換 隊 伍");
                        return HookResult.Stop; 
                    }
                }

                return HookResult.Continue;
            });

            // 4. 攔截倒數期間的所有隊伍變動廣播
            RegisterEventHandler<EventPlayerTeam>((@event, info) =>
            {
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
                if (player != null && isMatchSetup)
                {
                    player.PrintToChat($"{chatPrefix} 正 式 比 賽 期 間，內 建 投 票 功 能 已 被 禁 用");
                    return HookResult.Stop;
                }
                return HookResult.Continue; 
            });

            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerGivenC4>(EventPlayerGivenC4);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);

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

                    if (!isMatchSetup) {
                        AutoStart();
                    } else {
                        SetTeamNames(); 
                    }
                });
            });

            RegisterEventHandler<EventPlayerDeath>((@event, info) => {
                var player = @event.Userid;
                if (!isWarmup) return HookResult.Continue;
                if (!IsPlayerValid(player)) return HookResult.Continue;
                if (player!.InGameMoneyServices != null) player.InGameMoneyServices.Account = 16000;
                return HookResult.Continue;
            });

            AddCommandListener("noclip", OnConsoleNoClip);

            RegisterEventHandler<EventPlayerHurt>((@event, info) => {
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

                // --- 【核心重構：頂端攔截邏輯】滿人點名打 .r 的瞬間提前完成洗牌，完全避開倒數 ---
                var originalMessage = @event.Text.Trim();
                var message = originalMessage.ToLower();

                if (message == ".r" || message == ".ready") {
                    if (!matchStarted && readyAvailable) {
                        int currentReadyCount = GetReadyPlayersCount();
                        
                        // 檢查這次輸入是不是剛好達到開賽標準
                        if (currentReadyCount >= (minimumReadyRequired - 1)) {
                            var triggeringPlayer = Utilities.GetPlayerFromUserid(NativeAPI.GetUseridFromIndex(@event.Userid + 1));
                            
                            // 如果管理員開啟了隨機分隊，在即將滿人開賽的當下「立刻先洗牌」，完全避開倒數階段
                            if (isShufflePending) {
                                ExecuteShuffleLogicBeforeCountdown(triggeringPlayer);
                                return HookResult.Handled; // 攔截，不讓它走原版 OnPlayerReady 舊流程
                            }
                        }
                    }
                }

                // 如果倒數已經在跑，擋掉所有一般發話
                if (isCountdownActive && !originalMessage.Contains("倒數：")) {
                    return HookResult.Handled;
                }

                int index = @event.Userid + 1;
                var playerUserId = NativeAPI.GetUseridFromIndex(index);

                var parts = originalMessage.Split(' ');
                var messageCommand = parts.Length > 0 ? parts[0] : string.Empty;
                var messageCommandArg = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

                CCSPlayerController? player = null;
                if (playerData.TryGetValue(playerUserId, out CCSPlayerController? value)) {
                    player = value;
                }

                if (player == null) {
                    UpdatePlayersMap();
                    player = playerData[playerUserId];
                }

                // 處理對應指令
                if (commandActions.ContainsKey(message)) {
                    commandActions[message](player, null);
                }

                if (message.StartsWith(".map"))
                {
                    if (isMatchSetup)
                    {
                        Server.PrintToChatAll($"{chatPrefix} {ChatColors.LightRed}{player.PlayerName}{ChatColors.Default} 嘗試更換地圖。{ChatColors.LightRed}正式比賽地圖已鎖定{ChatColors.Default}，禁止更換！");
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
        // --- 核心重構：滿人準備當下，立刻在「倒數第 7 秒之前」完成洗牌與重命名 ---
        // ==========================================
        public void ExecuteShuffleLogicBeforeCountdown(CCSPlayerController? readyPlayer)
        {
            lock (_shuffleLock)
            {
                if (!isShufflePending) return;

                List<CCSPlayerController> activePlayers = Utilities.GetPlayers()
                    .Where(p => p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                    .ToList();

                if (activePlayers.Count >= 2) 
                {
                    // 1. 趁還沒跑倒數，原地把所有人隨機洗牌
                    Random rng = new();
                    int n = activePlayers.Count;
                    while (n > 1) {
                        n--;
                        int k = rng.Next(n + 1);
                        (activePlayers[k], activePlayers[n]) = (activePlayers[n], activePlayers[k]);
                    }

                    string? newCTLeaderName = null;
                    string? newTLeaderName = null;

                    int half = activePlayers.Count / 2;
                    for (int i = 0; i < activePlayers.Count; i++) {
                        if (i < half) {
                            activePlayers[i].SwitchTeam(CsTeam.CounterTerrorist);
                            if (newCTLeaderName == null && !string.IsNullOrWhiteSpace(activePlayers[i].PlayerName)) {
                                newCTLeaderName = string.Copy(activePlayers[i].PlayerName);
                            }
                        } else {
                            activePlayers[i].SwitchTeam(CsTeam.Terrorist);
                            if (newTLeaderName == null && !string.IsNullOrWhiteSpace(activePlayers[i].PlayerName)) {
                                newTLeaderName = string.Copy(activePlayers[i].PlayerName);
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(newCTLeaderName)) newCTLeaderName = "CT";
                    if (string.IsNullOrWhiteSpace(newTLeaderName)) newTLeaderName = "T";

                    string finalCTTeamName = "team_" + newCTLeaderName;
                    string finalTTeamName = "team_" + newTLeaderName;

                    matchzyTeam1.teamName = finalCTTeamName;
                    matchzyTeam2.teamName = finalTTeamName;
                    Server.ExecuteCommand($"mp_teamname_1 \"{finalCTTeamName}\"");
                    Server.ExecuteCommand($"mp_teamname_2 \"{finalTTeamName}\"");

                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Lime}[隨機分隊完成] 隊伍已重新分配與鎖定！");
                }

                isShufflePending = false; // 關閉隨機分配旗標
                UpdatePlayersMap();       // 刷新快取

                // 2. 延遲 0.2 秒（等引擎換隊封包完全處理完），才正式呼叫原版的準備函數，讓它開始乾淨地倒數
                int savedUserId = (readyPlayer != null && readyPlayer.IsValid) ? (int)(readyPlayer.UserId ?? -1) : -1;
                AddTimer(0.2f, () => {
                    CCSPlayerController? targetReadyPlayer = null;
                    if (savedUserId != -1) targetReadyPlayer = Utilities.GetPlayerFromUserid(savedUserId);

                    if (targetReadyPlayer != null && targetReadyPlayer.IsValid && targetReadyPlayer.Connected == PlayerConnectedState.Connected) {
                        OnPlayerReady(targetReadyPlayer, null); // 激活原版倒數
                    } else {
                        var fallbackPlayer = Utilities.GetPlayers().FirstOrDefault(p => 
                            p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3) && p.Connected == PlayerConnectedState.Connected
                        );
                        if (fallbackPlayer != null) OnPlayerReady(fallbackPlayer, null);
                    }
                });
            }
        }


        // ==========================================
        // --- 每秒執行一次的倒數 Callback (解決卡 1 秒的致命 Race Condition) ---
        // ==========================================
        public void MatchStartCountdownCallback()
        {
            if (!isCountdownActive) return;

            if (countdownRemaining > 0)
            {
                Server.PrintToChatAll($"{chatPrefix} 比賽即將開始，倒數：{countdownRemaining} 秒...");
                countdownRemaining--;
            }
            else
            {
                // 扣到 0 秒的瞬間第一時間搶先關閉開關，不讓後續任何移動有機會呼叫到重置
                isCountdownActive = false;
                
                if (matchStartCountdownTimer != null)
                {
                    matchStartCountdownTimer.Kill();
                    matchStartCountdownTimer = null;
                }

                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Lime}比 賽 正 式 開 始 ！祝 各位 好 運 ！");
                
                // 倒數結束直接觸發正賽開賽，不在此處做任何換隊
                TriggerActualMatchStart();
            }
        }

        // ==========================================
        // --- 重新定義人數統計邏輯，完全排除觀戰者與離線玩家 ---
        // ==========================================
        public int GetReadyPlayersCount()
        {
            int count = 0;
            foreach (var entry in playerReadyStatus)
            {
                if (entry.Value == true)
                {
                    var player = Utilities.GetPlayerFromUserid(entry.Key);
                    if (player != null && 
                        player.IsValid && 
                        player.Connected == PlayerConnectedState.Connected && 
                        (player.TeamNum == 2 || player.TeamNum == 3))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        [ConsoleCommand("css_shuffle", "預約隨機分隊")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)] 
        public void OnShuffleCommand(CCSPlayerController? player, CommandInfo command) {
            if (player != null && !IsPlayerAdmin(player)) {
                return;
            }

            if (isMatchSetup || !isWarmup) { 
                ReplyToUserCommand(player, "當前階段無法開啟隨機分隊");
                return;
            }

            isShufflePending = true;
            Server.PrintToChatAll($"{chatPrefix} 管 理 員「 {ChatColors.Lime}已 開 啟 隨 機 隊 伍 分 配 {ChatColors.Default}」 滿 人 準 備 時 將 先 自 動 洗 牌");
            
            if (player == null) {
                Console.WriteLine("[MatchZy] 已 開 啟 隨 機 隊 伍 分 配");
            }
        }

        [ConsoleCommand("css_unshuffle", "取消隨機分隊")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void OnUnshuffleCommand(CCSPlayerController? player, CommandInfo command) {
            if (player != null && !IsPlayerAdmin(player)) {
                return;
            }

            isShufflePending = false;
            Server.PrintToChatAll($"{chatPrefix} 管 理 員「 {ChatColors.LightRed}已 取 消 隨 機 隊 伍 分 配 {ChatColors.Default}」 維 持 隊 伍 不 變");
            
            if (player == null) {
                Console.WriteLine("[MatchZy] 已 取 消 隨 機 隊 伍 分 配");
            }
        }

        // ==========================================
        // --- 護航輔助函數：0秒時安全喚醒原本最底部的開賽功能 ---
        // ==========================================
        private void TriggerActualMatchStart()
        {
            var readyPlayer = Utilities.GetPlayers().FirstOrDefault(p => 
                p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3) && p.Connected == PlayerConnectedState.Connected
            );

            if (readyPlayer != null)
            {
                int savedUserId = (readyPlayer.IsValid) ? (int)(readyPlayer.UserId ?? -1) : -1;

                AddTimer(0.2f, () => {
                    UpdatePlayersMap();
                    
                    CCSPlayerController? targetReadyPlayer = null;
                    if (savedUserId != -1)
                    {
                        targetReadyPlayer = Utilities.GetPlayerFromUserid(savedUserId);
                    }

                    if (targetReadyPlayer != null && targetReadyPlayer.IsValid && targetReadyPlayer.Connected == PlayerConnectedState.Connected)
                    {
                        OnPlayerReady(targetReadyPlayer, null);
                    }
                    else
                    {
                        var fallbackPlayer = Utilities.GetPlayers().FirstOrDefault(p => 
                            p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3) && p.Connected == PlayerConnectedState.Connected
                        );
                        
                        if (fallbackPlayer != null)
                        {
                            OnPlayerReady(fallbackPlayer, null);
                        }
                    }
                });
            }
        }

    } // 這是 class MatchZy 的結束括號
} // 這是 namespace MatchZy 的結束括號
