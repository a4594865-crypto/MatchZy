using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Linq; // 必須引入以使用 LINQ 查詢

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (!isMatchLive) return;

        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // 3. 基本檢查
        if (isPaused) { ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]); return; }
        if (IsHalfTimePhase()) { ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]); return; }
        if (IsPostGamePhase()) { ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]); return; }

        // --- 【新增】回合開始防護：檢查是否處於凍結時間 ---
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        // 如果 gameRules.FreezePeriod 為 0，代表凍結時間已結束，回合正在進行中
        if (gameRules != null && gameRules.FreezePeriod <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Red}技術暫停只能在回合開始前的「凍結時間」內輸入！{ChatColors.Default}");
            return;
        }
        // ------------------------------------------------

        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        string teamKey = (playerMatchTeam == matchzyTeam1) ? "matchzyTeam1" : "matchzyTeam2";

        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 已經沒有可用的技術暫停次數！");
            return;
        }

        // 執行暫停
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = playerMatchTeam.teamName; 

        PrintToAllChat($" 隊伍 {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫停將在 \u0004300秒\u0001 後自動解除，或雙方輸入 \u0004.up\u0001 解除。");

        techPauseAutoUnpauseTimer?.Kill();
        // 已修正為 300.0f
        techPauseAutoUnpauseTimer = AddTimer(30.0f, () =>
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
        techPausesLeft["matchzyTeam1"] = 1;
        techPausesLeft["matchzyTeam2"] = 1;
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = null;
    }
}
