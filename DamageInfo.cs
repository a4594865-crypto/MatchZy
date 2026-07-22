using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;

namespace MatchZy
{
    public partial class MatchZy
    {
        // ==========================================
        // 核心數據區 (極致輕量，純整數儲存)
        // ==========================================
        public Dictionary<int, Dictionary<int, int>> playerDamageInfo = new Dictionary<int, Dictionary<int, int>>();
        public Dictionary<int, int> playerKillers = new Dictionary<int, int>();
        public Dictionary<int, int> playerLastHealth = new Dictionary<int, int>();

        // ==========================================
        // 回合清理 & 廣播 (對接 Utility.cs 用)
        // ==========================================
        
        // 取代原本的 OnRoundEnd，MatchZy 規定要叫這個名字
        public void InitPlayerDamageInfo()
        {
            playerDamageInfo.Clear();
            playerKillers.Clear();
            playerLastHealth.Clear();
        }

        // MatchZy 要求的空方法，留空就不會洗頻，也不會讓 Utility.cs 報錯
        public void ShowDamageInfo()
        {
        }

        // ==========================================
        // 擊殺者追蹤 (獨立攔截玩家死亡事件)
        // ==========================================
        [GameEventHandler(HookMode.Post)]
        public HookResult OnPlayerDeath_DamageInfo(EventPlayerDeath @event, GameEventInfo info)
        {
            CCSPlayerController? attacker = @event.Attacker;
            CCSPlayerController? victim = @event.Userid;

            if (!IsPlayerValidDamage(attacker) || !IsPlayerValidDamage(victim)) return HookResult.Continue;
            
            // 【安全升級】攔截極端斷線狀況，防止 NullReferenceException 導致伺服器抖動
            if (attacker!.UserId == null || victim!.UserId == null) return HookResult.Continue;
            
            // 不記錄自己死掉或隊友擊殺
            if (attacker.UserId == victim.UserId) return HookResult.Continue;
            if (attacker.TeamNum == victim.TeamNum) return HookResult.Continue;

            playerKillers[(int)victim.UserId] = (int)attacker.UserId;
            return HookResult.Continue;
        }

        // ==========================================
        // 核心算法區：血量追蹤與傷害計算
        // (MatchZy 原生核心會自動呼叫這個方法，不需掛 OnPlayerHurt)
        // ==========================================
        public void UpdatePlayerDamageInfo(EventPlayerHurt @event, int targetId)
        {
            CCSPlayerController? attacker = @event.Attacker;
            if (attacker == null) return;
            
            // 【安全升級】攔截攻擊者斷線的空值
            if (attacker.UserId == null) return;
            
            int attackerId = (int)attacker.UserId;

            // 高效 O(1) 字典創建與查找
            if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
            {
                attackerInfo = new Dictionary<int, int>();
                playerDamageInfo[attackerId] = attackerInfo;
            }

            // 取出目前已累積的傷害，沒有就是 0
            if (!attackerInfo.TryGetValue(targetId, out int currentDamage))
            {
                currentDamage = 0;
            }

            int currentHealth = @event.Health;          // 中槍後血量
            int systemDamage = @event.DmgHealth;        // 系統整數傷害
            
            // 取出中槍前血量 (若無紀錄則預設100)
            int lastHealth = playerLastHealth.TryGetValue(targetId, out int hp) ? hp : 100;
            
            // 絕對精準扣血量
            int actualDamage = lastHealth - currentHealth;

            if (actualDamage <= 0 || actualDamage > (systemDamage + 5)) 
            {
                actualDamage = systemDamage;
            }

            // 直接將傷害加上去存起來
            attackerInfo[targetId] = currentDamage + actualDamage;

            // 存入當前血量，供下一發子彈計算
            playerLastHealth[targetId] = currentHealth;
        }

        // ==========================================
        // 報位輸出區 (.hp 指令觸發 - 團隊頻道防洗頻版)
        // ==========================================
        public void ShowSinglePlayerDamage(CCSPlayerController player)
        {
            // 【安全升級】確保查詢指令的玩家 ID 存在
            if (player.UserId == null) return;
            
            int callerId = (int)player.UserId;

            // 條件 1：確認他是不是被殺死了，找他的擊殺者。
            // 如果找不到（代表沒死過，或是已經報過位被清除了），直接中斷！
            if (!playerKillers.TryGetValue(callerId, out int killerId)) return;

            // 條件 2：取得擊殺者狀態
            CCSPlayerController? killerController = Utilities.GetPlayerFromUserid(killerId);
            
            // 安全檢查：確認對方還在伺服器，而且活著！
            if (killerController == null || !killerController.IsValid) return;
            if (killerController.PawnIsAlive == false) return; 

            if (killerController.TeamNum == player.TeamNum) return;

            // 條件 3：檢查是否有有效傷害數據
            if (!playerDamageInfo.TryGetValue(callerId, out var myAttacks) || !myAttacks.TryGetValue(killerId, out int damageGiven)) return;
            if (damageGiven <= 0) return;

            // 抓取名字
            string killerName = killerController.PlayerName;
            string callerName = player.PlayerName;

            // 準備要發送到團隊頻道的訊息格式
            string damageMessage = $"{ChatColors.BlueGrey[T]{ChatColors.Default} {ChatColors.LightRed}●{ChatColors.Default}{ChatColors.BlueGrey}{callerName}：{ChatColors.Default} 命中 {ChatColors.Yellow}{killerName}{ChatColors.Default} 1 次 {ChatColors.LightRed}- {damageGiven}{ChatColors.Default} 傷 害";

            // 掃描伺服器玩家，只發給「有效」、「非機器人」且「同隊」的隊友
            foreach (var teammate in Utilities.GetPlayers())
            {
                if (teammate != null && teammate.IsValid && !teammate.IsBot && teammate.TeamNum == player.TeamNum)
                {
                    teammate.PrintToChat(damageMessage);
                }
            }

            // 【核心防洗頻機制】：報位完成後，立刻清除該玩家的這筆死亡紀錄！
            // 這樣他再次輸入 .hp 時，會在最上面的「條件 1」瞬間被攔截，絕對無法洗頻。
            playerKillers.Remove(callerId);
        }

        // ==========================================
        // 輔助驗證區
        // ==========================================
        private bool IsPlayerValidDamage(CCSPlayerController? player)
        {
            return player != null && player.IsValid && player.PlayerPawn != null && player.PlayerPawn.IsValid && !player.IsBot;
        }
    }
}
