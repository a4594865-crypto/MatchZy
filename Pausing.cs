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
    /// 當玩家在「回合正式開始後」輸入時，直接在這裡攔截並回絕，完全不放行給後續的官方邏輯！
    /// </summary>
    public HookResult CheckAndInterceptPause(CCSPlayerController? player, CommandInfo command)
    {
        // 如果比賽還沒正式開始，直接放行不處理
        if (!isMatchLive) return HookResult.Continue;

        // 取得玩家輸入的指令（轉成小寫，例如 .p, .pause, .tech, !p）
        string cmdName = command.GetArg(0).ToLower();

        // 平常我們只攔截暫停指令，解暫停 (.unpause / .up) 不可以攔截
        if (cmdName.Contains("pause") || cmdName == ".p" || cmdName == "!p" || cmdName.Contains("tech"))
        {
            if (player != null)
            {
                var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
                if (gameRules != null)
                {
                    // 【核心檢查】只要「不是凍結時間」且「不是熱身階段」，就代表回合已經正式開始了
                    if (!gameRules.FreezePeriod && !gameRules.WarmupPeriod)
                    {
                        string pauseType = cmdName.Contains("tech") ? "技術" : "戰術";
                        PrintToPlayerChat(player, $" 回 合 已 正 式 開 始，無 法 使 用 {pauseType} 暫 停");
                        
                        // 🛑 重點：回傳 Handled 代表指令在這裡被我們硬生生吃掉了！
                        // 官方 Utility.cs 裡面的 PauseMatch 還有其他的暫停邏輯連觸發的機會都沒有，100% 完美攔截！
                        return HookResult.Handled; 
                    }
                }
            }
        }

        // 如果是在凍結時間內，或者是其他指令，回傳 Continue 讓 MatchZy 原生邏輯繼續跑
        return HookResult.Continue;
    }

    /// <summary>
    /// 技術暫停 (.tech) 的核心實作方法
    /// </summary>
    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 1. 如果比賽還沒正式開始，不允許技術暫停
        if (!isMatchLive) return;

        // 【安全防護 - 防重複鎖】如果計時器已經在跑，或者當前已經是暫停狀態，直接攔截，防止多個計時器重疊讀秒
        if (techPauseAutoUnpauseTimer != null || isPaused)
        {
            if (player != null)
            {
                ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            }
            return;
        }

        // 2. 如果是伺服器 RCON 控制台輸入，直接當作強制暫停
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // 3. 基本檢查：是否在半場、是否已結束
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

        // 4. 檢查玩家是否在有效隊伍 (CsTeam.Terrorist 或 CsTeam.CounterTerrorist)
        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        // 5. 利用 MatchZy 的 reverseTeamSides 字典，精確將當前陣營映射到真實隊伍物件
        Team playerMatchTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];

        string teamKey = "";
        string currentTeamName = playerMatchTeam.teamName;

        // 配合您的專案變數名稱「matchzyTeam1」與「matchzyTeam2」進行真實隊伍比對
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

        // 6. 檢查次數限制
        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 已 經 沒 有 可 用 的 技 術 暫 停 次 數");
            return;
        }

        // 7. 扣除次數並執行 CS2 原生暫停
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = currentTeamName; 

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 啟用了技術暫停。剩餘次數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自 動 解 除，或 雙 方 輸 入 \u0004.up\u0001 解 除。");

        // 8. 重置時間
        techPauseElapsedTime = 0;

        // 9. 建立一個每 30 秒觸發一次的計時器
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

    // 建立一個安全銷毀技術暫停計時器的方法，避免懸空指針
    public void KillTechPauseTimer()
    {
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = null;
        techPauseElapsedTime = 0;
    }

    // 建立一個統一重設次數的方法
    public void ResetTechPauseCount()
    {
        techPausesLeft["matchzyTeam1"] = 1;
        techPausesLeft["matchzyTeam2"] = 1;
        KillTechPauseTimer();
    }
}
