using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.IO;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
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
            ResetTechPauseFiles();
            ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]);
            return;
        }
        if (IsPostGamePhase())
        {
            ResetTechPauseFiles();
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

        string sideKey = "";
        if (player.Team == CsTeam.CounterTerrorist) sideKey = "ct";
        else if (player.Team == CsTeam.Terrorist) sideKey = "t";
        else return;

        string lockFilePath = $"tech_lock_{sideKey}.txt";

        if (File.Exists(lockFilePath))
        {
            PrintToPlayerChat(player, $" \u0002[MatchZy] \u0007你們隊伍本場比賽的技術暫停次數（1次）已經用盡！");
            return;
        }

        try
        {
            File.WriteAllText(lockFilePath, "used");
        }
        catch (Exception) { }
        
        Team playerTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        PauseMatch(player, command);
    }

    private void ResetTechPauseFiles()
    {
        try
        {
            if (File.Exists("tech_lock_ct.txt")) File.Delete("tech_lock_ct.txt");
            if (File.Exists("tech_lock_t.txt")) File.Delete("tech_lock_t.txt");
        }
        catch (Exception) { }
    }
}
