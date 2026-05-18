using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers; // 確保有引用 Timer 模組

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 新增：用來記錄哪支隊伍同意解除技術暫停的暫存區
    public HashSet<string> techUnpauseVotes = new HashSet<string>();
    // 新增：用來管理倒數自動恢復比賽的計時器
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseTimer = null;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 刪除了原本的 WIP return，讓程式碼繼續往下跑

        if (!isMatchLive) return;

        // 如果是從伺服器控制台觸發
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

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

        if (!techPauseEnabled.Value && player != null)
        {
            PrintToPlayerChat(player, Localizer["matchzy.ready.techpausenotenabled"]);
            return;
        }

        if (maxTechPausesAllowed.Value <= 0) return;

        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        if (technicalPauseUsed[playerTeam] >= maxTechPausesAllowed.Value)
        {
            PrintToPlayerChat(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }

        // --- 補齊核心邏輯 ---
        technicalPauseUsed[playerTeam]++; // 增加該隊技術暫停的使用次數
        isTechPause = true;               // 標記 MatchZy 目前正在進行技術暫停
        isPaused = true;                 // 同步標記暫停狀態
        techUnpauseVotes.Clear();         // 清空過去的解除投票紀錄

        // 執行 CS2 官方引擎的原生暫停（確保比賽卡在凍結時間）
        Server.ExecuteCommand("mp_pause_match;");

        // 取得 config.cfg 的秒數（例如你的 300 秒）
        int duration = techPauseDuration.Value;

        // 發送中文系統訊息通知全伺服器玩家
        Server.PrintToChatAll($"{chatPrefix} 玩家 {ChatColors.Green}{player.PlayerName}{ChatColors.Default} 代表 {ChatColors.Orange}{playerTeam.teamName}{ChatColors.Default} 啟動了技術暫停！");
        Server.PrintToChatAll($"{chatPrefix} 本次暫停時間：{ChatColors.Green}{duration}{ChatColors.Default} 秒。雙方皆輸入 {ChatColors.Orange}.up{ChatColors.Default} 可提早解除。");

        // 啟動一個定時器，秒數到了就自動解除暫停
        techPauseTimer = AddTimer(duration, () => {
            if (isTechPause) {
                Server.PrintToChatAll($"{chatPrefix} 技術暫停時間已滿 {ChatColors.Green}{duration}{ChatColors.Default} 秒，正在自動恢復比賽！");
                isTechPause = false;
                isPaused = false;
                techUnpauseVotes.Clear();
                Server.ExecuteCommand("mp_unpause_match;");
                if (techPauseTimer != null) techPauseTimer = null;
            }
        });
    }
}
