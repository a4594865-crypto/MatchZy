using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟 跨檔案核心變數
    public bool isTechPause = false; 

    // 用來記錄哪支隊伍同意解除技術暫停的暫存區
    public HashSet<string> techUnpauseVotes = new HashSet<string>();
    
    // 用來管理倒數自動恢復比賽的技術暫停計時器
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseTimer = null;

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
        if (IsTacticalTimeoutActive())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.tacticaltimeout"]);
            return;
        }

        if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None) return;

        if (!techPauseEnabled.Value && player != null)
        {
            PrintToPlayerChat(player, Localizer["matchzy.ready.techpausenotenabled"]);
            return;
        }

        // --- 次數限制檢查（死死鎖定每隊 2 次） ---
        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        if (!technicalPauseUsed.ContainsKey(playerTeam))
        {
            technicalPauseUsed[playerTeam] = 0;
        }

        if (technicalPauseUsed[playerTeam] >= 2)
        {
            PrintToPlayerChat(player, $"{chatPrefix} {ChatColors.Red}你們隊伍的技術暫停次數（2次）已經用盡了！{ChatColors.Default}");
            return;
        }

        // --- 啟動偽裝成官方暫停的 .tech 邏輯 ---
        technicalPauseUsed[playerTeam]++; 
        isTechPause = true;               
        isPaused = true;                 
        techUnpauseVotes.Clear();         

        // 🌟【最核心改動】：動態把 CS2 官方的暫停時間修改成 300 秒！
        Server.ExecuteCommand("mp_team_timeout_time 300;");

        // 🌟 判斷是哪一隊叫的，並觸發官方對應隊伍的暫停指令
        // 這樣畫面正上方就會立刻跳出 300 秒的大大倒數 UI！
        if (player.TeamNum == 2)
        {
            Server.ExecuteCommand("timeout_terrorist_start");
        }
        else if (player.TeamNum == 3)
        {
            Server.ExecuteCommand("timeout_ct_start");
        }

        // 註冊 300 秒時間到的安全恢復計時器（如果玩家沒打 .up，時間到會自動恢復，並把官方設定調回預設的 90 秒）
        techPauseTimer = AddTimer(300, () => {
            if (isTechPause) {
                isTechPause = false;
                isPaused = false;
                techUnpauseVotes.Clear();
                Server.ExecuteCommand("mp_unpause_match;");
                Server.ExecuteCommand("mp_team_timeout_time 90;"); // 🌟 記得把一般暫停的時間還原回 90 秒
                if (techPauseTimer != null) techPauseTimer = null;
            }
        });
    }
}
