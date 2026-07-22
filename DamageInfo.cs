using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;

namespace PreciseDamageReporter
{
    public class DamageReporterModule
    {
        // ==========================================
        // 核心數據區 (極致輕量，連類別都拔掉了，純整數儲存)
        // ==========================================
        // 直接存 int (傷害量)，不再需要 DamagePlayerInfo 類別
        public Dictionary<int, Dictionary<int, int>> playerDamageInfo = new Dictionary<int, Dictionary<int, int>>();
        public Dictionary<int, int> playerKillers = new Dictionary<int, int>();
        public Dictionary<int, int> playerLastHealth = new Dictionary<int, int>();

        // ==========================================
        // 事件攔截區
        // ==========================================
        
        public void OnPlayerHurt(EventPlayerHurt @event)
        {
            CCSPlayerController? attacker = @event.Attacker;
            CCSPlayerController? victim = @event.Userid;

            if (!IsPlayerValid(attacker) || !IsPlayerValid(victim)) return;
            
            if (attacker!.UserId == victim!.UserId) return;
            
            if (attacker.TeamNum == victim.TeamNum) return;

            UpdatePlayerDamageInfo(@event, (int)victim.UserId!);
        }

        public void OnPlayerDeath(EventPlayerDeath @event)
        {
            CCSPlayerController? attacker = @event.Attacker;
            CCSPlayerController? victim = @event.Userid;

            if (!IsPlayerValid(attacker) || !IsPlayerValid(victim)) return;
            
            // 一樣不記錄自己死掉或隊友擊殺
            if (attacker!.UserId == victim!.UserId) return;
            if (attacker.TeamNum == victim.TeamNum) return;

            playerKillers[(int)victim.UserId!] = (int)attacker.UserId!;
        }

        // ==========================================
        // 核心算法區：血量追蹤與傷害計算
        // ==========================================
        
        private void UpdatePlayerDamageInfo(EventPlayerHurt @event, int targetId)
        {
            CCSPlayerController? attacker = @event.Attacker;
            if (attacker == null) return;
            
            int attackerId = (int)attacker.UserId!;

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
        // 報位輸出區 (.hp 指令觸發)
        // ==========================================
        
        public void ShowSinglePlayerDamage(CCSPlayerController player)
        {
            int callerId = (int)player.UserId!;

            // 條件 1：確認他是不是被殺死了，找他的擊殺者
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

            // 輸出格式
            player.PrintToChat($"[{ChatColors.Green}傷害報告{ChatColors.Default}] {callerName} 對 {ChatColors.Orange}{killerName}{ChatColors.Default} 造 成 {ChatColors.LightRed}- {damageGiven}{ChatColors.Default} 傷 害");
        }
        // ==========================================
        // 回合結束清理區 (防內存洩漏)
        // ==========================================
        
        public void OnRoundEnd()
        {
            try
            {
                // 如果你有回合結束廣播的邏輯，可以寫在這裡
            }
            finally
            {
                playerDamageInfo.Clear();
                playerKillers.Clear();
                playerLastHealth.Clear();
            }
        }

        // ==========================================
        // 輔助驗證區
        // ==========================================
        
        private bool IsPlayerValid(CCSPlayerController? player)
        {
            return player != null && player.IsValid && player.PlayerPawn != null && player.PlayerPawn.IsValid && !player.IsBot;
        }
    }
}
