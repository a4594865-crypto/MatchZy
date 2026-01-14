using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers; // 加入這一行才能識別 TimerFlags
namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<CsTeam, bool> teamReadyOverride = new() {
        {CsTeam.Terrorist, false},
        {CsTeam.CounterTerrorist, false},
        {CsTeam.Spectator, false}
    };

    public bool allowForceReady = true;

    public bool IsTeamsReady()
    {
        return IsTeamReady((int)CsTeam.CounterTerrorist) && IsTeamReady((int)CsTeam.Terrorist);
    }

    public bool IsSpectatorsReady()
    {
        return IsTeamReady((int)CsTeam.Spectator);
    }

    public bool IsTeamReady(int team)
    {
        // if (matchStarted) return true;

        int minPlayers = GetPlayersPerTeam(team);
        int minReady = GetTeamMinReady(team);
        (int playerCount, int readyCount) = GetTeamPlayerCount(team, false);

        Log($"[IsTeamReady] team: {team} minPlayers:{minPlayers} minReady:{minReady} playerCount:{playerCount} readyCount:{readyCount}");

        if (team == (int)CsTeam.Spectator && minReady == 0)
        {
            return true;
        }

        if (readyAvailable && playerCount == 0)
        {
            // We cannot ready for veto with no players, regardless of force status or min_players_to_ready.
            return false;
        }

        if (playerCount == readyCount && playerCount >= minPlayers)
        {
            return true;
        }

        if (IsTeamForcedReady((CsTeam)team) && readyCount >= minReady)
        {
            return true;
        }

        return false;
    }

    public int GetPlayersPerTeam(int team)
    {
        if (team == (int)CsTeam.CounterTerrorist || team == (int)CsTeam.Terrorist) return matchConfig.PlayersPerTeam;
        if (team == (int)CsTeam.Spectator) return matchConfig.MinSpectatorsToReady;
        return 0;
    }

    public int GetTeamMinReady(int team)
    {
        if (team == (int)CsTeam.CounterTerrorist || team == (int)CsTeam.Terrorist) return matchConfig.MinPlayersToReady;
        if (team == (int)CsTeam.Spectator) return matchConfig.MinSpectatorsToReady;
        return 0;
    }

    public (int, int) GetTeamPlayerCount(int team, bool includeCoaches = false)
    {
        int playerCount = 0;
        int readyCount = 0;
        foreach (var key in playerData.Keys)
        {
            if (!playerData[key].IsValid) continue;
            if (playerData[key].TeamNum == team) {
                playerCount++;
                if (playerReadyStatus[key] == true) readyCount++;
            }
        }
        return (playerCount, readyCount);
    }

    public bool IsTeamForcedReady(CsTeam team) {
        return teamReadyOverride[team];
    }

    [ConsoleCommand("css_forceready", "Force-readies the team")]
    public void OnForceReadyCommandCommand(CCSPlayerController? player, CommandInfo? command)
    {
        Log($"{readyAvailable} {isMatchSetup} {allowForceReady} {IsPlayerValid(player)}");
        if (!readyAvailable || !isMatchSetup || !allowForceReady || !IsPlayerValid(player)) return;

        int minReady = GetTeamMinReady(player!.TeamNum);
        (int playerCount, int readyCount) = GetTeamPlayerCount(player!.TeamNum, false);

        if (playerCount < minReady) 
        {
            // ReplyToUserCommand(player, $"You must have at least {minReady} player(s) on the server to ready up.");
            ReplyToUserCommand(player, Localizer["matchzy.rs.minreadyplayers", minReady]);
            return;
        }

        foreach (var key in playerData.Keys)
        {
            if (!playerData[key].IsValid) continue;
            if (playerData[key].TeamNum == player.TeamNum) {
                playerReadyStatus[key] = true;
                // ReplyToUserCommand(playerData[key], $"Your team was force-readied by {player.PlayerName}");
                ReplyToUserCommand(playerData[key], Localizer["matchzy.rs.forcereadiedby", player.PlayerName]);
            }
        }

        teamReadyOverride[(CsTeam)player.TeamNum] = true;
        CheckLiveRequired();
    }
   // --- 7 秒倒數 + 訊息攔截功能版 ---
    public void StartMatchCountdown()
    {
        if (matchStartCountdownTimer != null) return;

        // 【核心攔截】啟動開關，這會讓 PrintUnreadyPlayers 靜音
        isCountdownActive = true; 

        countdownRemaining = 7; 
        PrintToAllChat($"{ChatColors.Lime}所有玩家已就緒！比賽即將開始...");

        matchStartCountdownTimer = AddTimer(1.0f, () => {
            Server.NextFrame(() => {
                if (countdownRemaining > 0)
                {
                    string color = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
                    PrintToAllChat($"倒數：{color}{countdownRemaining}");

                    if (countdownRemaining <= 3)
                    {
                        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
                        {
                            p.ExecuteClientCommand("play sounds/ui/panorama/popup_reveal_01.vsnd");
                        }
                    }
                    countdownRemaining--;
                }
                else
                {
                    matchStartCountdownTimer?.Kill();
                    matchStartCountdownTimer = null;

                    // 【核心攔截】倒數結束，解除開關，恢復正常訊息顯示
                    isCountdownActive = false; 

                    if (matchStarted) return;
                    HandleMatchStart(); 
                }
            });
        }, TimerFlags.REPEAT);
    }

    public void CancelMatchCountdown(string reason)
    {
        if (matchStartCountdownTimer != null)
        {
            matchStartCountdownTimer.Kill();
            matchStartCountdownTimer = null;
            
            // 【核心攔截】中止時立即解除攔截，確保「倒數中止」紅字能顯示
            isCountdownActive = false; 

            PrintToAllChat($"{ChatColors.Red}倒數中止：{reason}");
        }
    }
}
