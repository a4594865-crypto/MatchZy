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

    public int AutoPauseMinPlayers => GetPauseConfig("matchzy_autopause_minplayers", 5);
    public int AutoPauseResumeDelay => GetPauseConfig("matchzy_autopause_resume_delay", 3);
    
    public bool runtimeAutoPauseEnabled = true;
    public bool hasRuntimeAutoPauseChanged = false;
    public bool AutoPauseEnabled => hasRuntimeAutoPauseChanged ? runtimeAutoPauseEnabled : GetPauseConfig("matchzy_autopause_enabled", 1) == 1;

    // ==========================================
    // ▼ 暫停次數與計時器全域變數區 ▼
    // ==========================================
    public Dictionary<string, int> techPausesUsed = new() { { "matchzyTeam1", 0 }, { "matchzyTeam2", 0 } };
    public Dictionary<string, int> tacPausesUsed = new() { { "matchzyTeam1", 0 }, { "matchzyTeam2", 0 } };
    
    public int lastTrackedOT = 0;
    public Dictionary<string, int> otTacPausesUsed = new() { { "matchzyTeam1", 0 }, { "matchzyTeam2", 0 } };

    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;
    public CounterStrikeSharp.API.Modules.Timers.Timer? tacPauseAutoUnpauseTimer = null;
    
    public int techPauseElapsedTime = 0;
    public int tacPauseElapsedTime = 0;

    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    public CounterStrikeSharp.API.Modules.Timers.Timer? autoPauseMainTimer = null;
    public bool autoPauseLimitAnnounced = false;
    public bool isAutoTriggeredTacPause = false;
    public int autoResumeCountdown = -1;
    public int autoPausePeakHumans = 0;
    
    public bool autoPauseShortAccepted = false;
    public int acceptedCtCount = 5;
    public int acceptedTCount = 5;

    public Dictionary<string, bool> untData = new() { { "ct", false }, { "t", false } };
    public Dictionary<string, bool> unpData = new() { { "ct", false }, { "t", false } };

    // ==========================================
    // ▼ 跨版本安全屬性檢查器 (已加入局數推算終極防護) ▼
    // ==========================================
    private bool IsOvertimePlayingSafe(CCSGameRules? gameRules)
    {
        if (gameRules == null) return false;
        string val = gameRules.OvertimePlaying.ToString() ?? "";
        if (val == "True" || val == "1") return true;

        // 加入底層局數推算，防止 CS2 引擎在換邊瞬間標籤延遲
        int totalRounds = gameRules.TotalRoundsPlayed;
        int maxRounds = 24;
        try {
            var cvar = ConVar.Find("mp_maxrounds");
            if (cvar != null) maxRounds = cvar.GetPrimitiveValue<int>();
        } catch { }
        
        return totalRounds >= maxRounds;
    }

    private bool IsOfficialTacActiveSafe(CCSGameRules? gameRules)
    {
        if (gameRules == null) return false;
        string tVal = gameRules.TerroristTimeOutActive.ToString() ?? "";
        string ctVal = gameRules.CTTimeOutActive.ToString() ?? "";
        return tVal == "True" || tVal == "1" || ctVal == "True" || ctVal == "1";
    }

    private bool IsFreezePeriodSafe(CCSGameRules? gameRules)
    {
        if (gameRules == null) return false;
        string val = gameRules.FreezePeriod.ToString() ?? "";
        return val == "True" || val == "1";
    }

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

        if (gameRules != null && !IsFreezePeriodSafe(gameRules))
        {
            if (player is not null) 
            {
                PrintToPlayerChat(player, $" {ChatColors.Orange}回合已開始，指令無法使用");
                player.PrintToCenter(" 回合已開始，指令無法使用 ");
            }
            return;
        }

        if (isPaused || IsOfficialTacActiveSafe(gameRules))
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
        
        // 修正：從記憶體指標比較，改為字串比較，免疫 OT 物件複製 BUG
        string teamKey = "";
        if (matchzyTeam1 != null && playerMatchTeam.teamName == matchzyTeam1.teamName) teamKey = "matchzyTeam1";
        else if (matchzyTeam2 != null && playerMatchTeam.teamName == matchzyTeam2.teamName) teamKey = "matchzyTeam2";
        else teamKey = playerMatchTeam.teamName; 

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
                
                CheckAndAcceptShortHanded(); 

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
        
        if (gameRules != null && !IsFreezePeriodSafe(gameRules))
        {
            if (player is not null) 
            {
                PrintToPlayerChat(player, $" {ChatColors.Orange}回合已開始，指令無法使用");
                player.PrintToCenter(" 回合已開始，指令無法使用 ");
            }
            return;
        }

        if (isPaused || IsOfficialTacActiveSafe(gameRules))
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
        
        // 修正：從記憶體指標比較，改為字串比較，免疫 OT 物件複製 BUG
        string teamKey = "";
        if (matchzyTeam1 != null && playerMatchTeam.teamName == matchzyTeam1.teamName) teamKey = "matchzyTeam1";
        else if (matchzyTeam2 != null && playerMatchTeam.teamName == matchzyTeam2.teamName) teamKey = "matchzyTeam2";
        else teamKey = playerMatchTeam.teamName; 

        if (string.IsNullOrEmpty(teamKey)) return;

        string currentTeamName = playerMatchTeam.teamName;

        bool isOvertime = IsOvertimePlayingSafe(gameRules);
        int maxLimit = isOvertime ? GetPauseConfig("mp_team_timeout_ot_max", 1) : MaxTacPauses;
        int durationLimit = TacPauseDuration;

        Dictionary<string, int> targetPauseUsedDict = isOvertime ? otTacPausesUsed : tacPausesUsed;

        if (!targetPauseUsedDict.ContainsKey(teamKey)) targetPauseUsedDict[teamKey] = 0;

        if (targetPauseUsedDict[teamKey] >= maxLimit)
        {
            string phaseStr = isOvertime ? "加時賽 " : "";
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 您 的 {ChatColors.Green}{phaseStr}戰術暫停 {ChatColors.Default}次 數 已 用 完");
            return;
        }

        string sideName = (player.Team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        
        targetPauseUsedDict[teamKey]++;
        int remainingCount = maxLimit - targetPauseUsedDict[teamKey];
        int currentPauseUsed = targetPauseUsedDict[teamKey]; 

        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpData["t"] = false;
        unpData["ct"] = false;

        string phasePrefix = isOvertime ? "加時賽 " : "";
        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 開啟{phasePrefix}戰術暫停。剩餘次數：{ChatColors.Green}{remainingCount} {ChatColors.Default}次");
        PrintToAllChat($" 暫 停 在 \u0004{durationLimit}秒\u0001 自 動 解 除，或 雙 方 輸 入 {ChatColors.Orange}.unp\u0001 解 除");

        tacPauseElapsedTime = 0;
        isAutoTriggeredTacPause = false; 

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
                
                CheckAndAcceptShortHanded(); 

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
                
                CheckAndAcceptShortHanded(); 

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
        techPausesUsed.Clear();
        techPausesUsed["matchzyTeam1"] = 0;
        techPausesUsed["matchzyTeam2"] = 0;
        KillTechPauseTimer();
    }

    public void ResetTacPauseCount()
    {
        tacPausesUsed.Clear();
        tacPausesUsed["matchzyTeam1"] = 0;
        tacPausesUsed["matchzyTeam2"] = 0;
        
        otTacPausesUsed.Clear();
        otTacPausesUsed["matchzyTeam1"] = 0;
        otTacPausesUsed["matchzyTeam2"] = 0;
        lastTrackedOT = 0;
        
        KillTacPauseTimer();
    }

    // ==========================================
    // ▼ 自動暫停系統 ▼
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
        if (!isMatchLive)
        {
            ResetTechPauseCount();
            ResetTacPauseCount();
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

        if (IsOvertimePlayingSafe(gameRules))
        {
            int totalRounds = gameRules!.TotalRoundsPlayed;
            
            int maxRounds = 24;
            try {
                var cvar = ConVar.Find("mp_maxrounds");
                if (cvar != null) maxRounds = cvar.GetPrimitiveValue<int>();
            } catch { }

            int otMaxRounds = 6;
            try {
                var cvarOT = ConVar.Find("mp_overtime_maxrounds");
                if (cvarOT != null) otMaxRounds = cvarOT.GetPrimitiveValue<int>();
            } catch { }
            
            if (totalRounds >= maxRounds && otMaxRounds > 0)
            {
                int otRounds = totalRounds - maxRounds;
                int currentOT = (otRounds / otMaxRounds) + 1;
                
                if (currentOT != lastTrackedOT)
                {
                    lastTrackedOT = currentOT;
                    otTacPausesUsed.Clear(); // 確保清除所有髒資料
                    otTacPausesUsed["matchzyTeam1"] = 0;
                    otTacPausesUsed["matchzyTeam2"] = 0;
                }
            }
        }
        else
        {
            lastTrackedOT = 0;
        }

        // ==========================================
        // ▼ 升級：回合開始 1 秒瞬間觸發暫停機制 ▼
        // ==========================================
        // 1. 強制重置 10 秒常態巡邏 (解決跨回合時間差)
        StopAutoPauseCheck();
        StartAutoPauseCheck();

        // 2. 額外加碼：凍結時間第 1 秒派臨時巡邏員點名，瞬間鎖定！
        AddTimer(1.0f, () => 
        {
            if (!isMatchLive || !AutoPauseEnabled || isPaused) return;
            if (IsHalfTimePhase() || IsPostGamePhase()) return;
            if (!IsAutoPauseActive()) return;

            int minP = AutoPauseMinPlayers;
            int ctCount = GetTeamPlayerCount(CsTeam.CounterTerrorist);
            int tCount = GetTeamPlayerCount(CsTeam.Terrorist);

            int targetCt = autoPauseShortAccepted ? acceptedCtCount : minP;
            int targetT = autoPauseShortAccepted ? acceptedTCount : minP;

            // 只要第一秒發現少人，直接無情觸發戰術暫停
            if (ctCount < targetCt) AutoTriggerTacPause(CsTeam.CounterTerrorist);
            else if (tCount < targetT) AutoTriggerTacPause(CsTeam.Terrorist);
        });

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnMatchEndAutoPauseReset(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        ResetTechPauseCount();
        ResetTacPauseCount();
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

    // 新增遺漏的停止檢查函數，修復錯誤
    public void StopAutoPauseCheck()
    {
        if (autoPauseMainTimer != null)
        {
            autoPauseMainTimer.Kill();
            autoPauseMainTimer = null;
        }
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
            
            if (gameRules != null && !IsFreezePeriodSafe(gameRules)) return;
            if (IsOfficialTacActiveSafe(gameRules)) return;

            if (ctCount < targetCt) AutoTriggerTacPause(CsTeam.CounterTerrorist);
            else if (tCount < targetT) AutoTriggerTacPause(CsTeam.Terrorist);

        }, TimerFlags.REPEAT);
    }

    private void AutoTriggerTacPause(CsTeam team)
    {
        if (tacPauseAutoUnpauseTimer is not null || techPauseAutoUnpauseTimer is not null) return;
        
        Team matchTeam = (team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        // 修正：從記憶體指標比較，改為字串比較，免疫 OT 物件複製 BUG
        string teamKey = "";
        if (matchzyTeam1 != null && matchTeam.teamName == matchzyTeam1.teamName) teamKey = "matchzyTeam1";
        else if (matchzyTeam2 != null && matchTeam.teamName == matchzyTeam2.teamName) teamKey = "matchzyTeam2";
        else teamKey = matchTeam.teamName; 

        if (string.IsNullOrEmpty(teamKey)) return;

        string currentTeamName = matchTeam.teamName;

        CCSGameRules? gameRules = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity is { GameRules: not null } proxy)
            {
                gameRules = proxy.GameRules;
                break;
            }
        }
        
        bool isOvertime = IsOvertimePlayingSafe(gameRules);
        int maxLimit = isOvertime ? GetPauseConfig("mp_team_timeout_ot_max", 1) : MaxTacPauses;
        int durationLimit = TacPauseDuration;

        Dictionary<string, int> targetPauseUsedDict = isOvertime ? otTacPausesUsed : tacPausesUsed;

        if (!targetPauseUsedDict.ContainsKey(teamKey)) targetPauseUsedDict[teamKey] = 0;

        if (targetPauseUsedDict[teamKey] >= maxLimit)
        {
            if (!autoPauseLimitAnnounced)
            {
                string phaseStr = isOvertime ? "加時賽 " : "";
                PrintToAllChat($" {ChatColors.Orange}玩家斷線{ChatColors.Default} {ChatColors.Green}{currentTeamName}{ChatColors.Default} 的{phaseStr}戰術暫停已用完，無法自動暫停");
                autoPauseLimitAnnounced = true;
            }
            return;
        }

        autoPauseLimitAnnounced = false; 
        string sideName = (team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        
        targetPauseUsedDict[teamKey]++;
        int remainingCount = maxLimit - targetPauseUsedDict[teamKey];
        int currentPauseUsed = targetPauseUsedDict[teamKey]; 

        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpData["t"] = false;
        unpData["ct"] = false;

        string phasePrefix = isOvertime ? "加時賽 " : "";
        PrintToAllChat($" {ChatColors.Orange}玩家斷線{ChatColors.Default} 系 統 為 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 開 啟 {phasePrefix}戰 術 暫 停。剩 餘 次 數：{ChatColors.Green}{remainingCount} {ChatColors.Default}次");
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
                            p.PrintToCenter($"玩 家 已 連 回 將 在 {autoResumeCountdown} 秒 後 解 除 暫 停");
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
                    PrintToAllChat($" {ChatColors.Green}玩 家 已 連 回，自 動 解 除 暫 停");
                    
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
                
                CheckAndAcceptShortHanded(); 

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
