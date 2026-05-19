using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    // 注意：變數與 ResetTechPauseCount 的宣告已在其他檔案完成，此處僅提供方法的實作邏輯

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 1. 如果是控制台 RCON 執行，直接強制暫停
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // 2. 獲取遊戲規則狀態
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerulesproxy").FirstOrDefault()?.GameRules;

        // 3. 檢查限制：必須在 FreezePeriod (準備階段) 才允許繼續
        // 如果不在準備階段，直接提示並中斷，不執行任何動作
        if (gameRules == null || !gameRules.FreezePeriod)
        {
            PrintToPlayerChat(player, $" {ChatColors.Red}技術暫停僅限於「回合準備階段 (Freezetime)」使用！");
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
            PrintToPlayerChat(player, $" {ChatColors.Green}{playerMatchTeam.teamName}{ChatColors.Default} 已經沒有可用的技術暫停次數！");
            return;
        }

        // 7. 扣除次數並執行暫停
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
                PrintToAllChat($" 技術暫停已達\u0004 300 秒 \u0001上限，系統自動解除暫停！");
            }
            techPauseAutoUnpauseTimer = null;
        });
    }
}
