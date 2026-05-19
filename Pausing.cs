using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MatchZy;

public partial class MatchZy
{
    // 保留你原本完全正常的次數紀錄字典
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // 使用標準實體方法，確保不論何時都能精準抓到插件資料夾路徑
    private string GetLockFilePath(string teamName)
    {
        string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string safeTeamName = string.Join("_", teamName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(pluginDir, $"tech_lock_{safeTeamName}.txt");
    }

    // 🌟【開機/重載 終極防線】：這是 CounterStrikeSharp 官方標準的插件載入點（實體方法）
    // 當伺服器開機、或者管理員輸入 cs2_plugins reload MatchZy 時，這裡百分之百會被第一時間執行！
    public void TechPauseOnPluginLoad()
    {
        technicalPauseUsed.Clear();
        ResetAllTechPauseFiles(); // 🌟 開機立刻物理擦除硬碟舊鎖！
    }

    // 🌟【換圖/.restart 終極防線 1】：MatchZy 官方在每場比賽重新初始化時必經此處
    public void InitTechPauseFileCleaner()
    {
        technicalPauseUsed.Clear();
        ResetAllTechPauseFiles(); // 🌟 清空記憶體與硬碟檔案
    }

    // 🌟【換圖/.restart 終極防線 2】：MatchZy 原廠的重置比賽主事件，打 .restart 或換圖時一定會跑這裡
    public void ResetTechPauseOnMatchReset()
    {
        technicalPauseUsed.Clear();
        ResetAllTechPauseFiles(); // 🌟 確保雙重保險
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 【熱身期與未開賽保險】：只要比賽還不是 Live 狀態，一律無限重置次數
        if (!isMatchLive) 
        {
            ResetAllTechPauseFiles();
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
            ResetAllTechPauseFiles();
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

        // 取得該隊伍的專屬實體檔案鎖路徑
        string lockFilePath = GetLockFilePath(playerTeam.teamName);

        // 檢查硬碟鎖，直接調用官方語言包
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
            await Task.Delay(300000); 

            Server.NextFrame(() => {
                if (!isPaused) return; 

                // 手動修正暫停狀態，徹底繞過原廠 ForceUnpauseMatch 的自帶廣播
                isPaused = false;

                // 直接對 CS2 伺服器引擎下達最高解除指令
                Server.ExecuteCommand("mp_unpause_match");

                // 唯一指定通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 解 除 技 術 暫 停");
            });
        });
    }

    // 核心清理工具：精準刪除外掛私有目錄下的所有 tech_lock_ 檔案
    private void ResetAllTechPauseFiles()
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
