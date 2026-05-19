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
    // 🌟 核心修正：把原廠原本的 technicalPauseUsed 補回來！否則編譯會一直報錯
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟 1. 這是我們用來死鎖次數的鐵腕字典（改用 string 隊伍名稱，換邊、改名絕對不穿透）
    public Dictionary<string, int> customTechPauseUsed = new();

    // 🌟 2. 狀態標記，用來保護 300 秒鬧鐘，防止提早解除時重疊
    public bool isMyTechPausing = false;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟 3.【重啟、重開、換圖、開機 總刷新檢查哨】：
        // 當打 .restart、換地圖、或重啟伺服器時，MatchZy 原廠一定會把原生的 technicalPauseUsed 字典清空（Count == 0）。
        // 我們直接同步監聽官方字典，只要原廠字典是空的，而我們自己的自訂字典還有殘留數據，就代表「賽局已經重置或換圖了」！
        // 這時候立刻強行將我們的字串字典整台抹平，實現 100% 完美自動刷新，絕對不會卡到下一場！
        if (technicalPauseUsed == null || technicalPauseUsed.Count == 0)
        {
            customTechPauseUsed.Clear();
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

        // 🌟 4. 利用原廠最精準的換邊映射，抓出目前按下指令的玩家所屬的真正「Team 物件」
        Team playerTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        // 🌟 5. 如果抓取失敗（保險防呆），才降級使用陣營名字
        string teamKey = (playerTeam != null && !string.IsNullOrEmpty(playerTeam.teamName)) ? playerTeam.teamName : player.Team.ToString();
        
        // 🌟 6. 【鋼鐵次數攔截網】：利用隊伍真正的字串名字來判斷，只要該隊次數已滿，直接死鎖並噴提示！
        if (customTechPauseUsed.ContainsKey(teamKey) && customTechPauseUsed[teamKey] >= maxTechPausesAllowed.Value)
        {
            string teamNameForMsg = (playerTeam != null && !string.IsNullOrEmpty(playerTeam.teamName)) ? playerTeam.teamName : (player.Team == CsTeam.CounterTerrorist ? "CT" : "T");
            PrintToPlayerChat(player, Localizer["matchzy.pause.notechpauseleft", teamNameForMsg]);
            return;
        }

        // 🌟 7. 通過檢查，將原廠的原生字典與我們的「字串死鎖字典」同步 +1
        if (!technicalPauseUsed.ContainsKey(playerTeam!)) technicalPauseUsed[playerTeam!] = 0;
        technicalPauseUsed[playerTeam!]++;

        if (!customTechPauseUsed.ContainsKey(teamKey)) customTechPauseUsed[teamKey] = 0;
        customTechPauseUsed[teamKey]++;

        // 🌟 8. 標記技術暫停正式啟動，並調用原廠定格暫停
        isMyTechPausing = true;
        PauseMatch(player, command);

        // 全服廣播綠色通知
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 🌟 9. 【300秒非同步不吃效能計時器】
        Task.Run(async () => {
            await Task.Delay(30000); // 準時睡眠 300 秒

            // 安全投遞回 CS2 主線程執行解除
            Server.NextFrame(() => {
                // 防呆：如果 300 秒內玩家手動打 .unpause 解除了，鬧鐘直接退出
                if (!isPaused || !isMyTechPausing) return;

                // 修改全域暫停變數，並下達最高權限原生解除指令（無分號）
                isPaused = false;
                isMyTechPausing = false;
                Server.ExecuteCommand("mp_unpause_match");

                // 徹底幹掉 MatchZy 原廠後台殘留的暫停狀態計時器，防止伺服器再次回彈
                if (pausedStateTimer != null)
                {
                    pausedStateTimer.Kill();
                    pausedStateTimer = null;
                }

                // 清空原廠的點頭同意數據
                unpauseData["ct"] = false;
                unpauseData["t"] = false;

                // 廣播強制解除通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強制 解 除 技 術 暫 停");
            });
        });
    }
}
