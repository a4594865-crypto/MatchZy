using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Cvars;
using System.Text.Json;

namespace MatchZy;

public partial class MatchZy
{

    public CounterStrikeSharp.API.Modules.Timers.Timer? coachKillTimer = null;

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
        player.Clan = $"[{matchZyCoachTeam.teamName} COACH]";
        if (player.InGameMoneyServices is not null) player.InGameMoneyServices.Account = 0;
        ReplyToUserCommand(player, $"You are now coaching {matchZyCoachTeam.teamName}! Use .uncoach to stop coaching");
        PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} is now coaching {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default}!");
    }

    public void HandleCoaches()
    {
        coachKillTimer?.Kill();
        coachKillTimer = null;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;
        
        // 拔除 Any()，改為 0 GC 迴圈
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
            Log($"[HandleCoaches] No coach spawns found, player positions will not be swapped!");
            return;
        }

        int freezeTime = ConVar.Find("mp_freezetime") is { } cvFreeze ? cvFreeze.GetPrimitiveValue<int>() : 2;
        freezeTime = freezeTime > 2 ? freezeTime: 2;
        coachKillTimer ??= AddTimer(freezeTime - 1f, KillCoaches);

        Random random = new();
        foreach (CCSPlayerController coach in coaches)
        {
            if (coach is null || !IsPlayerValid(coach)) continue;
            Team coachTeam = matchzyTeam1.coach.Contains(coach) ? matchzyTeam1 : matchzyTeam2;
            int coachTeamNum = teamSides[coachTeam] == "CT" ? 3 : 2;
            if (coach.InGameMoneyServices is not null) coach.InGameMoneyServices.Account = 0;

            AddTimer(0.5f, () => HandleCoachTeam(coach));

            if (coach.ActionTrackingServices is not null)
            {
                coach.ActionTrackingServices.MatchStats.Kills = 0;
                coach.ActionTrackingServices.MatchStats.Deaths = 0;
                coach.ActionTrackingServices.MatchStats.Assists = 0;
                coach.ActionTrackingServices.MatchStats.Damage = 0;
            }

            SetPlayerInvisible(player: coach, setWeaponsInvisible: false);
            // Stopping the coaches from moving, so that they don't block the players.
            if (coach.PlayerPawn.Value is { } pawn)
            {
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;

                if (coachSpawns.TryGetValue(coach.TeamNum, out var teamSpawns) && teamSpawns.Count > 0)
                {
                    // Picking a random position for the coach (from coachSpawns) to teleport them.
                    Position newPosition = teamSpawns[random.Next(0, teamSpawns.Count)];

                    // Elevating coach before dropping the C4 to prevent it going inside the ground.
                    AddTimer(0.05f, () =>
                    {
                        HandleCoachWeapons(coach);
                        if (coach.PlayerPawn.Value is { } validPawn)
                        {
                            validPawn.Teleport(newPosition.PlayerPosition, newPosition.PlayerAngle, new(0, 0, 0));
                        }
                    });
                }
            }
        }

        List<CCSPlayerController> players = Utilities.GetPlayers();
        HashSet<Position> occupiedSpawns = [];
        HashSet<CCSPlayerController> incorrectSpawnedPlayers = [];

        foreach (CCSPlayerController player in players)
        {
            if (player is null || !IsPlayerValid(player) || coaches.Contains(player)) continue;

            if (!spawnsData.TryGetValue(player.TeamNum, out var teamPositions) || teamPositions.Count == 0) continue;
            
            // 模式提取：保護並拆解多重解構帶來的潛在空指標
            if (player.PlayerPawn.Value?.CBodyComponent?.SceneNode is not { AbsOrigin: { } origin, AbsRotation: { } rotation }) 
                continue;

            Position playerPosition = new(origin, rotation);
            bool isCompetitiveSpawn = false;
            foreach (Position position in teamPositions)
            {
                if (position.Equals(playerPosition))
                {
                    occupiedSpawns.Add(position);
                    isCompetitiveSpawn = true;
                    break;
                }
            }
            if (isCompetitiveSpawn) continue;

            incorrectSpawnedPlayers.Add(player);
        }

        foreach (CCSPlayerController player in incorrectSpawnedPlayers)
        {
            if (player is null || !IsPlayerValid(player) || coaches.Contains(player)) continue;

            if (!spawnsData.TryGetValue(player.TeamNum, out var teamPositions)) continue;
            
            foreach (Position position in teamPositions)
            {
                if (occupiedSpawns.Contains(position)) continue;
                occupiedSpawns.Add(position);
                AddTimer(0.1f, () =>
                {
                    if (player.PlayerPawn.Value is { } pawn)
                    {
                        pawn.Teleport(position.PlayerPosition, position.PlayerAngle, new(0, 0, 0));
                    }
                });
                break;
            }
        }
    }

    private void HandleCoachWeapons(CCSPlayerController coach)
    {
        if (coach is null || !IsPlayerValid(coach)) return;
        coach.RemoveWeapons();
    }

    /// <summary>
    /// Transfers bomb from coach to first available non-coach terrorist.
    /// </summary> 
    public void TransferCoachBomb(CCSPlayerController coach) {
        if (coach is null || coach.TeamNum != (byte)CsTeam.Terrorist) return; // can't have bomb

        // find bomb and new target
        if (coach.PlayerPawn.Value?.WeaponServices?.MyWeapons is not { } weapons) return;

        // 🏆 修正 Line 223 警告：宣告為明確的可空實體，不再依賴危險的 CHandle
        CBasePlayerWeapon? bomb = null;
        
        // 拔除 LINQ FirstOrDefault，改為 0 GC 效能迴圈
        foreach (var weapon in weapons)
        {
            if (weapon.Value is { IsValid: true, DesignerName: "weapon_c4" } c4)
            {
                // 🏆 直接把提取出的炸彈實體 (c4) 存起來
                bomb = c4;
                break;
            }
        }

        // 🏆 修正 Line 237 警告：現在 bomb 已經是乾淨的實體，直接判斷 null 即可，徹底消除 CS8602 警告
        if (bomb is null) return; // should never trigger

        CCSPlayerController? target = null;
        
        // 拔除第二個 LINQ FirstOrDefault，改為 0 GC 效能迴圈
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

        if (target is null) return; // should never trigger

        // transfer bomb
        Log($"[EventPlayerGivenC4 INFO] Transferred bomb from {coach.PlayerName} (Coach) to {target.PlayerName}.");
        bomb.Remove();
        target.GiveNamedItem("weapon_c4");
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
        if (playerController is null) return;
        CsTeam oldTeam = GetCoachTeam(playerController);
        if (playerController.Team != oldTeam)
        {
            playerController.ChangeTeam(CsTeam.Spectator);
            AddTimer(0.01f, () => playerController.ChangeTeam(oldTeam));
        }
        if (playerController.InGameMoneyServices is not null) playerController.InGameMoneyServices.Account = 0;
    }

    private void KillCoaches()
    {
        if (isPaused || IsTacticalTimeoutActive()) return;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;
        
        string suicidePenalty = ConVar.Find("mp_suicide_penalty") is { } cvPenalty ? (GetConvarStringValue(cvPenalty) ?? "0") : "0";
        string specFreezeTime = ConVar.Find("spec_freeze_time") is { } cvFreeze ? (GetConvarStringValue(cvFreeze) ?? "2") : "2";
        string specFreezeTimeLock = ConVar.Find("spec_freeze_time_lock") is { } cvLock ? (GetConvarStringValue(cvLock) ?? "2") : "2";
        string specFreezeDeathanim = ConVar.Find("spec_freeze_deathanim_time") is { } cvAnim ? (GetConvarStringValue(cvAnim) ?? "0") : "0";

        Server.ExecuteCommand("mp_suicide_penalty 0;spec_freeze_time 0; spec_freeze_time_lock 0; spec_freeze_deathanim_time 0;");

        foreach (var coach in coaches)
        {
            if (coach is null || !IsPlayerValid(coach)) continue;
            if (isPaused || IsTacticalTimeoutActive()) continue;

            // 徹底防止舊版空參考去參照警告
            if (coach.PlayerPawn.Value is { } pawn && pawn.CBodyComponent?.SceneNode is { AbsOrigin: { } origin, AbsRotation: { } rotation })
            {
                Position coachPosition = new(origin, rotation);
                pawn.Teleport(new(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new(0, 0, 0));
                pawn.CommitSuicide(explode: false, force: true);
            }
        }
        Server.ExecuteCommand($"mp_suicide_penalty {suicidePenalty}; spec_freeze_time {specFreezeTime}; spec_freeze_time_lock {specFreezeTimeLock}; spec_freeze_deathanim_time {specFreezeDeathanim};");
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

                    // Parse position and angle
                    Vector vector = new(float.Parse(vectorArray[0]), float.Parse(vectorArray[1]), float.Parse(vectorArray[2]));
                    QAngle qAngle = new(float.Parse(angleArray[0]), float.Parse(angleArray[1]), float.Parse(angleArray[2]));

                    Position position = new(vector, qAngle);

                    positionList.Add(position);
                }
                coachSpawns[team] =  positionList;
            }
            Log($"[GetCoachSpawns] Loaded {coachSpawns.Count} coach spawns");
        }
        catch (Exception ex)
        {
            Log($"[GetCoachSpawns - FATAL] Error getting coach spawns. [ERROR]: {ex.Message}");
        }
    }
}
