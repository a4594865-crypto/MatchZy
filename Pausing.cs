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

    // 🔥【全新時間防線】用來記錄上一次玩家輸入任何戰術暫停的時間戳記（秒）
    private double lastTacticalPauseTime = 0.0;

    /// <summary>
    /// 輔助方法：判斷目前是否剛好處於原生戰術暫停的 93 秒安全期內
    /// </summary>
    private bool IsInTacticalPauseWindow()
    {
        if (lastTacticalPauseTime <= 0.0) return false;
        
        // 取得伺服器從開機到現在的總秒數
        double currentTime = Server.EngineTime;
        
        // 如果當前時間距離上一次輸入戰術暫停還不到 93 秒，就判定「正在戰術暫停中」
        return (currentTime - lastTacticalPauseTime) <= 93.0;
    }

    /// <summary>
    /// 【強力防線】全域攔截玩家輸入的所有暫停指令 (.p / .pause / .tech / .tac 等變體)
    /// </summary>
    public HookResult CheckAndInterceptPause(CCSPlayerController? player, CommandInfo command)
    {
        if (!isMatchLive) return HookResult.Continue;

        string cmdName = command.GetArg(0).ToLower();

        // 📝 核心動作：【精準比對】只有真正的戰術暫停指令，才蓋章記錄時間！（完美避開 .tech）
        if (cmdName == ".p" || cmdName == ".pause" || cmdName == ".tac" || 
            cmdName == "!p" || cmdName == "!pause" || cmdName == "!tac" ||
            cmdName == "css_p" || cmdName == "css_pause" || cmdName == "css_tac")
        {
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
            if (gameRules != null)
            {
                // 只有在凍結時間內輸入才會觸發官方原生暫停，這時我們才記錄時間
                if (gameRules.FreezePeriod || gameRules.WarmupPeriod)
                {
                    lastTacticalPauseTime = Server.EngineTime;
                }
            }
        }

        // 🛑 防線 1：如果目前已經在跑「300秒技術暫停」，此時任何人輸入任何戰術暫停，直接鎖死
        if (techPauseAutoUnpauseTimer != null)
        {
            if (cmdName == ".p" || cmdName == ".pause" || cmdName == ".tac" || 
                cmdName == "!p" || cmdName == "!pause" || cmdName == "!tac" ||
                cmdName == "css_p" || cmdName == "css_pause" || cmdName == "css_tac")
            {
                if (player != null)
                {
                    PrintToPlayerChat(player, $" 目前正在【 技 術 暫 停 】中，無法使用戰術暫停");
                }
                return HookResult.Handled; // 丟進虛無，完全不交給 CS2 官方原生系統
            }
        }

        // 🛑 防線 2：時間差攔截！如果輸入戰術暫停還沒超過 93 秒，此時打 .tech 直接無條件回絕！
        if (IsInTacticalPauseWindow() || techPauseAutoUnpauseTimer != null || isPaused)
        {
            if (cmdName == ".tech" || cmdName == "!tech" || cmdName == "css_tech")
            {
                if (player != null)
                {
                    PrintToPlayerChat(player, $" 已 處 於 暫 停 狀 態，無 法 啟 用 技 術 暫 停");
                }
                return HookResult.Handled; // 攔截，不讓技術暫停程式碼往下跑
            }
        }

        // 防線 3：原本的回合正式開始後攔截（非凍結時間、非熱身/刀房，禁止輸入暫停）
        if (cmdName == ".p" || cmdName == ".pause" || cmdName == ".tac" || 
            cmdName == "!p" || cmdName == "!pause" || cmdName == "!tac" ||
            cmdName == "css_p" || cmdName == "css_pause" || cmdName == "css_tac" || 
            cmdName == ".tech" || cmdName == "!tech" || cmdName == "css_tech")
        {
            if (player != null)
            {
                var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
                if (gameRules != null)
                {
                    if (!gameRules.FreezePeriod && !gameRules.WarmupPeriod)
                    {
                        string pauseType = (cmdName == ".tech" || cmdName == "!tech" || cmdName == "css_tech") ? "技術" : "戰術";
                        PrintToPlayerChat(player, $" 回 合 已 正 式 開 始，無 法 使 用 {pauseType} 暫 停");
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

        // 🛑 防線 4：技術暫停本體執行前的最終安全檢查
        if (IsInTacticalPauseWindow() || techPauseAutoUnpauseTimer != null || isPaused)
        {
            if (player != null)
            {
                PrintToPlayerChat(player, $" 已 處 於 暫 停 狀 態，無 法 啟 用 技 術 暫 停");
            }
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
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自 動 解 除，或 雙 方 輸 入 \u0004.up\u0001 解除。");

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
                PrintToAllChat($" 技術暫停已達\u0004 300 秒 \u0001上限，系統自動解除暫停");
                KillTechPauseTimer();
            }
            else
            {
                int remaining = 300 - techPauseElapsedTime;
                PrintToAllChat($" 技術暫停中... 距離自動解除還剩 \u0004{remaining} 秒\u0001 ");
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
