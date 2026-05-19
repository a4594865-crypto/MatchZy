using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Linq;

namespace MatchZy;

public partial class MatchZy
{
    // 如果這裡報錯，請檢查 MatchZy.cs 是否重複定義了這個變數
    // 確保這裡的 Dictionary 值一定是 int
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (!isMatchLive) return;
        if (player == null) { ForcePauseMatch(player, command); return; }

        // 1. 基礎檢查
        if (isPaused) { ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]); return; }
        if (IsHalfTimePhase()) { ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]); return; }
        if (IsPostGamePhase()) { ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]); return; }

        // 2. 取得凍結時間 (FreezePeriod 是底層 int 變數，大於 0 表示正在凍結)
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (gameRules == null || gameRules.FreezePeriod <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}技 術 暫 停 只 能 在 回 合 開 始 前 輸 入{ChatColors.Default}");
            return;
        }

        // 3. 隊伍判斷
        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        string teamKey = (playerMatchTeam == matchzyTeam1) ? "matchzyTeam1" : "matchzyTeam2";

        // 4. 強制轉型檢查 (解決 bool vs int 衝突)
        // 如果你的 Dictionary 其實存的是 bool，這裡會報錯，但因為上面定義是 int，這裡應順利運作
        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 已 經 沒 有 可 用 技 術 暫 停 次 數");
            return;
        }

        // 5. 執行暫停
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        PrintToAllChat($" 隊伍 {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 啟用了技術暫停。剩餘：{techPausesLeft[teamKey]} 次。");

        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = AddTimer(30.0f, () =>
        {
            if (isPaused)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                PrintToAllChat($" 技 術 暫 停 已 達 300 秒，系 統 自 動 解 除！");
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
