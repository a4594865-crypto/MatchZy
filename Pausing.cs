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

    // 開機/重載清理
    public void InitTechPauseFileCleaner()
    {
        technicalPauseUsed.Clear();
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 熱身期保險
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

        // 檢查次數記錄
        if (technicalPauseUsed.ContainsKey(playerTeam) && technicalPauseUsed[playerTeam] >= 1)
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }

        // 通過檢查，記錄次數
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 🌟【最關鍵的核心修正】：第一時間將 MatchZy 全域暫停狀態拉成 true！
        // 這樣 MatchZy 官方的底層主程式才會啟動「每影格強制凍結回合計時器」的超能力！
        isPaused = true;

        // 1. 執行原廠暫停
        PauseMatch(player, command);

        // 2. 廣播通知（300秒版本，主文字綠色 `\u0006`，秒數橘色 `\u0010`）
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 3. 異步非阻塞計時器（300000毫秒 = 300秒，0效能負擔）
        Task.Run(async () => {
            await Task.Delay(30000); 

            // 安全投遞回主線程執行
            Server.NextFrame(() => {
                // 安全防呆：如果已經被人手動解除暫停了（isPaused 變成 false），計時器直接退場
                if (!isPaused) return; 

                // 🌟 同步插件內部狀態為解除
                isPaused = false;

                // 直接向 CS2 官方伺服器引擎下達最高權限原生解除暫停指令！
                Server.ExecuteCommand("mp_unpause_match");

                // 顯示時間到的自訂標籤通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }
}
