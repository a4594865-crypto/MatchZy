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

    // true = 開局 0.2 秒瞬間無感變灰（直接看隊友第一人稱）
    // false = 買槍時間浮在高空俯瞰隊友，買槍結束前 1 秒才變灰
    public bool instantCoachGrayOut = true;

    public HashSet<CCSPlayerController> GetAllCoaches()
    {
        HashSet<CCSPlayerController> coaches = [.. matchzyTeam1.coach];
        coaches.UnionWith(matchzyTeam2.coach);

        return coaches;
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

        // 若只輸入 .coach 沒帶參數，自動依照玩家目前所在的陣營判別
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

        Team matchZyCoachTeam;
        if (side == "t")
        {
            matchZyCoachTeam = reverseTeamSides["TERRORIST"];
        }
        else if (side == "ct")
        {
            matchZyCoachTeam = reverseTeamSides["CT"];
        }
        else
        {
            return;
        }

        matchZyCoachTeam.coach.Add(player);
        player.Clan = $"[{matchZyCoachTeam.teamName} COACH]";
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");

        if (player.InGameMoneyServices is not null) player.InGameMoneyServices.Account = 0;

        // 若在熱身階段跨隊輸入 .coach，自動幫他切換到該陣營並整理隊友五色
        HandleCoachTeam(player);
        Server.NextFrame(EnforceCompetitiveTeammateColors);

        PrintToPlayerChat(player, $" 你 現 在 擔 任 {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default} 的教練！輸 入 {ChatColors.Green}.uncoach{ChatColors.Default} 可 退 出 教 練 席");
        PrintToAllChat($" {ChatColors.Green}{player.PlayerName}{ChatColors.Default} 現 在 擔 任 {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default} 的 教 練！");
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

        // 讀取地圖專屬教練高空座標（若無該地圖 JSON 檔也能照常運作，不會中斷）
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
                else if (pawn.CBodyComponent?.SceneNode is { AbsOrigin: { } origin, AbsRotation: { } rotation })
                {
                    // 若該地圖沒有設定教練 JSON，自動將教練往上升高 100 單位，避開地面選手
                    targetCoachPos = new Position(new Vector(origin.X, origin.Y, origin.Z + 100.0f), rotation);
                }

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

                // 第一階段：已經站在標準出生點 75 單位內的選手直接保留原位，不觸發傳送
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

                // 第二階段：將被教練擠去非標準出生點的選手，傳送到距離最近的空閒標準出生點
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

            CCSPlayerController? target = null;
            foreach (var p in Utilities.GetPlayers())
            {
                if (p is not null && IsPlayerValid(p) &&
                    !reverseTeamSides["TERRORIST"].coach.
