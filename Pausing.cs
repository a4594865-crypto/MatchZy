using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.IO;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟【關鍵優修】：動態獲取 MatchZy 插件自己所在的實體資料夾路徑，確保 Windows 絕對不會找錯地方
    private static string GetLockFilePath(string sideKey)
    {
        string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        return Path.Combine(pluginDir, $"tech_lock_{sideKey}.txt");
    }

    // 🌟【開機保險】：只要伺服器重開機、或是更換地圖插件載入，立刻去把這個路徑下的檔案擦乾淨！
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
            // 比賽結束換地圖時，執行擦除
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

        // 抓取玩家陣營
        string sideKey = "";
        if (player.Team == CsTeam.CounterTerrorist) sideKey = "ct";
        else if (player.Team == CsTeam.Terrorist) sideKey = "t";
        else return;

        // 🌟 使用絕對路徑
        string lockFilePath = GetLockFilePath(sideKey);

        // 檢查硬碟鎖
        if (File.Exists(lockFilePath))
        {
            PrintToPlayerChat(player, $" \u0006你們隊伍本場比賽的技術暫停次數（\u0010 1 次\u0006 ）已經用盡");
            return;
        }

        // 通過檢查，鎖定硬碟
        try
        {
            File.WriteAllText(lockFilePath, "used");
        }
        catch (Exception) { }
        
        Team playerTeam = (player.Team == CsTeam.CounterTerrorist) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        PauseMatch(player, command);
    }

    // 🌟 靜態清理工具：精準刪除 MatchZy 資料夾底下的鎖定檔
    private static void StaticResetTechPauseFiles()
    {
        try
        {
            string ctPath = GetLockFilePath("ct");
            string tPath = GetLockFilePath("t");

            if (File.Exists(ctPath)) File.Delete(ctPath);
            if (File.Exists(tPath)) File.Delete(tPath);
        }
        catch (Exception) { }
    }
}
