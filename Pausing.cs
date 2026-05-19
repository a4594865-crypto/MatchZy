using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟 宣告我們自訂的狀態標記，用來保護 300 秒鬧鐘不重複執行
    public bool isMyTechPausing = false;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟【自動刷新開關】：如果目前不是正式比賽 Live 狀態（例如打 .restart 或是剛換新地圖）
        // 玩家只要打指令，這裡第一時間直接清空次數，下一場完美恢復！
        if (!isMatchLive)
        {
            technicalPauseUsed.Clear();
            isMyTechPausing = false;
        }

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

        if (maxTechPausesAllowed.Value <= 0) return;

        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        // 🌟【鐵腕攔截次數】：如果該陣營使用次數已滿，直接阻擋並噴語言包！
        if (technicalPauseUsed.ContainsKey(playerTeam) && technicalPauseUsed[playerTeam] >= maxTechPausesAllowed.Value)
        {
            PrintToPlayerChat(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }

        // 🌟 通過檢查，該隊伍計數器 +1
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 🌟 標記技術暫停正式啟動，並調用原廠定格暫停
        isMyTechPausing = true;
        PauseMatch(player, command);

        // 全服廣播綠色通知
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 🌟【300秒非同步不吃效能計時器】
        Task.Run(async () => {
            await Task.Delay(30000); // 睡眠 300 秒 (300000 ms)

            // 安全投遞回 CS2 主線程執行解除
            Server.NextFrame(() => {
                // 防呆：如果 300 秒內玩家手動打 .unpause 解除了，鬧鐘直接退出
                if (!isPaused || !isMyTechPausing) return;

                // 🌟【解除核心】：修改全域暫停變數，並下達最高權限原生解除指令（無分號）
                isPaused = false;
                isMyTechPausing = false;
                Server.ExecuteCommand("mp_unpause_match");

                // 🌟【物理粉碎鎖】：徹底幹掉 MatchZy 原廠後台殘留的暫停狀態計時器，防止伺服器再次回彈
                if (pausedStateTimer != null)
                {
                    pausedStateTimer.Kill();
                    pausedStateTimer = null;
                }

                // 清空原廠的點頭同意數據
                unpauseData["ct"] = false;
                unpauseData["t"] = false;

                // 廣播強制解除通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }
}
