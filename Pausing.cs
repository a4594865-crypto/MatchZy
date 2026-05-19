using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.IO;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

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
            // 🌟【修改】：這裡不執行擦除檔案了，直接回傳原廠訊息。
            ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]);
            return;
        }
        if (IsPostGamePhase())
        {
            // 🌟【保留】：只有在整場比賽結束、準備換地圖時，才把檔案擦掉
            ResetTechPauseFiles();
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

        // 抓取玩家此時此刻所在的陣營
        string sideKey = "";
        if (player.Team == CsTeam.CounterTerrorist) sideKey = "ct";
        else if (player.Team == CsTeam.Terrorist) sideKey = "t";
        else return;

        string lockFilePath = $"tech_lock_{sideKey}.txt";

        // 檢查硬碟鎖
        if (File.Exists(lockFilePath))
        {
            PrintToPlayerChat(player, $" \u0002[MatchZy] \u0007你們隊伍本場比賽的技術暫停次數（1次）已經用盡！");
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

    // 🌟【工具】：只在賽後換地圖時被呼叫，擦除檔案
    private void ResetTechPauseFiles()
    {
        try
        {
            if (File.Exists("tech_lock_ct.txt")) File.Delete("tech_lock_ct.txt");
            if (File.Exists("tech_lock_t.txt")) File.Delete("tech_lock_t.txt");
        }
        catch (Exception) { }
    }
}
