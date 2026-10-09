using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
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

    // 1. m_szClan 標籤更新與同步函數
    public void UpdateCoachClanTag(CCSPlayerController coach)
    {
        if (coach is null || !IsPlayerValid(coach)) return;
        Team coachTeam = matchzyTeam1.coach.Contains(coach) ? matchzyTeam1 : matchzyTeam2;
        string targetTag = $"[{coachTeam.teamName} COACH]";

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

        // 2. 每隊限 1 名教練的限制（tac 名單防護）[span_8](start_span)[span_8](end_span)
        if (matchZyCoachTeam.coach.Count >= 1)
        {
            ReplyToUserCommand(player, "This team already has a coach! Only 1 coach allowed per team.");
            return;
        }

        matchZyCoachTeam.coach.Add(player);
        
        UpdateCoachClanTag(player);

        if (player.InGameMoneyServices is not null) player.InGameMoneyServices.Account = 0;
        ReplyToUserCommand(player, $"You are now coaching {matchZyCoachTeam.teamName}! Use .uncoach to stop coaching");
        PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} is now coaching {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default}!");
    }

    // 3. 賽事進行中禁止退出教練保護鎖（使用 OnUncoachCommandSafe 避免重複定義衝突）[span_9](start_span)[span_9](end_span)
    [ConsoleCommand("css_uncoach", "Exit coach mode safely")]
    public void OnUncoachCommandSafe(CCSPlayerController? player, CommandInfo? command)
    {
        if (player is null || !IsPlayerValid(player)) return;

        // 賽事進行中（含倒數、刀局、選邊、正賽）禁止退出[span_10](start_span)[span_10](end_span)
        if (isCountdownActive || isKnifeRound || isSideSelectionPhase || isMatchLive || matchStarted)
        {
            ReplyToUserCommand(player, "Cannot exit coach mode while match, knife round, or side selection is in progress!");
            return;
        }

        bool isCoach = false;
        Team? coachTeam = null;

        if (matchzyTeam1.coach.Contains(player))
        {
            isCoach = true;
            coachTeam = matchzyTeam1;
        }
        else if (matchzyTeam2.coach.Contains(player))
        {
            isCoach = true;
            coachTeam = matchzyTeam2;
        }

        if (!isCoach || coachTeam == null)
        {
            ReplyToUserCommand(player, "You are not coaching any team!");
            return;
        }

        coachTeam.coach.Remove(player);
        player.Clan = "";
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");

        if (player.UserId is int uid)
        {
            playerReadyStatus[uid] = false;
        }

        ReplyToUserCommand(player, "You have stopped coaching.");
        PrintToAllChat($"{ChatColors.Green}{player.PlayerName}{ChatColors.Default} has stopped coaching.");

        player.ChangeTeam(CsTeam.Spectator);

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
            if (coach.PlayerPawn.Value is { } pawn)
            {
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;

                if (coachSpawns.TryGetValue(coach.TeamNum, out var teamSpawns) && teamSpawns.Count > 0)
                {
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
            if (player is null || !IsPlayerValid(player) || coaches.Contains(player)) continue;

            if (!spawnsData.TryGetValue(player.TeamNum, out var teamPositions) || teamPositions.Count == 0) continue;
            
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

    // 4. 官方原版 C4 轉移邏輯[span_11](start_span)[span_11](end_span)
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
        
        UpdateCoachClanTag(playerController);
    }

    // 補回被漏掉的競技隊友顏色對齊方法，供 Teams.cs 呼叫[span_12](start_span)[span_12](end_span)
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

    // 5. 官方原版自殺邏輯 + 自殺經濟隔離防護（不給對手陣營錢）[span_13](start_span)[span_13](end_span)
    private void KillCoaches()
    {
        if (isPaused || IsTacticalTimeoutActive()) return;
        HashSet<CCSPlayerController> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;
        
        string suicidePenalty = ConVar.Find("mp_suicide_penalty") is { } cvPenalty ? (GetConvarStringValue(cvPenalty) ?? "0") : "0";
        string killDefault = ConVar.Find("cash_player_killed_enemy_default") is { } cv2 ? (GetConvarStringValue(cv2) ?? "300") : "300";
        string killFactor = ConVar.Find("cash_player_killed_enemy_factor") is { } cv3 ? (GetConvarStringValue(cv3) ?? "1") : "1";
        string specFreezeTime = ConVar.Find("spec_freeze_time") is { } cvFreeze ? (GetConvarStringValue(cvFreeze) ?? "2") : "2";
        string specFreezeTimeLock = ConVar.Find("spec_freeze_time_lock") is { } cvLock ? (GetConvarStringValue(cvLock) ?? "2") : "2";
        string specFreezeDeathanim = ConVar.Find("spec_freeze_deathanim_time") is { } cvAnim ? (GetConvarStringValue(cvAnim) ?? "0") : "0";

        // 瞬間將自殺罰款與擊殺獎勵設為 0，防止對手拿錢
        Server.ExecuteCommand("mp_suicide_penalty 0; cash_player_killed_enemy_default 0; cash_player_killed_enemy_factor 0; spec_freeze_time 0; spec_freeze_time_lock 0; spec_freeze_deathanim_time 0;");

        foreach (var coach in coaches)
        {
            if (coach is null || !IsPlayerValid(coach)) continue;
            if (isPaused || IsTacticalTimeoutActive()) continue;

            if (coach.PlayerPawn.Value is { } pawn && pawn.CBodyComponent?.SceneNode is { AbsOrigin: { } origin, AbsRotation: { } rotation })
            {
                Position coachPosition = new(origin, rotation);
                pawn.Teleport(new(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new(0, 0, 0));
                pawn.CommitSuicide(explode: false, force: true);
            }
        }

        // 0.2 秒後還原經濟設定，並將教練金錢歸零
        AddTimer(0.2f, () =>
        {
            Server.ExecuteCommand($"mp_suicide_penalty {suicidePenalty}; cash_player_killed_enemy_default {killDefault}; cash_player_killed_enemy_factor {killFactor}; spec_freeze_time {specFreezeTime}; spec_freeze_time_lock {specFreezeTimeLock}; spec_freeze_deathanim_time {specFreezeDeathanim};");

            foreach (var coach in coaches)
            {
                if (coach is not null && IsPlayerValid(coach) && coach.InGameMoneyServices is not null)
                {
                    coach.InGameMoneyServices.Account = 0;
                }
            }
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
