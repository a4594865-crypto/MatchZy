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
// --- 7秒倒數版：第3秒變紅，含訊息攔截開關 ---
    public void StartMatchCountdown()
    {
        if (matchStartCountdownTimer != null) return;

        // 啟動開關：這會讓所有定時人數提醒訊息在此期間「噤聲」
        isCountdownActive = true; 

        countdownRemaining = 7; 
        PrintToAllChat($"{ChatColors.Lime}所有玩家已就緒！比賽即將開始...");

        matchStartCountdownTimer = AddTimer(1.0f, () => {
            Server.NextFrame(() => {
                if (countdownRemaining > 0)
                {
                    // 邏輯：7, 6, 5, 4 是綠色；3, 2, 1 是紅色
                    string color = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
                    PrintToAllChat($"倒數：{color}{countdownRemaining}");

                    // 最後 3 秒播放揭曉音效
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

                    // 倒數結束，解除開關封鎖
                    isCountdownActive = false; 

                    if (matchStarted) return;
                    
                    // 修改原英文開賽訊息為中文
                    PrintToAllChat($"{chatPrefix} {ChatColors.Lime}系列賽已於 {ChatColors.Gold}{DateTime.Now:HH:mm:ss} {ChatColors.Lime}正式開始！");
                    
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

            // 重要：倒數中止時立即解鎖，確保後續重要警告能顯示
            isCountdownActive = false; 

            PrintToAllChat($"{ChatColors.Red}倒數中止：{reason}");

            // 呼叫下方補上的函數，避免編譯報錯
            PrintUnreadyPlayers();
        }
    }

    // --- 解決編譯報錯：手動補上此函數並對齊語言檔 ---
    public void PrintUnreadyPlayers()
    {
        // 如果正在 7 秒倒數中，攔截所有提醒訊息
        if (isCountdownActive) return;

        int readyCount = GetReadyPlayersCount();
        
        // 使用您提供的語言檔標籤：matchzy.utility.minimumreadyplayers
        if (readyAvailable && !matchStarted && readyCount < minimumReadyRequired)
        {
            // 自動讀取語言檔：最少需要 {0} 個已準備玩家，當前：{1}
            PrintToAllChat(Localizer["matchzy.utility.minimumreadyplayers", minimumReadyRequired, readyCount]);
        }
    }
 }   
