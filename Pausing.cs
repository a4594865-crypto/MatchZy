using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;
    public int techPauseElapsedTime = 0;
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🔥 強力時間防線：記錄玩家輸入任何戰術暫停指令的引擎時間
    private double lastTacticalPauseTime = 0.0;

    private bool IsInTacticalPauseWindow()
    {
        if (lastTacticalPauseTime <= 0.0) return false;
        
        double currentTime = Server.EngineTime;
        // 93秒精準防禦
        return (currentTime - lastTacticalPauseTime) <= 93.0;
    }

    /// <summary>
    /// 【重要】請在你的插件主入口（例如 Load() 方法）中呼叫此方法，用來掛鉤聊天訊息！
    /// </summary>
    public void RegisterPauseInterceptor()
    {
        // 必須監聽玩家公頻與隊伍頻道的發言
        AddCommandListener("say", OnPlayerChatCommand);
        AddCommandListener("say_team", OnPlayerChatCommand);
    }

    /// <summary>
    /// 負責將網頁/遊戲聊天的參數拆解，再送進你的防線做過濾
    /// </summary>
    private HookResult OnPlayerChatCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !isMatchLive) return HookResult.Continue;

        // 當玩家打字時，GetArg(0) 是 "say"，GetArg(1) 才是真正的打字內容
        string chatMessage = command.GetArg(1).Trim().ToLower();

        // 呼叫更新後的檢查邏輯
        return CheckAndInterceptPause(player, chatMessage);
    }

    /// <summary>
    /// 【強力防線】優化後的指令過濾器
    /// </summary>
    public HookResult CheckAndInterceptPause(CCSPlayerController player, string cmdName)
    {
        // 📝 核心動作：只要任何人「輸入」了戰術暫停指令，立刻蓋章記錄時間！
        if (cmdName == ".p" || cmdName == ".pause" || cmdName == ".tac" || 
            cmdName == "!p" || cmdName == "!pause" || cmdName == "!tac" ||
            cmdName == "css_p" || cmdName == "css_pause" || cmdName == "css_tac")
        {
            lastTacticalPauseTime = Server.EngineTime;
        }

        // 🛑 防線 1：如果目前已經在跑「300秒技術暫停」，此時任何人輸入任何戰術暫停，直接鎖死
        if (techPauseAutoUnpauseTimer != null)
        {
            if (cmdName == ".p" || cmdName == ".pause" || cmdName == ".tac" || 
                cmdName == "!p" || cmdName == "!pause" || cmdName == "!tac" ||
                cmdName == "css_p" || cmdName == "css_pause" || cmdName == "css_tac")
            {
                PrintToPlayerChat(player, $" 目前正在【 技 術 暫 停 】中，無法使用戰術暫停");
                return HookResult.Handled; // 攔截，不讓原本的 MatchZy 或遊戲執行
            }
        }

        // 🛑 防線 2：時間差攔截！如果輸入戰術暫停還沒超過 93 秒，此時打 .tech 直接無條件回絕！
        if (IsInTacticalPauseWindow() || techPauseAutoUnpauseTimer != null || isPaused)
        {
            if (cmdName == ".tech" || cmdName == "!tech" || cmdName == "css_tech")
            {
                PrintToPlayerChat(player, $" 已 處 於 暫 停 狀 態，無 法 啟 用 技 術 暫 停");
                return HookResult.Handled; // 丟進虛無，完美攔截！
            }
        }

        // 防線 3：原本的回合正式開始後攔截（非凍結時間、非熱身/刀房，禁止輸入暫停）
        if (cmdName == ".p" || cmdName == ".pause" || cmdName == ".tac" || 
            cmdName == "!p" || cmdName == "!pause" || cmdName == "!tac" ||
            cmdName == "css_p" || cmdName == "css_pause" || cmdName == "css_tac" || 
            cmdName == ".tech" || cmdName == "!tech" || cmdName == "css_tech")
        {
            var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
            if (gameRules != null)
            {
                if (!gameRules.FreezePeriod && !gameRules.WarmupPeriod)
                {
                    PrintToPlayerChat(player, $" 回 合 已 正 式 開 始，無 法 使 用 暫 停");
                    return HookResult.Handled; 
                }
            }
        }

        return HookResult.Continue;
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (!isMatchLive) return;

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

        if (playerMatchTeam == matchzyTeam1) teamKey = "matchzyTeam1";
        else if (playerMatchTeam == matchzyTeam2) teamKey = "matchzyTeam2";
        else return;

        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 已 經 沒 有 可 用 的 技 術 暫 停 次 數");
            return;
        }

        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = currentTeamName; 

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自動解除，或雙方輸入 \u0004.up\u0001 解除。");

        techPauseElapsedTime = 0;

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
