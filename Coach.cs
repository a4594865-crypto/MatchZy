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
        // 【.NET 10 升級】：集合表達式與展開運算子
        HashSet<CCSPlayerController> coaches = [.. matchzyTeam1.coach];
        coaches.UnionWith(matchzyTeam2.coach);

        return coaches;
    }

    public void HandleCoachCommand(CCSPlayerController? player, string side)
    {
        // 【.NET 10 升級】：現代化 is null 檢查
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

        // 【.NET 10 升級】：邏輯 OR 模式匹配
        if (side is not ("t" or "ct"))
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

        // if (matchZyCoachTeam.coach != null) {
        //     ReplyToUserCommand(player, "Coach slot for this team has been already taken!");
        //     return;
        // }

        matchZyCoachTeam.coach.Add(player);
        player.Clan = $"[{matchZyCoachTeam.teamName} COACH]";
        
        // 【.NET 10 升級】：模式匹配
        if (player.InGameMoneyServices is not null) player.InGameMoneyServices.Account = 0;
        
        ReplyToUserCommand(player, $"You are now coaching {matchZyCoachTeam.teamName}! Use .uncoach to stop coaching");
        PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} is now coaching {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default}!");
    }

    public void HandleCoaches()
    {
        coachKillTimer?.Kill();
        coachKillTimer = null;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count is 0) return;
        
        // 【.NET 10 升級】：拔除 LINQ Any，0 GC 記憶體分配
        bool hasEmptySpawns = false;
        foreach (var list in spawnsData.Values)
        {
            if (list.Count is 0)
            {
                hasEmptySpawns = true;
                break;
            }
        }
        if (hasEmptySpawns) GetSpawns();

        if (coachSpawns.Count is 0 || 
            coachSpawns[(byte)CsTeam.CounterTerrorist].Count is 0 || 
            coachSpawns[(byte)CsTeam.Terrorist].Count is 0)
        {
            Log($"[HandleCoaches] No coach spawns found, player positions will not be swapped!");
            return;
        }

        int freezeTime = ConVar.Find("mp_freezetime")!.GetPrimitiveValue<int>();
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

            // 【.NET 10 升級】：安全拆箱模式匹配，消滅潛在的空參考警告
            if (coach.ActionTrackingServices?.MatchStats is { } stats)
            {
                stats.Kills = 0;
                stats.Deaths = 0;
                stats.Assists = 0;
                stats.Damage = 0;
            }

            SetPlayerInvisible(player: coach, setWeaponsInvisible: false);
            
            // Stopping the coaches from moving, so that they don't block the players.
            // 【.NET 10 升級】：安全拆箱模式匹配，消滅潛在的空參考警告
            if (coach.PlayerPawn.Value is { } pawn)
            {
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                
                List<Position> coachTeamSpawns = coachSpawns[coach.TeamNum];
                Position coachPosition = new(pawn.CBodyComponent!.SceneNode!.AbsOrigin, pawn.CBodyComponent!.SceneNode!.AbsRotation);

                // Picking a random position for the coach (from coachSpawns) to teleport them.
                Position newPosition = coachTeamSpawns[random.Next(0, coachTeamSpawns.Count)];

                // Elevating coach before dropping the C4 to prevent it going inside the ground.
                AddTimer(0.05f, () =>
                {
                    HandleCoachWeapons(coach);
                    // 【.NET 10 升級】：再次確保 pawn 不為 null
                    if (coach.PlayerPawn.Value is { } validPawn)
                    {
                        validPawn.Teleport(newPosition.PlayerPosition, newPosition.PlayerAngle, new Vector(0, 0, 0));
                    }
                });
            }
        }

        List<CCSPlayerController> players = Utilities.GetPlayers();
        // 【.NET 10 升級】：集合表達式
        HashSet<Position> occupiedSpawns = [];
        HashSet<CCSPlayerController> incorrectSpawnedPlayers = [];

        // We will loop on the players 2 times, first loop is to get all the players who are on a non-competitive spawn, and to get all the non-occupied competitive spawn.
        // In the next loop, we will teleport the non-competitive spawned players to an available competitive spawn.

        foreach (CCSPlayerController player in players)
        {
            if (player is null || !IsPlayerValid(player) || coaches.Contains(player)) continue;

            List<Position> teamPositions = spawnsData[player.TeamNum];
            Position playerPosition = new(player.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin, player.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsRotation);
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

            // The player is not on a competitive spawn, we will put them on one in the next loop.
            incorrectSpawnedPlayers.Add(player);
        }

        foreach (CCSPlayerController player in incorrectSpawnedPlayers)
        {
            if (player is null || !IsPlayerValid(player) || coaches.Contains(player)) continue;

            List<Position> teamPositions = spawnsData[player.TeamNum];
            Position playerPosition = new(player.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin, player.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsRotation);
            foreach (Position position in teamPositions)
            {
                if (occupiedSpawns.Contains(position)) continue;
                occupiedSpawns.Add(position);
                AddTimer(0.1f, () =>
                {
                    if (player.PlayerPawn.Value is { } pawn)
                    {
                        pawn.Teleport(position.PlayerPosition, position.PlayerAngle, new Vector(0, 0, 0));
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
        if (coach.TeamNum != (int)CsTeam.Terrorist) return; // can't have bomb

        // find bomb and new target
        // 【.NET 10 升級】：拔除 LINQ Where 與 FirstOrDefault，0 GC 記憶體分配
        CBasePlayerWeapon? bombToTransfer = null;
        if (coach.PlayerPawn.Value?.WeaponServices?.MyWeapons is { } weapons) {
            foreach (var w in weapons) {
                if (w is { IsValid: true, Value.DesignerName: "weapon_c4" }) {
                    bombToTransfer = w.Value;
                    break;
                }
            }
        }
        
        if (bombToTransfer is null) return; // should never trigger

        // 【.NET 10 升級】：拔除 LINQ FirstOrDefault，0 GC 記憶體分配
        CCSPlayerController? target = null;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not null && IsPlayerValid(p) && !reverseTeamSides["TERRORIST"].coach.Contains(p) && p.TeamNum == (int)CsTeam.Terrorist && p.PawnIsAlive)
            {
                target = p;
                break;
            }
        }
        
        // 【完美消滅 L208 警告】：加上 target is not { IsValid: true }，向編譯器保證 target 絕對不會是 null
        if (target is not { IsValid: true } || !IsPlayerValid(target)) return; // should never trigger

        // transfer bomb
        Log($"[EventPlayerGivenC4 INFO] Transferred bomb from {coach.PlayerName} (Coach) to {target.PlayerName}.");
        bombToTransfer.Remove();
        target.GiveNamedItem("weapon_c4");
    }

    public CsTeam GetCoachTeam(CCSPlayerController coach)
    {
        if (matchzyTeam1.coach.Contains(coach))
        {
            if (teamSides[matchzyTeam1] == "CT")
            {
                return CsTeam.CounterTerrorist;
            }
            else if (teamSides[matchzyTeam1] == "TERRORIST")
            {
                return CsTeam.Terrorist;
            }
        }
        if (matchzyTeam2.coach.Contains(coach))
        {
            if (teamSides[matchzyTeam2] == "CT")
            {
                return CsTeam.CounterTerrorist;
            }
            else if (teamSides[matchzyTeam2] == "TERRORIST")
            {
                return CsTeam.Terrorist;
            }
        }
        return CsTeam.Spectator;
    }

    private void HandleCoachTeam(CCSPlayerController playerController)
    {
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
        if (IsWingmanMode() || coaches.Count is 0) return;
        string suicidePenalty = GetConvarStringValue(ConVar.Find("mp_suicide_penalty"));
        string specFreezeTime = GetConvarStringValue(ConVar.Find("spec_freeze_time"));
        string specFreezeTimeLock = GetConvarStringValue(ConVar.Find("spec_freeze_time_lock"));
        string specFreezeDeathanim = GetConvarStringValue(ConVar.Find("spec_freeze_deathanim_time"));
        Server.ExecuteCommand("mp_suicide_penalty 0;spec_freeze_time 0; spec_freeze_time_lock 0; spec_freeze_deathanim_time 0;");

        foreach (var coach in coaches)
        {
            if (coach is null || !IsPlayerValid(coach)) continue;
            if (isPaused || IsTacticalTimeoutActive()) continue;

            // 【.NET 10 升級】：安全模式匹配，防止 Pawn 瞬間消失引發空參考例外
            if (coach.PlayerPawn.Value is { } pawn)
            {
                Position coachPosition = new(pawn.CBodyComponent!.SceneNode!.AbsOrigin, pawn.CBodyComponent!.SceneNode!.AbsRotation);
                pawn.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
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
            string spawnsConfig = File.ReadAllText(spawnsConfigPath);

            var jsonDictionary = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(spawnsConfig);
            if (jsonDictionary is null) return;
            foreach (var entry in jsonDictionary)
            {
                byte team = byte.Parse(entry.Key);
                // 【.NET 10 升級】：集合表達式
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
