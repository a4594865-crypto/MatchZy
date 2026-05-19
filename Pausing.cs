using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy
{
    public partial class MatchZy
    {
        // 紀錄隊伍已使用的 .tech 次數 (隊伍代號: 2 = T, 3 = CT)
        public Dictionary<int, int> techPauseCount = new Dictionary<int, int>() { { 2, 0 }, { 3, 0 } };
        // 自動解除暫停的計時器
        public CounterStrikeSharp.API.Modules.Timers.Timer? techAutoUnpauseTimer = null;

        public void PauseMatch(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isMatchLive) return;

            if (isPaused)
            {
                ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
                return;
            }

            // 如果是伺服器 RCON 或管理員強制暫停，直接走 Force 邏輯
            if (player == null || IsPlayerAdmin(player))
            {
                ForcePauseMatch(player, command);
                StartTechTimer("Admin");
                return;
            }

            int teamNum = player.TeamNum;
            if (teamNum != 2 && teamNum != 3) return; // 觀戰者不可暫停

            // 檢查次數限制：每隊只能 1 次
            if (techPauseCount.ContainsKey(teamNum) && techPauseCount[teamNum] >= 1)
            {
                PrintToPlayerChat(player, $"{chatPrefix} {ChatColors.Red}貴隊本場比賽的技術暫停 (.tech) 次數已達上限 (1次)！");
                return;
            }

            // 扣除次數並執行暫停
            techPauseCount[teamNum]++;
            string teamName = (teamNum == 2) ? reverseTeamSides["TERRORIST"].teamName : reverseTeamSides["CT"].teamName;
            
            Server.ExecuteCommand("mp_pause_match;");
            isPaused = true;
            unpauseData["pauseTeam"] = teamName;
            unpauseData["ct"] = false;
            unpauseData["t"] = false;

            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{teamName} {ChatColors.Default}請求了技術暫停。剩餘次數：0");

            // 啟動 300 秒自動解除計時器
            StartTechTimer(teamName);
        }

        // 啟動 300 秒自動解除暫停邏輯
        public void StartTechTimer(string teamName)
        {
            // 安全清理舊計時器
            if (techAutoUnpauseTimer != null)
            {
                techAutoUnpauseTimer.Kill();
                techAutoUnpauseTimer = null;
            }

            // 建立一個 300 秒的計時器
            techAutoUnpauseTimer = AddTimer(300.0f, () =>
            {
                if (isPaused)
                {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.LightRed}技術暫停已滿 300 秒，系統自動強制解除暫停！");
                    Server.ExecuteCommand("mp_unpause_match;");
                    isPaused = false;
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;
                    
                    if (pausedStateTimer != null)
                    {
                        pausedStateTimer.Kill();
                        pausedStateTimer = null;
                    }
                }
                techAutoUnpauseTimer = null;
            });
        }

        // 用於手動取消暫停時，順便殺掉 300 秒計時器
        public void ClearTechTimer()
        {
            if (techAutoUnpauseTimer != null)
            {
                techAutoUnpauseTimer.Kill();
                techAutoUnpauseTimer = null;
            }
        }

        // 用於重置所有技術暫停數據
        public void ResetTechPauseData()
        {
            techPauseCount[2] = 0;
            techPauseCount[3] = 0;
            ClearTechTimer();
        }
    }
}
