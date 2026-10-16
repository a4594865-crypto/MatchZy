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
    // ▼ 0-GC 快取區 (消除每秒硬碟 I/O 與全圖實體掃描) ▼
    // ==========================================
    private CCSGameRulesProxy? _cachedPauseGameRulesProxy;
    private ConVar? _cachedMpMaxRoundsCvar;
    private ConVar? _cachedMpOvertimeMaxRoundsCvar;

    private string? _cachedPauseCfgPath;
    private DateTime _lastPauseCfgWriteTime = DateTime.MinValue;
    private long _lastPauseCfgCheckTick = 0;
    private readonly Dictionary<string, int> _pauseCfgValueCache = new(8);

    // ==========================================
    // ▼ 動態設定檔讀取區 (完美連動 config.cfg + 0-GC 快取) ▼
    // ==========================================
    private int GetPauseConfig(string cvarName, int defaultValue)
    {
        try 
        {
            var cvar = ConVar.Find(cvarName);
            if (cvar is not null) return cvar.GetPrimitiveValue<int>();

            _cachedPauseCfgPath ??= Path.Join(Server.GameDirectory + "/csgo/cfg/MatchZy/config.cfg");

            // 每 3 秒最多檢查一次檔案修改時間，若 config.cfg 被管理員修改則自動刷新快取，平時每秒巡邏 0 硬碟讀取！
            long nowTick = Environment.TickCount64;
            if (nowTick - _lastPauseCfgCheckTick > 3000)
            {
                _lastPauseCfgCheckTick = nowTick;
                if (File.Exists(_cachedPauseCfgPath))
                {
                    DateTime writeTime = File.GetLastWriteTimeUtc(_cachedPauseCfgPath);
                    if (writeTime != _lastPauseCfgWriteTime)
                    {
                        _lastPauseCfgWriteTime = writeTime;
                        _pauseCfgValueCache.Clear();
                    }
                }
            }

            if (_pauseCfgValueCache.TryGetValue(cvarName, out int cachedVal))
            {
                return cachedVal;
            }

            if (File.Exists(_cachedPauseCfgPath))
            {
                string? val = GetConvarValueFromCFGFile(_cachedPauseCfgPath, cvarName);
                if (val is not null && int.TryParse(val, out int res))
                {
                    _pauseCfgValueCache[cvarName] = res;
                    return res;
                }
            }

            _pauseCfgValueCache[cvarName] = defaultValue;
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

    // ★ 綁定隊伍實體的最高人數紀錄（支援 4v5 / 6v4 開賽與半場換邊自動跟隨）
    public Dictionary<string, int> teamPeakHumans = new() { { "matchzyTeam1", 0 }, { "matchzyTeam2", 0 } };
    public Dictionary<string, int> teamAcceptedCount = new() { { "matchzyTeam1", -1 }, { "matchzyTeam2", -1 } };
    
    public bool autoPauseShortAccepted = false;
    public int acceptedCtCount = 5;
    public int acceptedTCount = 5;

    public Dictionary<string, bool> untData = new() { { "ct", false }, { "t", false } };
    public Dictionary<string, bool> unpData = new() { { "ct", false }, { "t", false } };

    // ==========================================
    // ▼ 跨版本安全屬性檢查器 (已加入局數推算終極防護 + 實體快取) ▼
    // ==========================================
    private CCSGameRules? GetPauseGameRulesSafe()
    {
        if (_cachedPauseGameRulesProxy is { IsValid: true, GameRules: not null })
        {
            return _cachedPauseGameRulesProxy.GameRules;
        }

        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity is { IsValid: true, GameRules: not null } proxy)
            {
                _cachedPauseGameRulesProxy = proxy;
                return proxy.GameRules;
            }
        }
        return null;
    }

    private bool IsOvertimePlayingSafe(CCSGameRules? gameRules)
    {
        if (gameRules == null) return false;
        string val = gameRules.OvertimePlaying.ToString() ?? "";
        if (val == "True" || val == "1") return true;

        // 加入底層局數推算，防止 CS2 引擎在換邊瞬間標籤延遲
        int totalRounds = gameRules.TotalRoundsPlayed;
        int maxRounds = 24;
        try {
            _cachedMpMaxRoundsCvar ??= ConVar.Find("mp_maxrounds");
            if (_cachedMpMaxRoundsCvar != null) maxRounds = _cachedMpMaxRoundsCvar.GetPrimitiveValue<int>();
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

    // 取得目前 CT 或 T 對應的是 matchzyTeam1 還是 matchzyTeam2
    private string GetTeamKeyBySide(CsTeam side)
    {
        try
        {
            string sideStr = side == CsTeam.CounterTerrorist ? "CT" : "TERRORIST";
            if (reverseTeamSides != null && reverseTeamSides.TryGetValue(sideStr, out var matchTeam) && matchTeam != null)
            {
                if (matchzyTeam1 != null && matchTeam.teamName == matchzyTeam1.teamName) return "matchzyTeam1";
                if (matchzyTeam2 != null && matchTeam.teamName == matchzyTeam2.teamName) return "matchzyTeam2";
            }
        }
        catch { }
        return side == CsTeam.CounterTerrorist ? "matchzyTeam1" : "matchzyTeam2";
    }

    // 計算該陣營目前應該維持的目標人數（完美相容 5v5、4v5、6v4 與少人同意繼續）
    private int GetSideTargetPlayers(CsTeam side)
    {
        int minP = Math.Max(1, AutoPauseMinPlayers);
        string key = GetTeamKeyBySide(side);
        int peak = teamPeakHumans.TryGetValue(key, out int p) ? p : 0;

        // 若該隊開賽至今從未達 2 人（如 0 人或 1 人跑圖），不啟用該隊的自動暫停
        if (peak < 2 && peak < minP) return 0;

        // 基準門檻：取「該隊曾達到的最高人數」與「設定的 minplayers」兩者較小值
        // 例如設定 minplayers 5：
        // - 4v5 開賽：4人隊伍基準為 4（4人不暫停，掉到3人立刻暫停）；5人隊伍基準為 5
        // - 6v4 開賽：6人隊伍基準上限為 5（掉到4人才暫停）；4人隊伍基準為 4（掉到3人才暫停）
        int baseTarget = Math.Min(peak, minP);

        if (teamAcceptedCount.TryGetValue(key, out int accepted) && accepted != -1)
        {
            return accepted;
        }

        return baseTarget;
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

        CCSGameRules? gameRules = GetPauseGameRulesSafe();

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

        techPausesUsed.TryAdd(teamKey, 0);

        if (techPausesUsed[teamKey] >= maxLimit)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 您 的 {ChatColors.Green}技 術 暫 停 {ChatColors.Default}次 數 已 用 完");
            return;
        }

        string sideName = (player.Team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        
        int currentPauseUsed = ++techPausesUsed[teamKey];
        int remainingCount = maxLimit - currentPauseUsed;

        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        untData["t"] = false;
        untData["ct"] = false;

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

                string centerMsg = $"{sideName}技術暫停 {timeString} ( {currentPauseUsed} / {maxLimit} ){prompt}";
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                    {
                        p.PrintToCenter(centerMsg);
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

        CCSGameRules? gameRules = GetPauseGameRulesSafe();
        
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

        targetPauseUsedDict.TryAdd(teamKey, 0);

        if (targetPauseUsedDict[teamKey] >= maxLimit)
        {
            string phaseStr = isOvertime ? "加時賽 " : "";
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 您 的 {ChatColors.Green}{phaseStr}戰術暫停 {ChatColors.Default}次 數 已 用 完");
            return;
        }

        string sideName = (player.Team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        
        int currentPauseUsed = ++targetPauseUsedDict[teamKey];
        int remainingCount = maxLimit - currentPauseUsed;

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

                string centerMsg = $"{sideName} 暫停 {timeString} ( {currentPauseUsed} / {maxLimit} ){prompt}";
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                    {
                        p.PrintToCenter(centerMsg);
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
        int minP = Math.Max(1, AutoPauseMinPlayers);
        GetBothTeamsPlayerCount(out int currentCt, out int currentT);

        string ctKey = GetTeamKeyBySide(CsTeam.CounterTerrorist);
        string tKey = GetTeamKeyBySide(CsTeam.Terrorist);

        int ctBase = Math.Min(teamPeakHumans.TryGetValue(ctKey, out int pCt) ? pCt : 0, minP);
        int tBase = Math.Min(teamPeakHumans.TryGetValue(tKey, out int pT) ? pT : 0, minP);

        if (currentCt < ctBase)
        {
            teamAcceptedCount[ctKey] = currentCt;
            autoPauseShortAccepted = true;
            acceptedCtCount = currentCt;
        }
        if (currentT < tBase)
        {
            teamAcceptedCount[tKey] = currentT;
            autoPauseShortAccepted = true;
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

        autoPausePeakHumans = 0;
        teamPeakHumans["matchzyTeam1"] = 0;
        teamPeakHumans["matchzyTeam2"] = 0;
        teamAcceptedCount["matchzyTeam1"] = -1;
        teamAcceptedCount["matchzyTeam2"] = -1;
        autoPauseShortAccepted = false;
        autoPauseLimitAnnounced = false;
        
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
            StopAutoPauseCheck();
            return HookResult.Continue;
        }

        // 正賽回合開始時立刻更新各隊人數紀錄
        IsAutoPauseActive();

        CCSGameRules? gameRules = GetPauseGameRulesSafe();

        if (IsOvertimePlayingSafe(gameRules))
        {
            int totalRounds = gameRules!.TotalRoundsPlayed;
            
            int maxRounds = 24;
            try {
                _cachedMpMaxRoundsCvar ??= ConVar.Find("mp_maxrounds");
                if (_cachedMpMaxRoundsCvar != null) maxRounds = _cachedMpMaxRoundsCvar.GetPrimitiveValue<int>();
            } catch { }

            int otMaxRounds = 6;
            try {
                _cachedMpOvertimeMaxRoundsCvar ??= ConVar.Find("mp_overtime_maxrounds");
                if (_cachedMpOvertimeMaxRoundsCvar != null) otMaxRounds = _cachedMpOvertimeMaxRoundsCvar.GetPrimitiveValue<int>();
            } catch { }
            
            if (totalRounds >= maxRounds && otMaxRounds > 0)
            {
                int otRounds = totalRounds - maxRounds;
                int currentOT = (otRounds / otMaxRounds) + 1;
                
                if (currentOT != lastTrackedOT)
                {
                    lastTrackedOT = currentOT;
                    otTacPausesUsed.Clear();
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
        // ▼ 升級：僅在回合凍結時間內每秒巡邏，開打自動停止 ▼
        // ==========================================
        StopAutoPauseCheck();
        StartAutoPauseCheck();

        AddTimer(1.0f, () => 
        {
            EvaluateAutoPauseCondition();
        });

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnMatchEndAutoPauseReset(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        ResetTechPauseCount();
        ResetTacPauseCount();
        StopAutoPauseCheck();
        return HookResult.Continue;
    }

    public bool IsAutoPauseActive()
    {
        GetBothTeamsPlayerCount(out int ctCount, out int tCount);
        return IsAutoPauseActiveCore(ctCount, tCount);
    }

    // 【0-GC 核心優化】：接收單次掃描完畢的 ctCount 與 tCount，免除重複呼叫 GetPlayers()
    private bool IsAutoPauseActiveCore(int ctCount, int tCount)
    {
        int totalPlayers = ctCount + tCount;
        
        if (totalPlayers > autoPausePeakHumans) autoPausePeakHumans = totalPlayers;

        string ctKey = GetTeamKeyBySide(CsTeam.CounterTerrorist);
        string tKey = GetTeamKeyBySide(CsTeam.Terrorist);

        if (!teamPeakHumans.TryGetValue(ctKey, out int prevCtPeak) || ctCount > prevCtPeak)
        {
            teamPeakHumans[ctKey] = ctCount;
            prevCtPeak = ctCount;
        }
        if (!teamPeakHumans.TryGetValue(tKey, out int prevTPeak) || tCount > prevTPeak)
        {
            teamPeakHumans[tKey] = tCount;
            prevTPeak = tCount;
        }

        int minP = Math.Max(1, AutoPauseMinPlayers);
        int ctBase = Math.Min(prevCtPeak, minP);
        int tBase = Math.Min(prevTPeak, minP);

        // 若少人同意繼續後，斷線玩家又連回來補滿基準人數，自動解除「少人妥協狀態」恢復正常保護
        if (teamAcceptedCount.TryGetValue(ctKey, out int accCt) && accCt != -1 && ctCount >= ctBase)
            teamAcceptedCount[ctKey] = -1;
        if (teamAcceptedCount.TryGetValue(tKey, out int accT) && accT != -1 && tCount >= tBase)
            teamAcceptedCount[tKey] = -1;

        int minActivation = Math.Min(2, minP);
        // 只要任一隊伍曾經達到 2 人以上（或達到 minP），即啟動自動暫停系統
        return teamPeakHumans["matchzyTeam1"] >= minActivation || 
               teamPeakHumans["matchzyTeam2"] >= minActivation;
    }

    private void EvaluateAutoPauseCondition()
    {
        if (!isMatchLive || !AutoPauseEnabled) return;
        if (IsHalfTimePhase() || IsPostGamePhase()) return;

        CCSGameRules? gameRules = GetPauseGameRulesSafe();

        // ★ 核心：一旦買槍凍結時間結束（回合正式開打），立刻關掉計時器下班，交戰期間 0 負載！
        if (gameRules != null && !IsFreezePeriodSafe(gameRules))
        {
            StopAutoPauseCheck();
            return;
        }

        // 【0-GC 優化】：單次遍歷取得兩隊人數，避免原本每秒呼叫 4 次 GetTeamPlayerCount
        GetBothTeamsPlayerCount(out int ctCount, out int tCount);
        if (!IsAutoPauseActiveCore(ctCount, tCount)) return;

        int targetCt = GetSideTargetPlayers(CsTeam.CounterTerrorist);
        int targetT = GetSideTargetPlayers(CsTeam.Terrorist);

        if (ctCount >= targetCt && tCount >= targetT)
        {
            autoPauseLimitAnnounced = false;
            return;
        }

        if (isPaused) return;
        if (IsOfficialTacActiveSafe(gameRules)) return;

        if (targetCt > 0 && ctCount < targetCt) AutoTriggerTacPause(CsTeam.CounterTerrorist);
        else if (targetT > 0 && tCount < targetT) AutoTriggerTacPause(CsTeam.Terrorist);
    }

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
        // 僅在回合凍結時間內每 1.0 秒檢查一次，凍結結束後由 EvaluateAutoPauseCondition 自動關閉
        autoPauseMainTimer = AddTimer(1.0f, () =>
        {
            EvaluateAutoPauseCondition();
        }, TimerFlags.REPEAT);
    }

    private void AutoTriggerTacPause(CsTeam team)
    {
        if (tacPauseAutoUnpauseTimer is not null || techPauseAutoUnpauseTimer is not null) return;
        
        Team matchTeam = (team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        string teamKey = "";
        if (matchzyTeam1 != null && matchTeam.teamName == matchzyTeam1.teamName) teamKey = "matchzyTeam1";
        else if (matchzyTeam2 != null && matchTeam.teamName == matchzyTeam2.teamName) teamKey = "matchzyTeam2";
        else teamKey = matchTeam.teamName; 

        if (string.IsNullOrEmpty(teamKey)) return;

        string currentTeamName = matchTeam.teamName;

        CCSGameRules? gameRules = GetPauseGameRulesSafe();
        
        bool isOvertime = IsOvertimePlayingSafe(gameRules);
        int maxLimit = isOvertime ? GetPauseConfig("mp_team_timeout_ot_max", 1) : MaxTacPauses;
        int durationLimit = TacPauseDuration;

        Dictionary<string, int> targetPauseUsedDict = isOvertime ? otTacPausesUsed : tacPausesUsed;

        targetPauseUsedDict.TryAdd(teamKey, 0);

        if (targetPauseUsedDict[teamKey] >= maxLimit)
        {
            if (!autoPauseLimitAnnounced)
            {
                string phaseStr = isOvertime ? " 加時賽 " : "";
                PrintToAllChat($" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 因 {ChatColors.Orange}人員離線{ChatColors.Default} 戰術暫停已用完 ({phaseStr})");
                autoPauseLimitAnnounced = true;
            }
            return;
        }

        autoPauseLimitAnnounced = false; 
        string sideName = (team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        
        int currentPauseUsed = ++targetPauseUsedDict[teamKey];
        int remainingCount = maxLimit - currentPauseUsed;

        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpData["t"] = false;
        unpData["ct"] = false;

        string phasePrefix = isOvertime ? " 加時賽 " : "";
        PrintToAllChat($" {ChatColors.Green}{currentTeamName}{ChatColors.Default} {ChatColors.Orange}人員離線{ChatColors.Default} 觸發 {phasePrefix}暫停 剩餘：{ChatColors.Green}{remainingCount}{ChatColors.Default}次");
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

            GetBothTeamsPlayerCount(out int ctCount, out int tCount);
            IsAutoPauseActiveCore(ctCount, tCount);

            int targetCt = GetSideTargetPlayers(CsTeam.CounterTerrorist);
            int targetT = GetSideTargetPlayers(CsTeam.Terrorist);

            if (isAutoTriggeredTacPause && ctCount >= targetCt && tCount >= targetT)
            {
                if (autoResumeCountdown == -1) autoResumeCountdown = AutoPauseResumeDelay;

                if (autoResumeCountdown > 0)
                {
                    string resumeCenterMsg = $"玩 家 已 連 回 將 在 {autoResumeCountdown} 秒 後 解 除 暫 停";
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                        {
                            p.PrintToCenter(resumeCenterMsg);
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

                string centerMsg = $"{sideName} 暫停 {timeString} ( {currentPauseUsed} / {maxLimit} ){prompt}";
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false, TeamNum: 2 or 3 })
                    {
                        p.PrintToCenter(centerMsg);
                    }
                }
                tacPauseElapsedTime += 1;
            }
        }, TimerFlags.REPEAT);
    }

    private bool IsPlayerCoachForAutoPause(CCSPlayerController p)
    {
        // 【0-GC 優化】：若兩隊皆無教練，直接回傳 false，免除每次檢查時的雜湊查找與 try-catch 開銷
        if ((matchzyTeam1 == null || matchzyTeam1.coach.Count == 0) &&
            (matchzyTeam2 == null || matchzyTeam2.coach.Count == 0))
        {
            return false;
        }
        return (matchzyTeam1 != null && matchzyTeam1.coach.Contains(p)) || 
               (matchzyTeam2 != null && matchzyTeam2.coach.Contains(p));
    }

    // 【0-GC 優化】：單趟迴圈同時統計 CT 與 T 的選手人數，砍掉 75% 的重複 Utilities.GetPlayers() 呼叫
    private void GetBothTeamsPlayerCount(out int ctCount, out int tCount)
    {
        ctCount = 0;
        tCount = 0;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is { IsValid: true, IsBot: false })
            {
                CsTeam pTeam = p.Team;
                if (pTeam == CsTeam.CounterTerrorist && !IsPlayerCoachForAutoPause(p))
                {
                    ctCount++;
                }
                else if (pTeam == CsTeam.Terrorist && !IsPlayerCoachForAutoPause(p))
                {
                    tCount++;
                }
            }
        }
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
