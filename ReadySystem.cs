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

        // --- 直接開始 7 秒音效倒數（修正版：絕不提前點火 + 兼容隨機秒開不重生 + 完美同步置中 UI） ---
        public void StartMatchCountdown()
        {
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

            // ▼▼▼ 新增：手動補發第 0 秒 (倒數 7 秒) 的狀態，與音效、UI 完美同步！ ▼▼▼
            string initialColor = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
            PrintToAllChat($"倒數：{initialColor}{countdownRemaining}");
            
            foreach (var p in Utilities.GetPlayers())
            {
                if (p != null && p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                {
                    p.PrintToCenter($"比 賽 開 始 倒 數：{countdownRemaining} 秒");
                    p.ExecuteClientCommand("play sounds/ui/panorama/popup_reveal_01.vsnd");
                }
            }
            
            // 印完第 7 秒後，立刻減 1，讓 1 秒後的計時器從 6 秒開始接手
            countdownRemaining--; 

            matchStartCountdownTimer = AddTimer(1.0f, () => {
                Server.NextFrame(() => {
                    // ▼▼▼ 終極防禦：攔截「殘留的 NextFrame 幽靈呼叫」 ▼▼▼
                    if (!isCountdownActive || matchStartCountdownTimer == null) return;

                    if (countdownRemaining > 0)
                    {
                        // 3, 2, 1 秒顯示紅色，7, 6, 5, 4 秒顯示綠色
                        string color = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
                        
                        PrintToAllChat($"倒數：{color}{countdownRemaining}");

                        foreach (var p in Utilities.GetPlayers())
                        {
                            if (p != null && p.IsValid && !p.IsBot)
                            {
                                // 畫面置中純文字提示
                                p.PrintToCenter($"比 賽 開 始 倒 數：{countdownRemaining} 秒");
                                p.ExecuteClientCommand("play sounds/ui/panorama/popup_reveal_01.vsnd");
                            }
                        }
                        
                        countdownRemaining--;
                    }
                    else
                    {
                        // 當 countdownRemaining 減到 0 時，關閉計時器
                        matchStartCountdownTimer?.Kill();
                        matchStartCountdownTimer = null;
                        isCountdownActive = false; 

                        // 【新增】：瞬間清除畫面上的「1 秒」殘影！避免視覺卡頓
                        foreach (var p in Utilities.GetPlayers())
                        {
                            if (p != null && p.IsValid && !p.IsBot)
                            {
                                p.PrintToCenter(" "); 
                            }
                        }

                        // 在這裡才把隨機洗牌的標記安全關閉
                        if (isShufflePending) 
                        {
                            isShufflePending = false;
                        }

                        if (matchStarted) return;
                        
                        // 強制在開賽前 0 毫秒重新點名
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
                    List<string> unreadyPlayers = new List<string>();
                    foreach (var p in Utilities.GetPlayers())
                    {
                        // 護甲 1：除了你原本寫的 IsValid，必須再加上 Handle 檢查，直接在第一步過濾掉斷線的鬼魂！
                        if (p != null && p.IsValid && p.Handle != IntPtr.Zero)
                        {
                            if (!p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                            {
                                if (p.UserId != null)
                                {
                                    // 1. 維持你原本精準的 UID 檢查
                                    if (playerData.ContainsKey((int)p.UserId))
                                    {
                                        // 2. 維持你改對的 UID 查字典邏輯
                                        bool isReady = false;
                                        bool shouldAdd = true;
                                        
                                        if (playerReadyStatus.TryGetValue((int)p.UserId, out isReady)) 
                                        {
                                            shouldAdd = !isReady;
                                        }
                                        
                                        if (shouldAdd)
                                        {
                                            string name = string.Empty;
                                            try 
                                            {
                                                // 2：雙重防禦，避免在撈名字的極限瞬間指針死掉
                                                name = (p != null && p.IsValid && p.Handle != IntPtr.Zero) ? p.PlayerName : string.Empty;
                                            } 
                                            catch 
                                            {
                                                name = string.Empty;
                                            }

                                            // 過濾掉空字串
                                            if (!string.IsNullOrEmpty(name))
                                            {
                                                unreadyPlayers.Add(name); // 3：強迫 LINQ 在 try 的保護範圍內「立刻執行」，徹底拆除延遲執行的炸彈！(原本這段話保留)
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
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
        // 終極修正版：伺服器完全 0 人時自動重置 (Fail-Fast 優化版)
        // =========================================================================
        [GameEventHandler]
        public HookResult AutoReset_GhostMatchHandler(EventPlayerDisconnect @event, GameEventInfo info)
        {
            // 防線一：等 3 秒讓伺服器同步釋放剛離開的玩家實體
            AddTimer(3.0f, () => {
                
                // 1. 全域鎖防護：如果倒數已經在進行中，直接結束，避免重複計時
                if (isAutoResetTimerActive) return;

                // 2. 統計目前真實玩家數量
                int initialPlayerCount = 0;
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot) initialPlayerCount++;
                }

                // 3. 提早中斷：如果場上還有玩家，代表沒事，直接結束
                if (initialPlayerCount > 0) return;

                // 4. 提早中斷：如果是「正常退服」的階段 (結算畫面、JSON賽事前置)，不需重置
                if (IsPostGamePhase() || isMatchSetup || mapReloadRequired) return;

                // 5. 判斷是否為「死局卡死狀態」 (正賽打到一半，或卡在選邊)
                bool isGhostMatch = (!isWarmup && (isMatchLive || isKnifeRequired));
                if (!isGhostMatch && !isSideSelectionPhase) return;

                // ==========================================
                // 通過所有條件，確認為死局，點燃重置引信
                // ==========================================
                isAutoResetTimerActive = true; 
                Log("[AutoReset] 偵測到比賽中途伺服器清空，啟動 120 秒自動重置倒數...");

                AddTimer(120.0f, () => {
                    
                    isAutoResetTimerActive = false; // 解除全域鎖

                    // 120 秒後進行雙重驗證，確認這 120 秒內是不是真的都沒人連回來
                    int finalPlayerCount = 0;
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p != null && p.IsValid && !p.IsBot) finalPlayerCount++;
                    }

                    // 終極確認：如果依然是 0 人，且伺服器沒有自行推進到結算畫面，才大刀闊斧重啟！
                    if (finalPlayerCount == 0 && !IsPostGamePhase())
                    {
                        Server.ExecuteCommand("css_restart"); 
                        Log("[AutoReset] 120 秒內無人連線，已自動重開比賽清理殘局。");
                    }
                });
            });

            return HookResult.Continue;
        }
    } // MatchZy Class 結束
} // Namespace 結束
