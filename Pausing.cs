using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.IO;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 拋棄不可靠的 Assembly 抓取，直接用 ModuleDirectory 鎖定 MatchZy 正牌資料夾！
    private string GetLockFilePath(string teamId)
    {
        return Path.Combine(ModuleDirectory, $"tech_lock_{teamId}.txt");
    }

    // 🌟【修復核心】：刪除原本重複定義的 Load 函式
    // 改成這個自訂的清理接口，用來給原廠的 Load 順便調用，徹底解決報錯！
    public void InitTechPauseFileCleaner()
    {
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
            PrintToPlayerChat(player, $" \u0006你們隊伍本場比賽的技術暫停次數（\u0010 1 次\u0006 ）已經用盡");
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

        // 1. 執行原廠暫停
        PauseMatch(player, command);

        // 2. 廣播通知（標籤暗紅 `\u0002`，文字綠色 `\u0006`，秒數橘色 `\u0010`）
        Server.PrintToChatAll($" \u0006技術暫停已啟動！將在 \u0010 10 秒鐘後 \u0006自動解除並強制恢復比賽！");

        // 3. 添加高效能非同步計時器
        AddTimer(10.0f, () => {
            // 🌟【升級防呆】：
            // 狀況 A：如果已經不在暫停狀態 (!isPaused) -> 代表玩家或管理員早就提早按了解除，計時器直接退場。
            // 狀況 B：如果玩家正在打 .up 倒數開賽中 (unpauseCountdownStarted) -> 計時器也直接退場，把主導權還給官方倒數！
            if (!isPaused || unpauseCountdownStarted) return; 

            Server.PrintToChatAll($" \u0010 10 秒 \u0006時間已到！強制解除技術暫停");
            
            // 呼叫原廠的強制解除暫停函式
            ForceUnpauseMatch(null, null);
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
    }

    // 精準刪除 MatchZy 本家資料夾底下的隊伍鎖定檔
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
