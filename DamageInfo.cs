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
        // 核心數據區 (極致輕量，純整數儲存 + 預先配置容量)
        // ==========================================
        public Dictionary<int, Dictionary<int, int>> playerDamageInfo = new(64);
        public Dictionary<int, int> playerKillers = new(64);
        public Dictionary<int, int> playerLastHealth = new(64);
        
        public Dictionary<int, Dictionary<int, int>> playerHitInfo = new(64);

        // 【新增】回合狀態追蹤，精準阻斷回合結算畫面後的無效記錄
        private bool _isRoundLive = false;

        // ==========================================
        // 回合事件監聽 (追蹤回合開始與結束)
        // ==========================================

        [GameEventHandler(HookMode.Pre)]
        public HookResult OnRoundStart_DamageInfo(EventRoundStart @event, GameEventInfo info)
        {
            _isRoundLive = true; 
            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Pre)]
        public HookResult OnRoundEnd_DamageInfo(EventRoundEnd @event, GameEventInfo info)
        {
            _isRoundLive = false; 
            return HookResult.Continue;
        }

        // ==========================================
        // 回合清理 & 廣播
        // ==========================================
        public void InitPlayerDamageInfo()
        {
            // 【核心 0-GC 優化】：保留內層字典實例並僅清空內部數值，徹底消滅對槍開火瞬間的 new Dictionary 記憶體配置！
            if (playerDamageInfo.Count > 64)
            {
                playerDamageInfo.Clear();
            }
            else
            {
                foreach (var dict in playerDamageInfo.Values) dict.Clear();
            }

            if (playerHitInfo.Count > 64)
            {
                playerHitInfo.Clear();
            }
            else
            {
                foreach (var dict in playerHitInfo.Values) dict.Clear();
            }

            playerKillers.Clear();
            playerLastHealth.Clear();
        }

        public void ShowDamageInfo()
        {
        }

       
        // ==========================================
        // 核心算法區：血量追蹤與傷害計算
        // ==========================================
        public void UpdatePlayerDamageInfo(EventPlayerHurt @event, int targetId)
        {
            // 【終極效能優化】：如果回合已結束，或處於暖身/刀戰期間，連算都不算，直接中斷！
            if (!_isRoundLive || isWarmup || isKnifeRound) return;

            CCSPlayerController? attacker = @event.Attacker;
            if (attacker?.UserId is not int attackerId) return;

            if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
            {
                attackerInfo = new Dictionary<int, int>(16);
                playerDamageInfo[attackerId] = attackerInfo;
            }

            if (!playerHitInfo.TryGetValue(attackerId, out var hitInfo))
            {
                hitInfo = new Dictionary<int, int>(16);
                playerHitInfo[attackerId] = hitInfo;
            }

            if (!attackerInfo.TryGetValue(targetId, out int currentDamage)) currentDamage = 0;
            if (!hitInfo.TryGetValue(targetId, out int currentHits)) currentHits = 0;

            int currentHealth = @event.Health;          
            int systemDamage = @event.DmgHealth;        
            
            int lastHealth = playerLastHealth.TryGetValue(targetId, out int hp) ? hp : 100;
            
            int actualDamage = lastHealth - currentHealth;

            if (actualDamage <= 0 || actualDamage > (systemDamage + 5)) 
            {
                actualDamage = systemDamage;
            }

            attackerInfo[targetId] = currentDamage + actualDamage;
            hitInfo[targetId] = currentHits + 1; 

            playerLastHealth[targetId] = currentHealth;
        }

        // ==========================================
        // 報位輸出區 (.hp 指令觸發 - 團隊頻道防洗頻版)
        // ==========================================
        public void ShowSinglePlayerDamage(CCSPlayerController player)
        {
            // 【核心阻斷】：加上暖身與刀戰的雙重保險防呆
            if (!_isRoundLive || isWarmup || isKnifeRound) return;

            if (player.UserId is not int callerId) return;

            if (!playerKillers.TryGetValue(callerId, out int killerId)) return;

            CCSPlayerController? killerController = Utilities.GetPlayerFromUserid(killerId);
            
            if (killerController == null || !killerController.IsValid) return;
            if (killerController.PawnIsAlive == false) return; 

            if (killerController.TeamNum == player.TeamNum) return;

            if (!playerDamageInfo.TryGetValue(callerId, out var myAttacks) || !myAttacks.TryGetValue(killerId, out int damageGiven)) return;
            if (damageGiven <= 0) return;

            int hitCount = 1;
            if (playerHitInfo.TryGetValue(callerId, out var myHits) && myHits.TryGetValue(killerId, out int hits))
            {
                hitCount = hits;
            }

            string killerName = killerController.PlayerName;
            string callerName = player.PlayerName;

            string damageMessage = "";
            if (player.TeamNum == (int)CsTeam.CounterTerrorist)
            {
                damageMessage = $" {ChatColors.LightBlue}[CT]{ChatColors.Default} {ChatColors.Red}●{ChatColors.Default} {ChatColors.LightBlue}{callerName} : {ChatColors.Default}命 中 {ChatColors.Gold}{killerName}{ChatColors.Default} {hitCount} 次 傷 害 {ChatColors.Red}- {damageGiven}{ChatColors.Default}";
            }
            else if (player.TeamNum == (int)CsTeam.Terrorist)
            {
                damageMessage = $" {ChatColors.Gold}[T]{ChatColors.Default} {ChatColors.Red}●{ChatColors.Default} {ChatColors.Gold}{callerName} : {ChatColors.Default}命 中 {ChatColors.LightBlue}{killerName}{ChatColors.Default} {hitCount} 次 傷 害 {ChatColors.Red}- {damageGiven}{ChatColors.Default} ";
            }
            else 
            {
                damageMessage = $"[{ChatColors.Green}傷害資訊{ChatColors.Default}] {ChatColors.BlueGrey}{callerName}{ChatColors.Default} 對 {ChatColors.Yellow}{killerName}{ChatColors.Default} 造 成 {ChatColors.Red}- {damageGiven}{ChatColors.Default} 傷 害";
            }

            foreach (var teammate in Utilities.GetPlayers())
            {
                if (teammate != null && teammate.IsValid && !teammate.IsBot && teammate.TeamNum == player.TeamNum)
                {
                    teammate.PrintToChat(damageMessage);
                }
            }

            // 防洗頻核心機制：報位一次就移除紀錄
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
