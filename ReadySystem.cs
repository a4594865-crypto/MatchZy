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

// --- 7 秒倒數與攔截邏輯 ---
        public void StartMatchCountdown()
        {
            if (matchStartCountdownTimer != null) return;

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
                // 1. 停止計時器並清理
                matchStartCountdownTimer.Kill();
                matchStartCountdownTimer = null;
                
                // 2. 先關閉攔截開關
                isCountdownActive = false; 

                // 3. 自動對名字上色
                string coloredReason = reason
                    .Replace("玩家 ", $"玩家 {ChatColors.Red}")
                    .Replace(" 變動隊伍", $"{ChatColors.Default} 變動隊伍")
                    .Replace(" 斷開連線", $"{ChatColors.Default} 斷開連線")
                    .Replace(" 移至觀戰", $"{ChatColors.Default} 移至觀戰");

                // 4. 輸出最終訊息
                PrintToAllChat($"{ChatColors.Default}倒數中止：{coloredReason}");
                
                // 5. 立即顯示當前還缺多少人的提示
                PrintUnreadyPlayers();
            }
        }

        public void PrintUnreadyPlayers()
        {
            // 倒數時不顯示雜訊
            if (isCountdownActive) return; 

            int readyCount = GetReadyPlayersCount();
            if (readyAvailable && !matchStarted && readyCount < minimumReadyRequired)
            {
                PrintToAllChat(Localizer["matchzy.utility.minimumreadyplayers", minimumReadyRequired, readyCount]);
            }
        }
 } // MatchZy Class 結束
} // Namespace 結束
