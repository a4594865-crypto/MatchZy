using System.IO;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
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
            if (cvar != null) return cvar.GetPrimitiveValue<int>();

            string cfgPath = Path.Join(Server.GameDirectory + "/csgo/cfg/MatchZy/config.cfg");
            if (File.Exists(cfgPath))
            {
                string? val = GetConvarValueFromCFGFile(cfgPath, cvarName);
                if (val != null && int.TryParse(val, out int res)) return res;
            }
        } 
        catch { }
        return defaultValue;
    }

    public int TechPauseDuration => GetPauseConfig("matchzy_tech_pause_duration", 300);
    public int MaxTechPauses => GetPauseConfig("matchzy_max_tech_pauses_allowed", 1);
    public int TacPauseDuration => GetPauseConfig("matchzy_tac_pause_duration", 90);
    public int MaxTacPauses => GetPauseConfig("matchzy_max_tac_pauses_allowed", 3);

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

        if (tacPauseAutoUnpauseTimer != null)
        {
            if (player != null) PrintToPlayerChat(player, $"  正 處 於【 {ChatColors.Green}暫 停 狀 態{ChatColors.Default} 】中，無 法 啟 用 技 術 暫 停");
            return;
        }

        if (techPauseAutoUnpauseTimer != null)
        {
            if (player != null) ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            return;
        }

        CCSGameRules? gameRules = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity != null)
            {
                gameRules = entity.GameRules;
                break;
            }
        }

        if (gameRules != null && !gameRules.FreezePeriod)
        {
            if (player != null) 
            {
                PrintToPlayerChat(player, $" {ChatColors.Orange}回合已開始，指令無法使用");
                player.PrintToCenter(" 回合已開始，指令無法使用 ");
            }
            return;
        }

        bool isOfficialTacActive = gameRules != null && (gameRules.TerroristTimeOutActive || gameRules.CTTimeOutActive);

        if (isPaused || isOfficialTacActive)
        {
            if (player != null) PrintToPlayerChat(player, $" 正 處 於【 {ChatColors.Green}暫 停 狀 態{ChatColors.Default} 】中，無 法 啟 用 技 術 暫 停");
            return; 
        }

        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        if (IsHalfTimePhase()) return;
        if (IsPostGamePhase()) return;
        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

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
        PrintToAllChat($" 暫 停 在 \u0004 {durationLimit}秒 \u0001 自 動 解 除，或 雙 方 輸 入 \u0004.unt\u0001 解 除");

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
                PrintToAllChat($" 技 術 暫 停 已達\u0004{durationLimit}秒 \u0001上 限，系 統 自 動 解 除 暫 停");
                
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
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

                // ▼▼▼ 完美合體區：如果有人想解除，就把字串加在秒數下方 ▼▼▼
                string prompt = "";
                if (untData["t"] && !untData["ct"]) prompt = "\n恐怖份子想解除，請輸入 .unt 同意";
                else if (!untData["t"] && untData["ct"]) prompt = "\n反恐小組想解除，請輸入 .unt 同意";

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                    {
                        p.PrintToCenter($"{sideName}技術暫停 {timeString} ( {currentPauseUsed} / {maxLimit} ){prompt}");
                    }
                }
                techPauseElapsedTime += 1;
            }
        }, TimerFlags.REPEAT);
    }


    // ==========================================
    // 戰術暫停 (.P) 核心方法
    // ==========================================
    public void TacPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (!isMatchLive) return;

        if (techPauseAutoUnpauseTimer != null)
        {
            if (player != null) PrintToPlayerChat(player, $" 正處於【 {ChatColors.Green}暫 停 狀 態{ChatColors.Default} 】中，無 法 啟 用 戰 術 暫 停");
            return;
        }

        if (tacPauseAutoUnpauseTimer != null)
        {
            if (player != null) PrintToPlayerChat(player, $" 已 經 在{ChatColors.Green}戰術暫停{ChatColors.Default} 中");
            return;
        }

        CCSGameRules? gameRules = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity != null)
            {
                gameRules = entity.GameRules;
                break;
            }
        }
        
        if (gameRules != null && !gameRules.FreezePeriod)
        {
            if (player != null) 
            {
                PrintToPlayerChat(player, $" {ChatColors.Orange}回合已開始，指令無法使用");
                player.PrintToCenter(" 回合已開始，指令無法使用 ");
            }
            return;
        }

        bool isOfficialTacActive = gameRules != null && (gameRules.TerroristTimeOutActive || gameRules.CTTimeOutActive);

        if (isPaused || isOfficialTacActive)
        {
            if (player != null) PrintToPlayerChat(player, $" 正 處 於【 {ChatColors.Green}暫 停 狀 態{ChatColors.Default} 】中，無 法 啟 用 戰 術 暫 停");
            return; 
        }

        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        if (IsHalfTimePhase()) return;
        if (IsPostGamePhase()) return;
        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

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
        PrintToAllChat($" 暫 停 在 \u0004{durationLimit}秒\u0001 自 動 解 除，或 雙 方 輸 入 \u0004.unp\u0001 解 除");

        tacPauseElapsedTime = 0;

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
                    if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
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

                // ▼▼▼ 完美合體區：如果有人想解除，就把字串加在秒數下方 ▼▼▼
                string prompt = "";
                if (unpData["t"] && !unpData["ct"]) prompt = "\n恐怖份子想解除，請輸入 .unp 同意";
                else if (!unpData["t"] && unpData["ct"]) prompt = "\n反恐小組想解除，請輸入 .unp 同意";

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
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

    public void HandleUntCommand(CCSPlayerController player)
    {
        if (tacPauseAutoUnpauseTimer != null)
        {
            PrintToPlayerChat(player, $" 目 前 為 {ChatColors.Green}戰術暫停{ChatColors.Default}，請 雙 方 輸 入 {ChatColors.Orange}.unp{ChatColors.Default} 來 解 除");
            return;
        }

        if (techPauseAutoUnpauseTimer == null) return; 

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
                PrintToAllChat($" {ChatColors.Orange}雙 方 皆 已 同 意，已 解 除 技 術 暫 停");
                KillTechPauseTimer();
            }
            else
            {
                // 因為計時器會自動印出 HUD，這裡只需保留聊天室通知即可，不需再發送 PrintToCenter 避免打架
                PrintToAllChat($" {ChatColors.Green}{teamName}{ChatColors.Default} 想解除暫停 {ChatColors.Green}{opponentTeamName}{ChatColors.Default} 請輸入 {ChatColors.Orange}.unt{ChatColors.Default} 來同意");
            }
        }
    }

    public void HandleUnpCommand(CCSPlayerController player)
    {
        if (techPauseAutoUnpauseTimer != null)
        {
            PrintToPlayerChat(player, $" 目 前 為 {ChatColors.Green}技術暫停{ChatColors.Default}，請 雙 方 輸 入 {ChatColors.Orange}.unt{ChatColors.Default} 來 解 除");
            return;
        }

        if (tacPauseAutoUnpauseTimer == null) return; 

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
                PrintToAllChat($" {ChatColors.Orange}雙 方 皆 已 同 意，已 解 除 戰 術 暫 停");
                KillTacPauseTimer();
            }
            else
            {
                // 因為計時器會自動印出 HUD，這裡只需保留聊天室通知即可，不需再發送 PrintToCenter 避免打架
                PrintToAllChat($" {ChatColors.Green}{teamName}{ChatColors.Default} 想解除暫停 {ChatColors.Green}{opponentTeamName}{ChatColors.Default} 請輸入 {ChatColors.Orange}.unp{ChatColors.Default} 來同意");
            }
        }
    }

    // ==========================================
    // 計時器銷毀與次數重置區
    // ==========================================

    public void KillTechPauseTimer()
    {
        if (techPauseAutoUnpauseTimer != null)
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
        if (tacPauseAutoUnpauseTimer != null)
        {
            tacPauseAutoUnpauseTimer.Kill();
            tacPauseAutoUnpauseTimer = null;
        }
        tacPauseElapsedTime = 0;
        unpData["t"] = false;
        unpData["ct"] = false;
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
}
