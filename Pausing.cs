using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟【修改】：砍掉官方擺爛的 return; 讓技術暫停指令復活！

        if (!isMatchLive) return;

        // Treating .tech command as .forcepause if it is used via server console.
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        if (isPaused)
        {
            // ReplyToUserCommand(player, "Match is already paused!");
            ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            return;
        }
        if (IsHalfTimePhase())
        {
            // ReplyToUserCommand(player, "You cannot use this command during halftime.");
            ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]); ;
            return;
        }
        if (IsPostGamePhase())
        {
            // ReplyToUserCommand(player, "You cannot use this command after the game has ended.");
            ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]);
            return;
        }
        if (IsTacticalTimeoutActive())
        {
            // ReplyToUserCommand(player, "You cannot use this command when tactical timeout is active.");
            ReplyToUserCommand(player, Localizer["matchzy.pause.tacticaltimeout"]);
            return;
        }

        if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None) return;

        if (!techPauseEnabled.Value && player != null)
        {
            PrintToPlayerChat(player, Localizer["matchzy.ready.techpausenotenabled"]);
            return;
        }

        // 🌟【官方原本的檢查】：if (maxTechPausesAllowed.Value <= 0) return; 
        // 🌟【修改】：直接拿掉官方檢查，因為我們要死鎖 1 次，不需要去讀取設定檔的 2 次。

        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        // 如果這個隊伍還沒有紀錄，先幫它初始化為 0 次
        if (!technicalPauseUsed.ContainsKey(playerTeam))
        {
            technicalPauseUsed[playerTeam] = 0;
        }

        // 🌟【修改】：這裡直接寫死判定「>= 1」。只要這隊這場打過 1 次了，直接拒絕！
        if (technicalPauseUsed[playerTeam] >= 1)
        {
            PrintToPlayerChat(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }

        // 🌟【新增】：通過上面只能用 1 次的檢查後，次數累加，並直接去跑原廠的暫停流程！
        technicalPauseUsed[playerTeam]++;
        PauseMatch(player, command);
    }
}
