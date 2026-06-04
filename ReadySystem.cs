using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Generic;
using System.Linq;

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

    // =========================================================================
    // 🟢 核心搭配：完全改為您使用的 int (UserId) 字典快取對齊邏輯
    // =========================================================================
    public (int, int) GetTeamPlayerCount(int team, bool includeCoaches = false)
    {
        int playerCount = 0;
        int readyCount = 0;
        
        // 這裡的 key 已經是 int 類型的 UserId
        foreach (var key in playerData.Keys)
        {
            if (!playerData[key].IsValid) continue;
            if (playerData[key].TeamNum == team) {
                playerCount++;
                
                // 完美搭配：使用 int (UserId) 去 playerReadyStatus 檢查準備狀態
                if (playerReadyStatus.ContainsKey(key) && playerReadyStatus[key] == true) 
                {
                    readyCount++;
                }
            }
        }
        return (playerCount, readyCount);
    }

    public bool IsTeamForcedReady(CsTeam team) {
        return teamReadyOverride[team];
    }

    // =========================================================================
    // 🟢 核心搭配：強制準備指令也改為 int (UserId) 對齊邏輯
    // =========================================================================
    [ConsoleCommand("css_forceready", "Force-readies the team")]
    public void OnForceReadyCommandCommand(CCSPlayerController? player, CommandInfo? command)
    {
        Log($"{readyAvailable} {isMatchSetup} {allowForceReady} {IsPlayerValid(player)}");
        if (!readyAvailable || !isMatchSetup || !allowForceReady || !IsPlayerValid(player)) return;

        int minReady = GetTeamMinReady(player!.TeamNum);
        (int playerCount, int readyCount) = GetTeamPlayerCount(player!.TeamNum, false);

        if (playerCount < minReady) 
        {
            ReplyToUserCommand(player, Localizer["matchzy.rs.minreadyplayers", minReady]);
            return;
        }

        // 這裡的 key 已經是 int 類型的 UserId
        foreach (var key in playerData.Keys)
        {
            if (!playerData[key].IsValid) continue;
            if (playerData[key].TeamNum == player.TeamNum) {
                
                // 完美搭配：使用 int (UserId) 更新場上同隊玩家的準備狀態
                playerReadyStatus[key] = true;
                ReplyToUserCommand(playerData[key], Localizer["matchzy.rs.forcereadiedby", player.PlayerName]);
            }
        }

        teamReadyOverride[(CsTeam)player.TeamNum] = true;
        CheckLiveRequired();
    }

    // =========================================================================
    // 🟢 下方為拔除倒數、拔除 Respawn 的瞬發開賽擴充邏輯 (無縫銜接)
    // =========================================================================
    public void StartMatchCountdown()
    {
        if (matchStarted) return;

        // 清理計時器，不留任何背景非同步干擾
        matchStartCountdownTimer?.Kill();
        matchStartCountdownTimer = null;
        isCountdownActive = false; 
        countdownRemaining = 0;

        // 【極致瞬發】當影格人滿直接炸進刀局，交給官方 mp_restartgame 接管重生位置
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
            // 找出還沒準備的玩家名單 (同步改為您的 int UserId 字典查表邏輯)
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
}
