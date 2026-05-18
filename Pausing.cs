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

    // 🌟 跨檔案核心變數，讓 ConsoleCommands.cs 可以辨識當前是否為技術暫停
    public bool isTechPause = false; 

    // 用來記錄哪支隊伍同意解除技術暫停的暫存區
    public HashSet<string> techUnpauseVotes = new HashSet<string>();
    
    // 用來管理倒數自動恢復比賽的技術暫停計時器
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseTimer = null;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟 徹底砍掉原本 WIP 的 return; 讓這個檔案活過來！

        if (!isMatchLive) return;

        // Treating .tech command as .forcepause if it is used via server console.
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

        // 🌟【自動讀取設定檔：檢查次數限制】
        // 自動去讀取你的 matchzy_max_tech_pauses_allowed 設定
        if (maxTechPausesAllowed.Value <= 0) return;

        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        if (!technicalPauseUsed.ContainsKey(playerTeam))
        {
            technicalPauseUsed[playerTeam] = 0;
        }

        if (technicalPauseUsed[playerTeam] >= maxTechPausesAllowed.Value)
        {
            PrintToPlayerChat(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }

        // --- 核心技術暫停啟動邏輯 ---
        technicalPauseUsed[playerTeam]++; 
        isTechPause = true;               
        isPaused = true;                 
        techUnpauseVotes.Clear();         

        // 🌟【修復沒反應 Bug 核心 1】：強制使用硬性暫停，不管什麼時候按，伺服器先「立刻卡死、不能動彈」
        Server.ExecuteCommand("mp_pause_match;");

        // 🌟【自動讀取設定檔：暫停秒數】
        // 自動讀取你的 matchzy_tech_pause_duration 設定（預設 300）
        int duration = techPauseDuration.Value; 
        if (duration <= 0) duration = 300; // 防呆機制，如果設定檔出錯就用 300 秒

        // 🌟【修復沒反應 Bug 核心 2】：動態把官方暫停時間改成設定檔裡的秒數
        Server.ExecuteCommand($"mp_team_timeout_time {duration};");

        // 🌟【修復沒反應 Bug 核心 3】：強制呼叫官方暫停 UI，讓畫面正上方噴出大大的倒數秒數！
        if (player.TeamNum == 2)
        {
            Server.ExecuteCommand("timeout_terrorist_start;");
        }
        else if (player.TeamNum == 3)
        {
            Server.ExecuteCommand("timeout_ct_start;");
        }

        // 註冊安全計時器，時間到（自動讀取 duration）自動解開比賽，並把官方預設戰術暫停改回 90 秒
        techPauseTimer = AddTimer(duration, () => {
            if (isTechPause) {
                isTechPause = false;
                isPaused = false;
                techUnpauseVotes.Clear();
                Server.ExecuteCommand("mp_unpause_match;");
                Server.ExecuteCommand("timeout_ct_stop;");
                Server.ExecuteCommand("timeout_terrorist_stop;");
                Server.ExecuteCommand("mp_team_timeout_time 90;"); // 🌟 完好還原：不污染一般的 .p 暫停
                if (techPauseTimer != null) techPauseTimer = null;
            }
        });
    }
}
