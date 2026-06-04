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

// --- 直接開始 7 秒音效倒數（真正數到0才開賽完美版） ---
        public void StartMatchCountdown()
        {
            if (matchStartCountdownTimer != null) return;

            // 倒數開始立刻全體回巢重生
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                {
                    p.Respawn(); 
                }
            }

            isCountdownActive = true; 
            countdownRemaining = 7; 

            matchStartCountdownTimer = AddTimer(1.0f, () => {
                Server.NextFrame(() => {
                    if (countdownRemaining > 0)
                    {
                        string color = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
                        
                        // 先印出當前秒數 (7, 6, 5, 4, 3, 2, 1)
                        PrintToAllChat($"倒數：{color}{countdownRemaining}");

                        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
                        {
                            p.ExecuteClientCommand("play sounds/ui/panorama/popup_reveal_01.vsnd");
                        }
                        
                        // 扣除秒數
                        countdownRemaining--;
                    }
                    else 
                    {
                        // 🎯 當 countdownRemaining 已經是 0 的這一秒，代表 1 秒已經完整走完！
                        matchStartCountdownTimer?.Kill();
                        matchStartCountdownTimer = null;
                        isCountdownActive = false; 

                        if (!matchStarted)
                        {
                            // 留下一影格的緩衝時間，讓 CS2 引擎徹底清空暖場所有的垃圾封包
                            Server.NextFrame(() => {
                                HandleMatchStart(); // 絲滑切換進入刀局！
                            });
                        }
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
        isCountdownActive = false; 

        // --- 核心改動 ---
        Server.PrintToChatAll($"{reason}");

        PrintUnreadyPlayers();
    }
}

   public void PrintUnreadyPlayers()
    {
        // 只要在倒數，就攔截所有準備訊息
        if (isCountdownActive) return; 

        int readyCount = GetReadyPlayersCount();

        if (readyAvailable && !matchStarted && readyCount < minimumReadyRequired)
        {
            PrintToAllChat(Localizer["matchzy.utility.minimumreadyplayers", minimumReadyRequired, readyCount]);
        }
        else if (readyAvailable && !matchStarted)
        {
            // 找出還沒準備的玩家名單
            var unreadyPlayers = Utilities.GetPlayers()
                .Where(p => p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                .Where(p => {
                    if (p.UserId == null) return false;

                    // 1. 這裡維持你原本的 UID 檢查（對應 playerData 字典）
                    if (!playerData.ContainsKey((int)p.UserId)) return false;

                    // 2. 核心修正：改用玩家的「UID (int)」去 playerReadyStatus 查資料！
                    // 這樣就能完美搭配你在這三個檔案裡宣告的 private Dictionary<int, bool> playerReadyStatus
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
