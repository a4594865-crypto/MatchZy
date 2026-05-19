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

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 1. 如果比賽還沒正式開始，不允許技術暫停
        if (!isMatchLive) return;

        // 2. 如果是伺服器 RCON 控制台輸入，直接當作強制暫停
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // ================= 【100% 修正：限制只能在 15秒 Freeze Time 內使用】 =================
        // 改用最標準的 CS2 原生遊戲規則抓取法，徹底避開 Utilities 的版本衝突
        var gameRules = CounterStrikeSharp.API.Modules.Utils.GameRules;
        if (gameRules != null)
        {
            // 如果目前是在熱身階段 (Warmup)，直接阻擋不執行
            if (gameRules.WarmupPeriod) return;

            // 核心判定：如果目前「不在」凍結時間內 (FreezePeriod 為 false)，代表回合已經開打了！
            // 此時如果玩家不是管理員，就直接阻擋並跳出提示
            if (!gameRules.FreezePeriod && !IsPlayerAdmin(player))
            {
                PrintToPlayerChat(player, $"{chatPrefix} {ChatColors.Green}技 術 暫 停 只 能 回 合 開 始 前 使 用");
                return;
            }
        }
        // ===================================================================================

        // 3. 基本檢查：是否已經暫停、是否在半場、是否已結束
        if (isPaused)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
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
        if (IsTacticalTimeoutActive())
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.tacticaltimeout"]);
            return;
        }

        if (player.Team == CsTeam.Spectator || player.Team == CsTeam.None) return;

        // 4. 判斷玩家目前的戰隊 Key（徹底移除 team1/team1Obj 比對，改用 100% 安全的 player.Team）
        string teamKey = "";
        string teamName = "";

        if (player.Team == CsTeam.CounterTerrorist)
        {
            teamKey = "matchzyTeam1"; 
            if (reverseTeamSides.ContainsKey("CT"))
            {
                teamName = reverseTeamSides["CT"].teamName;
            }
        }
        else if (player.Team == CsTeam.Terrorist)
        {
            teamKey = "matchzyTeam2"; 
            if (reverseTeamSides.ContainsKey("TERRORIST"))
            {
                teamName = reverseTeamSides["TERRORIST"].teamName;
            }
        }

        if (string.IsNullOrEmpty(teamKey)) return;

        // 5. 檢查該戰隊是否還有暫停次數
        if (techPausesLeft.ContainsKey(teamKey) && techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $"{chatPrefix} 您 的 隊 伍 技 術 暫 停 次 數 已 達 上 限 ({ChatColors.Green} 1 次 {ChatColors.Default})");
            return;
        }

        // 6. 扣除該隊可用次數 (變為 0)
        techPausesLeft[teamKey] = 0;

        // 7. 執行 CS2 暫停指令並同步外掛狀態
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        unpauseData["pauseTeam"] = teamName;
        unpauseData["ct"] = false;
        unpauseData["t"] = false;

        PrintToAllChat($"{chatPrefix} {ChatColors.Green}{teamName} {ChatColors.Default}請 求 了 技 術 暫 停。剩 餘 次 數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自 動 解 除，或 雙 方 輸 入 \u0004.up\u0001 解 除。");

        // 8. 安全防護：如果原本有計時器在跑，先砍掉並重置時間
        techPauseAutoUnpauseTimer?.Kill();
        techPauseElapsedTime = 0;

        // 9. 建立一個每 30 秒觸發一次的計時器
        techPauseAutoUnpauseTimer = AddTimer(30.0f, () =>
        {
            if (!isPaused)
            {
                techPauseAutoUnpauseTimer?.Kill();
                techPauseAutoUnpauseTimer = null;
                return;
            }

            techPauseElapsedTime += 30;

            if (techPauseElapsedTime >= 300)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                PrintToAllChat($" 技 術 暫 停 已達\u0004 300 秒 \u0001上限，系 統 自 動 解 除 暫 停");
                techPauseAutoUnpauseTimer = null;
            }
            else
            {
                int remaining = 300 - techPauseElapsedTime;
                PrintToAllChat($" 技 術 暫 停 中... 剩 餘 \u0004{remaining}\u0001 秒 後 自 動 解 除 暫 停");
            }
        }, TimerFlags.REPEAT);
    }

    // 💡 新增：直接在 Pausing.cs 裡提供這個 Reset 函式，一舉解決 ConsoleCommands.cs 裡那 5 處的未定義錯誤
    public void ResetTechPauseCount()
    {
        techPausesLeft["matchzyTeam1"] = 1;
        techPausesLeft["matchzyTeam2"] = 1;
        techPauseElapsedTime = 0;
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = null;
    }
}
