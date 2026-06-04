using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers; 

namespace MatchZy
{
    public partial class MatchZy
    {
        // --- 核心宣告 ---

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
            int minPlayers = GetPlayersPerTeam(team);
            int minReady = GetTeamMinReady(team);
            (int playerCount, int readyCount) = GetTeamPlayerCount(team, false);

            if (team == (int)CsTeam.Spectator && minReady == 0) return true;
            if (readyAvailable && playerCount == 0) return false;

            if (playerCount == readyCount && playerCount >= minPlayers) return true;

            if (IsTeamForcedReady((CsTeam)team) && readyCount >= minReady) return true;

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
            if (!readyAvailable || !isMatchSetup || !allowForceReady || !IsPlayerValid(player)) return;

            int minReady = GetTeamMinReady(player!.TeamNum);
            (int playerCount, int readyCount) = GetTeamPlayerCount(player!.TeamNum, false);

            if (playerCount < minReady) 
            {
                ReplyToUserCommand(player, Localizer["matchzy.rs.minreadyplayers", minReady]);
                return;
            }

            foreach (var key in playerData.Keys)
            {
                if (!playerData[key].IsValid) continue;
                if (playerData[key].TeamNum == player.TeamNum) {
                    playerReadyStatus[key] = true;
                    ReplyToUserCommand(playerData[key], Localizer["matchzy.rs.forcereadiedby", player.PlayerName]);
                }
            }

            teamReadyOverride[(CsTeam)player.TeamNum] = true;
            CheckLiveRequired();
        }

       // =========================================================================
        // 【優化】瞬發開賽機制（已徹底拔除倒數計時器與多餘的 Respawn）
        // =========================================================================
        public void StartMatchCountdown()
        {
            if (matchStarted) return;

            // 清理定時器狀態與標記
            matchStartCountdownTimer?.Kill();
            matchStartCountdownTimer = null;
            isCountdownActive = false; 
            countdownRemaining = 0;

            // 【終極瞬發】順暢點火，直接執行開賽邏輯，讓原廠 mp_restartgame 接管重生！
            HandleMatchStart(); 
        }
        public void CancelMatchCountdown(string reason)
        {
            matchStartCountdownTimer?.Kill();
            matchStartCountdownTimer = null;
            isCountdownActive = false; 
            countdownRemaining = 0;

            if (!string.IsNullOrEmpty(reason))
            {
                Server.PrintToChatAll($"{reason}");
            }

            PrintUnreadyPlayers();
        }

        public void PrintUnreadyPlayers()
        {
            int readyCount = GetReadyPlayersCount();

            if (readyAvailable && !matchStarted && readyCount < minimumReadyRequired)
            {
                PrintToAllChat(Localizer["matchzy.utility.minimumreadyplayers", minimumReadyRequired, readyCount]);
            }
            else if (readyAvailable && !matchStarted)
            {
                var unreadyPlayers = Utilities.GetPlayers()
                    .Where(p => p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                    .Where(p => {
                        if (p.UserId == null) return false;
                        if (!playerData.ContainsKey((int)p.UserId)) return false;

                        bool isReady = false;
                        if (playerReadyStatus.TryGetValue((int)p.UserId, out isReady)) {
                            return !isReady;
                        }
                        
                        return true; 
                    })
                    .Select(p => p.PlayerName);
                
                string unreadyList = string.Join(", ", unreadyPlayers);

                if (!string.IsNullOrEmpty(unreadyList))
                {
                    PrintToAllChat(Localizer["matchzy.utility.unreadyplayers", unreadyList]);
                }
            }
            else if (!matchStarted)
            {
                PrintToAllChat(Localizer["matchzy.utility.readyplayers", readyCount]);
            }
        }
    } // MatchZy Class 結束
} // Namespace 結束
