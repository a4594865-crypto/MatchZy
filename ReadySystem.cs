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
    // --- 優化版：加入音效線程安全保護 ---
public void StartMatchCountdown()
    {
        // 1. 防止重複觸發
        if (matchStartCountdownTimer != null) return;

        // 設定倒數秒數
        countdownRemaining = 5; 
        PrintToAllChat($"{ChatColors.Lime}所有玩家已就緒！比賽即將開始...");

        // 2. 建立每秒執行一次的計時器
        matchStartCountdownTimer = AddTimer(1.0f, () => {
            // 確保所有邏輯回到主執行緒執行，避免崩潰
            Server.NextFrame(() => {
                if (countdownRemaining > 0)
                {
                    // 顏色邏輯：由於總共只有 5 秒，建議 3 秒以上綠色，2 秒以下紅色
                    string color = (countdownRemaining > 2) ? $"{ChatColors.Green}" : $"{ChatColors.Red}";
                    PrintToAllChat($"倒數：{color}{countdownRemaining}");

                    // 音效邏輯：最後 3, 2, 1 秒時播放 Panorama 揭曉音效
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
                    // 3. 倒數結束：清理計時器並啟動比賽
                    matchStartCountdownTimer?.Kill();
                    matchStartCountdownTimer = null;

                    // 如果比賽因其他因素已經開始，則跳出
                    if (matchStarted) return;

                    // 顯示您要求的彩色開賽訊息
                    // PrintToAllChat($"{ChatColors.LightRed}▶ {ChatColors.Lime}刀局開始，{ChatColors.Gold}勝者選邊 {ChatColors.LightRed}◀");

                    // 【關鍵】執行開賽邏輯，這行絕對不能註解，否則會卡在 1 秒
                    HandleMatchStart(); 
                }
            });
        }, TimerFlags.REPEAT);
    }

    public void CancelMatchCountdown(string reason)
    {
        // 中止邏輯：清理計時器並發送通知
        if (matchStartCountdownTimer != null)
        {
            matchStartCountdownTimer.Kill();
            matchStartCountdownTimer = null;
            PrintToAllChat($"{ChatColors.Red}倒數中止：{reason}");
        }
    }
}
