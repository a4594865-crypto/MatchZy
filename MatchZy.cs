// =========================================================================
        // 純粹隨機洗牌 + 非自殺換隊SwitchTeam
        // =========================================================================
        public void ExecuteShuffleLogic() 
        {
            // 1. 安全檢查：如果沒有預約洗牌，則直接跳出
            if (!isShufflePending) return;

            // 2. 獲取當前所有在場上的選手（排除機器人與觀戰者）
            List<CCSPlayerController> activePlayers = Utilities.GetPlayers()
                .Where(p => p.IsValid && !p.IsBot && (p.TeamNum == 2 || p.TeamNum == 3))
                .ToList();

            // 3. 人數檢查：至少需要 2 人才能洗牌
            if (activePlayers.Count < 2) 
            {
                Log("[Shuffle] 選手人數不足，無法執行隨機分隊。");
                isShufflePending = false; 
                return;
            }

            // 4. Fisher-Yates 洗牌演算法（將在場所有人的陣列順序完全隨機打亂）
            Random rng = new();
            int n = activePlayers.Count;
            while (n > 1) 
            {
                n--;
                int k = rng.Next(n + 1);
                (activePlayers[k], activePlayers[n]) = (activePlayers[n], activePlayers[k]);
            }

            // 5. 非自殺換隊優化：純粹更新記憶體數據與隊伍號碼
            int half = activePlayers.Count / 2;
            for (int i = 0; i < activePlayers.Count; i++) 
            {
                if (i < half) 
                {
                    // 改為 SwitchTeam，玩家不會當場自殺
                    activePlayers[i].SwitchTeam(CsTeam.Terrorist); // 前半段分配到 T 隊
                }
                else 
                {
                    activePlayers[i].SwitchTeam(CsTeam.CounterTerrorist); // 後半段分配到 CT 隊
                }
            }

            // 【自建記憶體精準撈人】：直接從分好的人堆裡，撈出 T 隊和 CT 隊的第一個玩家
            var realTPlayer = activePlayers.Count > 0 ? activePlayers[0] : null;
            var realCTPlayer = activePlayers.Count > half ? activePlayers[half] : null;

            // 【原廠快取刷新】：強迫外掛的大腦更新玩家地圖快取
            UpdatePlayersMap();

            // 【核心修正】：直接硬塞給 matchzyTeam1 和 matchzyTeam2，徹底繞過不穩定的字典
            if (matchzyTeam1 != null && matchzyTeam2 != null)
            {
                // 修正 matchzyTeam1 (預設 CT)：如果是空的或斷頭 "team_"，直接塞 CT 陣營的活人名字
                if (string.IsNullOrWhiteSpace(matchzyTeam1.teamName) || matchzyTeam1.teamName == "team_")
                {
                    if (realCTPlayer != null && realCTPlayer.IsValid) 
                        matchzyTeam1.teamName = $"team_{realCTPlayer.PlayerName}";
                }

                // 修正 matchzyTeam2 (預設 T)：如果是空的或斷頭 "team_"，直接塞 T 陣營的活人名字
                if (string.IsNullOrWhiteSpace(matchzyTeam2.teamName) || matchzyTeam2.teamName == "team_")
                {
                    if (realTPlayer != null && realTPlayer.IsValid) 
                        matchzyTeam2.teamName = $"team_{realTPlayer.PlayerName}";
                }

                // 【安全自定義同步】：不呼叫會崩潰的原廠 SetTeamNames()，我們直接用官方指令刷進計分板！
                Server.ExecuteCommand($"mp_teamname_1 \"{matchzyTeam1.teamName}\"");
                Server.ExecuteCommand($"mp_teamname_2 \"{matchzyTeam2.teamName}\"");
            }

            // 6. 輸出訊息與重置標記
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Lime}隨 機 分 隊 完 成！隊 伍 已 鎖 定。");
            
            isShufflePending = false;
        } // 這是 ExecuteShuffleLogic 的結束括號
