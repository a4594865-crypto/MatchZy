using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy;

public partial class MatchZy
{
    // 配合您的 MatchZy 版本，使用字串 "matchzyTeam1" 與 "matchzyTeam2" 作為字典 Key
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };

    // 用來控制 300 秒自動解除暫停的計時器變數
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;
    
    // 紀錄暫停經過時間的變數
    public int techPauseElapsedTime = 0;

    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    /// <summary>
    /// 【強力防線】全域攔截玩家輸入的所有暫停指令 (.p / .pause / .tech)
    /// </summary>
    public HookResult CheckAndInterceptPause(CCSPlayerController? player, CommandInfo command)
    {
        // 如果比賽還沒正式開始，直接放行不處理
        if (!isMatchLive) return HookResult.Continue;

        string cmdName = command.GetArg(0).ToLower();

        // 🛑 防線 1：如果目前已經在跑「300秒技術暫停」，此時任何人輸入 .p 或 .pause 想疊加原生暫停，直接鎖死攔截！
        if (techPauseAutoUnpauseTimer != null)
        {
            if (cmdName.Contains("pause") || cmdName == ".p" || cmdName == "!p")
            {
                if (player != null)
                {
                    PrintToPlayerChat(player, $" 目前正在【 技 術 暫 停 】中，無 法 使 用 戰 術 暫 停");
                }
                return HookResult.Handled; // 蒸汽蒸發，不交給官方原生系統
            }
        }

        // 防線 2：原本的回合正式開始後攔截（非凍結時間、非熱身/刀房，禁止輸入暫停）
        if (cmdName.Contains("pause") || cmdName == ".p" || cmdName == "!p" || cmdName.Contains("tech"))
        {
            if (player != null)
            {
                var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
                if (gameRules != null)
                {
                    if (!gameRules.FreezePeriod && !gameRules.WarmupPeriod)
                    {
                        string pauseType = cmdName.Contains("tech") ? "技術" : "戰術";
                        PrintToPlayerChat(player, $" 回 合 已 開 始，無 法 使 用 技 術 暫 停");
                        return HookResult.Handled; 
                    }
                }
            }
        }

        return HookResult.Continue;
    }

    /// <summary>
    /// 技術暫停 (.tech) 的核心實作方法
    /// </summary>
    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (!isMatchLive) return;

        // 🛑 防線 3：如果目前正在跑「300秒技術暫停」，禁止重複觸發
        if (techPauseAutoUnpauseTimer != null)
        {
            if (player != null)
            {
                ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            }
            return;
        }

        // 🛑 防線 4：如果目前「已經是原生暫停狀態（.p 戰術暫停中）」，絕對禁止再開 .tech 來亂！
        if (isPaused)
        {
            if (player != null)
                PrintToPlayerChat(player, $" 目前正處於【 戰 術 暫 停 】中，無 法 啟 用 技 術 暫 停");
            return;
        }

        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        if (IsHalfTimePhase())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]);
            return;
        }
        if (IsPostGamePhase())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]);
            return;
        }

        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];

        string teamKey = "";
        string currentTeamName = playerMatchTeam.teamName;

        if (playerMatchTeam == matchzyTeam1)
        {
            teamKey = "matchzyTeam1";
        }
        else if (playerMatchTeam == matchzyTeam2)
        {
            teamKey = "matchzyTeam2";
        }
        else
        {
            return;
        }

        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 已 經 沒 有 可 用 的 技 術 暫 停 次 數");
            return;
        }

        // 扣除次數並執行技術暫停
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = currentTeamName; 

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自 動 解 除，或 雙 方 輸 入 \u0004.up\u0001 解 除。");

        techPauseElapsedTime = 0;

        // 建立 300 秒倒數計時器
        techPauseAutoUnpauseTimer = AddTimer(30.0f, () =>
        {
            if (!isPaused)
            {
                KillTechPauseTimer();
                return;
            }

            techPauseElapsedTime += 30;

            if (techPauseElapsedTime >= 300)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                PrintToAllChat($" 技 術 暫 停 已達\u0004 300 秒 \u0001上 限，系 統 自 動 解 除 暫 停");
                KillTechPauseTimer();
            }
            else
            {
                int remaining = 300 - techPauseElapsedTime;
                PrintToAllChat($" 技 術 暫 停 中... 距 離 自 動 解 除 還 剩 \u0004{remaining} 秒\u0001 ");
            }
        }, TimerFlags.REPEAT);
    }

    public void KillTechPauseTimer()
    {
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = null;
        techPauseElapsedTime = 0;
    }

    public void ResetTechPauseCount()
    {
        techPausesLeft["matchzyTeam1"] = 1;
        techPausesLeft["matchzyTeam2"] = 1;
        KillTechPauseTimer();
    }
}
