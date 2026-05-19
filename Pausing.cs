using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    // 🌟【終極修復】：改用不隨換邊錯亂的陣營字串（"CT" 或 "T"）來當作獨立鎖定鑰匙
    public static Dictionary<string, int> customTechPauseLimit = new()
    {
        { "CT", 0 },
        { "T", 0 }
    };
    
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 砍掉官方擺爛的 return; 讓技術暫停指令復活
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

        // 🌟【核心邏輯優化】：直接抓取玩家此時此刻肉體所在的陣營（CT 還是 T）
        string sideKey = "";
        if (player.Team == CsTeam.CounterTerrorist) sideKey = "CT";
        else if (player.Team == CsTeam.Terrorist) sideKey = "T";
        else return;

        // 🌟 鐵律判定：只要這個陣營位置的紀錄大於等於 1，直接攔截噴紅字！
        if (customTechPauseLimit[sideKey] >= 1)
        {
            PrintToPlayerChat(player, $" \u0002[MatchZy] \u0007你們隊伍本場比賽的技術暫停次數（1次）已經用盡！");
            return;
        }

        // 成功通過，把該陣營位置的獨立計數器加 1（換邊時，這個位置的次數會跟著換過去給新隊伍，完全公平）
        customTechPauseLimit[sideKey]++;
        
        // 同步增加原廠變數（維持原廠架構完整，避免後台噴報錯）
        Team playerTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 執行原廠的暫停流程
        PauseMatch(player, command);
    }
}
