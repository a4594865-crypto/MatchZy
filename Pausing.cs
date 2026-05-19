using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Linq;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (!isMatchLive) return;
        if (player == null) { ForcePauseMatch(player, command); return; }

        if (isPaused) { ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]); return; }
        if (IsHalfTimePhase()) { ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]); return; }
        if (IsPostGamePhase()) { ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]); return; }

        // 【最終修正】直接讀取原生 GameRules 變數，避開所有自定義屬性問題
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        
        // 如果找不到規則或 FreezePeriod <= 0，代表不在凍結時間，禁止暫停
        if (gameRules == null || gameRules.FreezePeriod <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}技 術 暫 停 只 能 在 回 合 開 始 前 的「凍 結 時 間」內 輸 入{ChatColors.Default}");
            return;
        }

        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        string teamKey = (playerMatchTeam == matchzyTeam1) ? "matchzyTeam1" : (playerMatchTeam == matchzyTeam2 ? "matchzyTeam2" : "");
        
        if (string.IsNullOrEmpty(teamKey)) return;

        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 已 經 沒 有 可 用 技 術 暫 停 次 數");
            return;
        }

        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpauseData["t"] = false; unpauseData["ct"] = false; unpauseData["pauseTeam"] = playerMatchTeam.teamName; 

        PrintToAllChat($" 隊伍 {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自 動 解 除，或 雙 方 輸 入 \u0004.up\u0001 解 除。");

        techPauseAutoUnpauseTimer?.Kill();
        // 修正為 300 秒
        techPauseAutoUnpauseTimer = AddTimer(300.0f, () =>
        {
            if (isPaused)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                PrintToAllChat($" 技 術 暫 停 已 達\u0004 300 秒 \u0001上 限，系 統 自 動 解 除 暫 停！");
            }
            techPauseAutoUnpauseTimer = null;
        });
    }

    public void ResetTechPauseCount()
    {
        techPausesLeft["matchzyTeam1"] = 1; techPausesLeft["matchzyTeam2"] = 1;
        techPauseAutoUnpauseTimer?.Kill(); techPauseAutoUnpauseTimer = null;
    }
}
