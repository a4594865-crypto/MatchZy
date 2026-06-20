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

        // --- 直接開始 7 秒音效倒數（修正版：絕不提前點火 + 兼容隨機秒開不重生） ---
        public void StartMatchCountdown()
        {
            // 【修改 1】：把原本 isShufflePending 的秒開攔截整段刪除！讓它往下走。

            if (matchStartCountdownTimer != null) return;

            // 倒數第 1 秒立刻全體回巢重生 (雙重重生就在這裡發生，但無傷大雅)
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                {
                    p.Respawn(); 
                }
            }

            isCountdownActive = true; 
            countdownRemaining = 7; // 精準設定為 7 秒

            matchStartCountdownTimer = AddTimer(1.0f, () => {
                Server.NextFrame(() => {
                    if (countdownRemaining > 0)
                    {
                        // 3, 2, 1 秒顯示紅色，7, 6, 5, 4 秒顯示綠色
                        string color = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
                        
                        PrintToAllChat($"倒數：{color}{countdownRemaining}");

                        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
                        {
                            p.ExecuteClientCommand("play sounds/ui/panorama/popup_reveal_01.vsnd");
                        }
                        
                        countdownRemaining--;
                    }
                    else
                    {
                        // 當 countdownRemaining 減到 0 時，關閉計時器
                        matchStartCountdownTimer?.Kill();
                        matchStartCountdownTimer = null;
                        isCountdownActive = false; 

                        // 【修改 2】：在這裡才把隨機洗牌的標記安全關閉！
                        if (isShufflePending) 
                        {
                            isShufflePending = false;
                        }

                        if (matchStarted) return;
                        
                        //  【把這行終極核彈防護補上去！】：強制在開賽前 0 毫秒重新點名
                        UpdatePlayersMap(); 
                        
                        HandleMatchStart(); // 安全在 0 秒點火開賽
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

            try
            {
                int readyCount = GetReadyPlayersCount();

                if (readyAvailable && !matchStarted && readyCount < minimumReadyRequired)
                {
                    PrintToAllChat(Localizer["matchzy.utility.minimumreadyplayers", minimumReadyRequired, readyCount]);
                }
                else if (readyAvailable && !matchStarted)
                {
                    // 找出還沒準備的玩家名單
                    var unreadyPlayers = Utilities.GetPlayers()
                        // 護甲 1：除了你原本寫的 IsValid，必須再加上 Handle 檢查，直接在第一步過濾掉斷線的鬼魂！
                        .Where(p => p != null && p.IsValid && p.Handle != IntPtr.Zero) 
                        .Where(p => !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                        .Where(p => {
                            if (p.UserId == null) return false;

                            // 1. 維持你原本精準的 UID 檢查
                            if (!playerData.ContainsKey((int)p.UserId)) return false;

                            // 2. 維持你改對的 UID 查字典邏輯
                            bool isReady = false;
                            if (playerReadyStatus.TryGetValue((int)p.UserId, out isReady)) {
                                return !isReady;
                            }
                            
                            return true; 
                        })
                        .Select(p => {
                            try {
                                // 2：雙重防禦，避免在撈名字的極限瞬間指針死掉
                                return (p != null && p.IsValid && p.Handle != IntPtr.Zero) ? p.PlayerName : string.Empty;
                            } catch {
                                return string.Empty;
                            }
                        })
                        .Where(name => !string.IsNullOrEmpty(name)) // 過濾掉空字串
                        .ToList(); //  3：強迫 LINQ 在 try 的保護範圍內「立刻執行」，徹底拆除延遲執行的炸彈！
                    
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
            catch (Exception)
            {
                //  終極消音防線：萬一有極限時間差漏網之魚，直接吞掉錯誤，死死保住伺服器絕對不卡死！
            }
        }

        // =========================================================================
        // 終極修正版：伺服器完全 0 人時自動重置 (完美相容賽後結算面板)
        // =========================================================================
        [GameEventHandler]
        public HookResult AutoReset_GhostMatchHandler(EventPlayerDisconnect @event, GameEventInfo info)
        {
            // 防線一：等 3 秒讓伺服器同步人數
            AddTimer(3.0f, () => {
                
                // 1. 全域鎖防護：擋掉重複計時器
                if (isAutoResetTimerActive) return;

                int initialPlayerCount = 0;
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot) initialPlayerCount++;
                }

                // 2. 如果場上完全沒人 (0人)
                if (initialPlayerCount == 0)
                {
                    // 【核心修正】：讀取 MatchZy 的底層階段，如果在「結算計分板 (PostGame)」，直接放行不處置！
                    // 這代表比賽已經打完在等換圖了，這時候大家退服是正常的！
                    if (IsPostGamePhase()) return; 

                    // 如果是 JSON 賽事，也不自動處置
                    if (isMatchSetup || mapReloadRequired) return;

                    // 3. 死局狀態判定 (此時排除掉結算畫面了，代表是真的打到一半出事)
                    bool isGhostMatch = (!isWarmup && (isMatchLive || isKnifeRequired));
                    bool isStuckInSideSelection = isSideSelectionPhase;

                    if (isGhostMatch || isStuckInSideSelection)
                    {
                        // 點燃重置引信，鎖上全域鎖
                        isAutoResetTimerActive = true; 

                       AddTimer(120.0f, () => {
                            
                            isAutoResetTimerActive = false;

                            int finalPlayerCount = 0;
                            foreach (var p in Utilities.GetPlayers())
                            {
                                if (p != null && p.IsValid && !p.IsBot) finalPlayerCount++;
                            }

                            // 💡 修正盲點：如果 120 秒後 0 人，且 (不是熱身 或是 卡在選邊)，且不是在結算畫面，才重開！
                            if (finalPlayerCount == 0 && (!isWarmup || isSideSelectionPhase) && !IsPostGamePhase())
                            {
                                Server.ExecuteCommand("css_restart"); 
                                Log("[AutoReset] 偵測到比賽中途（含刀局選邊）全體退服，已自動重開比賽。");
                            }
                        });
                    }
                }
            });

            return HookResult.Continue;
        }
    } // MatchZy Class 結束
} // Namespace 結束
