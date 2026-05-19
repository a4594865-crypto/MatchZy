using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.IO;
using System.Threading.Tasks; // 🌟 引入 C# 核心非同步工作模組

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟 綁定隊伍代號（TeamA 或 TeamB），讓檔案鎖跟著隊伍走
    private static string GetLockFilePath(string teamId)
    {
        string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        return Path.Combine(pluginDir, $"tech_lock_{teamId}.txt");
    }

    // 🌟 開機或換地圖時，直接清空所有 tech_lock_ 開頭的檔案
    static MatchZy()
    {
        StaticResetTechPauseFiles();
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
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
            StaticResetTechPauseFiles();
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

        // 🌟 判定目前按指令的玩家，肉體在哪個原廠 Team 裡面
        Team playerTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        
        // 🌟 轉化為 MatchZy 的不變隊伍標籤 ("teamA" 或 "teamB")
        string teamKey = "";
        if (playerTeam == matchTeamA) teamKey = "teamA";
        else if (playerTeam == matchTeamB) teamKey = "teamB";
        else return; // 防呆

        // 🌟 取得該隊伍的專屬實體檔案鎖路徑
        string lockFilePath = GetLockFilePath(teamKey);

        // 檢查硬碟鎖（使用服主指定的自訂配色標籤：[系統訊息] 綠字與白括號）
        if (File.Exists(lockFilePath))
        {
            PrintToPlayerChat(player, $" \u0001[\u0006系統訊息\u0001] \u0006你們隊伍本場比賽的技術暫停次數（\u0010 1 次\u0006 ）已經用盡");
            return;
        }

        // 通過檢查，鎖定硬碟
        try
        {
            File.WriteAllText(lockFilePath, "used");
        }
        catch (Exception) { }
        
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 🌟 100% 執行你原本最完美、最正常的原廠暫停（時間、肉體雙重鎖定）
        PauseMatch(player, command);

        // 🌟 廣播通知（使用服主自訂標籤，300秒版本）
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 🌟【新武器】：添加 300 秒（300000毫秒）極致優化非同步鬧鐘
        Task.Run(async () => {
            await Task.Delay(300000); 

            // 安全投遞回主線程執行，防止 CS2 引擎崩潰
            Server.NextFrame(() => {
                // 安全防呆：如果中途已經被人手動解除暫停了，鬧鐘直接退場
                if (!isPaused) return; 

                // 呼叫 MatchZy 原廠內建最高權限解除暫停
                ForceUnpauseMatch(player, command);

                // 噴出時間到的自訂標籤通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }

    // 🌟 靜態清理工具：精準刪除 MatchZy 資料夾底下的隊伍鎖定檔
    private static void StaticResetTechPauseFiles()
    {
        try
        {
            string pathA = GetLockFilePath("teamA");
            string pathB = GetLockFilePath("teamB");

            if (File.Exists(pathA)) File.Delete(pathA);
            if (File.Exists(pathB)) File.Delete(pathB);
        }
        catch (Exception) { }
    }
}
