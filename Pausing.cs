using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers; // 🌟 確保引入計時器模組

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

        // 2. 廣播通知（改成 10 秒測試版，主文字綠色 `\u0006`，秒數橘色 `\u0010`）
        Server.PrintToChatAll($" \u0006技 術 暫 停 已 啟 動 將 在 \u0010 10 秒 鐘 後 \u0006自 動 解 除");

        // 3. 🌟【無情修復】：加上 TimerFlags.REALTIME 與位元運算
        // 這樣計時器看的是「手錶時間」，就算 CS2 引擎凍結了，10秒一到它依然會一腳把門踹開！
        AddTimer(10.0f, () => {
            // 安全防呆
            if (!isPaused) return; 

            // 同步插件內部狀態
            isPaused = false;

            // 直接向 CS2 官方伺服器引擎下達最高權限原生解除暫停指令！
            Server.ExecuteCommand("mp_unpause_match");

            // 顯示時間到的專屬綠橘色通知
            Server.PrintToChatAll($" \u0010 10 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            
        }, TimerFlags.STOP_ON_MAPCHANGE | TimerFlags.REALTIME); // 🌟 核心就在這裡！
    }
}
