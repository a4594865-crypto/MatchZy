using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.IO;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟【終極修復】：拋棄不可靠的 Assembly 抓取，直接用 ModuleDirectory 鎖定 MatchZy 正牌資料夾！
    private string GetLockFilePath(string teamId)
    {
        return Path.Combine(ModuleDirectory, $"tech_lock_{teamId}.txt");
    }

    // 🌟【開機/換圖保險】：構造函數，因為實例化前拿不到 ModuleDirectory，
    // 我們讓它在伺服器剛開機、插件剛啟動（OnLoad）時，再去確保舊檔案被擦乾淨。
    public override void Load(bool hotReload)
    {
        base.Load(hotReload);
        InstanceResetTechPauseFiles();
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
            InstanceResetTechPauseFiles();
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
        
        // 轉化為 MatchZy 的不變隊伍標籤 ("teamA" 或 "teamB")
        string teamKey = "";
        if (playerTeam == matchTeamA) teamKey = "teamA";
        else if (playerTeam == matchTeamB) teamKey = "teamB";
        else return;

        // 取得該隊伍的專屬實體檔案鎖路徑
        string lockFilePath = GetLockFilePath(teamKey);

        // 檢查硬碟鎖（隊伍鎖死，換邊一樣成功攔截！）
        if (File.Exists(lockFilePath))
        {
            PrintToPlayerChat(player, $" \u0006你們隊伍本場比賽的技術暫停次數（\u0007 1 次\u0006 ）已經用盡");
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

        PauseMatch(player, command);
    }

    // 🌟 實例化清理工具：精準刪除 MatchZy 本家資料夾底下的隊伍鎖定檔
    private void InstanceResetTechPauseFiles()
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
