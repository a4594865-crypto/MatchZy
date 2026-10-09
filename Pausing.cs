using System.IO;
using System;
using System.Collections.Generic;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Cvars;

namespace MatchZy;

public partial class MatchZy
{
    // ==========================================
    // ▼ 動態設定檔讀取區 (完美連動 config.cfg) ▼
    // ==========================================
    private int GetPauseConfig(string cvarName, int defaultValue)
    {
        try 
        {
            var cvar = ConVar.Find(cvarName);
            if (cvar is not null) return cvar.GetPrimitiveValue<int>();

            string cfgPath = Path.Join(Server.GameDirectory + "/csgo/cfg/MatchZy/config.cfg");
            if (File.Exists(cfgPath))
            {
                string? val = GetConvarValueFromCFGFile(cfgPath, cvarName);
                if (val is not null && int.TryParse(val, out int res)) return res;
            }
        } 
        catch { }
        return defaultValue;
    }

    public int TechPauseDuration => GetPauseConfig("matchzy_tech_pause_duration", 300);
    public int MaxTechPauses => GetPauseConfig("matchzy_max_tech_pauses_allowed", 1);
    public int TacPauseDuration => GetPauseConfig("matchzy_tac_pause_duration", 90);
    public int MaxTacPauses => GetPauseConfig("matchzy_max_tac_pauses_allowed", 3);

    // ▼ 自動暫停設定 ▼
    public int AutoPauseMinPlayers => GetPauseConfig("matchzy_autopause_minplayers", 5);
    public int AutoPauseResumeDelay => GetPauseConfig("matchzy_autopause_resume_delay", 3);
    
    // 管理員可熱切換的自動暫停開關
    public bool runtimeAutoPauseEnabled = true;
    public bool hasRuntimeAutoPauseChanged = false;
    public bool AutoPauseEnabled => hasRuntimeAutoPauseChanged ? runtimeAutoPauseEnabled : GetPauseConfig("matchzy_autopause_enabled", 1) == 1;


    // ==========================================
    // ▼ 暫停次數與計時器全域變數區 ▼
    // ==========================================
    public Dictionary<string, int> techPausesUsed = new() { { "matchzyTeam1", 0 }, { "matchzyTeam2", 0 } };
    public Dictionary<string, int> tacPausesUsed = new() { { "matchzyTeam1", 0 }, { "matchzyTeam2", 0 } };

    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;
    public CounterStrikeSharp.API.Modules.Timers.Timer? tacPauseAutoUnpauseTimer = null;
    
    public int techPauseElapsedTime = 0;
    public int tacPauseElapsedTime = 0;

    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // ▼ 自動暫停背景計時器與標記 ▼
    public CounterStrikeSharp.API.Modules.Timers.Timer? autoPauseMainTimer = null;
    public bool autoPauseLimitAnnounced = false;
    public bool isAutoTriggeredTacPause = false;
    public int autoResumeCountdown = -1;
    public int autoPausePeakHumans = 0;
    
    // ▼ 防止無限暫停迴圈：記憶「雙方同意以少打多」的動態門檻 ▼
    public bool autoPauseShortAccepted = false;
    public int acceptedCtCount = 5;
    public int acceptedTCount = 5;

    // ==========================================
    // ▼ 獨立雙方解除同意紀錄 (.unt 與 .unp 專用) ▼
    // ==========================================
    public Dictionary<string, bool> untData = new() { { "ct", false }, { "t", false } };
    public Dictionary<string, bool> unpData = new() { { "ct", false }, { "t", false } };


    // ==========================================
    // 技術暫停 (.tech) 核心方法
    // ==========================================
    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (!isMatchLive) return;

        if (tacPauseAutoUnpauseTimer is not null)
        {
            if (player is not null) PrintToPlayerChat(player, $"  正 處 於【 {ChatColors.Green}暫 停 狀 態{ChatColors.Default} 】中，無 法 啟 用 技 術 暫 停");
            return;
        }

        if (techPauseAutoUnpauseTimer is not null)
        {
            if (player is not null) ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            return;
        }

        CCSGameRules? gameRules = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity is { GameRules: not null } proxy)
            {
                gameRules = proxy.GameRules;
                break;
            }
        }

        if (gameRules is { FreezePeriod: false })
        {
            if (player is not null) 
            {
                PrintToPlayerChat(player, $" {ChatColors.Orange}回合已開始，指令無法使用");
                player.PrintToCenter(" 回合已開始，指令無法使用 ");
            }
            return;
        }

        bool isOfficialTacActive = gameRules is not null && (gameRules.TerroristTimeOutActive || gameRules.CTTimeOutActive);

        if (isPaused || isOfficialTacActive)
        {
            if (player is not null) PrintToPlayerChat(player, $" 正 處 於【 {ChatColors.Green}暫 停 狀 態{ChatColors.Default} 】中，無 法 啟 用 技 術 暫 停");
            return; 
        }

        if (player is null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        if (IsHalfTimePhase()) return;
        if (IsPostGamePhase()) return;
        
        if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) return;

        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        string teamKey = playerMatchTeam == matchzyTeam1 ? "matchzyTeam1" : (playerMatchTeam == matchzyTeam2 ? "matchzyTeam2" : "");
        if (string.IsNullOrEmpty(teamKey)) return;

        string currentTeamName = playerMatchTeam.teamName;
        int maxLimit = MaxTechPauses;
        int durationLimit = TechPauseDuration;

        if (!techPausesUsed.ContainsKey(teamKey)) techPausesUsed[teamKey] = 0;

        if (techPausesUsed[teamKey] >= maxLimit)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 您 的 {ChatColors.Green}技 術 暫 停 {ChatColors.Default}次 數 已 用 完");
            return;
        }

        string sideName = (player.Team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        
        techPausesUsed[teamKey]++;
        int remainingCount = maxLimit - techPausesUsed[teamKey];
        int currentPauseUsed = techPausesUsed[teamKey]; 

        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        untData["t"] = false;
        untData["ct"] = false;

        int maxM = durationLimit / 60;
        int maxS = durationLimit % 60;
        string maxTimeString = maxM > 0 ? $"{maxM}分{maxS:D2}秒" : $"{maxS}秒";

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 開 啟 技 術 暫 停。剩 餘 次 數：{ChatColors.Green}{remainingCount} {ChatColors.Default}次");
        PrintToAllChat($" 暫 停 在 \u0004 {durationLimit}秒 \u0001 自 動 解 除，或 雙 方 輸 入 {ChatColors.Orange}.unt\u0001 解 除");

        techPauseElapsedTime = 0;

        techPauseAutoUnpauseTimer = AddTimer(1.0f, () =>
        {
            if (!isPaused)
            {
                KillTechPauseTimer();
                return;
            }

            int remaining = durationLimit - techPauseElapsedTime;

            if (techPauseElapsedTime >= durationLimit)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                untData["ct"] = false;
                untData["t"] = false;
                
                CheckAndAcceptShortHanded(); // 嚴格防禦：超時解開時記錄動態人數

                PrintToAllChat($" 技 術 暫 停 已達\u0004{durationLimit}秒 \u0001上 限，系 統 自 動 解 除 暫 停");
                
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                    {
                        p.PrintToCenter(" 技 術 暫 停 已 結 束 ");
                    }
                }
                KillTechPauseTimer();
            }
            else
            {
                int m = remaining / 60;
                int s = remaining % 60;
                string timeString = m > 0 ? $"{m}分{s:D2}秒" : $"{s}秒";

                string prompt = "";
                if (untData["t"] && !untData["ct"]) prompt = "\n恐怖份子想解除，請輸入 .unt 同意";
                else if (!untData["t"] && untData["ct"]) prompt = "\n反恐小組想解除，請輸入 .unt 同意";

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                    {
                        p.PrintToCenter($"{sideName}技術暫停 {timeString} ( {currentPauseUsed} / {maxLimit} ){prompt}");
                    }
                }
                techPauseElapsedTime += 1;
            }
        }, TimerFlags.REPEAT);
    }


    // ==========================================
    // 戰術暫停 (.P / .tac) 核心方法
    // ==========================================
    public void TacPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (!isMatchLive) return;

        if (techPauseAutoUnpauseTimer is not null)
        {
            if (player is not null) PrintToPlayerChat(player, $" 正處於【 {ChatColors.Green}暫 停 狀 態{ChatColors.Default} 】中，無 法 啟 用 戰 術 暫 停");
            return;
        }

        if (tacPauseAutoUnpauseTimer is not null)
        {
            if (player is not null) PrintToPlayerChat(player, $" 已 經 在{ChatColors.Green}戰術暫停{ChatColors.Default} 中");
            return;
        }

        CCSGameRules? gameRules = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity is { GameRules: not null } proxy)
            {
                gameRules = proxy.GameRules;
                break;
            }
        }
        
        if (gameRules is { FreezePeriod: false })
        {
            if (player is not null) 
            {
                PrintToPlayerChat(player, $" {ChatColors.Orange}回合已開始，指令無法使用");
                player.PrintToCenter(" 回合已開始，指令無法使用 ");
            }
            return;
        }

        bool isOfficialTacActive = gameRules is not null && (gameRules.TerroristTimeOutActive || gameRules.CTTimeOutActive);

        if (isPaused || isOfficialTacActive)
        {
            if (player is not null) PrintToPlayerChat(player, $" 正 處 於【 {ChatColors.Green}暫 停 狀 態{ChatColors.Default} 】中，無 法 啟 用 戰 術 暫 停");
            return; 
        }

        if (player is null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        if (IsHalfTimePhase()) return;
        if (IsPostGamePhase()) return;
        
        if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) return;

        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        string teamKey = playerMatchTeam == matchzyTeam1 ? "matchzyTeam1" : (playerMatchTeam == matchzyTeam2 ? "matchzyTeam2" : "");
        if (string.IsNullOrEmpty(teamKey)) return;

        string currentTeamName = playerMatchTeam.teamName;
        int maxLimit = MaxTacPauses;
        int durationLimit = TacPauseDuration;

        if (!tacPausesUsed.ContainsKey(teamKey)) tacPausesUsed[teamKey] = 0;

        if (tacPausesUsed[teamKey] >= maxLimit)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 您 的 {ChatColors.Green}戰 術 暫 停 {ChatColors.Default}次 數 已 用 完");
            return;
        }

        string sideName = (player.Team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        
        tacPausesUsed[teamKey]++;
        int remainingCount = maxLimit - tacPausesUsed[teamKey];
        int currentPauseUsed = tacPausesUsed[teamKey]; 

        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpData["t"] = false;
        unpData["ct"] = false;

        int maxM = durationLimit / 60;
        int maxS = durationLimit % 60;
        string maxTimeString = maxM > 0 ? $"{maxM}分{maxS:D2}秒" : $"{maxS}秒";

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 開 啟 戰 術 暫 停。剩 餘 次 數：{ChatColors.Green}{remainingCount} {ChatColors.Default}次");
        PrintToAllChat($" 暫 停 在 \u0004{durationLimit}秒\u0001 自 動 解 除，或 雙 方 輸 入 {ChatColors.Orange}.unp\u0001 解 除");

        tacPauseElapsedTime = 0;
        isAutoTriggeredTacPause = false; // 標記為「玩家手動觸發」，取消自動連回機制

        tacPauseAutoUnpauseTimer = AddTimer(1.0f, () =>
        {
            if (!isPaused)
            {
                KillTacPauseTimer();
                return;
            }

            int remaining = durationLimit - tacPauseElapsedTime;

            if (tacPauseElapsedTime >= durationLimit)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpData["ct"] = false;
                unpData["t"] = false;

                // 若手動戰術暫停結束時仍未滿人，不主動紀錄以少打多，讓自動暫停接手保護比賽
                
                PrintToAllChat($" 戰 術 暫 停 已達\u0004 {durationLimit}秒 \u0001上 限，系 統 自 動 解 除 暫 停");
                
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                    {
                        p.PrintToCenter(" 戰 術 暫 停 已 結 束 ");
                    }
                }
                KillTacPauseTimer();
            }
            else
            {
                int m = remaining / 60;
                int s = remaining % 60;
                string timeString = m > 0 ? $"{m}分{s:D2}秒" : $"{s}秒";

                string prompt = "";
                if (unpData["t"] && !unpData["ct"]) prompt = "\n恐怖份子想解除，請輸入 .unp 同意";
                else if (!unpData["t"] && unpData["ct"]) prompt = "\n反恐小組想解除，請輸入 .unp 同意";

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                    {
                        p.PrintToCenter($"{sideName} 暫停 {timeString} ( {currentPauseUsed} / {maxLimit} ){prompt}");
                    }
                }
                tacPauseElapsedTime += 1;
            }
        }, TimerFlags.REPEAT);
    }


    // ==========================================
    // 解除指令攔截與同意機制 (.unt 與 .unp)
    // ==========================================

    // ▼ 核心記憶函數：雙方同意解除時，自動記錄以少打多狀態 ▼
    public void CheckAndAcceptShortHanded()
    {
        int minP = AutoPauseMinPlayers;
        int currentCt = GetTeamPlayerCount(CsTeam.CounterTerrorist);
        int currentT = GetTeamPlayerCount(CsTeam.Terrorist);

        if (currentCt < minP || currentT < minP)
        {
            autoPauseShortAccepted = true;
            acceptedCtCount = currentCt;
            acceptedTCount = currentT;
        }
    }

    public void HandleUntCommand(CCSPlayerController player)
    {
        if (tacPauseAutoUnpauseTimer is not null)
        {
            PrintToPlayerChat(player, $" 目 前 為 {ChatColors.Green}戰術暫停{ChatColors.Default}，請 雙 方 輸 入 {ChatColors.Orange}.unp{ChatColors.Default} 來 解 除");
            return;
        }

        if (techPauseAutoUnpauseTimer is null) return; 

        string team = player.TeamNum == 2 ? "t" : "ct";
        string teamName = player.TeamNum == 2 ? "恐怖份子" : "反恐小組";
        string opponentTeamName = player.TeamNum == 2 ? "反恐小組" : "恐怖份子"; 

        if (!untData[team])
        {
            untData[team] = true;
            
            if (untData["t"] && untData["ct"])
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                
                CheckAndAcceptShortHanded(); // 雙方同意解鎖技術暫停時，寫入動態門檻

                PrintToAllChat($" {ChatColors.Orange}雙 方 皆 已 同 意，已 解 除 技 術 暫 停");
                KillTechPauseTimer();
            }
            else
            {
                PrintToAllChat($" {ChatColors.Green}{teamName}{ChatColors.Default} 想解除暫停 {ChatColors.Green}{opponentTeamName}{ChatColors.Default} 請輸入 {ChatColors.Orange}.unt{ChatColors.Default} 來同意");
            }
        }
    }

    public void HandleUnpCommand(CCSPlayerController player)
    {
        if (techPauseAutoUnpauseTimer is not null)
        {
            PrintToPlayerChat(player, $" 目 前 為 {ChatColors.Green}技術暫停{ChatColors.Default}，請 雙 方 輸 入 {ChatColors.Orange}.unt{ChatColors.Default} 來 解 除");
            return;
        }

        if (tacPauseAutoUnpauseTimer is null) return; 

        string team = player.TeamNum == 2 ? "t" : "ct";
        string teamName = player.TeamNum == 2 ? "恐怖份子" : "反恐小組";
        string opponentTeamName = player.TeamNum == 2 ? "反恐小組" : "恐怖份子"; 

        if (!unpData[team])
        {
            unpData[team] = true;
            
            if (unpData["t"] && unpData["ct"])
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                
                CheckAndAcceptShortHanded(); // 無論是手動還是自動暫停，只要雙方同意解鎖，一律寫入動態門檻

                PrintToAllChat($" {ChatColors.Orange}雙 方 皆 已 同 意，已 解 除 戰 術 暫 停");
                KillTacPauseTimer();
            }
            else
            {
                PrintToAllChat($" {ChatColors.Green}{teamName}{ChatColors.Default} 想解除暫停 {ChatColors.Green}{opponentTeamName}{ChatColors.Default} 請輸入 {ChatColors.Orange}.unp{ChatColors.Default} 來同意");
            }
        }
    }

    // ==========================================
    // 計時器銷毀與次數重置區
    // ==========================================

    public void KillTechPauseTimer()
    {
        if (techPauseAutoUnpauseTimer is not null)
        {
            techPauseAutoUnpauseTimer.Kill();
            techPauseAutoUnpauseTimer = null;
        }
        techPauseElapsedTime = 0;
        untData["t"] = false;
        untData["ct"] = false;
    }

    public void KillTacPauseTimer()
    {
        if (tacPauseAutoUnpauseTimer is not null)
        {
            tacPauseAutoUnpauseTimer.Kill();
            tacPauseAutoUnpauseTimer = null;
        }
        tacPauseElapsedTime = 0;
        unpData["t"] = false;
        unpData["ct"] = false;
        isAutoTriggeredTacPause = false;
        autoResumeCountdown = -1;
    }

    public void ResetTechPauseCount()
    {
        techPausesUsed["matchzyTeam1"] = 0;
        techPausesUsed["matchzyTeam2"] = 0;
        KillTechPauseTimer();
    }

    public void ResetTacPauseCount()
    {
        tacPausesUsed["matchzyTeam1"] = 0;
        tacPausesUsed["matchzyTeam2"] = 0;
        KillTacPauseTimer();
    }

    // ==========================================
    // ▼ 極致融合：斷線自動扣除戰術暫停系統 ▼
    // ==========================================

    [ConsoleCommand("css_autopause", "Toggle Auto Pause feature on/off")]
    [ConsoleCommand(".autopause", "Toggle Auto Pause feature on/off")]
    public void OnAutoPauseCommand(CCSPlayerController? player, CommandInfo? command)
    {
        if (player != null && !IsPlayerAdmin(player)) return;

        runtimeAutoPauseEnabled = !AutoPauseEnabled;
        hasRuntimeAutoPauseChanged = true;

        string status = runtimeAutoPauseEnabled ? $"{ChatColors.Green}啟用{ChatColors.Default}" : $"{ChatColors.Red}停用{ChatColors.Default}";
        PrintToAllChat($" {ChatColors.Gold}[管理員]{ChatColors.Default} 已將 斷線自動暫停 {status}");
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundStartAutoPause(EventRoundStart @event, GameEventInfo info)
    {
        if (autoPauseMainTimer == null)
        {
            StartAutoPauseCheck();
        }
        return HookResult.Continue;
    }

    public bool IsAutoPauseActive()
    {
        int totalPlayers = GetTeamPlayerCount(CsTeam.CounterTerrorist) + GetTeamPlayerCount(CsTeam.Terrorist);
        
        if (totalPlayers > autoPausePeakHumans)
        {
            autoPausePeakHumans = totalPlayers;
        }
        
        return autoPausePeakHumans >= (2 * Math.Max(1, AutoPauseMinPlayers));
    }

    public void StartAutoPauseCheck()
    {
        StopAutoPauseCheck();
        autoPausePeakHumans = 0;

        autoPauseMainTimer = AddTimer(10.0f, () =>
        {
            if (!isMatchLive) return;
            if (!AutoPauseEnabled) return;
            if (IsHalfTimePhase() || IsPostGamePhase()) return;

            if (!IsAutoPauseActive()) return; 

            int minP = AutoPauseMinPlayers;
            int ctCount = GetTeamPlayerCount(CsTeam.CounterTerrorist);
            int tCount = GetTeamPlayerCount(CsTeam.Terrorist);

            // 若人數已滿，完全解除「以少打多」的動態記憶
            if (autoPauseShortAccepted && ctCount >= minP && tCount >= minP)
            {
                autoPauseShortAccepted = false;
            }

            int targetCt = autoPauseShortAccepted ? acceptedCtCount : minP;
            int targetT = autoPauseShortAccepted ? acceptedTCount : minP;

            if (ctCount >= targetCt && tCount >= targetT)
            {
                autoPauseLimitAnnounced = false; 
                return;
            }

            if (isPaused) return;

            CCSGameRules? gameRules = null;
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
            {
                if (entity is { GameRules: not null } proxy)
                {
                    gameRules = proxy.GameRules;
                    break;
                }
            }
            
            if (gameRules is { FreezePeriod: false }) return;

            bool isOfficialTacActive = gameRules is not null && (gameRules.TerroristTimeOutActive || gameRules.CTTimeOutActive);
            if (isOfficialTacActive) return;

            if (ctCount < targetCt) AutoTriggerTacPause(CsTeam.CounterTerrorist);
            else if (tCount < targetT) AutoTriggerTacPause(CsTeam.Terrorist);

        }, TimerFlags.REPEAT);
    }

    public void StopAutoPauseCheck()
    {
        if (autoPauseMainTimer is not null)
        {
            autoPauseMainTimer.Kill();
            autoPauseMainTimer = null;
        }
        autoPauseLimitAnnounced = false;
        autoPausePeakHumans = 0;
        autoPauseShortAccepted = false;
    }

    private void AutoTriggerTacPause(CsTeam team)
    {
        if (tacPauseAutoUnpauseTimer is not null || techPauseAutoUnpauseTimer is not null) return;
        
        Team matchTeam = (team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        string teamKey = matchTeam == matchzyTeam1 ? "matchzyTeam1" : (matchTeam == matchzyTeam2 ? "matchzyTeam2" : "");
        if (string.IsNullOrEmpty(teamKey)) return;

        string currentTeamName = matchTeam.teamName;
        int maxLimit = MaxTacPauses;
        int durationLimit = TacPauseDuration;

        if (!tacPausesUsed.ContainsKey(teamKey)) tacPausesUsed[teamKey] = 0;

        if (tacPausesUsed[teamKey] >= maxLimit)
        {
            if (!autoPauseLimitAnnounced)
            {
                PrintToAllChat($" {ChatColors.Red}玩家斷線{ChatColors.Default} 但 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 的戰術暫停已用完，無法自動暫停。");
                autoPauseLimitAnnounced = true;
            }
            return;
        }

        autoPauseLimitAnnounced = false; 
        string sideName = (team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        
        tacPausesUsed[teamKey]++;
        int remainingCount = maxLimit - tacPausesUsed[teamKey];
        int currentPauseUsed = tacPausesUsed[teamKey]; 

        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpData["t"] = false;
        unpData["ct"] = false;

        PrintToAllChat($" {ChatColors.Red}玩家斷線{ChatColors.Default} 系 統 為 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 戰 術 暫 停。剩 餘 次 數：{ChatColors.Green}{remainingCount} {ChatColors.Default}次");
        PrintToAllChat($" 暫 停 在 \u0004{durationLimit}秒\u0001 自 動 解 除，或 雙 方 輸 入 {ChatColors.Orange}.unp\u0001 解 除");

        tacPauseElapsedTime = 0;
        isAutoTriggeredTacPause = true; 
        autoResumeCountdown = -1;

        tacPauseAutoUnpauseTimer = AddTimer(1.0f, () =>
        {
            if (!isPaused)
            {
                KillTacPauseTimer();
                return;
            }

            int minP = AutoPauseMinPlayers;
            int ctCount = GetTeamPlayerCount(CsTeam.CounterTerrorist);
            int tCount = GetTeamPlayerCount(CsTeam.Terrorist);

            // ▼ 修正漏洞一：連回倒數使用「動態門檻 (target)」判斷，而非死板的 5 人滿血
            int targetCt = autoPauseShortAccepted ? acceptedCtCount : minP;
            int targetT = autoPauseShortAccepted ? acceptedTCount : minP;

            if (isAutoTriggeredTacPause && ctCount >= targetCt && tCount >= targetT)
            {
                if (autoResumeCountdown == -1) autoResumeCountdown = AutoPauseResumeDelay;

                if (autoResumeCountdown > 0)
                {
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                        {
                            p.PrintToCenter($"玩 家 已 全 數 連 回！\n將 在 {autoResumeCountdown} 秒 後 解 除 暫 停");
                        }
                    }
                    autoResumeCountdown--;
                    return; 
                }
                else
                {
                    Server.ExecuteCommand("mp_unpause_match;");
                    isPaused = false;
                    unpData["ct"] = false;
                    unpData["t"] = false;
                    isAutoTriggeredTacPause = false;
                    PrintToAllChat($" {ChatColors.Green}玩 家 已 連 回，自 動 解 除 暫 停！");
                    
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                            p.PrintToCenter(" 自 動 暫 停 結 束 ");
                    }
                    KillTacPauseTimer();
                    return;
                }
            }
            else
            {
                autoResumeCountdown = -1; 
            }

            int remaining = durationLimit - tacPauseElapsedTime;

            if (tacPauseElapsedTime >= durationLimit)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpData["ct"] = false;
                unpData["t"] = false;
                
                CheckAndAcceptShortHanded(); // 嚴格防禦：自動暫停超時強制解開時，記錄動態人數

                PrintToAllChat($" {ChatColors.Orange}戰 術 暫 停 已達 \u0004{durationLimit}秒 {ChatColors.Orange}上 限，系 統 自 動 解 除 暫 停");
                
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                    {
                        p.PrintToCenter(" 戰 術 暫 停 已 結 束 ");
                    }
                }
                KillTacPauseTimer();
            }
            else
            {
                int m = remaining / 60;
                int s = remaining % 60;
                string timeString = m > 0 ? $"{m}分{s:D2}秒" : $"{s}秒";

                string prompt = "";
                if (unpData["t"] && !unpData["ct"]) prompt = "\n恐怖份子想解除，請輸入 .unp 同意";
                else if (!unpData["t"] && unpData["ct"]) prompt = "\n反恐小組想解除，請輸入 .unp 同意";

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                    {
                        p.PrintToCenter($"{sideName} 暫停 {timeString} ( {currentPauseUsed} / {maxLimit} ){prompt}");
                    }
                }
                tacPauseElapsedTime += 1;
            }
        }, TimerFlags.REPEAT);
    }

    private bool IsPlayerCoachForAutoPause(CCSPlayerController p)
    {
        try {
            return (matchzyTeam1 != null && matchzyTeam1.coach.Contains(p)) || 
                   (matchzyTeam2 != null && matchzyTeam2.coach.Contains(p));
        } catch { return false; }
    }

    private int GetTeamPlayerCount(CsTeam team)
    {
        int count = 0;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is { IsValid: true, IsBot: false } && p.Team == team && !IsPlayerCoachForAutoPause(p))
            {
                count++;
            }
        }
        return count;
    }
}
