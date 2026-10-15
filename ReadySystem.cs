using System;
using System.Collections.Generic;
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
            foreach (var (key, pData) in playerData)
            {
                if (!pData.IsValid) continue;
                if (pData.TeamNum == team && (includeCoaches || (!matchzyTeam1.coach.Contains(pData) && !matchzyTeam2.coach.Contains(pData)))) {
                    playerCount++;
                    // 【0-GC 優化】：改用 TryGetValue 防止清空準備狀態時產生 KeyNotFoundException
                    if (playerReadyStatus.TryGetValue(key, out bool isReady) && isReady) readyCount++;
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

            foreach (var (key, pData) in playerData)
            {
                if (!pData.IsValid) continue;
                if (pData.TeamNum == player.TeamNum && !matchzyTeam1.coach.Contains(pData) && !matchzyTeam2.coach.Contains(pData)) {
                    playerReadyStatus[key] = true;
                    ReplyToUserCommand(pData, Localizer["matchzy.rs.forcereadiedby", player.PlayerName]);
                }
            }

            teamReadyOverride[(CsTeam)player.TeamNum] = true;
            CheckLiveRequired();
        }

        // --- 直接開始 7 秒音效倒數（0-GC 優化版：絕不提前點火 + 兼容隨機秒開不重生 + 完美同步置中 UI） ---
        public void StartMatchCountdown()
        {
            if (matchStartCountdownTimer != null) return;

            // 【0-GC 優化】：共用同一份清單抓取，消除倒數啟動時的多餘記憶體分配
            List<CCSPlayerController> currentPlayers = Utilities.GetPlayers();

            // 倒數第 1 秒立刻全體回巢重生
            foreach (var p in currentPlayers)
            {
                if (p is { IsValid: true, IsBot: false } && (p.TeamNum == 2 || p.TeamNum == 3))
                {
                    p.Respawn(); 
                }
            }

            isCountdownActive = true; 
            countdownRemaining = 7; // 精準設定為 7 秒

            string initialColor = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
            PrintToAllChat($"倒數：{initialColor}{countdownRemaining}");
            
            foreach (var p in currentPlayers)
            {
                if (p is { IsValid: true, IsBot: false } && (p.TeamNum == 2 || p.TeamNum == 3))
                {
                    p.PrintToCenter($"比 賽 開 始 倒 數：{countdownRemaining} 秒");
                    p.ExecuteClientCommand("play sounds/ui/panorama/popup_reveal_01.vsnd");
                }
            }
            
            countdownRemaining--; 

            matchStartCountdownTimer = AddTimer(1.0f, () => {
                Server.NextFrame(() => {
                    if (!isCountdownActive || matchStartCountdownTimer == null) return;

                    if (countdownRemaining > 0)
                    {
                        string color = (countdownRemaining <= 3) ? $"{ChatColors.Red}" : $"{ChatColors.Green}";
                        
                        PrintToAllChat($"倒數：{color}{countdownRemaining}");

                        foreach (var p in currentPlayers)
                        {
                            if (p is { IsValid: true, IsBot: false })
                            {
                                p.PrintToCenter($"比 賽 開 始 倒 數：{countdownRemaining} 秒");
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

                        foreach (var p in currentPlayers)
                        {
                            if (p is { IsValid: true, IsBot: false })
                            {
                                p.PrintToCenter(" "); 
                            }
                        }

                        if (isShufflePending) 
                        {
                            isShufflePending = false;
                        }

                        if (matchStarted) return;
                        
                        UpdatePlayersMap(); 
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

                Server.PrintToChatAll($"{reason}");
                PrintUnreadyPlayers();
            }
        }

        public void PrintUnreadyPlayers()
        {
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
                    // 【0-GC 優化】：使用結構化走訪，過濾未準備名單
                    List<string> unreadyPlayers = [];
                    foreach (var p in Utilities.GetPlayers())
                    {
                        if (p is { IsValid: true } && p.Handle != IntPtr.Zero)
                        {
                            if (!p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3) && !matchzyTeam1.coach.Contains(p) && !matchzyTeam2.coach.Contains(p))
                            {
                                if (p.UserId is int uid)
                                {
                                    if (playerData.ContainsKey(uid))
                                    {
                                        bool shouldAdd = true;
                                        if (playerReadyStatus.TryGetValue(uid, out bool isReady) && isReady) 
                                        {
                                            shouldAdd = false;
                                        }
                                        
                                        if (shouldAdd)
                                        {
                                            string name = string.Empty;
                                            try 
                                            {
                                                name = (p is { IsValid: true } && p.Handle != IntPtr.Zero) ? p.PlayerName : string.Empty;
                                            } 
                                            catch 
                                            {
                                                name = string.Empty;
                                            }

                                            if (!string.IsNullOrEmpty(name))
                                            {
                                                unreadyPlayers.Add(name); 
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
                // 終極消音防線
            }
        }

        [GameEventHandler]
        public HookResult AutoReset_GhostMatchHandler(EventPlayerDisconnect @event, GameEventInfo info)
        {
            AddTimer(3.0f, () => {
                
                if (isAutoResetTimerActive) return;

                int initialPlayerCount = 0;
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is { IsValid: true, IsBot: false }) initialPlayerCount++;
                }

                if (initialPlayerCount == 0)
                {
                    if (IsPostGamePhase()) return; 
                    if (isMatchSetup || mapReloadRequired) return;

                    bool isGhostMatch = (!isWarmup && (isMatchLive || isKnifeRequired));
                    bool isStuckInSideSelection = isSideSelectionPhase;

                    if (isGhostMatch || isStuckInSideSelection)
                    {
                        isAutoResetTimerActive = true; 

                        AddTimer(120.0f, () => {
                            
                            isAutoResetTimerActive = false;

                            int finalPlayerCount = 0;
                            foreach (var p in Utilities.GetPlayers())
                            {
                                if (p is { IsValid: true, IsBot: false }) finalPlayerCount++;
                            }

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
