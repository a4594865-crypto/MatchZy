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
    // 🌟【最穩固的計數器】：改用 CsTeam 記憶體列舉（CT 或 T），100% 精準鎖定當下按下指令的那個陣營
    public Dictionary<CsTeam, int> customTechPauseUsed = new();

    // 🌟【總開關 1】：換地圖、.restart 重開比賽時，MatchZy 核心必經此處，直接清空計數
    public void InitTechPauseFileCleaner()
    {
        customTechPauseUsed.Clear();
    }

    // 🌟【總開關 2】：對接你在 MatchZy.cs 的 ResetMatch 管道，確保雙重保險
    public void ResetTechPauseOnMatchReset()
    {
        customTechPauseUsed.Clear();
    }

    // 🌟【總開關 3】：開機或手動重載插件時點火
    public void TechPauseOnPluginLoad()
    {
        customTechPauseUsed.Clear();
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 1. 【熱身期保險】：只要比賽還不是 Live 狀態，一律無限重置次數，不准鎖死
        if (!isMatchLive) 
        {
            customTechPauseUsed.Clear();
        }

        if (!isMatchLive) return;

        if (player == null) return;

        // 2. 狀態檢查（利用 MatchZy 內建變數攔截）
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
            customTechPauseUsed.Clear();
            ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]);
            return;
        }
        if (IsTacticalTimeoutActive())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.tacticaltimeout"]);
            return;
        }

        // 防呆：觀察者或無團隊不准暫停
        if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None) return;

        if (!techPauseEnabled.Value)
        {
            PrintToPlayerChat(player, Localizer["matchzy.ready.techpausenotenabled"]);
            return;
        }

        // 🌟【核心攔截】：直接抓玩家目前的肉體團隊 (CsTeam.CounterTerrorist 或 CsTeam.Terrorist)
        CsTeam callingTeam = player.Team;

        // 如果這個陣營這場比賽已經用過暫停了，立刻攔截並噴語言包拒絕
        if (customTechPauseUsed.ContainsKey(callingTeam) && customTechPauseUsed[callingTeam] >= 1)
        {
            // 抓取原廠隊伍物件用來塞進語言包的 {0} 佔位符
            Team playerTeam = (callingTeam == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
            string teamNameForMsg = (playerTeam != null && !string.IsNullOrEmpty(playerTeam.teamName)) ? playerTeam.teamName : (callingTeam == CsTeam.CounterTerrorist ? "CT" : "T");
            
            ReplyToUserCommand(player, Localizer["matchzy.pause.notechpauseleft", teamNameForMsg]);
            return;
        }

        // 🌟【寫入紀錄】：通過檢查，該陣營的暫停次數扣除（記憶體計數 +1）
        if (!customTechPauseUsed.ContainsKey(callingTeam)) customTechPauseUsed[callingTeam] = 0;
        customTechPauseUsed[callingTeam]++;

        // 🌟【原生強制暫停】：不再呼叫原廠 PauseMatch！我們直接改寫全域變數，並對 CS2 引擎下達原生技術暫停指令
        isPaused = true;
        Server.ExecuteCommand("mp_pause_match");

        // 廣播通知全服玩家（自訂系統訊息標籤）
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 🌟【非同步極致優化鬧鐘】：300秒（300000毫秒）倒數
        Task.Run(async () => {
            await Task.Delay(30000); 

            // 安全投遞回 CS2 主線程執行
            Server.NextFrame(() => {
                // 安全防呆：如果玩家在 300 秒內已經手動打 .unpause 解除了，鬧鐘直接退場
                if (!isPaused) return; 

                // 🌟【原生強制解除】：100% 繞過原廠 null 攔截！我們自己改寫狀態，並向核心引擎下達解除指令
                isPaused = false;
                Server.ExecuteCommand("mp_unpause_match");

                // 噴出時間到的自訂通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }
}
