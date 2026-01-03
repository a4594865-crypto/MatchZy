using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using Newtonsoft.Json.Linq;
using System.Text.Json;

namespace MatchZy;

public partial class MatchZy
{
    // --- 基礎變數定義 (來自官方第 8 版) ---
    public MatchConfig matchConfig = new();
    public bool isMatchSetup = false;
    public bool matchModeOnly = false;
    public bool resetCvarsOnSeriesEnd = true;
    public string loadedConfigFile = "";

    public Team matchzyTeam1 = new() { teamName = "COUNTER-TERRORISTS" };
    public Team matchzyTeam2 = new() { teamName = "TERRORISTS" };

    public Dictionary<Team, string> teamSides = new();
    public Dictionary<string, Team> reverseTeamSides = new();

    // --- 核心攔截：.r 完畢或刀局中禁止換隊 ---
    [GameEventHandler]
    public HookResult EventPlayerTeamHandler(EventPlayerTeam @event, GameEventInfo info)
    {
        if (matchStarted || isKnifeRequired)
        {
            CCSPlayerController? player = @event.Userid;
            if (IsPlayerValid(player) && !@event.Silent)
            {
                ReplyToUserCommand(player, "刀局或比賽期間禁止自行更換隊伍！");
                return HookResult.Stop;
            }
        }
        return HookResult.Continue;
    }

    // --- 完整的 JSON 解析與 LoadMatchFromJSON (完全展開) ---
    public bool LoadMatchFromJSON(string jsonData)
    {
        try {
            Log($"[LoadMatchFromJSON] Attempting to parse match JSON data...");
            JObject jsonDataObject = JObject.Parse(jsonData);
            
            string validationError = ValidateMatchJsonStructure(jsonDataObject);
            if (validationError != "") {
                Log($"[LoadMatchDataCommand] Validation failed: {validationError}");
                return false;
            }

            if(jsonDataObject["matchid"] != null) liveMatchId = (long)jsonDataObject["matchid"]!;
            
            JToken team1 = jsonDataObject["team1"]!;
            JToken team2 = jsonDataObject["team2"]!;
            JToken maplist = jsonDataObject["maplist"]!;

            if (team1["id"] != null) matchzyTeam1.id = team1["id"]!.ToString();
            if (team2["id"] != null) matchzyTeam2.id = team2["id"]!.ToString();

            matchzyTeam1.teamName = RemoveSpecialCharacters(team1["name"]!.ToString());
            matchzyTeam2.teamName = RemoveSpecialCharacters(team2["name"]!.ToString());
            
            matchzyTeam1.teamPlayers = team1["players"];
            matchzyTeam2.teamPlayers = team2["players"];

            matchConfig = new() {
                MatchId = liveMatchId,
                MapsPool = maplist.ToObject<List<string>>()!,
                MapsLeftInVetoPool = maplist.ToObject<List<string>>()!,
                NumMaps = jsonDataObject["num_maps"]!.Value<int>(),
                MinPlayersToReady = minimumReadyRequired
            };

            GetOptionalMatchValues(jsonDataObject);
            GetCvarValues(jsonDataObject);

            if (matchConfig.SkipVeto) {
                matchConfig.SkipVeto = true;
                isPreVeto = false;
                for (int i = 0; i < matchConfig.NumMaps; i++) {
                    matchConfig.Maplist.Add(matchConfig.MapsPool[i]);
                    if (matchConfig.MapSides.Count < matchConfig.Maplist.Count) matchConfig.MapSides.Add("team1_ct");
                }
                ChangeMap(matchConfig.Maplist[0].ToString(), 0);
            }

            readyAvailable = true;
            ExecuteChangedConvars();
            StartWarmup();
            isMatchSetup = true;
            if(matchConfig.SkipVeto) SetMapSides();
            SetTeamNames();
            UpdatePlayersMap();
            UpdateHostname();
            
            return true;
        } catch (Exception e) { 
            Log($"[LoadMatchFromJSON FATAL] An error occurred while loading match: {e.Message}");
            return false; 
        }
    }

    public void SetMapSides() {
        int mapNumber = matchConfig.CurrentMapNumber;
        teamSides[matchzyTeam1] = "CT"; teamSides[matchzyTeam2] = "TERRORIST";
        reverseTeamSides["CT"] = matchzyTeam1; reverseTeamSides["TERRORIST"] = matchzyTeam2;
        
        if (matchConfig.MapSides.Count > mapNumber) {
            if (matchConfig.MapSides[mapNumber] == "team2_ct" || matchConfig.MapSides[mapNumber] == "team1_t") {
                (teamSides[matchzyTeam1], teamSides[matchzyTeam2]) = (teamSides[matchzyTeam2], teamSides[matchzyTeam1]);
                (reverseTeamSides["CT"], reverseTeamSides["TERRORIST"]) = (reverseTeamSides["TERRORIST"], reverseTeamSides["CT"]);
            }
        }
        SetTeamNames();
    }
    public void SetTeamNames() {
        Server.ExecuteCommand($"mp_teamname_1 {reverseTeamSides["CT"].teamName}");
        Server.ExecuteCommand($"mp_teamname_2 {reverseTeamSides["TERRORIST"].teamName}");
    }

    public void EndSeries(string? winnerName, int restartDelay, int t1score, int t2score) {
        if (winnerName == null) {
            Server.PrintToChatAll($"{chatPrefix} 雙方最終戰平，不分勝負！");
        } else {
            Server.PrintToChatAll($"{chatPrefix} 恭喜 {ChatColors.Green}{winnerName}{ChatColors.Default} 贏得了本場地圖的勝利！");
        }
        if (resetCvarsOnSeriesEnd) ResetChangedConvars();
        isMatchLive = false;
        AddTimer(restartDelay, () => { ResetMatch(false); });
    }

    [GameEventHandler]
    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
    {
        try {
            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;
            Log($"[FULL CONNECT] Player ID: {player!.UserId}, Name: {player.PlayerName} has connected!");

            if (player.UserId.HasValue) {
                int userId = player.UserId.Value;
                playerData[userId] = player;
                connectedPlayers++;
                if (readyAvailable && !matchStarted) playerReadyStatus[userId] = false;
                else playerReadyStatus[userId] = true;
            }
            if (readyAvailable && !matchStarted && GetRealPlayersCount() == 1) {
                ExecUnpracCommands();
                AutoStart();
            }
            return HookResult.Continue;
        } catch (Exception e) { Log($"[EventPlayerConnectFull FATAL] {e.Message}"); return HookResult.Continue; }
    }

    [GameEventHandler]
    public HookResult EventPlayerDisconnectHandler(EventPlayerDisconnect @event, GameEventInfo info)
    {
        try {
            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player) || !player!.UserId.HasValue) return HookResult.Continue;
            int userId = player.UserId.Value;
            if (playerReadyStatus.ContainsKey(userId)) {
                playerReadyStatus.Remove(userId);
                connectedPlayers--;
            }
            playerData.Remove(userId);
            if (matchzyTeam1.coach.Contains(player)) matchzyTeam1.coach.Remove(player);
            else if (matchzyTeam2.coach.Contains(player)) matchzyTeam2.coach.Remove(player);
            noFlashList.Remove(userId);
            lastGrenadesData.Remove(userId);
            return HookResult.Continue;
        } catch (Exception e) { Log($"[EventPlayerDisconnect FATAL] {e.Message}"); return HookResult.Continue; }
    }

    [GameEventHandler]
    public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event, GameEventInfo info) {
        try { HandleMatchEnd(); return HookResult.Continue; }
        catch (Exception e) { Log($"[EventCsWinPanelMatch FATAL] {e.Message}"); return HookResult.Continue; }
    }

    [GameEventHandler]
    public HookResult EventRoundStartHandler(EventRoundStart @event, GameEventInfo info) {
        try { HandlePostRoundStartEvent(@event); return HookResult.Continue; }
        catch (Exception e) { Log($"[EventRoundStart FATAL] {e.Message}"); return HookResult.Continue; }
    }

    public void OnEntitySpawnedHandler(CEntityInstance entity) {
        try {
            if (!isPractice || entity == null || entity.Entity == null) return;
            if (!Constants.ProjectileTypeMap.ContainsKey(entity.Entity.DesignerName)) return;
            Server.NextFrame(() => {
                CBaseCSGrenadeProjectile projectile = new CBaseCSGrenadeProjectile(entity.Handle);
                if (!projectile.IsValid || !projectile.Thrower.IsValid || projectile.Thrower.Value?.Controller.Value == null) return;
                CCSPlayerController player = new(projectile.Thrower.Value.Controller.Value.Handle);
                int client = player.UserId!.Value;
                string nadeType = Constants.ProjectileTypeMap[entity.Entity.DesignerName];
                
                if (smokeColorEnabled.Value && nadeType == "smoke") {
                    CSmokeGrenadeProjectile smoke = new(entity.Handle);
                    smoke.SmokeColor.X = GetPlayerTeammateColor(player).R;
                    smoke.SmokeColor.Y = GetPlayerTeammateColor(player).G;
                    smoke.SmokeColor.Z = GetPlayerTeammateColor(player).B;
                }
                
                if (!lastGrenadesData.ContainsKey(client)) lastGrenadesData[client] = new();
                GrenadeThrownData lastGrenadeThrown = new(
                    new Vector(projectile.AbsOrigin!.X, projectile.AbsOrigin.Y, projectile.AbsOrigin.Z),
                    new QAngle(projectile.AbsRotation!.X, projectile.AbsRotation.Y, projectile.AbsRotation.Z),
                    new Vector(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z),
                    player.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin,
                    player.PlayerPawn.Value.EyeAngles,
                    nadeType, DateTime.Now, projectile.ItemIndex
                );
                lastGrenadesData[client].Add(lastGrenadeThrown);
                lastGrenadeThrownTime[(int)projectile.Index] = DateTime.Now;
            });
        } catch (Exception e) { Log($"[OnEntitySpawnedHandler FATAL] {e.Message}"); }
    }

    [GameEventHandler]
    public HookResult EventSmokegrenadeDetonateHandler(EventSmokegrenadeDetonate @event, GameEventInfo info) {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (IsPlayerValid(player) && lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var t)) {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.smoke", player!.PlayerName, $"{(DateTime.Now - t).TotalSeconds:0.00}"]);
        }
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult EventFlashbangDetonateHandler(EventFlashbangDetonate @event, GameEventInfo info) {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (IsPlayerValid(player) && lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var t)) {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.flash", player!.PlayerName, $"{(DateTime.Now - t).TotalSeconds:0.00}"]);
        }
        return HookResult.Continue;
    }

    public void GetCvarValues(JObject jsonDataObject) {
        if (jsonDataObject["cvars"] == null) return;
        foreach (JProperty cvarData in jsonDataObject["cvars"]!) {
            string cvarName = cvarData.Name;
            string cvarValue = cvarData.Value.ToString();
            var cvar = ConVar.Find(cvarName);
            if (cvar != null) matchConfig.ChangedCvars[cvarName] = cvarValue;
        }
    }

    public void HandleTeamNameChangeCommand(CCSPlayerController? player, string teamName, int teamNum) {
        teamName = RemoveSpecialCharacters(teamName.Trim());
        if (teamNum == 1) matchzyTeam1.teamName = teamName;
        else if (teamNum == 2) matchzyTeam2.teamName = teamName;
        Server.ExecuteCommand($"mp_teamname_{teamNum} {teamName};");
    }

    static string ValidateMatchJsonStructure(JObject jsonData) {
        string[] requiredFields = { "maplist", "team1", "team2", "num_maps" };
        foreach (string field in requiredFields) if (jsonData[field] == null) return $"Missing required field: {field}";
        return "";
    }
}
