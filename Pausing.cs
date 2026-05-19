using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Linq;

namespace MatchZy;

public partial class MatchZy
{
    // 確保字典定義正確：Key 是字串，Value 是整數 (int)
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

        if (isPaused)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            return;
        }

        // 凍結時間檢查：直接檢查 gameRules.FreezePeriod
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (gameRules != null && gameRules.FreezePeriod <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}技 術 暫 停 只 能 在 回 合 開 始 前 輸 入{ChatColors.Default}");
            return;
        }

        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        // 取得真實隊伍物件
        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        string teamKey = (playerMatchTeam == matchzyTeam1) ? "matchzyTeam1" : (playerMatchTeam == matchzyTeam2 ? "matchzyTeam2" : "");

        if (string.IsNullOrEmpty(teamKey)) return;

        // 這裡檢查 int 是否 <= 0，就不會再報布林值錯誤了
        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 已 經 沒 有 可 用 技 術 暫 停 次 數");
            return;
        }

        // 扣除次數
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;

        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = playerMatchTeam.teamName;

        PrintToAllChat($" 隊伍 {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自 動 解 除，或 雙 方 輸 入 \u0004.up\u0001 解 除。");

        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = AddTimer(30.0f, () =>
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
        techPausesLeft["matchzyTeam1"] = 1;
        techPausesLeft["matchzyTeam2"] = 1;
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = null;
    }
}
