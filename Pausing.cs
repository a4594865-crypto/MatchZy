using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy;

public partial class MatchZy
{
    // ==========================================
    // ▼ 設定檔變數區 (配合 MatchZy 設定檔動態修改) ▼
    // ==========================================
    public int matchzy_tech_pause_duration = 300; 
    public int matchzy_max_tech_pauses_allowed = 2; 

    public int matchzy_tac_pause_duration = 90; 
    public int matchzy_max_tac_pauses_allowed = 3; 

    // ==========================================
    // ▼ 暫停次數與計時器全域變數區 ▼
    // ==========================================
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };
    public Dictionary<string, int> tacPausesLeft = new() { { "matchzyTeam1", 3 }, { "matchzyTeam2", 3 } };

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
        
        // ▼▼▼ 修補原版漏洞：必須在「回合凍結時間 (購買階段)」才能發起暫停 ▼▼▼
        if (gameRules != null && !gameRules.FreezePeriod)
        {
            if (player != null) 
                PrintToPlayerChat(player, $" {ChatColors.Orange}回合已開始，指令無法使用");
            return;
        }
        // ▲▲▲ 防護結束 ▲▲▲

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

        if (IsHalfTimePhase())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]);
            return;
        }
        if (IsPostGamePhase())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]);
            return;
        }

        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        string teamKey = playerMatchTeam == matchzyTeam1 ? "matchzyTeam1" : (playerMatchTeam == matchzyTeam2 ? "matchzyTeam2" : "");
        if (string.IsNullOrEmpty(teamKey)) return;

        string currentTeamName = playerMatchTeam.teamName;

        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 您 的 {ChatColors.Green}技 術 暫 停 {ChatColors.Default}次 數 已 用 完");
            return;
        }

        string sideName = (player.Team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        techPausesLeft[teamKey]--;
        
        int currentPauseUsed = matchzy_max_tech_pauses_allowed - techPausesLeft[teamKey]; 

        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        untData["t"] = false;
        untData["ct"] = false;

        int maxM = matchzy_tech_pause_duration / 60;
        int maxS = matchzy_tech_pause_duration % 60;
        string maxTimeString = maxM > 0 ? $"{maxM}分{maxS:D2}秒" : $"{maxS}秒";

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 開 啟 技 術 暫 停。剩 餘 次 數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次");
        PrintToAllChat($" 暫 停 在 \u0004 {matchzy_tech_pause_duration}秒 \u0001 自 動 解 除，或 雙 方 輸 入 \u0004.unt\u0001 解 除");

        techPauseElapsedTime = 0;

        techPauseAutoUnpauseTimer = AddTimer(1.0f, () =>
        {
            if (!isPaused)
            {
                KillTechPauseTimer();
                return;
            }

            int remaining = matchzy_tech_pause_duration - techPauseElapsedTime;

            if (techPauseElapsedTime >= matchzy_tech_pause_duration)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                untData["ct"] = false;
                untData["t"] = false;
                PrintToAllChat($" 技 術 暫 停 已達\u0004{matchzy_tech_pause_duration}秒 \u0001上 限，系 統 自 自 動 解 除 暫 停");
                
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

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                    {
                        p.PrintToCenter($"{sideName} 技術暫停 {timeString} ( {currentPauseUsed} / {matchzy_max_tech_pauses_allowed} )");
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
        
        // ▼▼▼ 修補原版漏洞：必須在「回合凍結時間 才能發起暫停 ▼▼▼
        if (gameRules != null && !gameRules.FreezePeriod)
        {
            if (player != null) 
                PrintToPlayerChat(player, $" {ChatColors.Orange}回合已開始，指令無法使用");
            return;
        }
        // ▲▲▲ 防護結束 ▲▲▲

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

        if (tacPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 您 的 {ChatColors.Green}戰 術 暫 停 {ChatColors.Default}次 數 已 用 完");
            return;
        }

        string sideName = (player.Team == CsTeam.CounterTerrorist) ? "反恐小組" : "恐怖份子";
        tacPausesLeft[teamKey]--;
        
        int currentPauseUsed = matchzy_max_tac_pauses_allowed - tacPausesLeft[teamKey]; 

        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpData["t"] = false;
        unpData["ct"] = false;

        int maxM = matchzy_tac_pause_duration / 60;
        int maxS = matchzy_tac_pause_duration % 60;
        string maxTimeString = maxM > 0 ? $"{maxM}分{maxS:D2}秒" : $"{maxS}秒";

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 開 啟 戰 術 暫 停。剩 餘 次 數：{ChatColors.Green}{tacPausesLeft[teamKey]} {ChatColors.Default}次");
        PrintToAllChat($" 暫 停 在 \u0004{matchzy_tac_pause_duration}秒\u0001 自 動 解 除，或 雙 方 輸 入 \u0004.unp\u0001 解 除");

        tacPauseElapsedTime = 0;

        tacPauseAutoUnpauseTimer = AddTimer(1.0f, () =>
        {
            if (!isPaused)
            {
                KillTacPauseTimer();
                return;
            }

            int remaining = matchzy_tac_pause_duration - tacPauseElapsedTime;

            if (tacPauseElapsedTime >= matchzy_tac_pause_duration)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpData["ct"] = false;
                unpData["t"] = false;
                PrintToAllChat($" 戰 術 暫 停 已達\u0004 {matchzy_tac_pause_duration}秒 \u0001上 限，系 統 自 動 解 除 暫 停");
                
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

                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                    {
                        p.PrintToCenter($"{sideName} 暫停 {timeString} ( {currentPauseUsed} / {matchzy_max_tac_pauses_allowed} )");
                    }
                }
                tacPauseElapsedTime += 1;
            }
        }, TimerFlags.REPEAT);
    }


    // ==========================================
    // 解除指令攔截與同意機制 (.unt 與 .unp)
    // ==========================================

    /// <summary>
    /// 處理玩家輸入 .unt (解技術暫停)
    /// </summary>
    public void HandleUntCommand(CCSPlayerController player)
    {
        if (tacPauseAutoUnpauseTimer != null)
        {
            PrintToPlayerChat(player, $" 目 前 為 {ChatColors.Green}戰術暫停{ChatColors.Default}，請 雙 方 輸 入 {ChatColors.Green}.unp{ChatColors.Default} 來 解 除");
            return;
        }

        if (techPauseAutoUnpauseTimer == null) return; 

        string team = player.TeamNum == 2 ? "t" : "ct";
        string teamName = player.TeamNum == 2 ? "恐怖份子" : "反恐小組";
        // 自動判斷對手陣營名稱
        string opponentTeamName = player.TeamNum == 2 ? "反恐小組" : "恐怖份子"; 

        if (!untData[team])
        {
            untData[team] = true;
            
            // 判斷是否雙方都已同意
            if (untData["t"] && untData["ct"])
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                PrintToAllChat($" {ChatColors.Orange}雙 方 皆 已 同 意， 解 除 技 術 暫 停");
                KillTechPauseTimer();
            }
            else
            {
                // 單方發起時，顯示要求對方確認的提示
                PrintToAllChat($" {ChatColors.Green}{teamName}{ChatColors.Default} 想解除暫停 {ChatColors.Green}{opponentTeamName}{ChatColors.Default} 請輸入 {ChatColors.Orange}.unt{ChatColors.Default} 來同意");
            }
        }
    }

    /// <summary>
    /// 處理玩家輸入 .unp (解戰術暫停)
    /// </summary>
    public void HandleUnpCommand(CCSPlayerController player)
    {
        if (techPauseAutoUnpauseTimer != null)
        {
            PrintToPlayerChat(player, $" 目 前 為 {ChatColors.Green}技術暫停{ChatColors.Default}，請 雙 方 輸 入 {ChatColors.Green}.unt{ChatColors.Default} 來 解 除");
            return;
        }

        if (tacPauseAutoUnpauseTimer == null) return; 

        string team = player.TeamNum == 2 ? "t" : "ct";
        string teamName = player.TeamNum == 2 ? "恐怖份子" : "反恐小組";
        // 自動判斷對手陣營名稱
        string opponentTeamName = player.TeamNum == 2 ? "反恐小組" : "恐怖份子"; 

        if (!unpData[team])
        {
            unpData[team] = true;
            
            // 判斷是否雙方都已同意
            if (unpData["t"] && unpData["ct"])
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                PrintToAllChat($" {ChatColors.Orange}雙 方 皆 已 同 意，解 除 戰 術 暫 停");
                KillTacPauseTimer();
            }
            else
            {
                // 單方發起時，完美呈現你指定的互動格式
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
        techPausesLeft["matchzyTeam1"] = matchzy_max_tech_pauses_allowed;
        techPausesLeft["matchzyTeam2"] = matchzy_max_tech_pauses_allowed;
        KillTechPauseTimer();
    }

    public void ResetTacPauseCount()
    {
        tacPausesLeft["matchzyTeam1"] = matchzy_max_tac_pauses_allowed;
        tacPausesLeft["matchzyTeam2"] = matchzy_max_tac_pauses_allowed;
        KillTacPauseTimer();
    }
}
