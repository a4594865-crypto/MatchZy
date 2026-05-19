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
    // 🌟 100% 使用官方全域字典，跟隨每回合肉體陣營，換邊絕對無法賴皮！
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟【開機/重載/換圖/.restart 三路總開關】：不論是開機(Load)、換地圖、還是打 .restart
    // 只要這裡被呼叫，直接把記憶體字典整台清空，次數 100% 刷新！
    public void InitTechPauseFileCleaner()
    {
        technicalPauseUsed.Clear();
    }

    // 🌟 同步對接你在 MatchZy.cs 裡面寫的 ResetMatch 管道
    public void ResetTechPauseOnMatchReset()
    {
        technicalPauseUsed.Clear();
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 【熱身期保險】：未正式開賽前打指令一律無限清空次數
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
            technicalPauseUsed.Clear();
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

        // 🌟【精準抓取】：精準判定目前按下指令的玩家「肉體在 CT 還是 T」
        Team playerTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        if (playerTeam == null) return;

        // 🌟【次數攔截】：直接檢查全域字典，如果該隊伍已經用過 1 次，直接調用繁中語言包拒絕
        if (technicalPauseUsed.ContainsKey(playerTeam) && technicalPauseUsed[playerTeam] >= 1)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }

        // 通過檢查，字典紀錄 +1
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 執行原廠暫停（時間與肉體雙重鎖定）
        PauseMatch(player, command);

        // 廣播通知（綠色系統訊息標籤）
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 300秒非同步鬧鐘
        Task.Run(async () => {
            await Task.Delay(30000); 

            Server.NextFrame(() => {
                if (!isPaused) return; 

                // 手動修正暫停狀態，徹底繞過原廠 ForceUnpauseMatch 的自帶廣播
                isPaused = false;

                // 直接對 CS2 伺服器引擎下達最高解除指令
                Server.ExecuteCommand("mp_unpause_match");

                // 唯一指定通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }
}
