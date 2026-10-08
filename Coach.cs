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
    public bool instantCoachGrayOut = true;

    public HashSet<CCSPlayerController> GetAllCoaches()
    {
        HashSet<CCSPlayerController> coaches = [.. matchzyTeam1.coach];
        coaches.UnionWith(matchzyTeam2.coach);

        return coaches;
    }

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
            ReplyToUserCommand(player, "Coach command can only be used in match mode!");
            return;
        }
        if (IsWingmanMode())
        {
            ReplyToUserCommand(player, "Coach command cannot be used in wingman!");
            return;
        }

        side = side.Trim().ToLower();

        if (side is not "t" and not "ct")
        {
            ReplyToUserCommand(player, "Usage: .coach t or .coach ct");
            return;
        }

        if (matchzyTeam1.coach.Contains(player) || matchzyTeam2.coach.Contains(player))
        {
            ReplyToUserCommand(player, "You are already coaching a team!");
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

        // 教練不需要按 .R：從準備名單中移除
        if (player.UserId is int uid)
        {
            playerReadyStatus.Remove(uid);
        }

        if (player.InGameMoneyServices is not null) player.InGameMoneyServices.Account = 0;

        // 無縫切換陣營並套用 [反恐教練] / [恐怖教練]
        HandleCoachTeam(player);
        UpdateCoachClanTag(player);
        Server.NextFrame(EnforceCompetitiveTeammateColors);

        ReplyToUserCommand(player, $"You are now coaching {matchZyCoachTeam.teamName}! Use .uncoach to stop coaching");
        PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} is now coaching {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default}!");

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

        float killDelay = instantCoachGrayOut ? 0.25f : Math.Max(0.5f, (ConVar.Find("mp_freezetime") is { } cvFreeze ? cvFreeze.GetPrimitiveValue<int>() : 15) - 1.0f);
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

        AddTimer(0.15f, EnforceCompetitiveSpawns);
        AddTimer(0.3f, EnforceCompetitiveTeammateColors);
    }

    private void EnforceCompetitiveSpawns()
    {
        try
        {
            HashSet<CCSPlayerController> coaches = GetAllCoaches();
            if (coaches.Count == 0) return;

            const float keepDistSq = 75.0f * 75.0f;

            foreach (byte side in new[] { (byte)CsTeam.CounterTerrorist, (byte)CsTeam.Terrorist })
            {
                if (!spawnsData.TryGetValue(side, out var teamSpawns) || teamSpawns.Count == 0) continue;

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
                    if (p is not null && IsPlayerValid(p) && p.TeamNum == side) sidePlayers.Add(p);
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
                    if (c >= 0 && c <= 4 && !usedColors.Contains(c)) usedColors.Add(c);
                    else needColor.Add(p);
                }

                int nextColor = 0;
                foreach (CCSPlayerController p in needColor)
                {
                    while (nextColor <= 4 && usedColors.Contains(nextColor)) nextColor++;
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
                    !reverseTeamSides["TERRORIST"].coach.Contains(p) &&
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
                }
            });
        });
    }

    public CsTeam GetCoachTeam(CCSPlayerController coach)
    {
        if (matchzyTeam1.coach.Contains(coach))
        {
            return teamSides[matchzyTeam1] == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        }
        if (matchzyTeam2.coach.Contains(coach))
        {
            return teamSides[matchzyTeam2] == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        }
        return CsTeam.Spectator;
    }

    private void HandleCoachTeam(CCSPlayerController playerController)
    {
        if (playerController is null || !IsPlayerValid(playerController)) return;
        CsTeam oldTeam = GetCoachTeam(playerController);
        if (playerController.Team != oldTeam)
        {
            playerController.ChangeTeam(CsTeam.Spectator);
            AddTimer(0.01f, () => playerController.ChangeTeam(oldTeam));
        }
        UpdateCoachClanTag(playerController);
        if (playerController.InGameMoneyServices is not null) playerController.InGameMoneyServices.Account = 0;
    }

    private void KillCoaches()
    {
        if (isPaused || IsTacticalTimeoutActive()) return;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;
        
        string suicidePenalty = ConVar.Find("mp_suicide_penalty") is { } cv1 ? (GetConvarStringValue(cv1) ?? "0") : "0";
        string killDefault = ConVar.Find("cash_player_killed_enemy_default") is { } cv2 ? (GetConvarStringValue(cv2) ?? "300") : "300";
        string killFactor = ConVar.Find("cash_player_killed_enemy_factor") is { } cv3 ? (GetConvarStringValue(cv3) ?? "1") : "1";
        string bonusShort = ConVar.Find("cash_team_bonus_shorthanded") is { } cv4 ? (GetConvarStringValue(cv4) ?? "0") : "0";
        string loserShort = ConVar.Find("cash_team_loser_bonus_shorthanded") is { } cv5 ? (GetConvarStringValue(cv5) ?? "0") : "0";
        string specFreezeTime = ConVar.Find("spec_freeze_time") is { } cv6 ? (GetConvarStringValue(cv6) ?? "2") : "2";
        string specFreezeTimeLock = ConVar.Find("spec_freeze_time_lock") is { } cv7 ? (GetConvarStringValue(cv7) ?? "2") : "2";
        string specFreezeDeathanim = ConVar.Find("spec_freeze_deathanim_time") is { } cv8 ? (GetConvarStringValue(cv8) ?? "0") : "0";

        Server.ExecuteCommand("mp_suicide_penalty 0; cash_player_killed_enemy_default 0; cash_player_killed_enemy_factor 0; cash_team_bonus_shorthanded 0; cash_team_loser_bonus_shorthanded 0; spec_freeze_time 0; spec_freeze_time_lock 0; spec_freeze_deathanim_time 0;");

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
                    pawn.TakesDamage = true;
                    pawn.CommitSuicide(explode: false, force: true);
                }
            }

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
                byte team = byte.Parse(entry.Key);
                List<Position> positionList = [];

                foreach (var positionData in entry.Value)
                {
                    string[] vectorArray = positionData["Vector"].Split(' ');
                    string[] angleArray = positionData["QAngle"].Split(' ');

                    Vector vector = new(float.Parse(vectorArray[0], CultureInfo.InvariantCulture), float.Parse(vectorArray[1], CultureInfo.InvariantCulture), float.Parse(vectorArray[2], CultureInfo.InvariantCulture));
                    QAngle qAngle = new(float.Parse(angleArray[0], CultureInfo.InvariantCulture), float.Parse(angleArray[1], CultureInfo.InvariantCulture), float.Parse(angleArray[2], CultureInfo.InvariantCulture));

                    Position position = new(vector, qAngle);
                    positionList.Add(position);
                }
                coachSpawns[team] = positionList;
            }
            Log($"[GetCoachSpawns] Loaded {coachSpawns.Count} coach spawns");
        }
        catch (Exception ex)
        {
            Log($"[GetCoachSpawns - FATAL] Error getting coach spawns. [ERROR]: {ex.Message}");
        }
    }
}
