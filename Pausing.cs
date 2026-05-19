using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Threading.Tasks; // 🌟 引入 C# 核心非同步工作模組

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

        // 1. 執行原廠暫停
        PauseMatch(player, command);

        // 2. 廣播通知（300秒版本，主文字綠色 `\u0006`，秒數橘色 `\u0010`）
        Server.PrintToChatAll($" \u0006技 術 暫 停 已 啟 動 將 在 \u0010 5 分 鐘 後 \u0006自 動 解 除");

        // 3. 🌟【終極絕招】：使用 Task.Run 加上 Task.Delay，徹底擺脫 TimerFlags 的束縛！
        // 300,000 毫秒 = 300 秒
        Task.Run(async () => {
            await Task.Delay(10000); 

            // 🌟 異步執行完後，將指令安全投遞回主線程執行，避免線程衝突（CS2 引擎安全規範）
            Server.NextFrame(() => {
                // 安全防呆：如果已經被人手動解除了，直接退場
                if (!isPaused) return; 

                // 同步插件內部狀態
                isPaused = false;

                // 直接向 CS2 官方伺服器引擎下達最高權限原生解除暫停指令！
                Server.ExecuteCommand("mp_unpause_match");

                // 顯示時間到的專屬綠橘色通知
                Server.PrintToChatAll($" \u0010 5 分 鐘 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }
}
