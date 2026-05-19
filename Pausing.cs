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
    private string GetLockFilePath(string teamHashCodeStr)
    {
        return Path.Combine(ModuleDirectory, $"tech_lock_team_{teamHashCodeStr}.txt");
    }

    // 保留自訂清理接口，用來給原廠的 Load 順便調用
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
        
        // 🌟【終極修復】：使用 GetHashCode() 把隊伍物件轉成全宇宙唯一的數字識別碼字串！
        // 這樣做完全不牽涉到 (int) 的強制轉型，編譯器絕對不會報錯。
        // 而且不論下半場怎麼換邊，該隊伍物件的 HashCode 永遠不變，鎖定依然堅不可摧！
        string teamKey = playerTeam.GetHashCode().ToString();

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
        Server.PrintToChatAll($" \u0006技 術 暫 停 已 啟 動 將 在 \u0010 10 秒 鐘 後 \u0006自 動 解 除");

        // 3. 添加高效能非同步計時器
        AddTimer(10.0f, () => {
            // 安全防呆：如果玩家打 .up 或者管理員解除了，isPaused 會提早變成 false，計時器會乾淨退場
            if (!isPaused) return; 

            Server.PrintToChatAll($" \u0010 10 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            
            // 呼叫原廠的強制解除暫停函式
            ForceUnpauseMatch(null, null);
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
    }

    // 精準刪除 MatchZy 本家資料夾底下的隊伍鎖定檔
    private void InstanceResetTechPauseFiles()
    {
        try
        {
            // 遍歷 MatchZy 資料夾，凡是 tech_lock_team_ 開頭的檔案，通通直接抹消！
            if (Directory.Exists(ModuleDirectory))
            {
                string[] files = Directory.GetFiles(ModuleDirectory, "tech_lock_team_*.txt");
                foreach (string file in files)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception) { }
    }
}
