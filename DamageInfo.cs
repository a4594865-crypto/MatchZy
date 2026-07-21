using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;


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
        
        // 新增：用來記錄「誰(Key) 被 誰(Value) 殺死」的死亡筆記本
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
                playerKillers.Clear(); // 新增：回合結束一併清空擊殺紀錄
            }
            catch (Exception e)
            {
                Log($"[ShowDamageInfo FATAL] An error occurred: {e.Message}");
            }

        }

        // ==========================================
        // 硬核版：單一玩家專屬傷害報告 (.hp 觸發)
        // ==========================================
        public void ShowSinglePlayerDamage(CCSPlayerController player)
        {
            if (!enableDamageReport.Value) return;
            if (player == null || !player.IsValid || player.IsBot) return;

            int callerId = (int)(player.UserId ?? -1);
            if (callerId == -1) return;

            // 條件 1：確認玩家是否已陣亡，且被誰擊殺
            if (!playerKillers.TryGetValue(callerId, out int killerId)) return; 

            // 條件 2：檢查擊殺者 (C) 是否還存活 (死了就不報位)
            if (!playerData.TryGetValue(killerId, out var killerController) || killerController == null || !killerController.IsValid) return;
            if (killerController.PlayerPawn.Value == null || killerController.PlayerPawn.Value.Health <= 0) return; 

            // 條件 3：檢查 A 是否有對 C 造成傷害 (0 輸出就不廢話)
            if (!playerDamageInfo.TryGetValue(callerId, out var myAttacks) || !myAttacks.TryGetValue(killerId, out var damageToKiller)) return;
            if (damageToKiller.DamageHP <= 0) return; 

            // 條件全部通過 ➔ 只印出 A 對 C 的有效輸出，不報剩餘血量讓玩家自己算
            int damageGiven = damageToKiller.DamageHP;
            int hitsGiven = damageToKiller.Hits;
            string killerName = killerController.PlayerName;

            PrintToPlayerChat(player, $"{ChatColors.Green}To: [{damageGiven} / {hitsGiven} hits] - {killerName}{ChatColors.Default}");
        }
    }

	public class DamagePlayerInfo
	{
		public int DamageHP { get; set; } = 0;
		public int Hits { get; set; } = 0;
	}
}
