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
    // --- 進階優化版：去秒、分段顏色、結尾音效 ---
    public void StartMatchCountdown()
    {
        // 1. 防止重複觸發
        if (matchStartCountdownTimer != null) return;

        countdownRemaining = 10; 
        PrintToAllChat($"{ChatColors.Lime}所有玩家已就緒！比賽即將開始...");

        // 2. 建立計時器
        matchStartCountdownTimer = AddTimer(1.0f, () => {
            if (countdownRemaining > 0)
            {
                // 顏色邏輯：10-6 秒綠色，5-1 秒紅色
                string color = (countdownRemaining > 5) ? $"{ChatColors.Green}" : $"{ChatColors.Red}";
                
                // 顯示文字：去掉「秒」字
                PrintToAllChat($"倒數：{color}{countdownRemaining}"); 

                // 音效邏輯：最後 3 秒播放音效
                if (countdownRemaining <= 3)
                {
                    // 執行伺服器指令播放音效給全體玩家
                    Server.ExecuteCommand("play sounds/ui/beep02.wav");
                }
                
                countdownRemaining--;
            }
            else
            {
                // 3. 倒數結束：清理計時器並切換回主執行緒
                matchStartCountdownTimer?.Kill();
                matchStartCountdownTimer = null;
                
                Server.NextFrame(() => {
                    if (matchStarted) return; 

                    PrintToAllChat($"{ChatColors.Lime}比賽正式開始！");
                    
                    // 正式執行開賽邏輯 (定義在 Utility.cs)
                    HandleMatchStart(); 
                });
            }
        }, TimerFlags.REPEAT);
    }

    // --- 中止倒數函數 ---
    public void CancelMatchCountdown(string reason)
    {
        if (matchStartCountdownTimer != null)
        {
            matchStartCountdownTimer.Kill();
            matchStartCountdownTimer = null;
            PrintToAllChat($"{ChatColors.Red}倒數中止：{reason}");
        }
    }
}
