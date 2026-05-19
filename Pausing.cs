using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.IO;
using System.Threading.Tasks;

namespace MatchZy;

public partial class MatchZy
{
    // 保留你原本完全正常的次數紀錄變數
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 🌟 修正：精準取得檔案鎖路徑（直接用隊伍內部名稱 teamName 作為 Key，100% 避開不存在的變數）
    private static string GetLockFilePath(string teamName)
    {
        string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        // 移除非法字元，確保檔案名稱安全
        string safeTeamName = string.Join("_", teamName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(pluginDir, $"tech_lock_{safeTeamName}.txt");
    }

    // 🌟【終極保險 1】：當 MatchZy 插件初次載入或手動重載時，立刻清理硬碟鎖
    static MatchZy()
    {
        StaticResetAllTechPauseFiles();
    }

    // 🌟【終極保險 2】：MatchZy 官方在每場比賽重新初始化（如 .restart 或新場次開始）時會呼叫這個方法
    // 我們在這裡同時清空記憶體與硬碟檔案，讓手動重開也能 100% 刷新次數！
    public void InitTechPauseFileCleaner()
    {
        technicalPauseUsed.Clear();
        StaticResetAllTechPauseFiles(); 
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟【終極保險 3】：如果比賽根本還沒正式 Live（例如還在熱身、手動重開剛進來），直接無限刷新次數
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
            // 正常比賽結束換圖，擦除檔案
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

        // 🌟 取得該隊伍的專屬實體檔案鎖路徑
        string lockFilePath = GetLockFilePath(playerTeam.teamName);

        // 檢查硬碟鎖（換上服主指定的綠白配色自訂標籤）
        if (File.Exists(lockFilePath))
        {
            PrintToPlayerChat(player, $" \u0001[\u0006系統訊息\u0001] \u0006你們隊伍本場比賽的技術暫停次數（\u0010 1 次\u0006 ）已經用盡");
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

        // 執行你原本最完美、最正常的暫停（時間與肉體雙重鎖定）
        PauseMatch(player, command);

        // 廣播通知
        Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0006技 術 暫 停 已 啟動 將 在 \u0010 300 秒 鐘 後 \u0006自 動 解 除");

        // 300秒非同步鬧鐘
        Task.Run(async () => {
            await Task.Delay(300000); 

            Server.NextFrame(() => {
                if (!isPaused) return; 

                // 呼叫 MatchZy 原廠解除暫停
                ForceUnpauseMatch(player, command);

                // 噴出時間到的自訂標籤通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }

    // 🌟 靜態清理工具：精準刪除硬碟中所有 tech_lock_ 開頭的暫存檔，確保不殘留
    private static void StaticResetAllTechPauseFiles()
    {
        try
        {
            string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            if (Directory.Exists(pluginDir))
            {
                string[] files = Directory.GetFiles(pluginDir, "tech_lock_*.txt");
                foreach (string file in files)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception) { }
    }
}
