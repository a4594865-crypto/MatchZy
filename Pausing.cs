using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟【關鍵變數宣告】用於跨檔案判斷與技術暫停管理
    public bool isTechPause = false; 

    // 用來記錄哪支隊伍同意解除技術暫停的暫存區
    public HashSet<string> techUnpauseVotes = new HashSet<string>();
    
    // 用來管理倒數自動恢復比賽的技術暫停計時器
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseTimer = null;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟 已移除原本官方 WIP 阻斷的 return; 讓技術暫停活過來！

        if (!isMatchLive) return;

        // Treating .tech command as .forcepause if it is used via server console.
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
            ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]); ;
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

        // 🌟【次數限制初始化】確保字典裡有這隊的次數資料
        Team playerTeam = (player!.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        if (!technicalPauseUsed.ContainsKey(playerTeam))
        {
            technicalPauseUsed[playerTeam] = 0;
        }

        // 🌟【死死鎖定：每隊整場限用 2 次】超過就拒絕暫停
        if (technicalPauseUsed[playerTeam] >= 2)
        {
            PrintToPlayerChat(player, $"{chatPrefix} {ChatColors.Red}你們隊伍的技術暫停次數（2次）已經用盡了！{ChatColors.Default}");
            return;
        }

        // --- 啟動自訂的 .tech 技術暫停邏輯 ---
        technicalPauseUsed[playerTeam]++; // 🌟 確實增加該隊已使用次數
        isTechPause = true;               
        isPaused = true;                 
        techUnpauseVotes.Clear();         

        // 使用遊戲底層硬性暫停來凍結比賽
        Server.ExecuteCommand("mp_pause_match;");

        // 🌟 死死鎖定 300 秒暫停時間
        int duration = 300; 

        // 繁體中文全伺服器廣播提示
        Server.PrintToChatAll($"{chatPrefix} 玩家 {ChatColors.Green}{player.PlayerName}{ChatColors.Default} 代表 {ChatColors.Orange}{playerTeam.teamName}{ChatColors.Default} 啟動了技術暫停！");
        Server.PrintToChatAll($"{chatPrefix} 本次暫停時間：{ChatColors.Green}{duration}{ChatColors.Default} 秒（該隊剩餘次數：{ChatColors.Red}{2 - technicalPauseUsed[playerTeam]}{ChatColors.Default}次）。");
        Server.PrintToChatAll($"{chatPrefix} 雙方皆輸入 {ChatColors.Orange}.up{ChatColors.Default} 可提早解除暫停。");

        // 註冊時間到自動恢復比賽的計時器
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
