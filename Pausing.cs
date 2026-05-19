using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    [cite_start]// 配合您的 MatchZy 版本，使用字串 "matchzyTeam1" 與 "matchzyTeam2" 作為字典 Key [cite: 2]
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };
    
    [cite_start]// 用來控制 300 秒自動解除暫停的計時器變數 [cite: 3]
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;

    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        [cite_start]// 1. 如果比賽還沒正式開始，不允許技術暫停 [cite: 4]
        if (!isMatchLive) return;

        [cite_start]// 2. 如果是伺服器 RCON 控制台輸入，直接當作強制暫停 [cite: 5]
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return; // [cite: 6]
        }

        // --- 新增：檢查是否在回合剛開始 (Freezetime) ---
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerulesproxy").FirstOrDefault()?.GameRules;
        if (gameRules != null && gameRules.FreezePeriod)
        {
            ReplyToUserCommand(player, "回合剛開始（Freezetime），此時不允許技術暫停！");
            return;
        }

        [cite_start]// 3. 基本檢查：是否已經暫停、是否在半場、是否已結束 [cite: 6, 7, 8]
        if (isPaused)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            return; // [cite: 7]
        }
        if (IsHalfTimePhase())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]);
            return; // [cite: 8]
        }
        if (IsPostGamePhase())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]);
            return; // [cite: 9]
        }

        [cite_start]// 4. 檢查玩家是否在有效隊伍 [cite: 10]
        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        [cite_start]// 5. 映射隊伍 [cite: 11, 12, 13]
        Team playerMatchTeam;
        if (player.Team == CsTeam.CounterTerrorist)
        {
            playerMatchTeam = reverseTeamSides["CT"]; // [cite: 12]
        }
        else
        {
            playerMatchTeam = reverseTeamSides["TERRORIST"]; // [cite: 13]
        }

        string teamKey = "";
        string currentTeamName = playerMatchTeam.teamName;
        
        [cite_start]// 判斷隊伍 [cite: 14, 15, 16, 17]
        if (playerMatchTeam == matchzyTeam1)
        {
            teamKey = "matchzyTeam1"; // [cite: 15]
        }
        else if (playerMatchTeam == matchzyTeam2)
        {
            teamKey = "matchzyTeam2"; // [cite: 16]
        }
        else
        {
            return; // [cite: 17]
        }

        [cite_start]// 6. 檢查次數限制 [cite: 18]
        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 已經沒有可用的技術暫停次數！");
            return; // [cite: 19]
        }

        [cite_start]// 7. 執行暫停 [cite: 19]
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = currentTeamName; 

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫停將在 \u0004300秒\u0001 後自動解除，或雙方輸入 \u0004.up\u0001 解除。");

        [cite_start]// 8. 安全防護：重置計時器 
        techPauseAutoUnpauseTimer?.Kill();

        [cite_start]// 9. 建立 300 秒自動解除計時器 (已修正為 300.0f) [cite: 21]
        techPauseAutoUnpauseTimer = AddTimer(300.0f, () =>
        {
            if (isPaused)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                PrintToAllChat($" 技術暫停已達\u0004 300 秒 \u0001上限，系統自動解除暫停！");
            }
            techPauseAutoUnpauseTimer = null;
        });
    }

    public void ResetTechPauseCount()
    {
        techPausesLeft["matchzyTeam1"] = 1; // [cite: 22]
        techPausesLeft["matchzyTeam2"] = 1;
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = null;
    }
}
