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
        public bool isCountdownActive = false; // 加入這一行，宣告全域開關
        public bool isShufflePending = false; // A方案：預約隨機分隊標記
        public bool isShuffleNameLocked = false; // 🎯 新增：隨機分隊完成後，等待刀局開局命名的標記
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
                { ".rk999", OnKnifeCommand },
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

                // 只有開啟 .whitelist 指令時才檢查
                if (isWhitelistRequired && player != null && player.IsValid && !player.IsBot) {
                    
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
                        isAllowed = lines.Any(line => line.Trim() == playerSid);
                    }

                    if (!isAllowed) {
                        // 延遲踢除，確保訊息發送
                        AddTimer(1.5f, () => {
                            if (player != null && player.IsValid) {
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
                if (player == null) return HookResult.Continue;
                int userId = (int)(player.UserId ?? -1);

                // --- A. 倒數期間斷線：中止倒數 ---
// --- A. 倒數期間斷線：中止倒數 ---
if (matchStartCountdownTimer != null)
{
    // 1. 定義要發送的訊息內容
    string disconnectMsg = $"{chatPrefix} {ChatColors.White}玩 家 {ChatColors.Green}{player.PlayerName} {ChatColors.White}斷 開 連 線 倒 數 中 止 請 重 新 輸 入 {ChatColors.LightRed}.R {ChatColors.White}準 備";

    // 2. 立即發送 (第 1 次)
    CancelMatchCountdown(disconnectMsg);
    
    // 3. 延遲 3 秒發送 (第 2 次)
    AddTimer(4.0f, () => {
        Server.PrintToChatAll(disconnectMsg);
    });

    // 4. 延遲 6 秒發送 (第 3 次)
    AddTimer(8.0f, () => {
        Server.PrintToChatAll(disconnectMsg);
    });

    // 5. 清空狀態與計時器邏輯
    playerReadyStatus.Clear(); 
    matchStartCountdownTimer = null;
    isCountdownActive = false; 
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

                // 呼叫原本可能定義在其他檔案的處理程序 (保持原架構相容)
                return EventPlayerDisconnectHandler(@event, info);
            });

            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerGivenC4>(EventPlayerGivenC4);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);


// 🎯 正確的攔截點：EventRoundPrestart 是每回合「重置、凍結時間開始」的第 0 秒
RegisterEventHandler<EventRoundPrestart>((@event, info) => {
    
    // 【第一道保險】：隨機分隊名字鎖定開著，代表這是倒數結束後的「第一個回合」
    if (isShuffleNameLocked) 
    {
        // 進入凍結時間的第 2 秒
        AddTimer(2.0f, () => {
            
            // 🔥【第二道終極保險】：實時檢查，如果現在「不是」刀局，立刻退出並解鎖！
            if (!isKnifeRound) 
            {
                isShuffleNameLocked = false;
                return;
            }
            
            // 現場撈取選手（安全過濾：必須有效、非機器人、且確實待在 T(2) 或 CT(3) 隊伍中）
            var tCaptain = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && !p.IsBot && p.TeamNum == 2);
            var ctCaptain = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && !p.IsBot && p.TeamNum == 3);

            string tName = (tCaptain != null && !string.IsNullOrEmpty(tCaptain.PlayerName)) 
                            ? $"team_{tCaptain.PlayerName}" 
                            : "team_Terrorists";

            string ctName = (ctCaptain != null && !string.IsNullOrEmpty(ctCaptain.PlayerName)) 
                            ? $"team_{ctCaptain.PlayerName}" 
                            : "team_CounterTerrorists";

            // 強制鎖死 ConVar
            Server.ExecuteCommand($"mp_teamname_1 \"{tName}\"");
            Server.ExecuteCommand($"mp_teamname_2 \"{ctName}\"");
            
            // 功成身退，關閉旗標
            isShuffleNameLocked = false;
        });
    }

    return HookResult.Continue;
});
// 2. 鐵腕版：倒數期間絕對禁止換隊與觀戰
AddCommandListener("jointeam", (player, info) =>
{
    if (player == null || player.IsBot || isSleep) return HookResult.Continue;

    string targetTeam = info.ArgByIndex(1); 
    int userId = (int)(player.UserId ?? -1);

    // --- 核心邏輯：只要正在倒數中，管你在不在熱身，通通不准換隊 ---
    if (matchStartCountdownTimer != null || isCountdownActive)
    {
        // 顯示警告訊息給該玩家
        player.PrintToChat($"{chatPrefix} {ChatColors.Default}倒 數 期 間 禁 止 切 換 隊 伍 或 觀 戰");
        
        // 返回 HookResult.Stop 就能直接吃掉這個指令，讓玩家留在原地
        return HookResult.Stop; 
    }

    // --- 以下為非倒數期間的正常比賽邏輯 ---
    
    // 如果是熱身階段（且沒在倒數），允許自由換隊
    if (isWarmup) return HookResult.Continue;

    // 比賽正式開始後
    if (matchStarted)
    {
        // 允許去觀戰 (targetTeam "1" 是觀戰)
        if (targetTeam == "1") return HookResult.Continue;

        // 禁止 CT/T 互換 (targetTeam "2" 是 T, "3" 是 CT)
        byte currentTeam = player.TeamNum;
        if ((targetTeam == "2" || targetTeam == "3") && (currentTeam == 2 || currentTeam == 3))
        {
            player.PrintToChat($"{chatPrefix} {ChatColors.Default}比 賽 已 開 始，禁 止 互 換 隊 伍");
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
                var originalMessage = @event.Text.Trim();
                var message = originalMessage.ToLower();

// 1. 攔截開賽指令
if (message == ".r" || message == ".ready") {
    // 判斷是否為最後一個準備的人
    if (!matchStarted && readyAvailable && GetReadyPlayersCount() >= (minimumReadyRequired - 1)) {
        
        // 📢 【步驟 A】先執行原本的準備邏輯：讓 MatchZy 原生去啟動 7 秒倒數、去執行它的重置
        OnPlayerReady(Utilities.GetPlayerFromUserid(NativeAPI.GetUseridFromIndex(@event.Userid + 1)), null);
        
        // ⏱️ 【步驟 B】使用 0.1 秒極短定時器：繞過 OnPlayerReady 的變數重置地雷
        AddTimer(0.1f, () => {
            // 倒數已經安全啟動了，此時我們再來檢查並執行洗牌
            if (isShufflePending) 
            {
                ExecuteShuffleLogic(); // 👈 執行洗牌，並將 isShuffleNameLocked 設為 true
                UpdatePlayersMap();    // 強制更新玩家隊伍緩存
            }
            
            isCountdownActive = true;  // 開啟你的靜音開關
        });
        
        return HookResult.Handled; 
    }
}

                // 2. 如果倒數已經在跑，擋掉所有一般發話 (除了系統發出的「倒數：」)
                if (isCountdownActive && !originalMessage.Contains("倒數：")) {
                    return HookResult.Handled;
                }
                // --- [第一步結束] ---

                int currentVersion = Api.GetVersion();
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

                // Handling player commands
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
        // --- 指令函數與核心修正代碼 ---
        // ==========================================

        // --- 核心修正：重新定義人數統計邏輯，完全排除觀戰者 ---
        public int GetReadyPlayersCount()
{
    int count = 0;
    foreach (var entry in playerReadyStatus)
    {
        if (entry.Value == true)
        {
            var player = Utilities.GetPlayerFromUserid(entry.Key);
            // 雙重保險：即使在名單內是 True，也必須人在場上才給分
            if (player != null && player.IsValid && (player.TeamNum == 2 || player.TeamNum == 3))
            {
                count++;
            }
        }
    }
    return count;
}
[ConsoleCommand("css_shuffle", "預約隨機分隊")]
[CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)] // 強制宣告客戶端與伺服器皆可執行
public void OnShuffleCommand(CCSPlayerController? player, CommandInfo command) {
    // 1. 權限檢查：如果是玩家發出的，檢查管理員權限；如果是伺服器 (player == null)，直接通過
    if (player != null && !IsPlayerAdmin(player)) {
        return;
    }

    if (isMatchSetup) { 
        ReplyToUserCommand(player, "正式比賽模式禁用隨機分隊！");
        return;
    }

    isShufflePending = true;
    
    // 2. 執行廣播
    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}管理員已開啟「 {ChatColors.Yellow}隨 機 隊 伍 分 配 {ChatColors.Green}」。開賽時將自動洗牌");
    
    // 3. 確保黑視窗有回饋
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
public void ExecuteShuffleLogic() 
{
    // 1. 安全檢查
    if (!isShufflePending) return;

    List<CCSPlayerController> activePlayers = Utilities.GetPlayers()
        .Where(p => p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
        .ToList();

    if (activePlayers.Count < 2) 
    {
        isShufflePending = false; 
        return;
    }

    // 2. Fisher-Yates 洗牌演算法
    Random rng = new();
    int n = activePlayers.Count;
    while (n > 1) 
    {
        n--;
        int k = rng.Next(n + 1);
        (activePlayers[k], activePlayers[n]) = (activePlayers[n], activePlayers[k]);
    }

    // 3. 分配隊伍
    int half = activePlayers.Count / 2;
    for (int i = 0; i < activePlayers.Count; i++) 
    {
        if (i < half) 
        {
            activePlayers[i].ChangeTeam(CsTeam.Terrorist);
        }
        else 
        {
            activePlayers[i].ChangeTeam(CsTeam.CounterTerrorist);
        }
    }

    // === 軌道一：【記憶體預測鎖定】(解決 Demo 與 JSON 紀錄不同步) ===
    // 不等引擎 ChangeTeam 生效，直接從我們剛剛洗牌好的 activePlayers 陣列前半段與後半段抓人名！
    var predictedTCaptain = activePlayers.ElementAtOrDefault(0);
    var predictedCTCaptain = activePlayers.ElementAtOrDefault(half);

    string tName = (predictedTCaptain != null && !string.IsNullOrEmpty(predictedTCaptain.PlayerName)) 
                    ? $"team_{predictedTCaptain.PlayerName}" 
                    : "team_Terrorists";

    string ctName = (predictedCTCaptain != null && !string.IsNullOrEmpty(predictedCTCaptain.PlayerName)) 
                    ? $"team_{predictedCTCaptain.PlayerName}" 
                    : "team_CounterTerrorists";

    // 立即在第 0 秒寫入！確保 MatchZy 核心初始化與 Demo 錄製抓到這組名字
    Server.ExecuteCommand($"mp_teamname_1 \"{tName}\"");
    Server.ExecuteCommand($"mp_teamname_2 \"{ctName}\"");

    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Lime}隨 機 分 隊 完 成！已預先鎖定官方隊名。");
    Log($"[Shuffle-PreLock] 第0秒預先同步隊名: T={tName}, CT={ctName}");

    isShufflePending = false;
    isShuffleNameLocked = true; // 開啟標記，交給刀局第 5 秒做最終校正
}
    } // 結束 public partial class MatchZy
} // 結束 namespace MatchZy
