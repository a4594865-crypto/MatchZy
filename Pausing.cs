using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    // 🌟【關鍵修復】：宣告一個完全獨立的、不會被原廠邏輯暗中清空的鐵鎖計數器
    public static Dictionary<string, int> customTechPauseLimit = new();
    
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

        // 取得當前隊伍物件
        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        // 🌟【核心修改】：使用隊伍的唯一區別名稱（避免被換邊或重置影響）
        string teamKey = playerTeam.teamName;

        if (!customTechPauseLimit.ContainsKey(teamKey))
        {
            customTechPauseLimit[teamKey] = 0;
        }

        // 🌟 鐵律判定：只要這個隊伍名稱的紀錄大於等於 1，誰來都直接攔截噴紅字！
        if (customTechPauseLimit[teamKey] >= 1)
        {
            PrintToPlayerChat(player, $" \u0002[MatchZy] \u0007你們隊伍（{teamKey}）本場比賽的技術暫停次數（1次）已經用盡！");
            return;
        }

        // 🌟 成功通過，把我們獨立的計數器加 1（這個變數原廠絕對動不到，不會被洗掉）
        customTechPauseLimit[teamKey]++;
        
        // 同步增加原廠變數（維持原廠架構完整）
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 執行原廠的暫停流程
        PauseMatch(player, command);
    }
}
