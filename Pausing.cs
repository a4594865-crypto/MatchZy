using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using System.Collections.Generic;

namespace MatchZy
{
    public partial class MatchZy
    {
        // 紀錄隊伍已使用的 .tech 次數 (2 = T 隊, 3 = CT 隊)
        public Dictionary<int, int> techPauseCount = new Dictionary<int, int>() { { 2, 0 }, { 3, 0 } };
        
        // 300 秒自動強制解除暫停的計時器
        public CounterStrikeSharp.API.Modules.Timers.Timer? techAutoUnpauseTimer = null;
        
        // 新增：用來標記目前回合是否正在交火中
        public bool isRoundActive = false; 

        // 核心修正：將函數名稱改成 OnTechCommand，直接接管 .tech 指令
        public void OnTechCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isMatchLive) return;

            // 攔截回合未正式開始（如：凍結時間內、或剛結束時）的使用者
            if (!isRoundActive && player != null && !IsPlayerAdmin(player))
            {
                PrintToPlayerChat(player, $"{chatPrefix} {ChatColors.Red}技術暫停 (.tech) 只能在回合正式開始（凍結時間結束後）才能使用！");
                return;
            }

            if (isPaused)
            {
                ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
                return;
            }

            // 如果是伺服器主機控制台 (RCON) 執行，直接走強制暫停，不消耗次數
            if (player == null)
            {
                ForcePauseMatch(player, command);
                StartTechTimer("Admin");
                return;
            }

            int teamNum = player.TeamNum;
            if (teamNum != 2 && teamNum != 3) return; // 觀戰者或無隊伍不予理會

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

            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{teamName} {ChatColors.Default}請求了技術暫停。剩餘可用次數：0");

            // 啟動 300 秒自動解除計時器
            StartTechTimer(teamName);
        }

        // 啟動 300 秒自動解除暫停邏輯
        public void StartTechTimer(string teamName)
        {
            if (techAutoUnpauseTimer != null)
            {
                techAutoUnpauseTimer.Kill();
                techAutoUnpauseTimer = null;
            }

            techAutoUnpauseTimer = AddTimer(300.0f, () =>
            {
                if (isPaused)
                {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.LightRed}技術暫停已滿 300 秒，雙方未解除，系統自動強制恢復比賽！");
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

        // 用於玩家手動取消暫停時，提早停止 300 秒計時器
        public void ClearTechTimer()
        {
            if (techAutoUnpauseTimer != null)
            {
                techAutoUnpauseTimer.Kill();
                techAutoUnpauseTimer = null;
            }
        }

        // 用於重置所有技術暫停數據與刷新次數
        public void ResetTechPauseData()
        {
            techPauseCount[2] = 0;
            techPauseCount[3] = 0;
            isRoundActive = false; // 重置時安全防護也歸零
            ClearTechTimer();
        }
    }
}
