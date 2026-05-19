using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace MatchZy;

public partial class MatchZy
{
    // 配合您的 MatchZy 版本，使用字串 "matchzyTeam1" 與 "matchzyTeam2" 作為字典 Key
    public Dictionary<string, int> techPausesLeft = new() { { "matchzyTeam1", 1 }, { "matchzyTeam2", 1 } };

    // 用來控制 300 秒自動解除暫停的計時器變數
    public CounterStrikeSharp.API.Modules.Timers.Timer? techPauseAutoUnpauseTimer = null;
    
    // 紀錄暫停經過時間的變數
    public int techPauseElapsedTime = 0;

    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    public void TechPause(CCSPlayerController? player, CommandInfo? command)
    {
        // 1. 如果比賽還沒正式開始，不允許技術暫停
        if (!isMatchLive) return;

        // 新增規則：回合正式開始後（非凍結時間且非回合結束空檔），禁止輸入 .tech
        if (player != null)
        {
            var gameRulesEntities = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules");
            if (gameRulesEntities.Any())
            {
                var gameRules = gameRulesEntities.First().GameRules;
                if (gameRules != null)
                {
                    // m_bFreezePeriod = 是否在凍結時間, m_bRoundTerminating = 是否正在處理回合結束
                    // 如果「不在凍結時間」而且「沒有在回合結束處理中」，代表回合已經正式開始、玩家正在對槍中，此時禁止暫停
                    if (!gameRules.M_bFreezePeriod && !gameRules.M_bRoundTerminating)
                    {
                        PrintToPlayerChat(player, $" {ChatColors.Red}回 合 已 正 式 開 始，現 在 無 法 使 用 技 術 暫 停");
                        return;
                    }
                }
            }
        }

        // 2. 如果是伺服器 RCON 控制台輸入，直接當作強制暫停
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        // 3. 基本檢查：是否已經暫停、是否在半場、是否已結束
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
            ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]);
            return;
        }

        // 4. 檢查玩家是否在有效隊伍 (CsTeam.Terrorist 或 CsTeam.CounterTerrorist)
        if (player.Team != CsTeam.Terrorist && player.Team != CsTeam.CounterTerrorist) return;

        // 5. 利用 MatchZy 的 reverseTeamSides 字典，精確將當前陣營映射到真實隊伍物件
        Team playerMatchTeam;
        if (player.Team == CsTeam.CounterTerrorist)
        {
            playerMatchTeam = reverseTeamSides["CT"];
        }
        else
        {
            playerMatchTeam = reverseTeamSides["TERRORIST"];
        }

        string teamKey = "";
        string currentTeamName = playerMatchTeam.teamName;

        // 配合您的專案變數名稱「matchzyTeam1」與「matchzyTeam2」進行真實隊伍比對
        if (playerMatchTeam == matchzyTeam1)
        {
            teamKey = "matchzyTeam1";
        }
        else if (playerMatchTeam == matchzyTeam2)
        {
            teamKey = "matchzyTeam2";
        }
        else
        {
            return;
        }

        // 6. 檢查次數限制
        if (techPausesLeft[teamKey] <= 0)
        {
            PrintToPlayerChat(player, $" {ChatColors.Green}{currentTeamName}{ChatColors.Default} 已 經 沒 有 可 用 的 技 術 暫 停 次 數");
            return;
        }

        // 7. 扣除次數並執行 CS2 原生暫停
        techPausesLeft[teamKey]--;
        Server.ExecuteCommand("mp_pause_match;");
        isPaused = true;
        
        unpauseData["t"] = false;
        unpauseData["ct"] = false;
        unpauseData["pauseTeam"] = currentTeamName; 

        PrintToAllChat($" 隊伍 {ChatColors.Green}{currentTeamName}{ChatColors.Default} 啟 用 了 技 術 暫 停。剩 餘 次 數：{ChatColors.Green}{techPausesLeft[teamKey]} {ChatColors.Default}次。");
        PrintToAllChat($" 暫 停 將 在 \u0004300秒\u0001 後 自 動 解 除，或 雙 方 輸 入 \u0004.up\u0001 解 除。");

        // 8. 安全防護：如果原本有計時器在跑，先砍掉並重置時間
        techPauseAutoUnpauseTimer?.Kill();
        techPauseElapsedTime = 0;

        // 9. 建立一個每 30 秒觸發一次的計時器
        techPauseAutoUnpauseTimer = AddTimer(30.0f, () =>
        {
            if (!isPaused)
            {
                techPauseAutoUnpauseTimer?.Kill();
                techPauseAutoUnpauseTimer = null;
                return;
            }

            techPauseElapsedTime += 30;

            if (techPauseElapsedTime >= 300)
            {
                Server.ExecuteCommand("mp_unpause_match;");
                isPaused = false;
                unpauseData["ct"] = false;
                unpauseData["t"] = false;
                PrintToAllChat($" 技 術 暫 停 已達\u0004 300 秒 \u0001上 限，系 統 自 動 解 除 暫 停");
                techPauseAutoUnpauseTimer = null;
            }
            else
            {
                int remaining = 300 - techPauseElapsedTime;
                PrintToAllChat($" 技 術 暫 停 中... 距 離 自 動 解 除 還 剩 \u0004{remaining} 秒\u0001 ");
            }
        }, TimerFlags.REPEAT);
    }

    // 建立一個統一重設次數的方法
    public void ResetTechPauseCount()
    {
        techPausesLeft["matchzyTeam1"] = 1;
        techPausesLeft["matchzyTeam2"] = 1;
        techPauseElapsedTime = 0;
        techPauseAutoUnpauseTimer?.Kill();
        techPauseAutoUnpauseTimer = null;
    }
}
