using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    // 配合您的 MatchZy 版本，使用字串 "matchzyTeam1" 與 "matchzyTeam2" 作為字典 Key
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };

    // 用來控制 300 秒自動解除暫停的計時器變數
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 1. 如果是伺服器 RCON 控制台輸入，直接當作強制暫停（不受任何階段限制）
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // 2. 獲取遊戲規則狀態
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerulesproxy").FirstOrDefault()?.GameRules;

        // 3. 關鍵邏輯：只在「比賽已開始(isMatchLive)」且「不在凍結時間(FreezePeriod)」時攔截
        // 如果現在是 Freezetime，這段 if 就不會成立，程式會繼續向下執行，允許暫停
        if (isMatchLive && gameRules != null && !gameRules.FreezePeriod)
        {
            PrintToPlayerChat(player, $" {ChatColors.Red}技術暫停僅限於「回合準備階段 (Freezetime)」使用，比賽進行中無法啟用！");
            return;
        }

        // 4. 基本檢查：是否已經暫停、是否在半場、是否已結束
        if (isPaused)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            return;
        }
        if (IsHalfTimePhase() || IsPostGamePhase())
        {
            ReplyToUserCommand(player, "目前無法進行技術暫停。");
            return;
        }

        // 5. 檢查玩家是否在有效隊伍
        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        string teamKey = (playerMatchTeam == matchzyTeam1) ? "matchzyTeam1" : "matchzyTeam2";

        // 6. 檢查次數限制
        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 已 經 沒 有 可 用 技 術 暫 停 次 數");
            return;
        }

        // 7. 執行暫停
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = playerMatchTeam.teamName;

        PrintToAllChat($" 隊伍 {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自 動 解 除，或 雙 方 輸 入 \u0004.up\u0001 解 除。");

        // 8. 建立 300 秒自動解除計時器
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = AddTimer(30.0f, () =>
        {
            if (isPaused)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                PrintToAllChat($" 技 術 暫 停 已 達\u0004 300 秒 \u0001上 限，系 統 自 動 解 除 暫 停");
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
