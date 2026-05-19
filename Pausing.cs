using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    // 修正：使用字串 "teamA" 與 "teamB" 作為字典 Key，這兩個名字可以完美對應 MatchZy 內部的物件屬性
    public Dictionary<string, int> techPausesLeft = new() { { "teamA", 1 }, { "teamB", 1 } };

    // 用來控制 300 秒自動解除暫停的計時器變數
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

        // 4. 檢查玩家是否在有效隊伍 (CsTeam.Terrorist 或 CsTeam.CounterTerrorist)
        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        // 5. 【架構修正】利用 MatchZy 的 reverseTeamSides 字典，精確將當前陣營映射到真實隊伍物件
        Team playerMatchTeam;
        if (player.Team == CsTeam.CounterTerrorist)
        {
            playerMatchTeam = reverseTeamSides["CT"];
        }
        else
        {
            playerMatchTeam = reverseTeamSides["TERRORIST"];
        }

        string teamKey = "";
        string currentTeamName = playerMatchTeam.teamName;

        // 透過 MatchZy 正確的成員變上名稱「teamA」與「teamB」進行比對
        if (playerMatchTeam == teamA)
        {
            teamKey = "teamA";
        }
        else if (playerMatchTeam == teamB)
        {
            teamKey = "teamB";
        }
        else
        {
            // 安全防護：如果不屬於任何一隊（例如獨立觀眾），則不處理
            return;
        }

        // 6. 檢查次數限制
        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" [\u0002MatchZy\u0001] {ChatColors.Green}{currentTeamName}{ChatColors.Default} 已經沒有可用的技術暫停次數！");
            return;
        }

        // 7. 扣除次數並執行 CS2 原生暫停
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = currentTeamName; 

        PrintToAllChat($" [\u0002MatchZy\u0001] 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{techPausesLeft[teamKey]} 次。");
        PrintToAllChat($" [\u0002MatchZy\u0001] 暫停將在 \u0004300秒\u0001 後自動解除，或雙方輸入 \u0004.unpause\u0001 解除。");

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
                PrintToAllChat($" [\u0002MatchZy\u0001] 技術暫停已達\u0002 300 秒 \u0001上限，系統自動解除暫停！");
            }
            techPauseAutoUnpauseTimer = null;
        });
    }

    // 這裡修正：建立一個統一重設次數的方法
    public void ResetTechPauseCount()
    {
        // 對應修正後的真實隊伍 Key
        techPausesLeft["teamA"] = 1;
        techPausesLeft["teamB"] = 1;
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = null;
    }
}
