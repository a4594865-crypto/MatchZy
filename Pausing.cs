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
    // 🌟 使用原廠內建的字典來記錄次數，這樣換圖、.restart 時，原廠主程式會自動幫我們 Clear() 清空，完美刷新！
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟 核心標記：用來防止 300 秒鬧鐘在玩家提早解除時重疊廣播
    public bool isMyCustomTechPausing = false;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟 砍掉原廠原本的 return; 讓技術暫停功能真正活過來！
        
        if (!isMatchLive) return;

        // 如果是伺服器控制台發送，走原廠強制暫停
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // 原廠標準狀態檢查（防呆）
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

        // 🌟 呼叫原廠精準換邊判定：抓出按下指令的玩家目前肉體所屬的真實 Team 物件
        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"]; //
        
        // 🌟【次數鐵腕攔截】：只要這個隊伍已經用過 1 次（或超過設定值），立刻擋下並噴語言包！
        if (technicalPauseUsed.ContainsKey(playerTeam) && technicalPauseUsed[playerTeam] >= maxTechPausesAllowed.Value) //
        {
            PrintToPlayerChat(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]); //
            return;
        }

        // 🌟 通過檢查，原廠字典計數 +1
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 🌟 標記我們的技術暫停啟動，並呼叫原廠最完美的定格暫停
        isMyCustomTechPausing = true;
        PauseMatch(player, command);

        // 全服廣播技術暫停通知
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 🌟【300秒非同步完美計時鬧鐘】
        Task.Run(async () => {
            await Task.Delay(30000); // 準時睡眠 300 秒

            // 安全投遞回 CS2 主線程執行解除
            Server.NextFrame(() => {
                // 防呆：如果 300 秒內玩家自己手動打 .unpause 解除了，鬧鐘直接退場
                if (!isPaused || !isMyCustomTechPausing) return; 

                // 🌟【解除核心】：直接修改全域變數，並下達最高權限原生解除指令
                isPaused = false;
                isMyCustomTechPausing = false;
                Server.ExecuteCommand("mp_unpause_match;");

                // 🌟【終極粉碎】：徹底殺死 MatchZy 後台殘留的暫停狀態計時器，防止原廠再次強行覆蓋暫停
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
