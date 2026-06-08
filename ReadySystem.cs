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

// --- 直接開始 7 秒音效倒數（修正版：隨機分隊時不倒數、不回出生地） ---
        public void StartMatchCountdown()
        {
            // 【核心修正：分流判定】
            // 如果當前有隨機洗牌指令 (isShufflePending 為 true)
            // 我們直接繞過 7 秒倒數，直接執行開賽，並且絕對不執行 Respawn()，防止換隊網絡封包出錯！
            if (isShufflePending)
            {
                isCountdownActive = false;
                countdownRemaining = 0;

                if (matchStartCountdownTimer != null)
                {
                    matchStartCountdownTimer.Kill();
                    matchStartCountdownTimer = null;
                }

                if (!matchStarted)
                {
                    HandleMatchStart(); // 隨機分隊在這裡秒開點火
                }

                isShufflePending = false; // 開賽完成後，才關掉隨機標記
                return; // 直接跳出，不執行下面的倒數與回出生地代碼！
            }

           // --- 直接開始 7 秒音效倒數（終極安全防撞車版） ---
        public void StartMatchCountdown()
        {
            // 🚀 【第一關：洗牌專用防護盾】
            // 如果目前是隨機分隊開賽，進來立刻點火秒開，並直接 return 封鎖下面所有邏輯！
            if (isShufflePending)
            {
                isCountdownActive = false; 
                countdownRemaining = 0;

                if (matchStartCountdownTimer != null) 
                {
                    matchStartCountdownTimer.Kill();
                    matchStartCountdownTimer = null;
                }

                if (!matchStarted) 
                {
                    HandleMatchStart(); // ➔ 隨機分隊在這裡秒開正賽！
                }

                isShufflePending = false; // ➔ 開賽成功後關閉標記
                return; // ➔ 核心攔截！直接跳出，下面什麼回出生地、倒數通通抓不到它
            }

            // -----------------------------------------------------------------
            // 以下是一般開賽 (沒洗牌) 的流程
            // -----------------------------------------------------------------
            if (matchStartCountdownTimer != null) return;

            // 【核心改動】：我們不在此處「馬上」呼叫 p.Respawn()！
            // 我們把它移到下方的 Timer 裡面，跟著倒數一起安全出發！

            isCountdownActive = true; 
            countdownRemaining = 7; // 精準設定為 7 秒

            // 宣告一個標記，確保回出生地只在第一秒執行一次
            bool hasRespawned = false;

            matchStartCountdownTimer = AddTimer(1.0f, () => {
                Server.NextFrame(() => {
                    if (countdownRemaining > 0)
                    {
                        // 【一般開賽回出生地搬移到此】：在倒數啟動的第一秒才執行傳送
                        if (!hasRespawned)
                        {
                            foreach (var p in Utilities.GetPlayers())
                            {
                                if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                                {
                                    p.Respawn(); // 這裡才傳送，完美避開點火瞬间
                                }
                            }
                            hasRespawned = true; // 標記已傳送，後面幾秒不再重複傳送
                        }

                        // 3, 2, 1 秒顯示紅色，7, 6, 5, 4 秒顯示綠色
                        string color = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
                        
                        // 印出當前秒數
                        PrintToAllChat($"倒數：{color}{countdownRemaining}");

                        // 每一秒都播音效
                        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
                        {
                            p.ExecuteClientCommand("play sounds/ui/panorama/popup_reveal_01.vsnd");
                        }
                        
                        countdownRemaining--;
                    }
                    else
                    {
                        // 當 countdownRemaining 減到 0 時，正賽開始！
                        matchStartCountdownTimer?.Kill();
                        matchStartCountdownTimer = null;
                        isCountdownActive = false; 

                        if (matchStarted) return;
                        HandleMatchStart(); // 0 秒點火開賽
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
