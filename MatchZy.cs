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

// --- A. 倒數期間斷線：中止倒數（終極優雅：直接呼叫 MatchZy 核心函數，0 雜訊免管理員） ---
if (matchStartCountdownTimer != null)
{
    // 1. 定義您指定的專屬廣播訊息
    string disconnectMsg = $"{chatPrefix} {ChatColors.White}玩 家 {ChatColors.Green}{player.PlayerName} {ChatColors.White}斷 開 連 線 請 重 新 輸 入 {ChatColors.LightRed}.R {ChatColors.White}準 備";

    // 2. 立即停止計時器並關閉所有外掛倒數狀態
    matchStartCountdownTimer.Kill();
    matchStartCountdownTimer = null;
    isCountdownActive = false;
    matchStarted = false;

    // 3. 物理重置我們自訂的字典與洗牌預約
    playerReadyStatus.Clear(); 
    isShufflePending = false; 

    // 【終極神操作】：不透過 Server.ExecuteCommand 了！
    // 直接在程式碼裡呼叫 MatchZy 官方的重啟函數，傳入 null 讓它在後台默默重置。
    // 這樣做 100% 擁有最高權限、免管理員、而且聊天框絕對不會噴出「1秒重開」的官方雜訊！
    OnRestartMatchCommand(null, null); 

    // 4. 發送您指定的訊息到聊天框
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
        player.PrintToChat($"{chatPrefix} 倒 數 期 間 禁 止 切 換 隊 伍 或 觀 戰");
        
        // 返回 HookResult.Stop 就能直接吃掉這個指令，讓玩家留在原地
        return HookResult.Stop; 
    }

   // 刀局與正賽管制期間 // --- 以下為非倒數期間的正常比賽邏輯 ---
    
    // 1. 如果是熱身階段（且沒在倒數），允許自由換隊、自由去觀戰
    if (isWarmup) return HookResult.Continue;

    // 2. 比賽正式開始後（包含刀局與正賽）
    if (matchStarted)
    {
        // 【關鍵差別點一：刀局期間全面封鎖】
        if (isKnifeRound) 
        {
            // 在刀局期間，不管你是要換隊（2, 3）還是去觀戰（1），只要你在場上（CT/T），一律禁止！
            byte currentTeam = player.TeamNum;
            if ((currentTeam == 2 || currentTeam == 3) && (targetTeam == "1" || targetTeam == "2" || targetTeam == "3"))
            {
                player.PrintToChat($"{chatPrefix} 刀 局 期 間，禁 止 互 換 隊 伍");
                return HookResult.Stop; 
            }
        }

        // 【關鍵差別點二：LIVE正賽期間（非刀局）才放行觀戰】
        // 允許去觀戰 (targetTeam "1" 是觀戰)
        if (targetTeam == "1") return HookResult.Continue;

        // 5. 正式局（LIVE後）限制：禁止 T/CT 互換 
        byte playerTeam = player.TeamNum;
        if ((targetTeam == "2" || targetTeam == "3") && (playerTeam == 2 || playerTeam == 3))
        {
            player.PrintToChat($"{chatPrefix} 比 賽 已 開 始，禁 止 互 換 隊 伍");
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
                    player.PrintToChat($"{chatPrefix} 正 式 比 賽 期 間，內 建 投 票 功 能 已 被 禁 用");
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
    // 判斷是否為最後一個準備的人（例如 10 人房的第 9 人）
    if (!matchStarted && readyAvailable && GetReadyPlayersCount() >= (minimumReadyRequired - 1)) {
        
        // --- 新增：在正式倒數開始前，如果玩家有預約洗牌，立即執行 ---
        if (isShufflePending) 
        {
            ExecuteShuffleLogic(); // 執行洗牌
            UpdatePlayersMap();    // 強制更新玩家隊伍緩存
        }
        // -------------------------------------------------------

        // 1. 執行原本的準備邏輯
        OnPlayerReady(Utilities.GetPlayerFromUserid(NativeAPI.GetUseridFromIndex(@event.Userid + 1)), null);
        
        // 2. 開啟靜音開關
        AddTimer(0.2f, () => {
            isCountdownActive = true; 
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

       // --- 核心修正：重新定義人數統計邏輯，完全排除觀戰者與離線玩家 ---
        public int GetReadyPlayersCount()
        {
            int count = 0;
            foreach (var entry in playerReadyStatus)
            {
                if (entry.Value == true)
                {
                    var player = Utilities.GetPlayerFromUserid(entry.Key);
                    // 超強防護網：必須「IsValid 且在線 (PlayerConnected) 且在 T/CT 隊上」才算人數
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
[CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)] // 強制宣告客戶端與伺服器皆可執行
public void OnShuffleCommand(CCSPlayerController? player, CommandInfo command) {
    // 1. 權限檢查：如果是玩家發出的，檢查管理員權限；如果是伺服器 (player == null)，直接通過
    if (player != null && !IsPlayerAdmin(player)) {
        return;
    }

    if (isMatchSetup) { 
        ReplyToUserCommand(player, "比 賽 已 開 始，無 法 隨 機 分 隊");
        return;
    }

    // 【熱身階段檢查】防線
    if (!isWarmup) {
        ReplyToUserCommand(player, $"比 賽 已 開 始，無 法 隨 機 分 隊");
        return;
    }

    isShufflePending = true;
    
    // 2. 執行全服廣播
    Server.PrintToChatAll($"{chatPrefix} 管 理 員「 {ChatColors.Lime}已 開 啟 隨 機 隊 伍 分 配 {ChatColors.Default}」 將 自 動 洗 牌");
    
    // 3. 確保伺服器後台黑視窗有回饋
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
        public void ExecuteShuffleLogic() 
{
    // 1. 安全檢查：如果沒有預約洗牌，則直接跳出
    if (!isShufflePending) return;

    // 2. 獲取當前所有在場上的選手（排除機器人與觀戰者）
    List<CCSPlayerController> activePlayers = Utilities.GetPlayers()
        .Where(p => p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
        .ToList();

    // 3. 人數檢查：至少需要 2 人才能洗牌
    if (activePlayers.Count < 2) 
    {
        Log("[Shuffle] 選手人數不足，無法執行隨機分隊。");
        isShufflePending = false; // 失敗也要重置標記，避免狀態殘留
        return;
    }

    // 4. Fisher-Yates 洗牌演算法
    Random rng = new();
    int n = activePlayers.Count;
    while (n > 1) 
    {
        n--;
        int k = rng.Next(n + 1);
        (activePlayers[k], activePlayers[n]) = (activePlayers[n], activePlayers[k]);
    }

    // 5. 分配隊伍
    int half = activePlayers.Count / 2;
    for (int i = 0; i < activePlayers.Count; i++) 
    {
        // 為了確保 ChangeTeam 成功，建議在下一幀或立即執行時確保狀態一致
        if (i < half) 
        {
            activePlayers[i].ChangeTeam(CsTeam.Terrorist);
        }
        else 
        {
            activePlayers[i].ChangeTeam(CsTeam.CounterTerrorist);
        }
    }

    // 6. 輸出訊息與重置標記
    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Lime}隨 機 分 隊 完 成！隊 伍 已 鎖 定。");
    Log($"[Shuffle] 已完成隨機分隊，共分配 {activePlayers.Count} 名玩家。");
    
    isShufflePending = false;
}
// 這是你專屬的超級過濾網，專門在開賽微秒間超車使用，跟原廠 private 不衝突
        public void ForceUpdateOnlinePlayersMap()
        {
            try
            {
                playerData.Clear();
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player != null && 
                        player.IsValid && 
                        !player.IsBot && 
                        player.Connected == PlayerConnectedState.Connected) 
                    {
                        int userId = (int)(player.UserId ?? -1);
                        if (userId != -1) playerData[userId] = player;
                    }
                }
                
                // 立即同步清理準備名單，拔除斷線殘影
                var offlineUserIds = playerReadyStatus.Keys.Where(id => !playerData.ContainsKey(id)).ToList();
                foreach (var id in offlineUserIds)
                {
                    playerReadyStatus.Remove(id);
                }
            }
            catch (Exception) { /* 靜默忽略錯誤 */ }
        }

    } // 結束 public partial class MatchZy (整個檔案倒數第 2 個大括號)
} // 結束 namespace MatchZy (整個檔案最後 1 個大括號)
