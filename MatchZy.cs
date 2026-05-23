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
        public bool isCountdownActive = false; 
        public bool isShufflePending = false; 
        public bool isShuffleNameLocked = false; 
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

        public int chatTimerDelay = 13;

        // Game Config
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2; 
        public bool isWhitelistRequired = false;
        public bool isSaveNadesAsGlobalEnabled = false;
        public bool isPlayOutEnabled = false;
        public bool playerHasTakenDamage = false;

        public Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;
        private Database database = new();
    
        public override void Load(bool hotReload) {
            
            LoadAdmins();
            database.InitializeDatabase(ModuleDirectory);

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
            
            RegisterEventHandler<EventPlayerDisconnect>((@event, info) => {
                var player = @event.Userid;
                if (player == null) return HookResult.Continue;
                int userId = (int)(player.UserId ?? -1);

                if (matchStartCountdownTimer != null)
                {
                    string disconnectMsg = $"{chatPrefix} {ChatColors.White}玩 家 {ChatColors.Green}{player.PlayerName} {ChatColors.White}斷 開 連 線 倒 數 中 止 請 重 新 輸 入 {ChatColors.LightRed}.R {ChatColors.White}準 備";

                    CancelMatchCountdown(disconnectMsg);
                    
                    AddTimer(4.0f, () => {
                        Server.PrintToChatAll(disconnectMsg);
                    });

                    AddTimer(8.0f, () => {
                        Server.PrintToChatAll(disconnectMsg);
                    });

                    playerReadyStatus.Clear(); 
                    matchStartCountdownTimer = null;
                    isCountdownActive = false; 
                }

                if (!isWarmup && !matchStarted && !isPractice)
                {
                    if (userId != -1 && playerReadyStatus.ContainsKey(userId)) 
                    {
                        playerReadyStatus.Remove(userId);
                    }
                    UpdatePlayersMap();
                }

                return EventEventPlayerDisconnectHandler(@event, info);
            });

            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerGivenC4>(EventPlayerGivenC4);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);

            // 🎯 核心攔截點：回合開始時，更新 MatchZy 資料核心與 UI 隊伍名稱
            RegisterEventHandler<EventRoundStart>((@event, info) => {
                if (isKnifeRound && isShuffleNameLocked) 
                {
                    var tCaptain = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && !p.IsBot && p.TeamNum == 2);
                    var ctCaptain = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && !p.IsBot && p.TeamNum == 3);

                    string tName = (tCaptain != null && !string.IsNullOrEmpty(tCaptain.PlayerName)) ? $"team_{tCaptain.PlayerName}" : "team_Terrorists";
                    string ctName = (ctCaptain != null && !string.IsNullOrEmpty(ctCaptain.PlayerName)) ? $"team_{ctCaptain.PlayerName}" : "team_CounterTerrorists";

                    // 🎯 修正版（第 325-326 行）：同步數據核心，移除會報錯的 matchConfig.Team1
                    if (teamSides != null) 
                    {
                        if (teamSides.ContainsKey("TERRORIST")) teamSides["TERRORIST"].teamName = tName;
                        if (teamSides.ContainsKey("CT")) teamSides["CT"].teamName = ctName;
                    }

                    Server.ExecuteCommand($"mp_teamname_1 \"{tName}\"");
                    Server.ExecuteCommand($"mp_teamname_2 \"{ctName}\"");
                    
                    isShuffleNameLocked = false;
                }
                return HookResult.Continue;
            });

            AddCommandListener("jointeam", (player, info) =>
            {
                if (player == null || player.IsBot || isSleep) return HookResult.Continue;

                string targetTeam = info.ArgByIndex(1); 
                if (matchStartCountdownTimer != null || isCountdownActive)
                {
                    player.PrintToChat($"{chatPrefix} {ChatColors.Default}倒 數 期 間 禁 止 切 換 隊 伍 或 觀 戰");
                    return HookResult.Stop; 
                }
                
                if (isWarmup) return HookResult.Continue;

                if (matchStarted)
                {
                    if (targetTeam == "1") return HookResult.Continue;

                    byte currentTeam = player.TeamNum;
                    if ((targetTeam == "2" || targetTeam == "3") && (currentTeam == 2 || currentTeam == 3))
                    {
                        player.PrintToChat($"{chatPrefix} {ChatColors.Default}比 賽 已 開 始，禁 止 互 換 隊 伍");
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
                if (player != null && isMatchSetup) 
                {
                    player.PrintToChat($"{chatPrefix} {ChatColors.LightRed}正 式 比 賽 期 間，內 建 投 票 功 能 已 被 禁 用");
                    return HookResult.Stop; 
                }
                return HookResult.Continue; 
            });
            AddCommandListener("noclip", OnConsoleNoClip);

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
                        if (teamSides != null) 
                        {
                            if (teamSides.ContainsKey("TERRORIST")) teamSides["TERRORIST"].teamName = "team_Terrorists";
                            if (teamSides.ContainsKey("CT")) teamSides["CT"].teamName = "team_CounterTerrorists";
                        }
                        Server.ExecuteCommand("mp_teamname_1 \"team_Terrorists\"");
                        Server.ExecuteCommand("mp_teamname_2 \"team_CounterTerrorists\"");
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
                var originalMessage = @event.Text.Trim();
                var message = originalMessage.ToLower();

                if (message == ".r" || message == ".ready") {
                    if (!matchStarted && readyAvailable && GetReadyPlayersCount() >= (minimumReadyRequired - 1)) {
                        if (isShufflePending) 
                        {
                            ExecuteShuffleLogic(); 
                            UpdatePlayersMap();    
                        }
                        OnPlayerReady(Utilities.GetPlayerFromUserid(NativeAPI.GetUseridFromIndex(@event.Userid + 1)), null);
                        AddTimer(0.2f, () => {
                            isCountdownActive = true; 
                        });
                        return HookResult.Handled; 
                    }
                }

                if (isCountdownActive && !originalMessage.Contains("倒數：")) {
                    return HookResult.Handled;
                }

                int index = @event.Userid + 1;
                var playerUserId = NativeAPI.GetUseridFromIndex(index);
                var parts = originalMessage.Split(' ');
                var messageCommandArg = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

                CCSPlayerController? player = null;
                if (playerData.TryGetValue(playerUserId, out CCSPlayerController? value)) {
                    player = value;
                }

                if (player == null) {
                    UpdatePlayersMap();
                    player = playerData[playerUserId];
                }

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
                    HandleTeamNameChangeCommand(1, messageCommandArg);
                }
                if (message.StartsWith(".team2"))
                {
                    HandleTeamNameChangeCommand(2, messageCommandArg);
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
        } 

        public int GetReadyPlayersCount()
        {
            int count = 0;
            foreach (var entry in playerReadyStatus)
            {
                if (entry.Value == true)
                {
                    var player = Utilities.GetPlayerFromUserid(entry.Key);
                    if (player != null && player.IsValid && (player.TeamNum == 2 || player.TeamNum == 3))
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

            if (isMatchSetup) { 
                ReplyToUserCommand(player, "正式比賽模式禁用隨機分隊！");
                return;
            }

            isShufflePending = true;
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}管理員已開啟「 {ChatColors.Yellow}隨 機 隊 伍 分 配 {ChatColors.Green}」。開賽時將自動洗牌");
            
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
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Red}管理員已取消「 {ChatColors.Yellow}隨 機 隊 伍 分 配 {ChatColors.Green} 」。將維持目前隊伍開賽。");
            
            if (player == null) {
                Console.WriteLine("[MatchZy] 已 取 消 隨 機 隊 伍 分 配");
            }
        }

        // 🎯 修正版（原第 813-814 行）：手動修改指令時，同步更新 teamSides 字典數據核
        public void HandleTeamNameChangeCommand(int teamID, string newName)
        {
            if (teamSides != null) 
            {
                if (teamID == 1 && teamSides.ContainsKey("TERRORIST")) teamSides["TERRORIST"].teamName = newName;
                if (teamID == 2 && teamSides.ContainsKey("CT")) teamSides["CT"].teamName = newName;
            }

            if (teamID == 1)
            {
                Server.ExecuteCommand($"mp_teamname_1 \"{newName}\"");
            }
            else if (teamID == 2)
            {
                Server.ExecuteCommand($"mp_teamname_2 \"{newName}\"");
            }
        }

        public void ExecuteShuffleLogic() 
        {
            if (!isShufflePending) return;

            List<CCSPlayerController> activePlayers = Utilities.GetPlayers()
                .Where(p => p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                .ToList();

            if (activePlayers.Count < 2) 
            {
                isShufflePending = false;
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
                activePlayers[i].ChangeTeam(i < half ? CsTeam.Terrorist : CsTeam.CounterTerrorist);
            }

            var tCaptain = activePlayers.FirstOrDefault(p => p.TeamNum == 2);
            var ctCaptain = activePlayers.FirstOrDefault(p => p.TeamNum == 3);

            string tName = (tCaptain != null && !string.IsNullOrEmpty(tCaptain.PlayerName)) ? $"team_{tCaptain.PlayerName}" : "team_Terrorists";
            string ctName = (ctCaptain != null && !string.IsNullOrEmpty(ctCaptain.PlayerName)) ? $"team_{ctCaptain.PlayerName}" : "team_CounterTerrorists";

            if (teamSides != null) 
            {
                if (teamSides.ContainsKey("TERRORIST")) teamSides["TERRORIST"].teamName = tName;
                if (teamSides.ContainsKey("CT")) teamSides["CT"].teamName = ctName;
            }

            Server.ExecuteCommand($"mp_teamname_1 \"{tName}\"");
            Server.ExecuteCommand($"mp_teamname_2 \"{ctName}\"");

            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Lime}隨 機 分 隊 完 成！隊 伍 名 已 更 新。");
            isShufflePending = false;
            isShuffleNameLocked = false; 
        }
    } 
}
