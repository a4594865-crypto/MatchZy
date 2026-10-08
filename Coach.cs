using System.Globalization;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public CounterStrikeSharp.API.Modules.Timers.Timer? coachKillTimer = null;

    // true = 開局 0.25 秒瞬間無感變灰（直接看隊友第一人稱）
    // false = 買槍時間浮在高空俯瞰隊友，買槍結束前 1 秒才變灰
    public bool instantCoachGrayOut = true;

    public HashSet<CCSPlayerController> GetAllCoaches()
    {
        HashSet<CCSPlayerController> coaches = [.. matchzyTeam1.coach];
        coaches.UnionWith(matchzyTeam2.coach);

        return coaches;
    }

    // 根據教練目前所在的 CT / T 陣營，顯示 [反恐教練] 或 [恐怖教練]
    public void UpdateCoachClanTag(CCSPlayerController coach)
    {
        if (coach is null || !IsPlayerValid(coach)) return;

        CsTeam currentCoachTeam = GetCoachTeam(coach);
        string targetTag = currentCoachTeam == CsTeam.CounterTerrorist ? "[反恐教練]" : "[恐怖教練]";

        if (coach.Clan != targetTag)
        {
            coach.Clan = targetTag;
            Utilities.SetStateChanged(coach, "CCSPlayerController", "m_szClan");
        }
    }

    public void HandleCoachCommand(CCSPlayerController? player, string side)
    {
        if (player is null || !IsPlayerValid(player)) return;

        if (isPractice)
        {
            PrintToPlayerChat(player, $" 練 習 模 式 中 無 法 使 用 {ChatColors.Red}教練指令{ChatColors.Default}");
            return;
        }
        if (IsWingmanMode())
        {
            PrintToPlayerChat(player, $" 搭 檔 模 式 中 無 法 使 用 {ChatColors.Red}教練指令{ChatColors.Default}");
            return;
        }

        // 防止玩家在回合已經開打（非買槍/非熱身）時突然打 .coach 落跑導致錢歸零或少打一人
        CCSGameRules? gameRules = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity is { GameRules: not null } proxy)
            {
                gameRules = proxy.GameRules;
                break;
            }
        }
        if ((isMatchLive || isKnifeRound) && gameRules is { FreezePeriod: false, WarmupPeriod: false })
        {
            PrintToPlayerChat(player, $" 回 合 進 行 中，無 法 切 換 為 {ChatColors.Red}教練身分{ChatColors.Default}");
            return;
        }

        if (matchzyTeam1.coach.Contains(player) || matchzyTeam2.coach.Contains(player))
        {
            PrintToPlayerChat(player, $" 你 已 經 是 教 練 了！若 要 退 出 請 輸 入 {ChatColors.Green}.uncoach{ChatColors.Default}");
            return;
        }

        side = side.Trim().ToLower();

        // 支援只打 .coach 自動依玩家目前所在隊伍判別
        if (string.IsNullOrEmpty(side))
        {
            if (player.TeamNum == (byte)CsTeam.Terrorist) side = "t";
            else if (player.TeamNum == (byte)CsTeam.CounterTerrorist) side = "ct";
            else
            {
                PrintToPlayerChat(player, $" 請 先 加 入 隊 伍，或 輸 入 {ChatColors.Green}.coach t{ChatColors.Default} / {ChatColors.Green}.coach ct{ChatColors.Default}");
                return;
            }
        }

        if (side is not "t" and not "ct")
        {
            PrintToPlayerChat(player, $" 指令格式：{ChatColors.Green}.coach t{ChatColors.Default} 或 {ChatColors.Green}.coach ct{ChatColors.Default}");
            return;
        }

        // 比賽開始後，嚴格禁止跨隊去當對面的教練（防偷窺）
        byte wantedTeam = side == "t" ? (byte)CsTeam.Terrorist : (byte)CsTeam.CounterTerrorist;
        if (matchStarted && player.TeamNum != wantedTeam)
        {
            PrintToPlayerChat(player, $" 比 賽 進 行 中，僅 能 擔 任 {ChatColors.Red}自己所屬隊伍{ChatColors.Default} 的教練");
            return;
        }

        // ★ 核心安全讀取：只用 TryGetValue 讀取，絕不修改 reverseTeamSides 字典，100% 不影響隨機分隊隊名！
        Team matchZyCoachTeam;
        if (side == "t")
        {
            matchZyCoachTeam = reverseTeamSides.TryGetValue("TERRORIST", out var tTeam) ? tTeam : matchzyTeam2;
        }
        else
        {
            matchZyCoachTeam = reverseTeamSides.TryGetValue("CT", out var ctTeam) ? ctTeam : matchzyTeam1;
        }

        matchZyCoachTeam.coach.Add(player);

        // 教練不需要按 .R：從準備名單中移除，避免佔用準備人數
        if (player.UserId is int uid)
        {
            playerReadyStatus.Remove(uid);
        }

        if (player.InGameMoneyServices is not null) player.InGameMoneyServices.Account = 0;

        // 切換陣營並套用 [反恐教練] / [恐怖教練]
        HandleCoachTeam(player);
        UpdateCoachClanTag(player);
        Server.NextFrame(EnforceCompetitiveTeammateColors);

        string displayTeamName = string.IsNullOrWhiteSpace(matchZyCoachTeam.teamName)
            ? (side == "ct" ? "反恐小組" : "恐怖分子")
            : matchZyCoachTeam.teamName;

        PrintToPlayerChat(player, $" 你 現 在 擔 任 {ChatColors.Green}{displayTeamName}{ChatColors.Default} 的教練！輸 入 {ChatColors.Green}.uncoach{ChatColors.Default} 可 退 出 教 練 席");
        PrintToAllChat($" {ChatColors.Green}{player.PlayerName}{ChatColors.Default} 現 在 擔 任 {ChatColors.Green}{displayTeamName}{ChatColors.Default} 的 教 練！");

        if (readyAvailable && !matchStarted)
        {
            CheckLiveRequired();
        }
    }

    public void HandleCoaches()
    {
        coachKillTimer?.Kill();
        coachKillTimer = null;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;

        bool anySpawnsEmpty = false;
        foreach (var list in spawnsData.Values)
        {
            if (list.Count == 0)
            {
                anySpawnsEmpty = true;
                break;
            }
        }
        if (anySpawnsEmpty) GetSpawns();

        if (coachSpawns.Count == 0 ||
            !coachSpawns.TryGetValue((byte)CsTeam.CounterTerrorist, out var ctSpawns) || ctSpawns.Count == 0 ||
            !coachSpawns.TryGetValue((byte)CsTeam.Terrorist, out var tSpawns) || tSpawns.Count == 0)
        {
            GetCoachSpawns();
        }

        // 決定變灰時間：instantCoachGrayOut 為 true 時開局 0.25 秒瞬間無感變灰；false 時買槍結束前 1 秒變灰
        float killDelay;
        if (instantCoachGrayOut)
        {
            killDelay = 0.25f;
        }
        else
        {
            int freezeTime = ConVar.Find("mp_freezetime") is { } cvFreeze ? cvFreeze.GetPrimitiveValue<int>() : 15;
            killDelay = Math.Max(0.5f, freezeTime - 1.0f);
        }
        coachKillTimer ??= AddTimer(killDelay, KillCoaches);

        Random random = new();
        int coachIdx = 0;
        foreach (CCSPlayerController coach in coaches)
        {
            if (coach is null || !IsPlayerValid(coach)) continue;

            if (coach.InGameMoneyServices is not null) coach.InGameMoneyServices.Account = 0;

            AddTimer(0.1f, () => HandleCoachTeam(coach));
            ResetCoachStats(coach);

            SetPlayerInvisible(player: coach, setWeaponsInvisible: false);

            if (coach.PlayerPawn.Value is { } pawn)
            {
                // 先鎖定移動與關閉受傷判定（無敵），防止落地發出腳步聲或被隊友揮刀誤傷
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                pawn.TakesDamage = false;

                Position? targetCoachPos = null;
                if (coachSpawns.TryGetValue(coach.TeamNum, out var teamSpawns) && teamSpawns.Count > 0)
                {
                    targetCoachPos = teamSpawns[random.Next(0, teamSpawns.Count)];
                }
                else if (TryGetBehindTeamCoachSpawn(coach.TeamNum, coachIdx, out Position behindPos))
                {
                    targetCoachPos = behindPos;
                }
                else if (pawn.CBodyComponent?.SceneNode is { AbsOrigin: { } origin, AbsRotation: { } rotation })
                {
                    targetCoachPos = new Position(new Vector(origin.X, origin.Y, origin.Z + 100.0f), rotation);
                }

                coachIdx++;

                if (targetCoachPos is not null)
                {
                    Position finalPos = targetCoachPos;
                    AddTimer(0.05f, () =>
                    {
                        HandleCoachWeapons(coach);
                        if (coach.PlayerPawn.Value is { } validPawn)
                        {
                            validPawn.Teleport(finalPos.PlayerPosition, finalPos.PlayerAngle, new(0, 0, 0));
                        }
                    });
                }
            }
        }

        // 修正被教練擠歪的正式選手出生點，並歸還五色標記
        AddTimer(0.15f, EnforceCompetitiveSpawns);
        AddTimer(0.3f, EnforceCompetitiveTeammateColors);
    }

    /// <summary>
    /// 當該地圖沒有 JSON 座標檔時，自動計算隊伍出生點後方高空俯瞰點
    /// </summary>
    private bool TryGetBehindTeamCoachSpawn(byte teamNum, int coachIdx, out Position result)
    {
        result = null!;
        try
        {
            if (!spawnsData.TryGetValue(teamNum, out var spawns) || spawns.Count == 0)
                return false;

            float cx = 0, cy = 0, cz = 0, fx = 0, fy = 0;
            foreach (var s in spawns)
            {
                cx += s.PlayerPosition.X;
                cy += s.PlayerPosition.Y;
                cz += s.PlayerPosition.Z;
                double yaw = s.PlayerAngle.Y * Math.PI / 180.0;
                fx += (float)Math.Cos(yaw);
                fy += (float)Math.Sin(yaw);
            }
            int n = spawns.Count;
            cx /= n; cy /= n; cz /= n;

            float flen = (float)Math.Sqrt(fx * fx + fy * fy);
            if (flen < 0.0001f) { fx = 1; fy = 0; flen = 1; }
            fx /= flen; fy /= flen;

            float minProj = 0;
            foreach (var s in spawns)
            {
                float proj = (s.PlayerPosition.X - cx) * fx + (s.PlayerPosition.Y - cy) * fy;
                if (proj < minProj) minProj = proj;
            }

            const float up = 90.0f;
            const float pitch = 12.0f;
            float yawDeg = (float)(Math.Atan2(fy, fx) * 180.0 / Math.PI);
            float rx = fy, ry = -fx;
            float spread = coachIdx * 55.0f;
            var rear = new Vector(cx + fx * minProj + rx * spread, cy + fy * minProj + ry * spread, cz + 64.0f);

            result = new Position(new Vector(rear.X - fx * 40.0f, rear.Y - fy * 40.0f, cz + up), new QAngle(pitch, yawDeg, 0.0f));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 75 單位容錯出生點保護：已站在標準競技出生點上的選手不移動，僅將被教練擠歪的第 5 人拉回空出的標準出生點
    /// </summary>
    private void EnforceCompetitiveSpawns()
    {
        try
        {
            HashSet<CCSPlayerController> coaches = GetAllCoaches();
            if (coaches.Count == 0) return;

            const float keepDistSq = 75.0f * 75.0f;

            foreach (byte side in new[] { (byte)CsTeam.CounterTerrorist, (byte)CsTeam.Terrorist })
            {
                if (!spawnsData.TryGetValue(side, out var teamSpawns) || teamSpawns.Count == 0)
                    continue;

                List<CCSPlayerController> remainingPlayers = [];
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is not null && IsPlayerValid(p) && p.TeamNum == side && !coaches.Contains(p) &&
                        p.PawnIsAlive && p.PlayerPawn.Value?.CBodyComponent?.SceneNode is not null)
                    {
                        remainingPlayers.Add(p);
                    }
                }

                if (remainingPlayers.Count == 0) continue;

                List<Position> remainingSpawns = [.. teamSpawns];

                while (remainingPlayers.Count > 0 && remainingSpawns.Count > 0)
                {
                    int keepP = -1, keepS = -1;
                    float keepBest = float.MaxValue;

                    for (int pi = 0; pi < remainingPlayers.Count; pi++)
                    {
                        Vector pos = remainingPlayers[pi].PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                        for (int si = 0; si < remainingSpawns.Count; si++)
                        {
                            Vector sp = remainingSpawns[si].PlayerPosition;
                            float dx = sp.X - pos.X, dy = sp.Y - pos.Y, dz = sp.Z - pos.Z;
                            float dist = dx * dx + dy * dy + dz * dz;
                            if (dist < keepBest)
                            {
                                keepBest = dist;
                                keepP = pi;
                                keepS = si;
                            }
                        }
                    }

                    if (keepP < 0 || keepBest > keepDistSq) break;

                    remainingPlayers.RemoveAt(keepP);
                    remainingSpawns.RemoveAt(keepS);
                }

                while (remainingPlayers.Count > 0 && remainingSpawns.Count > 0)
                {
                    int bestP = -1, bestS = -1;
                    float bestDist = float.MaxValue;

                    for (int pi = 0; pi < remainingPlayers.Count; pi++)
                    {
                        Vector pos = remainingPlayers[pi].PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                        for (int si = 0; si < remainingSpawns.Count; si++)
                        {
                            Vector sp = remainingSpawns[si].PlayerPosition;
                            float dx = sp.X - pos.X, dy = sp.Y - pos.Y, dz = sp.Z - pos.Z;
                            float dist = dx * dx + dy * dy + dz * dz;
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestP = pi;
                                bestS = si;
                            }
                        }
                    }

                    if (bestP < 0 || bestS < 0) break;

                    CCSPlayerController displacedPlayer = remainingPlayers[bestP];
                    Position targetSpawn = remainingSpawns[bestS];

                    if (displacedPlayer.PlayerPawn.Value is { } pawn)
                    {
                        pawn.Teleport(targetSpawn.PlayerPosition, targetSpawn.PlayerAngle, new(0, 0, 0));
                    }

                    remainingPlayers.RemoveAt(bestP);
                    remainingSpawns.RemoveAt(bestS);
                }
            }
        }
        catch (Exception e)
        {
            Log($"[EnforceCompetitiveSpawns] Error: {e.Message}");
        }
    }

    /// <summary>
    /// 拔除教練占用的隊伍顏色 (-1)，確保場上 5 名正式隊員完整擁有 5 種代表色
    /// </summary>
    private void EnforceCompetitiveTeammateColors()
    {
        try
        {
            HashSet<CCSPlayerController> coaches = GetAllCoaches();
            foreach (byte side in new[] { (byte)CsTeam.CounterTerrorist, (byte)CsTeam.Terrorist })
            {
                List<CCSPlayerController> sidePlayers = [];
                foreach (var p in Utilities.GetPlayers())
                {
                    if (p is not null && IsPlayerValid(p) && p.TeamNum == side)
                    {
                        sidePlayers.Add(p);
                    }
                }

                HashSet<int> usedColors = [];
                List<CCSPlayerController> needColor = [];

                foreach (CCSPlayerController p in sidePlayers)
                {
                    if (coaches.Contains(p))
                    {
                        if (p.CompTeammateColor != -1)
                        {
                            p.CompTeammateColor = -1;
                            Utilities.SetStateChanged(p, "CCSPlayerController", "m_iCompTeammateColor");
                        }
                        continue;
                    }

                    int c = p.CompTeammateColor;
                    if (c >= 0 && c <= 4 && !usedColors.Contains(c))
                        usedColors.Add(c);
                    else
                        needColor.Add(p);
                }

                int nextColor = 0;
                foreach (CCSPlayerController p in needColor)
                {
                    while (nextColor <= 4 && usedColors.Contains(nextColor))
                        nextColor++;
                    if (nextColor > 4) break;

                    p.CompTeammateColor = nextColor;
                    Utilities.SetStateChanged(p, "CCSPlayerController", "m_iCompTeammateColor");
                    usedColors.Add(nextColor);
                }
            }
        }
        catch (Exception e)
        {
            Log($"[EnforceCompetitiveTeammateColors] Error: {e.Message}");
        }
    }

    private void ResetCoachStats(CCSPlayerController coach)
    {
        if (coach is null || !IsPlayerValid(coach)) return;
        if (coach.InGameMoneyServices is not null)
        {
            coach.InGameMoneyServices.Account = 0;
            Utilities.SetStateChanged(coach, "CCSPlayerController", "m_pInGameMoneyServices");
        }
        if (coach.ActionTrackingServices is not null)
        {
            coach.ActionTrackingServices.MatchStats.Kills = 0;
            coach.ActionTrackingServices.MatchStats.Deaths = 0;
            coach.ActionTrackingServices.MatchStats.Assists = 0;
            coach.ActionTrackingServices.MatchStats.Damage = 0;
            Utilities.SetStateChanged(coach, "CCSPlayerController", "m_pActionTrackingServices");
        }
    }

    private void HandleCoachWeapons(CCSPlayerController coach)
    {
        if (coach is null || !IsPlayerValid(coach)) return;
        coach.RemoveWeapons();
    }

    /// <summary>
    /// 跨影格安全轉移 C4：避免在 EventPlayerGivenC4 同步移除實體引發引擎崩潰
    /// </summary>
    public void TransferCoachBomb(CCSPlayerController coach)
    {
        if (coach is null || !IsPlayerValid(coach) || coach.TeamNum != (byte)CsTeam.Terrorist) return;

        Server.NextFrame(() =>
        {
            if (coach is null || !IsPlayerValid(coach) || coach.TeamNum != (byte)CsTeam.Terrorist) return;
            if (coach.PlayerPawn.Value?.WeaponServices?.MyWeapons is not { } weapons) return;

            CBasePlayerWeapon? bomb = null;
            foreach (var weapon in weapons)
            {
                if (weapon.Value is { IsValid: true, DesignerName: "weapon_c4" } c4)
                {
                    bomb = c4;
                    break;
                }
            }

            if (bomb is null) return;

            HashSet<CCSPlayerController> allCoaches = GetAllCoaches();
            CCSPlayerController? target = null;
            foreach (var p in Utilities.GetPlayers())
            {
                if (p is not null && IsPlayerValid(p) &&
                    !allCoaches.Contains(p) &&
                    p.TeamNum == (byte)CsTeam.Terrorist &&
                    p.PawnIsAlive)
                {
                    target = p;
                    break;
                }
            }

            if (target is null) return;

            bomb.Remove();
            CCSPlayerController finalTarget = target;
            Server.NextFrame(() =>
            {
                if (IsPlayerValid(finalTarget) && finalTarget.PawnIsAlive && finalTarget.TeamNum == (byte)CsTeam.Terrorist)
                {
                    finalTarget.GiveNamedItem("weapon_c4");
                    Log($"[EventPlayerGivenC4 INFO] Transferred bomb from {coach.PlayerName} (Coach) to {finalTarget.PlayerName}.");
                }
            });
        });
    }

    public CsTeam GetCoachTeam(CCSPlayerController coach)
    {
        // ★ 核心安全讀取：只用 TryGetValue 讀取，絕不修改 teamSides 字典！
        if (matchzyTeam1.coach.Contains(coach))
        {
            if (teamSides.TryGetValue(matchzyTeam1, out var s1))
            {
                return s1 == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            }
            return CsTeam.CounterTerrorist;
        }
        if (matchzyTeam2.coach.Contains(coach))
        {
            if (teamSides.TryGetValue(matchzyTeam2, out var s2))
            {
                return s2 == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            }
            return CsTeam.Terrorist;
        }
        return CsTeam.Spectator;
    }

    private void HandleCoachTeam(CCSPlayerController playerController)
    {
        if (playerController is null || !IsPlayerValid(playerController)) return;

        CsTeam targetTeam = GetCoachTeam(playerController);
        if (playerController.Team != targetTeam && (targetTeam == CsTeam.Terrorist || targetTeam == CsTeam.CounterTerrorist))
        {
            // 直接使用 SwitchTeam 無縫切換陣營，不經過觀戰席以防閃爍與偷看敵隊畫面
            playerController.SwitchTeam(targetTeam);
        }

        // 每回合與下半場換邊時，自動更新為 [反恐教練] 或 [恐怖教練]
        UpdateCoachClanTag(playerController);

        if (playerController.InGameMoneyServices is not null) playerController.InGameMoneyServices.Account = 0;
    }

    private void KillCoaches()
    {
        if (isPaused || IsTacticalTimeoutActive()) return;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;

        // 1. 完整備份所有自殺補償金、少人補償金與死亡鏡頭延遲參數
        string suicidePenalty = ConVar.Find("mp_suicide_penalty") is { } cv1 ? (GetConvarStringValue(cv1) ?? "0") : "0";
        string killDefault = ConVar.Find("cash_player_killed_enemy_default") is { } cv2 ? (GetConvarStringValue(cv2) ?? "300") : "300";
        string killFactor = ConVar.Find("cash_player_killed_enemy_factor") is { } cv3 ? (GetConvarStringValue(cv3) ?? "1") : "1";
        string bonusShort = ConVar.Find("cash_team_bonus_shorthanded") is { } cv4 ? (GetConvarStringValue(cv4) ?? "0") : "0";
        string loserShort = ConVar.Find("cash_team_loser_bonus_shorthanded") is { } cv5 ? (GetConvarStringValue(cv5) ?? "0") : "0";
        string specFreezeTime = ConVar.Find("spec_freeze_time") is { } cv6 ? (GetConvarStringValue(cv6) ?? "2") : "2";
        string specFreezeTimeLock = ConVar.Find("spec_freeze_time_lock") is { } cv7 ? (GetConvarStringValue(cv7) ?? "2") : "2";
        string specFreezeDeathanim = ConVar.Find("spec_freeze_deathanim_time") is { } cv8 ? (GetConvarStringValue(cv8) ?? "0") : "0";

        // 2. 先下達歸零指令，徹底關閉敵方自殺補償金與死亡黑畫面過渡
        Server.ExecuteCommand("mp_suicide_penalty 0; cash_player_killed_enemy_default 0; cash_player_killed_enemy_factor 0; cash_team_bonus_shorthanded 0; cash_team_loser_bonus_shorthanded 0; spec_freeze_time 0; spec_freeze_time_lock 0; spec_freeze_deathanim_time 0;");

        // 3. 推遲至下一個影格（確保上述歸零指令已在引擎生效）再執行無聲處死轉觀戰
        Server.NextFrame(() =>
        {
            foreach (var coach in coaches)
            {
                if (coach is null || !IsPlayerValid(coach) || !coach.PawnIsAlive) continue;
                if (isPaused || IsTacticalTimeoutActive()) continue;

                if (coach.PlayerPawn.Value is { } pawn && pawn.CBodyComponent?.SceneNode is { AbsOrigin: { } origin, AbsRotation: { } rotation })
                {
                    Position coachPosition = new(origin, rotation);
                    pawn.Teleport(new(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new(0, 0, 0));
                    
                    // 瞬間解開無敵讓自殺生效
                    pawn.TakesDamage = true;
                    pawn.CommitSuicide(explode: false, force: true);
                }
            }

            // 4. 等死亡結算完畢後（0.15 秒），還原伺服器原始參數，並將教練計分板戰績洗回 0 殺 0 死
            AddTimer(0.15f, () =>
            {
                Server.ExecuteCommand($"mp_suicide_penalty {suicidePenalty}; cash_player_killed_enemy_default {killDefault}; cash_player_killed_enemy_factor {killFactor}; cash_team_bonus_shorthanded {bonusShort}; cash_team_loser_bonus_shorthanded {loserShort}; spec_freeze_time {specFreezeTime}; spec_freeze_time_lock {specFreezeTimeLock}; spec_freeze_deathanim_time {specFreezeDeathanim};");

                foreach (var coach in coaches)
                {
                    ResetCoachStats(coach);
                }
            });
        });
    }

    private void GetCoachSpawns()
    {
        coachSpawns = GetEmptySpawnsData();
        try
        {
            string spawnsConfigPath = Path.Combine(ModuleDirectory, "spawns", "coach", $"{Server.MapName}.json");

            if (!File.Exists(spawnsConfigPath)) return;

            string spawnsConfig = File.ReadAllText(spawnsConfigPath);

            var jsonDictionary = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(spawnsConfig);
            if (jsonDictionary is null) return;

            foreach (var entry in jsonDictionary)
            {
                if (!byte.TryParse(entry.Key, out byte team)) continue;
                List<Position> positionList = [];

                foreach (var positionData in entry.Value)
                {
                    string[] vectorArray = positionData["Vector"].Split(' ');
                    string[] angleArray = positionData["QAngle"].Split(' ');

                    float x = float.Parse(vectorArray[0].Replace(",", ""), CultureInfo.InvariantCulture);
                    float y = float.Parse(vectorArray[1].Replace(",", ""), CultureInfo.InvariantCulture);
                    float z = float.Parse(vectorArray[2].Replace(",", ""), CultureInfo.InvariantCulture);

                    float pitch = float.Parse(angleArray[0].Replace(",", ""), CultureInfo.InvariantCulture);
                    float yaw = float.Parse(angleArray[1].Replace(",", ""), CultureInfo.InvariantCulture);
                    float roll = float.Parse(angleArray[2].Replace(",", ""), CultureInfo.InvariantCulture);

                    Vector vector = new(x, y, z);
                    QAngle qAngle = new(pitch, yaw, roll);

                    positionList.Add(new Position(vector, qAngle));
                }
                coachSpawns[team] = positionList;
            }
            Log($"[GetCoachSpawns] Loaded {coachSpawns.Count} coach spawns for {Server.MapName}");
        }
        catch (Exception ex)
        {
            Log($"[GetCoachSpawns - FATAL] Error getting coach spawns. [ERROR]: {ex.Message}");
        }
    }
}
