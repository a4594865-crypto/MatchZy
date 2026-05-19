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
    // 🌟 1. 宣告我們自己獨立控制的記憶體計數器，使用 CsTeam 記憶體列舉，換邊改名絕對穿透不了
    public Dictionary<CsTeam, int> customTechPauseUsed = new();
    
    // 🌟 2. 狀態標記，用來保護 300 秒鬧鐘不會在玩家提早解除時重疊
    public bool isMyTechPausing = false;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟【終極刷新防線】：只要目前比賽不是正式 Live 狀態（比如打 .restart 回到熱身、或者換新地圖剛進來）
        // 玩家只要打指令，這裡第一時間直接清空計數器，次數 100% 完美刷新！
        if (!isMatchLive) 
        {
            customTechPauseUsed.Clear();
            isMyTechPausing = false;
        }

        if (!isMatchLive) return;

        // 如果是伺服器控制台發送，走原廠強制暫停
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // 3. 原廠正統狀態檢查（防呆）
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

        if (!techPauseEnabled.Value)
        {
            PrintToPlayerChat(player, Localizer["matchzy.ready.techpausenotenabled"]);
            return;
        }

        // 🌟 4. 抓取當前按下指令玩家的肉體陣營（CT 或者是 T）
        CsTeam callingTeam = player.Team;

        // 🌟 5.【鐵腕攔截】：只要這個陣營在這場比賽中已經用過 1 次，直接噴語言包阻擋，死活不放行！
        if (customTechPauseUsed.ContainsKey(callingTeam) && customTechPauseUsed[callingTeam] >= 1)
        {
            Team playerTeam = (callingTeam == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
            string teamNameForMsg = (playerTeam != null && !string.IsNullOrEmpty(playerTeam.teamName)) ? playerTeam.teamName : (callingTeam == CsTeam.CounterTerrorist ? "CT" : "T");
            
            ReplyToUserCommand(player, Localizer["matchzy.pause.notechpauseleft", teamNameForMsg]);
            return;
        }

        // 🌟 6. 通過檢查，該陣營技術暫停次數在記憶體中 +1
        if (!customTechPauseUsed.ContainsKey(callingTeam)) customTechPauseUsed[callingTeam] = 0;
        customTechPauseUsed[callingTeam]++;

        // 🌟 7. 啟動技術暫停標記，並呼叫原廠最完美的定格暫停方法
        isMyTechPausing = true;
        PauseMatch(player, command);

        // 全服廣播自訂的技術暫停訊息
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 🌟 8.【300秒非同步完美計時鬧鐘】
        Task.Run(async () => {
            await Task.Delay(30000); // 準時睡眠 300 秒

            // 安全投遞回 CS2 主線程執行解除
            Server.NextFrame(() => {
                // 防呆：如果 300 秒內玩家自己手動打 .unpause 解除了，鬧鐘直接退場
                if (!isPaused || !isMyTechPausing) return; 

                // 🌟 9.【解除核心】：直接修改全域變數，並下達最高權限原生解除指令
                isPaused = false;
                isMyTechPausing = false;
                Server.ExecuteCommand("mp_unpause_match;");

                // 🌟 10.【終極粉碎】：徹底殺死 MatchZy 後台殘留的暫停狀態計時器，防止原廠再次強行覆蓋暫停
                if (pausedStateTimer != null)
                {
                    pausedStateTimer.Kill();
                    pausedStateTimer = null;
                }

                // 清空點頭數據
                unpauseData["ct"] = false;
                unpauseData["t"] = false;

                // 噴出時間到廣播
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }
}
