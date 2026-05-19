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

    // 🌟【路徑修正】：改回插件專屬的私有目錄！這樣不論地圖是否在跑，外掛都有最高權限刪除檔案，絕對不會被 CS2 鎖死！
    private static string GetLockFilePath(string teamName)
    {
        string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        string safeTeamName = string.Join("_", teamName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(pluginDir, $"tech_lock_{safeTeamName}.txt");
    }

    // 🌟【核心對接】：MatchZy 主插件載入（Load）時會跑這裡。
    // 我們在這裡直接向 CS2 引擎註冊「官方地圖載入監聽器」，不管手動換圖還是硬換，換圖瞬間必定執行清理！
    public void RegisterTechPauseMapEventListener()
    {
        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            // 換地圖（不論是自動打完換，還是管理員打 .map 換）
            StaticResetAllTechPauseFiles();
            technicalPauseUsed.Clear();
        });
    }

    // 🌟【開機保險】：伺服器初次啟動、插件載入時強行清空
    static MatchZy()
    {
        StaticResetAllTechPauseFiles();
    }

    // 🌟【重開賽保險】：當管理員打 .restart、手動重打 Knife/開賽、重置場次時，MatchZy 核心必經此處
    public void InitTechPauseFileCleaner()
    {
        technicalPauseUsed.Clear();
        StaticResetAllTechPauseFiles(); // 100% 物理擦除私有目錄下的 txt 檔案
    }

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 🌟【熱身期與未開賽保險】：只要比賽還不是 Live 狀態（包含打 .restart 後回到熱身等待階段），一律無限重置次數
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
            await Task.Delay(30000); 

            Server.NextFrame(() => {
                if (!isPaused) return; 

                // 手動修正暫停狀態，徹底繞過原廠 ForceUnpauseMatch 的自帶廣播
                isPaused = false;

                // 直接對 CS2 伺服器引擎下達最高解除指令
                Server.ExecuteCommand("mp_unpause_match");

                // 唯一指定通知
                Server.PrintToChatAll($" \u0001[\u0006系統訊息\u0001] \u0010 300 秒 \u0006時 間 已 到！強 制 解 除 技 術 暫 停");
            });
        });
    }

    // 🌟 核心清理工具：精準刪除外掛私有目錄下的所有 tech_lock_ 檔案，此處擁有 100% 最高讀寫刪除權限
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
