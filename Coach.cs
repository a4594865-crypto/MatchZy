using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public CounterStrikeSharp.API.Modules.Timers.Timer? coachKillTimer = null;

    // false = 買槍時間浮在高空俯瞰隊友，買槍結束前 1 秒才變灰（推薦搭配高空座標 JSON）
    // true = 開局 0.25 秒瞬間無感變灰（直接看隊友第一人稱）
    public bool instantCoachGrayOut = false;

    // ▼▼▼ 【0-GC 快取與預配置容器區】 ▼▼▼
    private static readonly byte[] _competitiveTeamSides = [(byte)CsTeam.CounterTerrorist, (byte)CsTeam.Terrorist];
    private static readonly Vector _zeroVelocity = new(0f, 0f, 0f);
    private static readonly QAngle _abyssPitchAngle = new(89.0f, 0.0f, 0.0f);
    private static readonly Vector _reusableAbyssOrigin = new(0f, 0f, 0f);

    private readonly HashSet<CCSPlayerController> _cachedAllCoaches = new(4);
    private readonly List<CCSPlayerController> _reusableRemainingPlayers = new(16);
    private readonly List<Position> _reusableRemainingSpawns = new(16);
    private readonly List<CCSPlayerController> _reusableSidePlayers = new(16);
    private readonly HashSet<int> _reusableUsedColors = new(8);
    private readonly List<CCSPlayerController> _reusableNeedColor = new(16);

    private CCSGameRulesProxy? _cachedCoachGameRulesProxy = null;

    private ConVar? _cvCoachMpFreezeTime = null;
    private ConVar? _cvSuicidePenalty = null;
    private ConVar? _cvKillDefault = null;
    private ConVar? _cvKillFactor = null;
    private ConVar? _cvBonusShort = null;
    private ConVar? _cvLoserShort = null;
    private ConVar? _cvSpecFreezeTime = null;
    private ConVar? _cvSpecFreezeTimeLock = null;
    private ConVar? _cvSpecFreezeDeathanim = null;

    private Action? _enforceSpawnsAction;
    private Action? _enforceTeammateColorsAction;
    private Action? _killCoachesAction;
    // ▲▲▲ ▲▲▲ ▲▲▲

    public HashSet<CCSPlayerController> GetAllCoaches()
    {
        // 【0-GC 優化】：複用預配置的 HashSet，免除每回合多次呼叫時重複 new HashSet
        _cachedAllCoaches.Clear();
        foreach (var coach in matchzyTeam1.coach)
        {
            _cachedAllCoaches.Add(coach);
        }
        foreach (var coach in matchzyTeam2.coach)
        {
            _cachedAllCoaches.Add(coach);
        }

        return _cachedAllCoaches;
    }

    private CCSGameRules? GetCoachCachedGameRules()
    {
        if (_cachedCoachGameRulesProxy is { IsValid: true, GameRules: not null })
        {
            return _cachedCoachGameRulesProxy.GameRules;
        }

        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity is { IsValid: true, GameRules: not null } proxy)
            {
                _cachedCoachGameRulesProxy = proxy;
                return proxy.GameRules;
            }
        }
        return null;
    }

    public void UpdateCoachClanTag(CCSPlayerController coach)
    {
        if (coach is null || !IsPlayerValid(coach)) return;

        CsTeam currentCoachTeam = GetCoachTeam(coach);
        string targetTag = currentCoachTeam == CsTeam.CounterTerrorist ? " 教 練 " : " 教 練 ";

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
            PrintToPlayerChat(player, $" 練 習 模 式 中 無 法 使 用 {ChatColors.Green}教練指令{ChatColors.Default}");
            return;
        }
        if (IsWingmanMode())
        {
            PrintToPlayerChat(player, $" 搭 檔 模 式 中 無 法 使 用 {ChatColors.Green}教練指令{ChatColors.Default}");
            return;
        }

        // 【0-GC 優化】：使用安全快取取得 GameRules，免除全圖實體掃描
        CCSGameRules? gameRules = GetCoachCachedGameRules();
        if ((isMatchLive || isKnifeRound) && gameRules is { FreezePeriod: false, WarmupPeriod: false })
        {
            PrintToPlayerChat(player, $" 回 合 進 行 中，無 法 切 換 為 {ChatColors.Green}教 練 身 分{ChatColors.Default}");
            return;
        }

        if (matchzyTeam1.coach.Contains(player) || matchzyTeam2.coach.Contains(player))
        {
            PrintToPlayerChat(player, $" 你 已 經 是 教 練 了！若 要 退 出 請 輸 入 {ChatColors.Green} .uncoach {ChatColors.Default}");
            return;
        }

        // 【0-GC 優化】：使用 ReadOnlySpan 比對，免除 Trim().ToLower() 產生暫存字串
        ReadOnlySpan<char> sideSpan = side.AsSpan().Trim();
        string normalizedSide;

        if (sideSpan.IsEmpty)
        {
            if (player.TeamNum == (byte)CsTeam.Terrorist) normalizedSide = "t";
            else if (player.TeamNum == (byte)CsTeam.CounterTerrorist) normalizedSide = "ct";
            else
            {
                PrintToPlayerChat(player, $" 請 先 加 入 隊 伍，或 輸 入 {ChatColors.Green}.coach t{ChatColors.Default} / {ChatColors.Green}.coach ct{ChatColors.Default}");
                return;
            }
        }
        else if (sideSpan.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSide = "t";
        }
        else if (sideSpan.Equals("ct", StringComparison.OrdinalIgnoreCase))
        {
            normalizedSide = "ct";
        }
        else
        {
            PrintToPlayerChat(player, $" 指令格式：{ChatColors.Green}.coach t{ChatColors.Default} 或 {ChatColors.Green}.coach ct{ChatColors.Default}");
            return;
        }

        byte wantedTeam = normalizedSide == "t" ? (byte)CsTeam.Terrorist : (byte)CsTeam.CounterTerrorist;
        if (matchStarted && player.TeamNum != wantedTeam)
        {
            PrintToPlayerChat(player, $" 比 賽 進 行 中，僅 擔 任 {ChatColors.Green}自己所屬隊伍{ChatColors.Default} 教 練");
            return;
        }

        Team matchZyCoachTeam;
        if (normalizedSide == "t")
        {
            matchZyCoachTeam = reverseTeamSides.TryGetValue("TERRORIST", out var tTeam) ? tTeam : matchzyTeam2;
        }
        else
        {
            matchZyCoachTeam = reverseTeamSides.TryGetValue("CT", out var ctTeam) ? ctTeam : matchzyTeam1;
        }

        if (matchZyCoachTeam.coach.Count >= 1)
        {
            PrintToPlayerChat(player, $" 該 隊 已 經 有 {ChatColors.Green}教 練{ChatColors.Default} 了，每隊僅限 1 名教練");
            return;
        }

        matchZyCoachTeam.coach.Add(player);

        if (player.UserId is int uid)
        {
            playerReadyStatus[uid] = false;
        }

        if (player.InGameMoneyServices is not null) player.InGameMoneyServices.Account = 0;

        HandleCoachTeam(player);
        UpdateCoachClanTag(player);
        Server.NextFrame(_enforceTeammateColorsAction ??= EnforceCompetitiveTeammateColors);

        string sideDisplayName = normalizedSide == "ct" ? "反恐小組" : "恐怖份子";
        PrintToPlayerChat(player, $" 你 現 在 擔 任 {ChatColors.Green}{sideDisplayName}{ChatColors.Default} 教練！輸 入{ChatColors.Green} .uncoach {ChatColors.Default}退 出 教 練 席");
        PrintToAllChat($" {ChatColors.Green}{player.PlayerName}{ChatColors.Default} 現 在 擔 任 {ChatColors.Green}{sideDisplayName}{ChatColors.Default} 的 教 練");

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

        float killDelay;
        if (instantCoachGrayOut)
        {
            killDelay = 0.25f;
        }
        else
        {
            // 【0-GC 優化】：快取 mp_freezetime ConVar 指標
            _cvCoachMpFreezeTime ??= ConVar.Find("mp_freezetime");
            int freezeTime = _cvCoachMpFreezeTime is not null ? _cvCoachMpFreezeTime.GetPrimitiveValue<int>() : 15;
            killDelay = Math.Max(0.5f, freezeTime - 1.0f);
        }
        coachKillTimer ??= AddTimer(killDelay, _killCoachesAction ??= KillCoaches);

        int coachIdx = 0;
        
        foreach (CCSPlayerController coach in coaches)
        {
            if (coach is null || !IsPlayerValid(coach)) continue;

            if (coach.InGameMoneyServices is not null) coach.InGameMoneyServices.Account = 0;

            CCSPlayerController capturedCoach = coach;
            AddTimer(0.1f, () => HandleCoachTeam(capturedCoach));
            ResetCoachStats(capturedCoach);

            int currentIdx = coachIdx; 
            Server.NextFrame(() =>
            {
                if (!IsPlayerValid(capturedCoach)) return;
                
                SetPlayerInvisible(player: capturedCoach, setWeaponsInvisible: false);

                if (capturedCoach.PlayerPawn.Value is { } pawn)
                {
                    pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                    pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                    pawn.TakesDamage = false;

                    Position? targetCoachPos = null;
                    if (coachSpawns.TryGetValue(capturedCoach.TeamNum, out var teamSpawns) && teamSpawns.Count > 0)
                    {
                        // 【0-GC 優化】：改用 Random.Shared 免除 new Random() 記憶體配置
                        targetCoachPos = teamSpawns[Random.Shared.Next(0, teamSpawns.Count)];
                    }
                    else if (TryGetBehindTeamCoachSpawn(capturedCoach.TeamNum, currentIdx, out Position behindPos))
                    {
                        targetCoachPos = behindPos;
                    }
                    else if (pawn.CBodyComponent?.SceneNode is { AbsOrigin: { } origin, AbsRotation: { } rotation })
                    {
                        targetCoachPos = new Position(new Vector(origin.X, origin.Y, origin.Z + 100.0f), rotation);
                    }

                    if (targetCoachPos is not null)
                    {
                        Position finalPos = targetCoachPos;
                        AddTimer(0.05f, () =>
                        {
                            if (IsPlayerValid(capturedCoach) && capturedCoach.PlayerPawn.Value is { } validPawn)
                            {
                                HandleCoachWeapons(capturedCoach);
                                validPawn.Teleport(finalPos.PlayerPosition, finalPos.PlayerAngle, _zeroVelocity);
                            }
                        });
                    }
                }
            });
            coachIdx++;
        }

        AddTimer(0.15f, _enforceSpawnsAction ??= EnforceCompetitiveSpawns);
        AddTimer(0.3f, _enforceTeammateColorsAction ??= EnforceCompetitiveTeammateColors);
    }

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
            float rearX = cx + fx * minProj + rx * spread;
            float rearY = cy + fy * minProj + ry * spread;

            result = new Position(new Vector(rearX - fx * 40.0f, rearY - fy * 40.0f, cz + up), new QAngle(pitch, yawDeg, 0.0f));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnforceCompetitiveSpawns()
    {
        try
        {
            HashSet<CCSPlayerController> coaches = GetAllCoaches();
            if (coaches.Count == 0) return;

            const float keepDistSq = 75.0f * 75.0f;
            var allPlayers = Utilities.GetPlayers();

            // 【0-GC 優化】：複用靜態陣列與預配置 List，免除每回合重複 new List 與雙重 GetPlayers()
            foreach (byte side in _competitiveTeamSides)
            {
                if (!spawnsData.TryGetValue(side, out var teamSpawns) || teamSpawns.Count == 0)
                    continue;

                _reusableRemainingPlayers.Clear();
                foreach (var p in allPlayers)
                {
                    if (p is not null && IsPlayerValid(p) && p.TeamNum == side && !coaches.Contains(p) &&
                        p.PawnIsAlive && p.PlayerPawn.Value?.CBodyComponent?.SceneNode is not null)
                    {
                        _reusableRemainingPlayers.Add(p);
                    }
                }

                if (_reusableRemainingPlayers.Count == 0) continue;

                _reusableRemainingSpawns.Clear();
                _reusableRemainingSpawns.AddRange(teamSpawns);

                while (_reusableRemainingPlayers.Count > 0 && _reusableRemainingSpawns.Count > 0)
                {
                    int keepP = -1, keepS = -1;
                    float keepBest = float.MaxValue;

                    for (int pi = 0; pi < _reusableRemainingPlayers.Count; pi++)
                    {
                        Vector pos = _reusableRemainingPlayers[pi].PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                        for (int si = 0; si < _reusableRemainingSpawns.Count; si++)
                        {
                            Vector sp = _reusableRemainingSpawns[si].PlayerPosition;
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

                    _reusableRemainingPlayers.RemoveAt(keepP);
                    _reusableRemainingSpawns.RemoveAt(keepS);
                }

                while (_reusableRemainingPlayers.Count > 0 && _reusableRemainingSpawns.Count > 0)
                {
                    int bestP = -1, bestS = -1;
                    float bestDist = float.MaxValue;

                    for (int pi = 0; pi < _reusableRemainingPlayers.Count; pi++)
                    {
                        Vector pos = _reusableRemainingPlayers[pi].PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin;
                        for (int si = 0; si < _reusableRemainingSpawns.Count; si++)
                        {
                            Vector sp = _reusableRemainingSpawns[si].PlayerPosition;
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

                    CCSPlayerController displacedPlayer = _reusableRemainingPlayers[bestP];
                    Position targetSpawn = _reusableRemainingSpawns[bestS];

                    if (displacedPlayer.PlayerPawn.Value is { } pawn)
                    {
                        pawn.Teleport(targetSpawn.PlayerPosition, targetSpawn.PlayerAngle, _zeroVelocity);
                    }

                    _reusableRemainingPlayers.RemoveAt(bestP);
                    _reusableRemainingSpawns.RemoveAt(bestS);
                }
            }
        }
        catch (Exception e)
        {
            Log($"[EnforceCompetitiveSpawns] Error: {e.Message}");
        }
    }

    private void EnforceCompetitiveTeammateColors()
    {
        try
        {
            HashSet<CCSPlayerController> coaches = GetAllCoaches();
            var allPlayers = Utilities.GetPlayers();

            // 【0-GC 優化】：複用預配置容器，消除每回合 6 個 List/HashSet 配置與雙重 GetPlayers()
            foreach (byte side in _competitiveTeamSides)
            {
                _reusableSidePlayers.Clear();
                foreach (var p in allPlayers)
                {
                    if (p is not null && IsPlayerValid(p) && p.TeamNum == side)
                    {
                        _reusableSidePlayers.Add(p);
                    }
                }

                _reusableUsedColors.Clear();
                _reusableNeedColor.Clear();

                foreach (CCSPlayerController p in _reusableSidePlayers)
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
                    if (c >= 0 && c <= 4 && !_reusableUsedColors.Contains(c))
                        _reusableUsedColors.Add(c);
                    else
                        _reusableNeedColor.Add(p);
                }

                int nextColor = 0;
                foreach (CCSPlayerController p in _reusableNeedColor)
                {
                    while (nextColor <= 4 && _reusableUsedColors.Contains(nextColor))
                        nextColor++;
                    if (nextColor > 4) break;

                    p.CompTeammateColor = nextColor;
                    Utilities.SetStateChanged(p, "CCSPlayerController", "m_iCompTeammateColor");
                    _reusableUsedColors.Add(nextColor);
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

    // 已替換為 Coach原官方.cs 的 C4 處理方式
    public void TransferCoachBomb(CCSPlayerController coach) {
        if (coach is null || coach.TeamNum != (byte)CsTeam.Terrorist) return; 

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
        bool hasTerroristTeamMapping = reverseTeamSides.TryGetValue("TERRORIST", out var terroristTeam);
        
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not null && IsPlayerValid(p) &&
                (!hasTerroristTeamMapping || !terroristTeam!.coach.Contains(p)) && 
                p.TeamNum == (byte)CsTeam.Terrorist && 
                p.PawnIsAlive)
            {
                target = p;
                break;
            }
        }

        if (target is null) return; 

        Log($"[EventPlayerGivenC4 INFO] Transferred bomb from {coach.PlayerName} (Coach) to {target.PlayerName}.");
        bomb.Remove();
        target.GiveNamedItem("weapon_c4");
    }

    public CsTeam GetCoachTeam(CCSPlayerController coach)
    {
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
        if (playerController.Team != targetTeam && targetTeam is CsTeam.Terrorist or CsTeam.CounterTerrorist)
        {
            if (playerController.PawnIsAlive)
            {
                if (playerController.PlayerPawn.Value is { } pawn)
                {
                    pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                    pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                    pawn.TakesDamage = true;

                    // ▼▼▼ 防偷看機制：閃白 + 深淵傳送 ▼▼▼
                    pawn.FlashDuration = 2.0f;
                    pawn.FlashMaxAlpha = 255.0f;
                    Utilities.SetStateChanged(pawn, "C_CSPlayerPawnBase", "m_flFlashDuration");
                    Utilities.SetStateChanged(pawn, "C_CSPlayerPawnBase", "m_flFlashMaxAlpha");

                    if (pawn.CBodyComponent?.SceneNode is { AbsOrigin: { } origin })
                    {
                        // 【0-GC 優化】：複用靜態 Vector / QAngle 容器，免除 new 座標垃圾
                        _reusableAbyssOrigin.X = origin.X;
                        _reusableAbyssOrigin.Y = origin.Y;
                        _reusableAbyssOrigin.Z = origin.Z - 10000.0f;
                        pawn.Teleport(_reusableAbyssOrigin, _abyssPitchAngle, _zeroVelocity);
                    }

                    pawn.CommitSuicide(explode: false, force: true);
                }
                
                Server.NextFrame(() =>
                {
                    if (IsPlayerValid(playerController) && playerController.Team != targetTeam)
                    {
                        playerController.SwitchTeam(targetTeam);
                    }
                });
            }
            else
            {
                playerController.SwitchTeam(targetTeam);
            }
        }

        UpdateCoachClanTag(playerController);

        if (playerController.InGameMoneyServices is not null) playerController.InGameMoneyServices.Account = 0;
    }

    private void KillCoaches()
    {
        if (isPaused || IsTacticalTimeoutActive()) return;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;

        // 【0-GC 關鍵熱區優化】：快取 8 個 ConVar 指標，免除買槍結束前 1 秒連續 8 次跨 C++ 搜尋 ConVar
        _cvSuicidePenalty ??= ConVar.Find("mp_suicide_penalty");
        _cvKillDefault ??= ConVar.Find("cash_player_killed_enemy_default");
        _cvKillFactor ??= ConVar.Find("cash_player_killed_enemy_factor");
        _cvBonusShort ??= ConVar.Find("cash_team_bonus_shorthanded");
        _cvLoserShort ??= ConVar.Find("cash_team_loser_bonus_shorthanded");
        _cvSpecFreezeTime ??= ConVar.Find("spec_freeze_time");
        _cvSpecFreezeTimeLock ??= ConVar.Find("spec_freeze_time_lock");
        _cvSpecFreezeDeathanim ??= ConVar.Find("spec_freeze_deathanim_time");

        string suicidePenalty = _cvSuicidePenalty is not null ? (GetConvarStringValue(_cvSuicidePenalty) ?? "0") : "0";
        string killDefault = _cvKillDefault is not null ? (GetConvarStringValue(_cvKillDefault) ?? "300") : "300";
        string killFactor = _cvKillFactor is not null ? (GetConvarStringValue(_cvKillFactor) ?? "1") : "1";
        string bonusShort = _cvBonusShort is not null ? (GetConvarStringValue(_cvBonusShort) ?? "0") : "0";
        string loserShort = _cvLoserShort is not null ? (GetConvarStringValue(_cvLoserShort) ?? "0") : "0";
        string specFreezeTime = _cvSpecFreezeTime is not null ? (GetConvarStringValue(_cvSpecFreezeTime) ?? "2") : "2";
        string specFreezeTimeLock = _cvSpecFreezeTimeLock is not null ? (GetConvarStringValue(_cvSpecFreezeTimeLock) ?? "2") : "2";
        string specFreezeDeathanim = _cvSpecFreezeDeathanim is not null ? (GetConvarStringValue(_cvSpecFreezeDeathanim) ?? "0") : "0";

        Server.ExecuteCommand("mp_suicide_penalty 0; cash_player_killed_enemy_default 0; cash_player_killed_enemy_factor 0; cash_team_bonus_shorthanded 0; cash_team_loser_bonus_shorthanded 0; spec_freeze_time 0; spec_freeze_time_lock 0; spec_freeze_deathanim_time 0;");

        AddTimer(0.1f, () =>
        {
            foreach (var coach in GetAllCoaches())
            {
                if (coach is null || !IsPlayerValid(coach) || !coach.PawnIsAlive) continue;
                if (isPaused || IsTacticalTimeoutActive()) continue;

                if (coach.PlayerPawn.Value is { } pawn && pawn.CBodyComponent?.SceneNode is { AbsOrigin: { } origin })
                {
                    pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                    pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                    pawn.TakesDamage = true;

                    // ▼▼▼ 核心修復：防偷看機制 (Anti-Info Leak) ▼▼▼
                    // 1. 強制閃白螢幕，遮蔽最後一幀的畫面過渡
                    pawn.FlashDuration = 2.0f;
                    pawn.FlashMaxAlpha = 255.0f;
                    Utilities.SetStateChanged(pawn, "C_CSPlayerPawnBase", "m_flFlashDuration");
                    Utilities.SetStateChanged(pawn, "C_CSPlayerPawnBase", "m_flFlashMaxAlpha");

                    // 2. 【0-GC 優化】：複用靜態容器，將教練實體瞬間傳送到極深地底 (-10000) 並讓視角朝下 (Pitch 89)
                    // 強制移出敵人的 PVS (可視範圍)，伺服器會瞬間切斷網路封包，徹底防止雷達與死亡視角漏位！
                    _reusableAbyssOrigin.X = origin.X;
                    _reusableAbyssOrigin.Y = origin.Y;
                    _reusableAbyssOrigin.Z = origin.Z - 10000.0f;
                    pawn.Teleport(_reusableAbyssOrigin, _abyssPitchAngle, _zeroVelocity);
                    
                    pawn.CommitSuicide(explode: false, force: true);
                }
            }

            AddTimer(0.2f, () =>
            {
                Server.ExecuteCommand($"mp_suicide_penalty {suicidePenalty}; cash_player_killed_enemy_default {killDefault}; cash_player_killed_enemy_factor {killFactor}; cash_team_bonus_shorthanded {bonusShort}; cash_team_loser_bonus_shorthanded {loserShort}; spec_freeze_time {specFreezeTime}; spec_freeze_time_lock {specFreezeTimeLock}; spec_freeze_deathanim_time {specFreezeDeathanim};");

                foreach (var coach in GetAllCoaches())
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
