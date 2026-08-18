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
        // 【.NET 10 升級】：採用 Target-typed new，自動推斷型別
        public Dictionary<int, Dictionary<int, int>> playerDamageInfo = new();
        public Dictionary<int, int> playerKillers = new();
        public Dictionary<int, int> playerLastHealth = new();
        
        public Dictionary<int, Dictionary<int, int>> playerHitInfo = new();

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
            playerDamageInfo.Clear();
            playerKillers.Clear();
            playerLastHealth.Clear();
            playerHitInfo.Clear(); 
        }

        public void ShowDamageInfo()
        {
        }

        // ==========================================
        // 擊殺者追蹤 (獨立攔截玩家死亡事件)
        // ==========================================
        [GameEventHandler(HookMode.Post)]
        public HookResult OnPlayerDeath_DamageInfo(EventPlayerDeath @event, GameEventInfo info)
        {
            // 【終極效能優化】：如果回合已結束，或處於暖身/刀戰期間，直接拒絕寫入記憶體！
            if (!_isRoundLive || isWarmup || isKnifeRound) return HookResult.Continue;

            CCSPlayerController? attacker = @event.Attacker;
            CCSPlayerController? victim = @event.Userid;

            if (!IsPlayerValidDamage(attacker) || !IsPlayerValidDamage(victim)) return HookResult.Continue;
            
            // 【.NET 10 升級】：現代化 is null 檢查
            if (attacker!.UserId is null || victim!.UserId is null) return HookResult.Continue;
            
            if (attacker.UserId == victim.UserId) return HookResult.Continue;
            if (attacker.TeamNum == victim.TeamNum) return HookResult.Continue;

            playerKillers[(int)victim.UserId] = (int)attacker.UserId;
            return HookResult.Continue;
        }

        // ==========================================
        // 核心算法區：血量追蹤與傷害計算
        // ==========================================
        public void UpdatePlayerDamageInfo(EventPlayerHurt @event, int targetId)
        {
            // 【終極效能優化】：如果回合已結束，或處於暖身/刀戰期間，連算都不算，直接中斷！
            if (!_isRoundLive || isWarmup || isKnifeRound) return;

            CCSPlayerController? attacker = @event.Attacker;
            // 【.NET 10 升級】：模式匹配，一行過濾 Null 與 UserId Null
            if (attacker is not { UserId: not null }) return;
            
            int attackerId = (int)attacker.UserId;

            if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
            {
                attackerInfo = new();
                playerDamageInfo[attackerId] = attackerInfo;
            }

            if (!playerHitInfo.TryGetValue(attackerId, out var hitInfo))
            {
                hitInfo = new();
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

            // 【.NET 10 升級】：現代化 is null 檢查
            if (player.UserId is null) return;
            
            int callerId = (int)player.UserId;

            if (!playerKillers.TryGetValue(callerId, out int killerId)) return;

            CCSPlayerController? killerController = Utilities.GetPlayerFromUserid(killerId);
            
            // 【.NET 10 升級】：合併多行檢查為單次模式匹配
            if (killerController is not { IsValid: true, PawnIsAlive: true }) return; 

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
                // 【.NET 10 升級】：合併多層屬性安全判定
                if (teammate is { IsValid: true, IsBot: false } && teammate.TeamNum == player.TeamNum)
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
            // 【.NET 10 升級】：多層次巢狀屬性模式匹配 (取代 player != null && player.IsValid && player.PlayerPawn != null && ...)
            return player is { IsValid: true, IsBot: false, PlayerPawn: { IsValid: true } };
        }
    }
}
