using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MatchZy;

public partial class MatchZy
{
    // 🌟 核心防線：全域獨立計數器（以肉體 CsTeam 為準），換邊、重開絕對不會錯亂
    public Dictionary<CsTeam, int> customTechPauseUsed = new();
    
    // 🌟 狀態標記：用來記錄目前是不是正處於我們的「300秒技術暫停」期間
    public bool isMyTechPausing = false;

    // 🌟【自動刷新大腦 1】：偷聽遊戲重開事件（.restart、mp_restartgame、換地圖都會觸發）
    [GameEventHandler("cs_match_reset")]
    public HookResult OnMatchReset(EventCsMatchReset @event, GameEventInfo info)
    {
        customTechPauseUsed.Clear();
        isMyTechPausing = false;
        return HookResult.Continue;
    }

    // 🌟【自動刷新大腦 2】：偷聽地圖載入/宣告冷卻結束事件（確保換圖 100% 刷新）
    [GameEventHandler("round_announce_match_start")]
    public HookResult OnRoundAnnounceMatchStart(EventRoundAnnounceMatchStart @event, GameEventInfo info)
    {
        customTechPauseUsed.Clear();
        isMyTechPausing = false;
        return HookResult.Continue;
    }

    // 🌟 這是提供給 ConsoleCommands.cs 呼叫的「真正具備 300 秒限制與攔截」的實體方法！
    public void ExecuteCustomTechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 未開賽或熱身期保險：一律無限重置次數
        if (!isMatchLive) 
        {
            customTechPauseUsed.Clear();
            isMyTechPausing = false;
        }

        if (!isMatchLive) return;
        if (player == null) return;

        // 狀態檢查（防呆）
        if (isPaused)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            return;
        }
        if (IsHalfTimePhase() || IsPostGamePhase() || IsTacticalTimeoutActive())
        {
            return;
        }
        if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None) return;

        if (!techPauseEnabled.Value)
        {
            PrintToPlayerChat(player, Localizer["matchzy.ready.techpausenotenabled"]);
            return;
        }

        // 🌟 抓取當前玩家的肉體陣營（CT 或 T）
        CsTeam callingTeam = player.Team;

        // 🌟【次數鐵腕攔截】：只要這個陣營已經暫停過 1 次，直接噴語言包阻擋，絕對進不去！
        if (customTechPauseUsed.ContainsKey(callingTeam) && customTechPauseUsed[callingTeam] >= 1)
        {
            Team playerTeam = (callingTeam == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
            string teamNameForMsg = (playerTeam != null && !string.IsNullOrEmpty(playerTeam.teamName)) ? playerTeam.teamName : (callingTeam == CsTeam.CounterTerrorist ? "CT" : "T");
            
            ReplyToUserCommand(player, Localizer["matchzy.pause.notechpauseleft", teamNameForMsg]);
            return;
        }

        // 🌟 透過檢查，該陣營技術暫停次數在記憶體中 +1
        if (!customTechPauseUsed.ContainsKey(callingTeam)) customTechPauseUsed[callingTeam] = 0;
        customTechPauseUsed[callingTeam]++;

        // 🌟 標記暫停狀態，並直接執行原廠最安全的暫停方法（讓時間與肉體完美定格）
        isMyTechPausing = true;
        PauseMatch(player, command);

        // 全服廣播技術暫停通知
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 自動 解 除");

        // 🌟【300秒非同步不吃效能鬧鐘】
        Task.Run(async () => {
            await Task.Delay(30000); // 準時睡眠 300 秒

            // 安全投遞回 CS2 主線程執行解除
            Server.NextFrame(() => {
                // 防呆：如果 300 秒內，玩家自己打 .unpause 解除了，鬧鐘直接退場
                if (!isPaused || !isMyTechPausing) return; 

                // 🌟【解除核心】：直接利用 CS2 引擎最高權限，強制解除暫停
                isPaused = false;
                isMyTechPausing = false;
                Server.ExecuteCommand("mp_unpause_match;");

                // 清空原廠的點頭同意數據，防止殘留
                unpauseData["ct"] = false;
                unpauseData["t"] = false;

                // 噴出時間到廣播
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }
}
