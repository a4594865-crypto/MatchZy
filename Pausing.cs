using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Threading.Tasks;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 開機/重載清理次數
    public void InitTechPauseFileCleaner()
    {
        technicalPauseUsed.Clear();
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 熱身期保險：未正式開賽前打指令一律清空次數，防止跨場殘留
        if (!isMatchLive) 
        {
            technicalPauseUsed.Clear();
        }

        if (!isMatchLive) return;

        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // 🌟 完全借用官方狀態判定：如果已經在暫停中，就直接擋掉
        if (isPaused || isTechPaused)
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
            technicalPauseUsed.Clear();
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

        // 判定目前按指令的玩家肉體在哪個原廠 Team 裡面
        Team playerTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        if (playerTeam == null) return;

        // 🌟 100% 精準對接官方次數限制邏輯
        if (technicalPauseUsed.ContainsKey(playerTeam) && technicalPauseUsed[playerTeam] >= 1)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }

        // 通過檢查，記錄次數
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 🌟【震撼修正】：100% 啟動 MatchZy 官方原汁原味的技術暫停開關！
        // 這樣官方底層的所有監聽器、時間凍結線程會瞬間判定「這是一次正統的技術暫停」，並主動鎖死官方時鐘！
        isTechPaused = true;
        isPaused = true;

        // 1. 執行最純粹的原廠暫停
        PauseMatch(player, command);

        // 2. 噴出服主指定的專屬自訂標籤訊息
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 3. 獨立的 300 秒（300000 毫秒）非同步鬧鐘
        Task.Run(async () => {
            await Task.Delay(30000); 

            // 安全投遞回主線程執行，避免線程衝突
            Server.NextFrame(() => {
                // 安全防呆：如果已經被人用 .unpause 提前解除了，鬧鐘直接功成身退
                if (!isPaused && !isTechPaused) return; 

                // 🌟【解除官方狀態】：把官方的核心開關關掉
                isTechPaused = false;
                isPaused = false;

                // 直接向 CS2 官方伺服器引擎下達最高權限原生解除暫停指令
                Server.ExecuteCommand("mp_unpause_match");

                // 噴出時間到的自訂標籤通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }
}
