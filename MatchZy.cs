using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes;
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

        public string chatPrefix = $"[{ChatColors.Green}MatchZy{ChatColors.Default}]";
        public string adminChatPrefix = $"[{ChatColors.Red}ADMIN{ChatColors.Default}]";

        // Plugin start phase data
        public bool isPractice = false;
        public bool isSleep = false;
        public bool readyAvailable = false;
        public bool matchStarted = false;
        public bool isWarmup = false;
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
        public int minimumReadyRequired = 2; 
        public bool isWhitelistRequired = false;
        public bool isSaveNadesAsGlobalEnabled = false;

        public bool isPlayOutEnabled = false;

        public bool playerHasTakenDamage = false;

        // User command - action map
        public Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;

        // SQLite/MySQL Database 
        private Database database = new();
    
        public override void Load(bool hotReload) {
            
            LoadAdmins();

            database.InitializeDatabase(ModuleDirectory);

            Server.ExecuteCommand("execifexists MatchZy/config.cfg");

            teamSides[matchzyTeam1] = "CT";
            teamSides[matchzyTeam2] = "TERRORIST";
            reverseTeamSides["CT"] = matchzyTeam1;
            reverseTeamSides["TERRORIST"] = matchzyTeam2;

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
                { ".loadpos", OnLoadPosCommand}
            };

            // 1. 基礎事件註冊 (強力白名單：只要 .whitelist 開啟，路人進服 2 秒就踢)
            RegisterEventHandler<EventPlayerConnectFull>((@event, info) => {
                var player = @event.Userid;
                if (isWhitelistRequired && player != null && player.IsValid && !player.IsBot) {
                    if (IsPlayerAdmin(player, "css_whitelist", "@css/chat")) {
                        return HookResult.Continue;
                    }
                    if (!playerData.ContainsKey((int)player.UserId!) && !isSleep) {
                        AddTimer(2.0f, () => {
                            if (player.IsValid) {
                                Server.ExecuteCommand($"kickid {player.UserId} \"伺服器白名單已開啟，您不在名單中。\"");
                                Log($"[WHITELIST] 已強制踢出路人: {player.PlayerName}");
                            }
                        });
                    }
                }
                return EventPlayerConnectFullHandler(@event, info);
            });

            RegisterEventHandler<EventPlayerDisconnect>(EventPlayerDisconnectHandler);
            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerGivenC4>(EventPlayerGivenC4);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);

            // 2. 換隊攔截 (正式比賽鎖定，熱身期間放行)
            AddCommandListener("jointeam", (player, info) => {
                if (player != null && isMatchSetup && (matchStarted || isKnifeRequired)) {
                    if (isWarmup) return HookResult.Continue;
                    player.PrintToChat($"{chatPrefix} {ChatColors.LightRed}正式比賽期間禁止自行更換隊伍！");
                    return HookResult.Stop; 
                }
                return HookResult.Continue; 
            });
            AddCommandListener("noclip", OnConsoleNoClip);

            // 3. 聊天指令監聽 (整合地圖鎖定與所有內建指令)
            RegisterEventHandler<EventPlayerChat>((@event, info) => {
                CCSPlayerController? player = Utilities.GetPlayerFromIndex(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                string message = @event.Text.Trim().ToLower();
                string[] args = message.Split(' ');
                string messageCommandArg = args.Length > 1 ? string.Join(' ', args.Skip(1)) : "";

                // JSON 換圖鎖定
                if (message.StartsWith(".map") && isMatchSetup) {
                    Server.PrintToChatAll($"{chatPrefix} 玩家 {ChatColors.LightRed}{player.PlayerName}{ChatColors.Default} 嘗試更換地圖。{ChatColors.Red}正式比賽地圖已鎖定，禁止換圖！{ChatColors.Default}");
                    return HookResult.Continue;
                }
                
                // JSON 模式鎖定
                if ((message.StartsWith(".prac") || message.StartsWith(".match")) && isMatchSetup) {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.LightRed}正式比賽期間禁止切換模式！");
                    return HookResult.Continue;
                }

                // 執行 commandActions 中的所有指令 (如 .ready, .whitelist 等)
                if (commandActions != null && commandActions.ContainsKey(args[0])) {
                    commandActions[args[0]](player, null);
                }

                // 特殊指令快捷處理
                if (message.StartsWith(".restore")) HandleRestoreCommand(player, messageCommandArg);
                if (message.StartsWith(".asay") && IsPlayerAdmin(player, "css_asay", "@css/chat")) {
                    if (messageCommandArg != "") Server.PrintToChatAll($"{adminChatPrefix} {messageCommandArg}");
                }
                if (message.StartsWith(".savenade") || message.StartsWith(".sn")) HandleSaveNadeCommand(player, messageCommandArg);
                if (message.StartsWith(".delnade") || message.StartsWith(".dn") || message.StartsWith(".deletenade")) HandleDeleteNadeCommand(player, messageCommandArg);
                if (message.StartsWith(".importnade") || message.StartsWith(".in")) HandleImportNadeCommand(player, messageCommandArg);
                if (message.StartsWith(".listnades") || message.StartsWith(".lin")) HandleListNadesCommand(player, messageCommandArg);
                if (message.StartsWith(".loadnade") || message.StartsWith(".ln")) HandleLoadNadeCommand(player, messageCommandArg);
                if (message.StartsWith(".spawn")) HandleSpawnCommand(player, messageCommandArg, player.TeamNum, "spawn");
                if (message.StartsWith(".ctspawn") || message.StartsWith(".cts")) HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
                if (message.StartsWith(".tspawn") || message.StartsWith(".ts")) HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.Terrorist, "tspawn");
                if (message.StartsWith(".team1")) HandleTeamNameChangeCommand(player, messageCommandArg, 1);
                if (message.StartsWith(".team2")) HandleTeamNameChangeCommand(player, messageCommandArg, 2);
                if (message.StartsWith(".rcon") && IsPlayerAdmin(player, "css_rcon", "@css/rcon")) {
                    Server.ExecuteCommand(messageCommandArg);
                }
                if (message.StartsWith(".coach")) HandleCoachCommand(player, messageCommandArg);
                if (message.StartsWith(".ban")) HandeMapBanCommand(player, messageCommandArg);
                if (message.StartsWith(".pick")) HandeMapPickCommand(player, messageCommandArg);
                if (message.StartsWith(".back")) HandleBackCommand(player, messageCommandArg);
                if (message.StartsWith(".delay")) HandleDelayCommand(player, messageCommandArg);
                if (message.StartsWith(".throwindex") || message.StartsWith(".throwidx")) HandleThrowIndexCommand(player, messageCommandArg);

                return HookResult.Continue;
            });

            // 4. 回合與傷害處理 (一條都沒少)
            RegisterEventHandler<EventRoundEnd>(EventRoundEndHandler, HookMode.Pre);
            RegisterEventHandler<EventRoundEnd>(EventRoundEndHandler, HookMode.Post);

            RegisterListener<Listeners.OnMapStart>(mapName => { 
                AddTimer(1.0f, () => {
                    ResetTeamDataCaches(); 
                    teamSides[matchzyTeam1] = "CT";
                    teamSides[matchzyTeam2] = "TERRORIST";
                    reverseTeamSides["CT"] = matchzyTeam1;
                    reverseTeamSides["TERRORIST"] = matchzyTeam2;
                    if (!isMatchSetup) AutoStart();
                    else SetTeamNames();
                });
            });

            RegisterEventHandler<EventPlayerDeath>((@event, info) => {
                var player = @event.Userid;
                if (!isWarmup) return HookResult.Continue;
                if (!IsPlayerValid(player)) return HookResult.Continue;
                if (player!.InGameMoneyServices != null) player.InGameMoneyServices.Account = 16000;
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerHurt>(EventPlayerHurtHandler);
            RegisterEventHandler<EventPlayerBlind>(EventPlayerBlindHandler);
            RegisterEventHandler<EventSmokegrenadeDetonate>(EventSmokegrenadeDetonateHandler);
            RegisterEventHandler<EventFlashbangDetonate>(EventFlashbangDetonateHandler);
            RegisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            RegisterEventHandler<EventMolotovDetonate>(EventMolotovDetonateHandler);
            RegisterEventHandler<EventDecoyStarted>(EventDecoyDetonateHandler);

            Console.WriteLine($"[{ModuleName} {ModuleVersion} LOADED] 修復版整合成功！");
        }
    }
}
