using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.IO;
using System.Threading.Tasks;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 使用 Server.GameDirectory 確保路徑在伺服器開機、換圖時 100% 絕對精準！
    private static string GetLockFilePath(string teamName)
    {
        string baseDir = Server.GameDirectory; 
        string safeTeamName = string.Join("_", teamName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(baseDir, $"tech_lock_{safeTeamName}.txt");
    }

    // 當 MatchZy 插件初次加載時執行清理
    static MatchZy()
    {
        StaticResetAllTechPauseFiles();
    }

    // 當管理員手動打 .restart、重開賽、或是換場初始化時，100% 執行物理擦除
    public void InitTechPauseFileCleaner()
    {
        technicalPauseUsed.Clear();
        StaticResetAllTechPauseFiles(); 
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 如果比賽根本還沒正式 Live（例如還在熱身、手動重開剛進來），直接無限刷新次數
        if (!isMatchLive) 
        {
            StaticResetAllTechPauseFiles();
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
            StaticResetAllTechPauseFiles();
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
        
        if (playerTeam == null || string.IsNullOrEmpty(playerTeam.teamName)) return;

        string lockFilePath = GetLockFilePath(playerTeam.teamName);

        // 🌟【完美進化】：檢查硬碟鎖時，不再寫死中文字！
        // 直接調用官方語言包，並把隊伍名稱帶進去，乾淨又專業！
        if (File.Exists(lockFilePath))
        {
            ReplyToUserCommand(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }

        // 通過檢查，寫入鎖定
        try
        {
            File.WriteAllText(lockFilePath, "used");
        }
        catch (Exception) { }
        
        if (!technicalPauseUsed.ContainsKey(playerTeam)) technicalPauseUsed[playerTeam] = 0;
        technicalPauseUsed[playerTeam]++;

        // 執行原廠暫停（時間與肉體雙重鎖定）
        PauseMatch(player, command);

        // 廣播通知
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟 動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 300秒非同步鬧鐘
        Task.Run(async () => {
            await Task.Delay(30000); 

            Server.NextFrame(() => {
                if (!isPaused) return; 

                // 呼叫 MatchZy 原廠解除暫停
                ForceUnpauseMatch(player, command);

                // 噴出時間到的自訂標籤通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }

    // 靜態清理工具：精準刪除硬碟中所有 tech_lock_ 開頭的暫存檔
    private static void StaticResetAllTechPauseFiles()
    {
        try
        {
            string baseDir = Server.GameDirectory;
            if (Directory.Exists(baseDir))
            {
                string[] files = Directory.GetFiles(baseDir, "tech_lock_*.txt");
                foreach (string file in files)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception) { }
    }
}
