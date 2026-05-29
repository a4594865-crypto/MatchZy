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

// --- 直接開始 5 秒音效倒數 ---
        public void StartMatchCountdown()
        {
            if (matchStartCountdownTimer != null) return;

            isCountdownActive = true; 
            countdownRemaining = 7; // 設定為 5 秒

            // 已拿掉：PrintToAllChat($"{ChatColors.Lime}所有玩家已就緒！...");

            matchStartCountdownTimer = AddTimer(1.0f, () => {
                Server.NextFrame(() => {
                    if (countdownRemaining > 0)
                    {
                        // 顏色邏輯：3, 2, 1 秒顯示紅色，5, 4 秒顯示綠色
                        string color = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
                        
                        // 這裡噴出的訊息包含「倒數：」，所以會穿過 MatchZy.cs 與 Utility.cs 的防火牆
                        PrintToAllChat($"倒數：{color}{countdownRemaining}");

                        // 每一秒都播音效 (5, 4, 3, 2, 1)
                        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
                        {
                            p.ExecuteClientCommand("play sounds/ui/panorama/popup_reveal_01.vsnd");
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
        matchStartCountdownTimer.Kill();
        matchStartCountdownTimer = null;
        isCountdownActive = false; 

        // --- 核心改動 ---
        // 刪掉原本的 PrintToAllChat(reason); 
        // 因為會自動幫你加一次 {chatPrefix}
        
        // 改用這行，它會原封不動印出你傳過來的「整句話」
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
        // 增加 p.UserId.HasValue 確保查詢安全
        var unreadyPlayers = Utilities.GetPlayers()
            .Where(p => p.IsValid && !p.IsBot && p.UserId.HasValue && (p.TeamNum == 2 || p.TeamNum == 3))
            .Where(p => {
                bool isReady = false;
                // 使用正確的 UserId 對齊字典 Key，這是最穩定的做法
                if (playerReadyStatus.TryGetValue(p.UserId.Value, out isReady)) {
                    return !isReady;
                }
                // 若找不到狀態，預設視為未準備，確保準備人數不會被灌水
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
