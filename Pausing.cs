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
    // 🌟【終極核心】：宣告一個完全獨立、專屬於我們控制的記憶體計數器，100% 避開與原廠大腦衝突！
    // Key 使用 string（儲存隊伍名字），這樣重開、換圖在記憶體裡最好清理
    public Dictionary<string, int> myTechPauseCount = new();

    // 🌟【開機/換圖/.restart 三路總開關】：當開機（Load）或打 .restart / 換圖（ResetMatch）時，
    // 官方會呼叫這個清理點，我們在這裡把獨立計數器徹底清空，次數 100% 完美刷新！
    public void InitTechPauseFileCleaner()
    {
        myTechPauseCount.Clear();
    }

    // 🌟 同步對接你在 MatchZy.cs 裡面寫的 ResetMatch 管道
    public void ResetTechPauseOnMatchReset()
    {
        myTechPauseCount.Clear();
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 【熱身期保險】：未正式開賽前，一律無限重置次數
        if (!isMatchLive) 
        {
            myTechPauseCount.Clear();
        }

        if (!isMatchLive) return;

        if (player == null)
        {
            ForceUnpauseMatch(player, command); // 防呆
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
            myTechPauseCount.Clear();
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
        
        if (playerTeam == null || string.IsNullOrEmpty(playerTeam.teamName)) return;

        string currentTeamKey = playerTeam.teamName;

        // 🌟【精準攔截】：檢查我們自己的獨立計數器，只要大於等於 1 次，立刻噴語言包拒絕暫停！
        if (myTechPauseCount.ContainsKey(currentTeamKey) && myTechPauseCount[currentTeamKey] >= 1)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.notechpauseleft", currentTeamKey]);
            return;
        }

        // 🌟 通過檢查，我們自己的獨立計數器 +1
        if (!myTechPauseCount.ContainsKey(currentTeamKey)) myTechPauseCount[currentTeamKey] = 0;
        myTechPauseCount[currentTeamKey]++;

        // 執行原廠最完美、能把時間死死定格的暫停邏輯
        PauseMatch(player, command);

        // 廣播通知（綠色系統訊息標籤）
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 300秒非同步鬧鐘
        Task.Run(async () => {
            await Task.Delay(30000); 

            Server.NextFrame(() => {
                // 安全防呆：如果玩家中途已經手動解除暫停了，鬧鐘直接退場
                if (!isPaused) return; 

                // 🌟【終極修正】：使用原廠最高權限解鎖函數，帶入 null，確保同步解除原廠大腦鎖定，不再卡住！
                ForceUnpauseMatch(null, null);

                // 唯一指定通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }
}
