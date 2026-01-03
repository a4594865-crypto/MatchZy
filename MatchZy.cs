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
        // Configurable using matchzy_chat_messages_timer_delay <seconds>
        public int chatTimerDelay = 13;

        // Game Config
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2; // Number of ready players required start the match. If set to 0, all connected players have to ready-up to start the match.
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

            // This sets default config ConVars
            Server.ExecuteCommand("execifexists MatchZy/config.cfg");

            teamSides[matchzyTeam1] = "CT";
            teamSides[matchzyTeam2] = "TERRORIST";
            reverseTeamSides["CT"] = matchzyTeam1;
            reverseTeamSides["TERRORIST"] = matchzyTeam2;

            if (!hotReload) {
                AutoStart();
            } else {
                // Pluign should not be reloaded while a match is live (this would messup with the match flags which were set)
                // Only hot-reload the plugin if you are testing something and don't want to restart the server time and again.
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

            // 1. 基礎事件註冊
            RegisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFullHandler);
            RegisterEventHandler<EventPlayerDisconnect>(EventPlayerDisconnectHandler);
            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerGivenC4>(EventPlayerGivenC4);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);

            // --- 核心修正：加入緩衝判斷，防止換圖後無法換回正確隊伍 ---
            AddCommandListener("jointeam", (player, info) =>
            {
                // 如果比賽已開始
                if (player != null && (matchStarted || isKnifeRequired)) 
                {
                    // 檢查目前是否在「凍結時間」(Freeze Period)
                    // 如果在凍結時間內，通常是換圖後的剛開始，允許玩家調整隊伍
                    if (GameState == MatchZy.GameState.FreezeTime || !matchStarted) 
                    {
                        return HookResult.Continue; 
                    }

                    // 如果已經正式開打（離開凍結時間），才真正擋住
                    player.PrintToChat($"{chatPrefix} {ChatColors.Red}比賽進行中{ChatColors.Default}禁止自行更換隊伍！");
                    return HookResult.Stop; 
                }
                return HookResult.Continue; 
            });

            AddCommandListener("noclip", OnConsoleNoClip);

            // 4. 聊天指令監聽 (相容性修正版)
            RegisterEventHandler<EventPlayerChat>((@event, info) =>
            {
                // 使用 GetPlayerFromIndex 解決 L248 的 int 轉換問題
                CCSPlayerController? player = Utilities.GetPlayerFromIndex(@event.Userid);
                if (player == null || !player.IsValid) return HookResult.Continue;

                string message = @event.Text.Trim().ToLower();
                string[] args = message.Split(' ');
                string messageCommandArg = args.Length > 1 ? args[1] : "";

                // 5. JSON 期間禁止換圖
                if (message.StartsWith(".map"))
                {
                    if (isMatchSetup)
                    {
                        Server.PrintToChatAll($"{chatPrefix} 玩家 {ChatColors.Default}{ChatColors.LightRed}{player.PlayerName}{ChatColors.Default} 嘗試更換地圖。{ChatColors.Red}正式比賽地圖已鎖定{ChatColors.Default}，禁止更換！");
                        return HookResult.Continue;
                    }
                    HandleMapChangeCommand(player, messageCommandArg);
                }
                
                // 6. JSON 期間禁止切換模式
                if (message.StartsWith(".prac") || message.StartsWith(".match"))
                {
                    if (isMatchSetup)
                    {
                        Server.PrintToChatAll($"{chatPrefix} {ChatColors.LightRed}玩家 {ChatColors.Default}{player.PlayerName} {ChatColors.LightRed}嘗試切換模式。{ChatColors.Red}正式比賽期間禁止切換遊戲模式！{ChatColors.Default}");
                        return HookResult.Continue;
                    }
                }

                // 已刪除原本報錯的 .ready / .r 判斷邏輯，因為插件已有內建指令監聽處理
                
                return HookResult.Continue;
            });
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

            // RegisterEventHandler<EventMapShutdown>((@event, info) => {
            //     Log($"[EventMapShutdown] Resetting match!");
            //     ResetMatch();
            //     return HookResult.Continue;
            // });

RegisterListener<Listeners.OnMapStart>(mapName => { 
    AddTimer(1.0f, () => {
        // 核心修正：括號內不要傳入任何東西（預設為 false），這樣就不會洗掉 1-0 的比分
        ResetTeamDataCaches(); 

        // 強制對齊 Team 1 為 CT (解決 11 變 22)
        teamSides[matchzyTeam1] = "CT";
        teamSides[matchzyTeam2] = "TERRORIST";
        reverseTeamSides["CT"] = matchzyTeam1;
        reverseTeamSides["TERRORIST"] = matchzyTeam2;

        if (!isMatchSetup) {
            AutoStart();
            return;
        }
        SetTeamNames(); // 確保隊名刷新
    });
});
            // RegisterListener<Listeners.OnMapEnd>(() => {
            //     Log($"[Listeners.OnMapEnd] Resetting match!");
            //     ResetMatch();
            // });

            RegisterEventHandler<EventPlayerDeath>((@event, info) => {
                // Setting money back to 16000 when a player dies in warmup
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

                int currentVersion = Api.GetVersion();
                int index = @event.Userid + 1;
                var playerUserId = NativeAPI.GetUseridFromIndex(index);

                var originalMessage = @event.Text.Trim();
                var message = @event.Text.Trim().ToLower();

                var parts = originalMessage.Split(' ');
                var messageCommand = parts.Length > 0 ? parts[0] : string.Empty;
                var messageCommandArg = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

                CCSPlayerController? player = null;
                if (playerData.TryGetValue(playerUserId, out CCSPlayerController? value)) {
                    player = value;
                }

                if (player == null) {
                    // Somehow we did not had the player in playerData, hence updating the maps again before getting the player
                    UpdatePlayersMap();
                    player = playerData[playerUserId];
                }

                // Handling player commands
                if (commandActions.ContainsKey(message)) {
                    commandActions[message](player, null);
                }

               if (message.StartsWith(".map"))
                {
                    // 核心判斷：如果是 JSON 載入的正式比賽
                    if (isMatchSetup)
                    {
                        // 1. 全服廣播：讓所有人看到是誰嘗試換圖，並告知地圖已鎖定
                        Server.PrintToChatAll($"{chatPrefix} 玩家 {ChatColors.LightRed}{player.PlayerName}{ChatColors.Default} 嘗試更換地圖。{ChatColors.LightRed}正式比賽地圖已鎖定{ChatColors.Default}，禁止更換！");
                        
                        // 2. 攔截指令：不執行後續的 HandleMapChangeCommand
                        return HookResult.Continue;
                    }

                    // 如果不是 JSON 比賽，才執行原本的換圖/投票邏輯
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
                            // ReplyToUserCommand(player, "Usage: .asay <message>");
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
        }
    }
}
