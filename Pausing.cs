using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy;

public partial class MatchZy
{
    // 使用字串 "matchzyTeam1" 與 "matchzyTeam2" 作為字典 Key
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };

    // 用來控制 300 秒自動解除暫停的計時器變數
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;
    
    // 紀錄暫停經過時間的變數
    public int techPauseElapsedTime = 0;

    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    /// <summary>
    /// 技術暫停 (.tech) 的核心實作方法
    /// </summary>
    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (!isMatchLive) return;

        // 防線 3：如果目前正在跑「300秒技術暫停」，禁止重複觸發
        if (techPauseAutoUnpauseTimer != null)
        {
            if (player != null)
            {
                ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            }
            return;
        }

        // 
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        bool isOfficialTacActive = gameRules != null && (gameRules.TerroristTimeOutActive || gameRules.CTTimeOutActive);

        if (isPaused || isOfficialTacActive)
        {
            if (player != null)
                PrintToPlayerChat(player, $" 正 處 於【 暫 停 狀 態 】中，無 法 啟 用 技 術 暫 停");
            return; // 
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
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 您 的 {ChatColors.Green}技 術 暫 停 {ChatColors.Default}次 數 已 用 完");
            return;
        }

        // 扣除次數並執行技術暫停
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = currentTeamName; 

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 開 啟 技 術 暫 停。剩 餘 次 數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自 動 解 除，或 雙 方 輸 入 \u0004.up\u0001 解 除。");

        techPauseElapsedTime = 0;

        // 建立 300 秒倒數計時器，改為每 1.0 秒觸發一次來更新 UI
        techPauseAutoUnpauseTimer = AddTimer(1.0f, () =>
        {
            if (!isPaused)
            {
                KillTechPauseTimer();
                return;
            }

            // 每次觸發增加 1 秒
            techPauseElapsedTime += 1;
            int remaining = 300 - techPauseElapsedTime;

            if (techPauseElapsedTime >= 300)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                PrintToAllChat($" 技 術 暫 停 已達\u0004 300 秒 \u0001上 限，系 統 自 動 解 除 暫 停");
                
                // 結束時，發送最後一次置中提示告訴玩家暫停結束
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot)
                    {
                        p.PrintToCenter("~ 技 術 暫 停 結 束 ~");
                    }
                }
                
                KillTechPauseTimer();
            }
            else
            {
                // 【聊天室提示】：每隔 30 秒才在聊天室廣播一次，避免洗頻
                if (techPauseElapsedTime % 30 == 0)
                {
                    PrintToAllChat($" 技 術 暫 停 中... 距 離 自 動 解 除 還 剩 \u0004{remaining} 秒\u0001 ");
                }

                // 【畫面置中提示】：每 1 秒在畫面正下方更新剩餘秒數
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p != null && p.IsValid && !p.IsBot)
                    {
                        p.PrintToCenter($"技 術 暫 停 中 : {remaining} 秒");
                    }
                }
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
