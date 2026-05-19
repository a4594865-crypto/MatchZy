using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    // 這裡新增：紀錄雙方隊伍剩餘的 .tech 暫停次數（預設每場比賽/地圖 1 次）
    public Dictionary<string, int> techPausesLeft = new() { { "CT", 1 }, { "TERRORIST", 1 } };

    // 這裡新增：用來控制 300 秒自動解除暫停的計時器變數
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;

    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 1. 如果比賽還沒正式開始，不允許技術暫停
        if (!isMatchLive) return;

        // 2. 如果是伺服器 RCON 控制台輸入，直接當作強制暫停
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // 3. 基本檢查：是否已經暫停、是否在半場、是否已結束
        if (isPaused)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
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

        // 4. 檢查玩家是否在有效隊伍 (2 = T, 3 = CT)
        if (player.TeamNum != 2 && player.TeamNum != 3) return;

        // 5. 判定是哪一個陣營發起的
        string teamKey = (player.TeamNum == 3) ? "CT" : "TERRORIST";
        string currentTeamName = (player.TeamNum == 3) ? reverseTeamSides["CT"].teamName : reverseTeamSides["TERRORIST"].teamName;

        // 6. 檢查次數限制
        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $"[\u0002MatchZy\u0001] {ChatColors.Green}{currentTeamName}{ChatColors.Default} 已經沒有可用的技術暫停次數！");
            return;
        }

        // 7. 扣除次數並執行 CS2 原生暫停
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = currentTeamName; 

        PrintToAllChat($"[\u0002MatchZy\u0001] 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{techPausesLeft[teamKey]} 次。");
        PrintToAllChat($"[\u0002MatchZy\u0001] 暫停將在 \u0004300秒\u0001 後自動解除，或雙方輸入 \u0004.unpause\u0001 解除。");

        // 8. 安全防護：如果原本有計時器在跑，先砍掉
        techPauseAutoUnpauseTimer?.Kill();

        // 9. 建立一個 300 秒後觸發的自動解除計時器
        techPauseAutoUnpauseTimer = AddTimer(300.0f, () =>
        {
            if (isPaused)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                PrintToAllChat($"[\u0002MatchZy\u0001] \u0002技術暫停已達 300 秒上限，系統自動解除暫停！\u0001");
            }
            techPauseAutoUnpauseTimer = null;
        });
    }

    // 這裡新增：建立一個統一重設次數的方法
    public void ResetTechPauseCount()
    {
        techPausesLeft["CT"] = 1;
        techPausesLeft["TERRORIST"] = 1;
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = null;
    }
}
