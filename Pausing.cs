using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟【開機清理】：給原廠 Load 調用，確保重開機/重載插件時，次數必定歸零
    public void InitTechPauseFileCleaner()
    {
        technicalPauseUsed.Clear();
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟【熱身期保險】：如果比賽還沒正式開始，有人打指令就順便重置次數，徹底防止跨場次殘留！
        if (!isMatchLive) 
        {
            technicalPauseUsed.Clear();
        }

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
            technicalPauseUsed.Clear(); // 賽後自動清空
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

        // 判定目前按指令的玩家肉體在哪個原廠 Team 裡面
        Team playerTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        if (playerTeam == null) return;

        // 🌟【終極修復】：直接檢查 MatchZy 內建的次數記錄字典！
        // 如果這個隊伍已經用過暫停（次數 >= 1）
        if (technicalPauseUsed.ContainsKey(playerTeam) && technicalPauseUsed[playerTeam] >= 1)
        {
            // 🌟 直接呼叫你發現的這行原廠語言包，並把隊伍名稱帶進去（符合 "{0} 沒有更多的技術暫停了！"）
            ReplyToUserCommand(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }

        // 通過檢查，記錄該隊伍已使用 1 次技術暫停
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 1. 執行原廠暫停
        PauseMatch(player, command);

        // 2. 廣播通知（300秒版本）
        Server.PrintToChatAll($" \u0006技 術 暫 停 已 啟 動 將 在 \u0010 5 分 鐘 後 \u0006自 動 解 除");

        // 3. 添加 300 秒的高效能非同步計時器
        AddTimer(300.0f, () => {
            // 安全防呆
            if (!isPaused) return; 

            // 手動將 MatchZy 內部的暫停開關改回 false
            isPaused = false;

            // 直接向 CS2 官方伺服器引擎下達最高權限原生解除暫停指令！
            Server.ExecuteCommand("mp_unpause_match");

            // 顯示 300 秒時間到的專屬綠橘色通知
            Server.PrintToChatAll($" \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
    }
}
