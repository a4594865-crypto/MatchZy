using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;

namespace MatchZy
{

    public partial class MatchZy
    {

        private void InitPlayerDamageInfo()
        {
            foreach (var key in playerData.Keys) {
                if (!playerData[key].IsValid) continue;
                if (playerData[key].IsBot) continue;
                int attackerId = key;
                foreach (var key2 in playerData.Keys) {
                    if (key == key2) continue;
                    if (!playerData[key2].IsValid || playerData[key2].IsBot) continue;
                    if (playerData[key].TeamNum == playerData[key2].TeamNum) continue;
                    if (playerData[key].TeamNum == 2) {
                        if (playerData[key2].TeamNum != 3) continue;
                        int targetId = key2;
                        if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
                            playerDamageInfo[attackerId] = attackerInfo = new Dictionary<int, DamagePlayerInfo>();

                        if (!attackerInfo.TryGetValue(targetId, out var targetInfo))
                            attackerInfo[targetId] = targetInfo = new DamagePlayerInfo();
                    } else if (playerData[key].TeamNum == 3) {
                        if (playerData[key2].TeamNum != 2) continue;
                        int targetId = key2;
                        if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
                            playerDamageInfo[attackerId] = attackerInfo = new Dictionary<int, DamagePlayerInfo>();

                        if (!attackerInfo.TryGetValue(targetId, out var targetInfo))
                            attackerInfo[targetId] = targetInfo = new DamagePlayerInfo(); 
                    }
                }
            }
        }

        public Dictionary<int, Dictionary<int, DamagePlayerInfo>> playerDamageInfo = new Dictionary<int, Dictionary<int, DamagePlayerInfo>>();
        
        public Dictionary<int, int> playerKillers = new Dictionary<int, int>();
        
        private void UpdatePlayerDamageInfo(EventPlayerHurt @event, int targetId)
        {
            CCSPlayerController? attacker = @event.Attacker;

            if (!IsPlayerValid(attacker)) return;
            int attackerId = (int)attacker!.UserId!;
            if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
                playerDamageInfo[attackerId] = attackerInfo = new Dictionary<int, DamagePlayerInfo>();

            if (!attackerInfo.TryGetValue(targetId, out var targetInfo))
                attackerInfo[targetId] = targetInfo = new DamagePlayerInfo();

            targetInfo.DamageHP += @event.DmgHealth;
            targetInfo.Hits++;
        }

        private void ShowDamageInfo()
        {
            // 回合結束的自動報告依然受 config 控制，設為 false 就不洗頻
            if (!enableDamageReport.Value) return;
            try
            {
                HashSet<(int, int)> processedPairs = new HashSet<(int, int)>();

                foreach (var entry in playerDamageInfo)
                {
                    int attackerId = entry.Key;
                    foreach (var (targetId, targetEntry) in entry.Value)
                    {
                        if (processedPairs.Contains((attackerId, targetId)) || processedPairs.Contains((targetId, attackerId)))
                            continue;

                        // Access and use the damage information as needed.
                        int damageGiven = targetEntry.DamageHP;
                        int hitsGiven = targetEntry.Hits;
                        int damageTaken = 0;
                        int hitsTaken = 0;

                        if (playerDamageInfo.TryGetValue(targetId, out var targetInfo) && targetInfo.TryGetValue(attackerId, out var takenInfo))
                        {
                            damageTaken = takenInfo.DamageHP;
                            hitsTaken = takenInfo.Hits;
                        }

                        if (!playerData.ContainsKey(attackerId) || !playerData.ContainsKey(targetId)) continue;

                        var attackerController = playerData[attackerId];
                        var targetController = playerData[targetId];

                        if (attackerController != null && targetController != null)
                        {
                            if (!attackerController.IsValid || !targetController.IsValid) continue;
                            if (attackerController.Connected != PlayerConnectedState.Connected) continue;
                            if (targetController.Connected != PlayerConnectedState.Connected) continue;
                            if (!attackerController.PlayerPawn.IsValid || !targetController.PlayerPawn.IsValid) continue;
                            if (attackerController.PlayerPawn.Value == null || targetController.PlayerPawn.Value == null) continue;

                            int attackerHP = attackerController.PlayerPawn.Value.Health < 0 ? 0 : attackerController.PlayerPawn.Value.Health;
                            string attackerName = attackerController.PlayerName;

                            int targetHP = targetController.PlayerPawn.Value.Health < 0 ? 0 : targetController.PlayerPawn.Value.Health;
                            string targetName = targetController.PlayerName;

                            PrintToPlayerChat(attackerController, $"{ChatColors.Green}To: [{damageGiven} / {hitsGiven} hits] From: [{damageTaken} / {hitsTaken} hits] - {targetName} - ({targetHP} hp){ChatColors.Default}");
                            PrintToPlayerChat(targetController, $"{ChatColors.Green}To: [{damageTaken} / {hitsTaken} hits] From: [{damageGiven} / {hitsGiven} hits] - {attackerName} - ({attackerHP} hp){ChatColors.Default}");
                        }

                        // Mark this pair as processed to avoid duplicates.
                        processedPairs.Add((attackerId, targetId));
                    }
                }
                playerDamageInfo.Clear();
                playerKillers.Clear(); 
            }
            catch (Exception e)
            {
                Log($"[ShowDamageInfo FATAL] An error occurred: {e.Message}");
            }

        }

        // ==========================================
        // 硬核版：單一玩家專屬傷害報告 (.hp 觸發) (極簡不囉嗦版)
        // ==========================================
        public void ShowSinglePlayerDamage(CCSPlayerController player)
        {
            // 已移除對 enableDamageReport.Value 的限制，隨時可查
            if (player == null || !player.IsValid || player.IsBot) return;

            int callerId = (int)(player.UserId ?? -1);
            if (callerId == -1) return;

            // 條件 1：確認目標對象 (無紀錄則安靜結束)
            if (!playerKillers.TryGetValue(callerId, out int killerId)) return; 

            // 條件 2：檢查目標對象狀態 (對方若已死亡，倒下就不報位，安靜結束)
            if (!playerData.TryGetValue(killerId, out var killerController) || killerController == null || !killerController.IsValid) return;
            if (killerController.PlayerPawn.Value == null || killerController.PlayerPawn.Value.Health <= 0) return; 

            // 條件 3：檢查是否有有效傷害數據 (0 輸出就不廢話，安靜結束)
            if (!playerDamageInfo.TryGetValue(callerId, out var myAttacks) || !myAttacks.TryGetValue(killerId, out var damageToKiller)) return;
            if (damageToKiller.DamageHP <= 0) return; 

            // 條件全部通過 ➔ 準備發送給隊友
            int damageGiven = damageToKiller.DamageHP;
            string killerName = killerController.PlayerName;
            byte callerTeam = player.TeamNum; 

            // 極簡字串：[傷害報告] 對 {B} 玩家造成-{80}
            string message = $"[{ChatColors.Green}傷害報告{ChatColors.Default}] 對 {ChatColors.Orange}{killerName}{ChatColors.Default} 造成 {ChatColors.LightRed}-{damageGiven}{ChatColors.Default}";

            // 遍歷所有玩家，只發送給相同隊伍的人 (包含死掉的隊友也能看到)
            foreach (var target in Utilities.GetPlayers())
            {
                if (target != null && target.IsValid && target.Connected == PlayerConnectedState.Connected && !target.IsBot)
                {
                    if (target.TeamNum == callerTeam)
                    {
                        PrintToPlayerChat(target, message);
                    }
                }
            }
        }
    }

    public class DamagePlayerInfo
    {
        public int DamageHP { get; set; } = 0;
        public int Hits { get; set; } = 0;
    }
}
