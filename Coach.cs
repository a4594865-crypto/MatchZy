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
        // .NET 10 集合表達式，簡化初始化
        HashSet<CCSPlayerController> coaches = [.. matchzyTeam1.coach];
        coaches.UnionWith(matchzyTeam2.coach);

        return coaches;
    }

    public void HandleCoachCommand(CCSPlayerController? player, string side)
    {
        if (!IsPlayerValid(player) || player is null) return;
        
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
        
        // 屬性安全防護
        if (player.InGameMoneyServices is not null) 
            player.InGameMoneyServices.Account = 0;
            
        ReplyToUserCommand(player, $"You are now coaching {matchZyCoachTeam.teamName}! Use .uncoach to stop coaching");
        PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} is now coaching {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default}!");
    }

    public void HandleCoaches()
    {
        coachKillTimer?.Kill();
        coachKillTimer = null;
        
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;
        
        // 改為純迴圈零垃圾檢查
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

        // 嚴格字典取值防護 (TryGetValue) 防止 KeyNotFoundException
        if (coachSpawns.Count == 0 || 
            !coachSpawns.TryGetValue((byte)CsTeam.CounterTerrorist, out var ctSpawns) || ctSpawns.Count == 0 || 
            !coachSpawns.TryGetValue((byte)CsTeam.Terrorist, out var tSpawns) || tSpawns.Count == 0)
        {
            Log($"[HandleCoaches] No coach spawns found, player positions will not be swapped!");
            return;
        }

        //  安全讀取 ConVar
        var cvarFreezeTime = ConVar.Find("mp_freezetime");
        int freezeTime = cvarFreezeTime is not null ? cvarFreezeTime.GetPrimitiveValue<int>() : 2;
        freezeTime = freezeTime > 2 ? freezeTime : 2;
        
        coachKillTimer ??= AddTimer(freezeTime - 1f, KillCoaches);

        Random random = new();
        foreach (CCSPlayerController coach in coaches)
        {
            if (!IsPlayerValid(coach)) continue;
            
            Team coachTeam = matchzyTeam1.coach.Contains(coach) ? matchzyTeam1 : matchzyTeam2;
            
            if (coach.InGameMoneyServices is not null) 
                coach.InGameMoneyServices.Account = 0;

            AddTimer(0.5f, () => HandleCoachTeam(coach));

            // 屬性模式防護空參考
            if (coach.ActionTrackingServices is not null)
            {
                coach.ActionTrackingServices.MatchStats.Kills = 0;
                coach.ActionTrackingServices.MatchStats.Deaths = 0;
                coach.ActionTrackingServices.MatchStats.Assists = 0;
                coach.ActionTrackingServices.MatchStats.Damage = 0;
            }

            SetPlayerInvisible(player: coach, setWeaponsInvisible: false);
            
            // 模式匹配防空指標
            if (coach.PlayerPawn.Value is { } pawn)
            {
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;

                if (coachSpawns.TryGetValue(coach.TeamNum, out var teamSpawns) && teamSpawns.Count > 0)
                {
                    // Picking a random position
                    Position newPosition = teamSpawns[random.Next(0, teamSpawns.Count)];

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
            if (!IsPlayerValid(player) || coaches.Contains(player)) continue;

            if (!spawnsData.TryGetValue(player.TeamNum, out var teamPositions) || teamPositions.Count == 0) continue;
            
            // 深度解構模式匹配，徹底消滅多重驚嘆號引發的 CS8602 潛在空參考
            if (player.PlayerPawn.Value?.CBodyComponent?.SceneNode is not { AbsOrigin: not null, AbsRotation: not null } sceneNode) 
                continue;

            Position playerPosition = new(sceneNode.AbsOrigin, sceneNode.AbsRotation);
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
            if (!IsPlayerValid(player) || coaches.Contains(player)) continue;
            
            if (!spawnsData.TryGetValue(player.TeamNum, out var teamPositions)) continue;

            foreach (Position position in teamPositions)
            {
                if (occupiedSpawns.Contains(position)) continue;
                occupiedSpawns.Add(position);
                
                AddTimer(0.1f, () =>
                {
                    player.PlayerPawn.Value?.Teleport(position.PlayerPosition, position.PlayerAngle, new(0, 0, 0));
                });
                break;
            }
        }
    }

    private void HandleCoachWeapons(CCSPlayerController coach)
    {
        if (!IsPlayerValid(coach)) return;
        coach.RemoveWeapons();
    }

    /// <summary>
    /// Transfers bomb from coach to first available non-coach terrorist.
    /// </summary> 
    public void TransferCoachBomb(CCSPlayerController coach) 
    {
        // 嚴格檢查 TeamNum，將 `(int)` 轉為統一的 `(byte)` 對應底層類別
        if (coach.TeamNum != (byte)CsTeam.Terrorist) return; 

        // 屬性模式解構，確保擁有武器清單
        if (coach.PlayerPawn.Value?.WeaponServices?.MyWeapons is not { } weapons) return;

        CHandle<CBasePlayerWeapon> bombHandle = default;
        bool foundBomb = false;
        
        // 改用迴圈 0 記憶體垃圾！
        foreach (var weapon in weapons)
        {
            if (weapon.Value is { IsValid: true, DesignerName: "weapon_c4" })
            {
                bombHandle = weapon;
                foundBomb = true;
                break;
            }
        }

        if (!foundBomb || bombHandle.Value is null) return; 

        CCSPlayerController? target = null;
        
        // 改用極速迴圈尋找目標
        foreach (var p in Utilities.GetPlayers())
        {
            if (IsPlayerValid(p) && 
                !reverseTeamSides["TERRORIST"].coach.Contains(p) && 
                p.TeamNum == (byte)CsTeam.Terrorist && 
                p.PawnIsAlive)
            {
                target = p;
                break;
            }
        }

        // 完美阻斷空值 (Null) 引發的崩潰 (Crash)
        if (target is null) return; 

        Log($"[EventPlayerGivenC4 INFO] Transferred bomb from {coach.PlayerName} (Coach) to {target.PlayerName}.");
        bombHandle.Value.Remove();
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
        CsTeam oldTeam = GetCoachTeam(playerController);
        if (playerController.Team != oldTeam)
        {
            playerController.ChangeTeam(CsTeam.Spectator);
            AddTimer(0.01f, () => playerController.ChangeTeam(oldTeam));
        }
        if (playerController.InGameMoneyServices is not null) 
            playerController.InGameMoneyServices.Account = 0;
    }

    private void KillCoaches()
    {
        if (isPaused || IsTacticalTimeoutActive()) return;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;
        
        // 嚴格防護 ConVar 空值與 API 字串回傳值
        var cvarPenalty = ConVar.Find("mp_suicide_penalty");
        var cvarFreezeTime = ConVar.Find("spec_freeze_time");
        var cvarFreezeLock = ConVar.Find("spec_freeze_time_lock");
        var cvarFreezeAnim = ConVar.Find("spec_freeze_deathanim_time");

        // 加入 ?? 確保無論如何都不會把 Null 塞給不可為空的 string
        string suicidePenalty = cvarPenalty is not null ? (GetConvarStringValue(cvarPenalty) ?? "0") : "0";
        string specFreezeTime = cvarFreezeTime is not null ? (GetConvarStringValue(cvarFreezeTime) ?? "2") : "2";
        string specFreezeTimeLock = cvarFreezeLock is not null ? (GetConvarStringValue(cvarFreezeLock) ?? "2") : "2";
        string specFreezeDeathanim = cvarFreezeAnim is not null ? (GetConvarStringValue(cvarFreezeAnim) ?? "0") : "0";

        Server.ExecuteCommand("mp_suicide_penalty 0;spec_freeze_time 0; spec_freeze_time_lock 0; spec_freeze_deathanim_time 0;");

        foreach (var coach in coaches)
        {
            if (!IsPlayerValid(coach) || isPaused || IsTacticalTimeoutActive()) continue;

            // 徹底解決深層實體解構時的 NullReference 崩潰警告
            // 利用 is { } pawn 先把實體抓出來，確保接下來呼叫 Teleport() 跟 CommitSuicide() 時絕對安全
            if (coach.PlayerPawn.Value is { } pawn && pawn.CBodyComponent?.SceneNode is { AbsOrigin: not null, AbsRotation: not null } sceneNode)
            {
                Position coachPosition = new(sceneNode.AbsOrigin, sceneNode.AbsRotation);
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

                    Vector vector = new(float.Parse(vectorArray[0]), float.Parse(vectorArray[1]), float.Parse(vectorArray[2]));
                    QAngle qAngle = new(float.Parse(angleArray[0]), float.Parse(angleArray[1]), float.Parse(angleArray[2]));

                    positionList.Add(new(vector, qAngle));
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
