using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using System.Text.Json;

namespace MatchZy
{
    public class Position
    {
        public Vector PlayerPosition { get; private set; }
        public QAngle PlayerAngle { get; private set; }

        // Copy constructor
        public Position(Position other)
        {
            PlayerPosition = other.PlayerPosition;
            PlayerAngle = other.PlayerAngle;
        }

        public Position(Vector playerPosition, QAngle playerAngle)
        {
            // Create deep copies of the Vector and QAngle objects
            PlayerPosition = new(playerPosition.X, playerPosition.Y, playerPosition.Z);
            PlayerAngle = new(playerAngle.X, playerAngle.Y, playerAngle.Z);
        }

        public void Teleport(CCSPlayerController player)
        {
            player.PlayerPawn.Value?.Teleport(PlayerPosition, PlayerAngle, new(0, 0, 0));
        }

        public override bool Equals(object? obj)
        {
            if (obj is not Position otherPosition)
            {
                return false;
            }

            return PlayerPosition.X == otherPosition.PlayerPosition.X &&
                PlayerPosition.Y == otherPosition.PlayerPosition.Y &&
                PlayerAngle.X == otherPosition.PlayerAngle.X &&
                PlayerAngle.Y == otherPosition.PlayerAngle.Y &&
                PlayerAngle.Z == otherPosition.PlayerAngle.Z;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + PlayerPosition.X.GetHashCode();
                hash = hash * 23 + PlayerPosition.Y.GetHashCode();
                hash = hash * 23 + PlayerPosition.Z.GetHashCode();
                hash = hash * 23 + PlayerAngle.X.GetHashCode();
                hash = hash * 23 + PlayerAngle.Y.GetHashCode();
                hash = hash * 23 + PlayerAngle.Z.GetHashCode();
                return hash;
            }
        }
    }

    public static class StringSimilarity
    {
        // Dice coefficient function
        public static double DiceCoefficient(string s1, string s2)
        {
            var bigrams1 = GetBigrams(s1);
            var bigrams2 = GetBigrams(s2);

            int intersection = bigrams1.Intersect(bigrams2).Count();
            return (2.0 * intersection) / (bigrams1.Count + bigrams2.Count);
        }

        // Get bigrams function
        private static List<string> GetBigrams(string input)
        {
            List<string> bigrams = [];
            for (int i = 0; i < input.Length - 1; i++)
            {
                bigrams.Add(input.Substring(i, 2));
            }
            return bigrams;
        }

        /// <summary>
        /// Finds the name from a list of names that is nearest to the input name using the Dice coefficient.
        /// </summary>
        /// <param name="inputName">The input name to match.</param>
        /// <param name="names">The list of names to search from.</param>
        /// <returns>The nearest matching name from the list.</returns>
        public static string FindNearestName(string inputName, List<string> names)
        {
            if (inputName.Length == 1)
            {
                // If input name is a single character, find the name that starts with the same character
                var matchingName = names.FirstOrDefault(name => name.StartsWith(inputName, StringComparison.OrdinalIgnoreCase));
                if (matchingName is not null)
                {
                    return matchingName;
                }
            }
            // Otherwise, use the Dice coefficient to find the nearest name
            string nearestName = names.OrderByDescending(name => DiceCoefficient(inputName, name)).FirstOrDefault() ?? inputName;
            return nearestName;
        }
    }

    public partial class MatchZy
    {
        int maxLastGrenadesSavedLimit = 512;
        Dictionary<int, List<GrenadeThrownData>> lastGrenadesData = [];
        Dictionary<int, Dictionary<string, GrenadeThrownData>> nadeSpecificLastGrenadeData = [];
        Dictionary<int, DateTime> lastGrenadeThrownTime = [];
        Dictionary<int, PlayerPracticeTimer> playerTimers = [];
        Dictionary<int, PlayerLocationData> savedPlayerLocationData = [];

        public Dictionary<byte, List<Position>> spawnsData = GetEmptySpawnsData();
        public Dictionary<byte, List<Position>> coachSpawns = GetEmptySpawnsData();

        public const string practiceCfgPath = "MatchZy/prac.cfg";
        public const string dryrunCfgPath = "MatchZy/dryrun.cfg";

        // This map stores the bots which are being used in prac (probably spawned using .bot). Key is the userid of the bot.
        public Dictionary<int, Dictionary<string, object>> pracUsedBots = [];

        private CounterStrikeSharp.API.Modules.Timers.Timer? collisionGroupTimer;

        public bool isSpawningBot;
        public bool isDryRun = false;
        public List<int> noFlashList = [];

        public static Dictionary<byte, List<Position>> GetEmptySpawnsData()
        {
            return new()
            {
                { (byte)CsTeam.CounterTerrorist, [] },
                { (byte)CsTeam.Terrorist, [] }
            };
        }

        public void StartPracticeMode()
        {
            if (matchStarted) return;
            isPractice = true;
            isDryRun = false;
            isWarmup = false;
            readyAvailable = false;

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", practiceCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", practiceCfgPath)))
            {
                Log($"[StartWarmup] Starting Practice Mode! Executing Practice CFG from {practiceCfgPath}");
                Server.ExecuteCommand($"exec {practiceCfgPath}");
            }
            else
            {
                Log($"[StartWarmup] Starting Practice Mode! Practice CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("""sv_cheats "true"; mp_force_pick_time "0"; bot_quota "0"; sv_showimpacts "1"; mp_limitteams "0"; sv_deadtalk "true"; sv_full_alltalk "true"; sv_ignoregrenaderadio "false"; mp_forcecamera "0"; sv_grenade_trajectory_prac_pipreview "true"; sv_grenade_trajectory_prac_trailtime "3"; sv_infinite_ammo "1"; weapon_auto_cleanup_time "15"; weapon_max_before_cleanup "30"; mp_buy_anywhere "1"; mp_maxmoney "9999999"; mp_startmoney "9999999";""");
                Server.ExecuteCommand("""mp_weapons_allow_typecount "-1"; mp_death_drop_breachcharge "false"; mp_death_drop_defuser "false"; mp_death_drop_taser "false"; mp_drop_knife_enable "true"; mp_death_drop_grenade "0"; ammo_grenade_limit_total "5"; mp_defuser_allocation "2"; mp_free_armor "2"; mp_ct_default_grenades "weapon_incgrenade weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_ct_default_primary "weapon_m4a1";""");
                Server.ExecuteCommand("""mp_t_default_grenades "weapon_molotov weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_t_default_primary "weapon_ak47"; mp_warmup_online_enabled "true"; mp_warmup_pausetimer "1"; mp_warmup_start; bot_quota_mode fill; mp_solid_teammates 2; mp_autoteambalance false; mp_teammates_are_enemies false; buddha 1; buddha_ignore_bots 1; buddha_reset_hp 100;""");
            }
            GetSpawns();
            PrintToAllChat($"Practice mode loaded!");
            Server.PrintToChatAll($" {ChatColors.Green}Spawns: {ChatColors.Default}.spawn, .ctspawn, .tspawn, .bestspawn, .worstspawn");
            Server.PrintToChatAll($" {ChatColors.Green}Bots: {ChatColors.Default}.bot, .nobots, .crouchbot, .boost, .crouchboost");
            Server.PrintToChatAll($" {ChatColors.Green}Nades: {ChatColors.Default}.loadnade, .savenade, .importnade, .listnades");
            Server.PrintToChatAll($" {ChatColors.Green}Nade Throw: {ChatColors.Default}.rethrow, .throwindex <index>, .lastindex, .delay <number>");
            Server.PrintToChatAll($" {ChatColors.Green}Utility & Toggles: {ChatColors.Default}.clear, .fastforward, .last, .back, .solid, .impacts, .traj");
            // On new line to prevent text cutting off
            Server.PrintToChatAll($" {ChatColors.Green}Utility & Toggles: {ChatColors.Default}.savepos, .loadpos");
            Server.PrintToChatAll($" {ChatColors.Green}Sides & Others: {ChatColors.Default}.ct, .t, .spec, .fas, .god, .dryrun, .break, .exitprac");
        }

        public void GetSpawns()
        {
            // Resetting spawn data to avoid any glitches
            spawnsData = GetEmptySpawnsData();

            int minPriority = 1;

            var spawnsct = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist");
            foreach (var spawn in spawnsct)
            {
                if (spawn is { IsValid: true, Enabled: true } && spawn.Priority < minPriority)
                {
                    minPriority = spawn.Priority;
                }
            }

            foreach (var spawn in spawnsct)
            {
                if (spawn is { IsValid: true, Enabled: true } && spawn.Priority == minPriority)
                {
                    if (spawn.CBodyComponent?.SceneNode is { AbsOrigin: not null, AbsRotation: not null } sceneNode)
                    {
                        spawnsData[(byte)CsTeam.CounterTerrorist].Add(new Position(sceneNode.AbsOrigin, sceneNode.AbsRotation));
                    }
                }
            }

            var spawnst = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist");
            foreach (var spawn in spawnst)
            {
                if (spawn is { IsValid: true, Enabled: true } && spawn.Priority == minPriority)
                {
                    if (spawn.CBodyComponent?.SceneNode is { AbsOrigin: not null, AbsRotation: not null } sceneNode)
                    {
                        spawnsData[(byte)CsTeam.Terrorist].Add(new Position(sceneNode.AbsOrigin, sceneNode.AbsRotation));
                    }
                }
            }

            GetCoachSpawns();
        }

        private void HandleSpawnCommand(CCSPlayerController? player, string commandArg, byte teamNum, string command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null) return;
            if (teamNum is not 2 and not 3) return;
            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int spawnNumber) && spawnNumber >= 1)
                {
                    // Adjusting the spawnNumber according to the array index.
                    spawnNumber -= 1;
                    if (spawnsData.TryGetValue(teamNum, out var spawnList) && spawnList.Count <= spawnNumber) return;
                    if (spawnsData.TryGetValue(teamNum, out var list) && list.Count > spawnNumber && player.PlayerPawn.Value is { } pawn)
                    {
                        pawn.Teleport(list[spawnNumber].PlayerPosition, list[spawnNumber].PlayerAngle, new(0, 0, 0));
                        ReplyToUserCommand(player, Localizer["matchzy.pm.movedtospawn", $"{spawnNumber + 1}/{list.Count}"]);
                    }
                }
                else
                {
                    ReplyToUserCommand(player, Localizer["matchzy.pm.negativenumber"]);
                    return;
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!{command} <number>"]);
            }
        }

        private static string GetNadeType(string? nadeName) => nadeName switch
        {
            "weapon_flashbang" => "Flash",
            "weapon_smokegrenade" => "Smoke",
            "weapon_hegrenade" => "HE",
            "weapon_decoy" => "Decoy",
            "weapon_molotov" or "weapon_incgrenade" => "Molly",
            _ => ""
        };

        private void HandleSaveNadeCommand(CCSPlayerController? player, string saveNadeName)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null) return;

            if (!string.IsNullOrWhiteSpace(saveNadeName))
            {
                // Split string into 2 parts
                string[] lineupUserString = saveNadeName.Split(' ');
                string lineupName = lineupUserString[0];
                string lineupDesc = string.Join(" ", lineupUserString, 1, lineupUserString.Length - 1);

                // Get player info: steamid, pos, ang
                string playerSteamID = !isSaveNadesAsGlobalEnabled ? player.SteamID.ToString() : "default";

                if (player.PlayerPawn.Value is not { } playerPawn || player.Pawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin is not { } playerPos)
                    return;

                QAngle playerAngle = playerPawn.EyeAngles;
                string currentMapName = Server.MapName;
                string activeWeaponName = playerPawn.WeaponServices?.ActiveWeapon.Value?.DesignerName ?? "";
                string nadeType = GetNadeType(activeWeaponName);

                // Define the file path
                string savednadesfileName = "MatchZy/savednades.json";
                string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                // Check if the file exists, if not, create it with an empty JSON object
                if (!File.Exists(savednadesPath))
                {
                    File.WriteAllText(savednadesPath, "{}");
                }

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                        ?? [];

                    // Check if the lineup name already exists for the given SteamID
                    if (savedNadesDict.TryGetValue(playerSteamID, out var playerDict) && playerDict.TryGetValue(lineupName, out var currentLineup))
                    {
                        // Check if the lineup already exists on the same map
                        if (currentLineup.TryGetValue("Map", out var map) && map == currentMapName)
                        {
                            ReplyToUserCommand(player, Localizer["matchzy.pm.lineupissaved"]);
                            return;
                        }
                    }

                    // Update or add the new lineup information
                    if (!savedNadesDict.ContainsKey(playerSteamID))
                    {
                        savedNadesDict[playerSteamID] = [];
                    }

                    savedNadesDict[playerSteamID][lineupName] = new()
                    {
                        { "LineupPos", $"{playerPos.X} {playerPos.Y} {playerPos.Z + 4}" },
                        { "LineupAng", $"{playerAngle.X} {playerAngle.Y} {playerAngle.Z}" },
                        { "Desc", lineupDesc },
                        { "Map", currentMapName },
                        { "Type", nadeType }
                    };

                    // Serialize the updated dictionary back to JSON
                    string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                    // Write the updated JSON content back to the file
                    File.WriteAllText(savednadesPath, updatedJson);

                    PrintToPlayerChat(player, Localizer["matchzy.pm.lineupsavedsucces", lineupName]);
                    PrintToAllChat(Localizer["matchzy.pm.playersavedlineup", player.PlayerName, $"{lineupName} {playerPos} {playerAngle}"]);
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $".savenade <name>"]);
            }
        }

        private void HandleDeleteNadeCommand(CCSPlayerController? player, string saveNadeName)
        {
            if (!isPractice || player is null) return;

            if (!string.IsNullOrWhiteSpace(saveNadeName))
            {
                // Grab player steamid
                string playerSteamID = !isSaveNadesAsGlobalEnabled ? player.SteamID.ToString() : "default";

                // Define the file path
                string savednadesfileName = "MatchZy/savednades.json";
                string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                        ?? [];

                    // Check if the lineup exists for the given SteamID and name
                    if (savedNadesDict.TryGetValue(playerSteamID, out var playerDict) && playerDict.TryGetValue(saveNadeName, out var lineupInfo))
                    {
                        // Check if the lineup is for the current maps
                        if (lineupInfo.TryGetValue("Map", out var map) && map == Server.MapName)
                        {
                            // Remove the specified lineup
                            savedNadesDict[playerSteamID].Remove(saveNadeName);

                            // Serialize the updated dictionary back to JSON
                            string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                            // Write the updated JSON content back to the file
                            File.WriteAllText(savednadesPath, updatedJson);

                            ReplyToUserCommand(player, Localizer["matchzy.pm.lineupdeletesuccess", saveNadeName]);
                        }
                        else
                        {
                            ReplyToUserCommand(player, Localizer["matchzy.pm.nadenotfoundonmap", saveNadeName]);
                        }
                    }
                    else
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupnotfound", saveNadeName]);
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $".delnade <name>"]);
            }
        }

        private void HandleImportNadeCommand(CCSPlayerController? player, string saveNadeCode)
        {
            if (!isPractice || player is null) return;

            if (!string.IsNullOrWhiteSpace(saveNadeCode))
            {
                try
                {
                    // Split the code into parts
                    string[] parts = saveNadeCode.Split(' ');

                    // Check if there are enough parts
                    if (parts.Length == 7)
                    {
                        // Extract name, pos, and ang from the parts
                        string lineupName = parts[0].Trim();
                        string[] posAng = parts.Skip(1).Select(p => p.Replace(",", "")).ToArray(); // Replace ',' with '' for proper parsing

                        // Get player info: steamid
                        string playerSteamID = player.SteamID.ToString();
                        string currentMapName = Server.MapName;

                        // Define the file path
                        string savednadesfileName = "MatchZy/savednades.json";
                        string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                        // Read existing JSON content
                        string existingJson = File.ReadAllText(savednadesPath);

                        // Deserialize the existing JSON content
                        var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                            ?? [];

                        // Check if the lineup name already exists for the given SteamID on the same map
                        if (savedNadesDict.TryGetValue(playerSteamID, out var playerDict) && playerDict.TryGetValue(lineupName, out var existingLineup))
                        {
                            if (existingLineup.TryGetValue("Map", out var map) && map == currentMapName)
                            {
                                ReplyToUserCommand(player, Localizer["matchzy.pm.lineupalreadyexists", lineupName]);
                                return;
                            }
                        }

                        // Update or add the new lineup information
                        if (!savedNadesDict.ContainsKey(playerSteamID))
                        {
                            savedNadesDict[playerSteamID] = [];
                        }

                        savedNadesDict[playerSteamID][lineupName] = new()
                        {
                            { "LineupPos", $"{posAng[0]} {posAng[1]} {posAng[2]}" },
                            { "LineupAng", $"{posAng[3]} {posAng[4]} {posAng[5]}" },
                            { "Desc", "" },
                            { "Map", currentMapName }
                        };

                        // Serialize the updated dictionary back to JSON
                        string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                        // Write the updated JSON content back to the file
                        File.WriteAllText(savednadesPath, updatedJson);

                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupimportedsuccess"]);
                    }
                    else
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupinvalidcode"]);
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $".importnade <code>"]);
            }
        }

        private void HandleListNadesCommand(CCSPlayerController? player, string nadeFilter)
        {
            if (!isPractice || player is null) return;

            // Define the file path
            string savednadesfileName = "MatchZy/savednades.json";
            string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

            try
            {
                // Read existing JSON content
                string existingJson = File.ReadAllText(savednadesPath);

                // Deserialize the existing JSON content
                var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                    ?? [];

                ReplyToUserCommand(player, $"\x0D-----All Saved Lineups for \x06{Server.MapName}\x0D-----");

                // List lineups for the specified player
                ListLineups(player, "default", Server.MapName, savedNadesDict, nadeFilter);

                // List lineups for the current player
                ListLineups(player, player.SteamID.ToString(), Server.MapName, savedNadesDict, nadeFilter);
            }
            catch (JsonException ex)
            {
                Log($"Error handling JSON: {ex.Message}");
                ReplyToUserCommand(player, $"Error handling JSON. Please check the server logs.");
            }
        }

        private void ListLineups(CCSPlayerController player, string steamID, string mapName, Dictionary<string, Dictionary<string, Dictionary<string, string>>> savedNadesDict, string nadeFilter)
        {
            if (savedNadesDict.TryGetValue(steamID, out var userDict))
            {
                foreach (var (key, value) in userDict)
                {
                    // Check if a filter is provided, and if so, apply the filter
                    if ((string.IsNullOrWhiteSpace(nadeFilter) || key.Contains(nadeFilter, StringComparison.OrdinalIgnoreCase))
                        && value.TryGetValue("Map", out var map) && map == mapName)
                    {
                        string type = value.TryGetValue("Type", out var t) ? t : "";
                        // Format and reply with the lineup name
                        ReplyToUserCommand(player, $"\x06[{type}] \x0D.loadnade \x06{key}");
                    }
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.pm.nosavedlineups", steamID]);
            }
        }

        private void HandleLoadNadeCommand(CCSPlayerController? player, string loadNadeName)
        {
            if (!isPractice || player is null || !IsPlayerValid(player)) return;

            if (!string.IsNullOrWhiteSpace(loadNadeName))
            {
                // Get player info: steamid
                string playerSteamID = player.SteamID.ToString();

                // Define the file path
                string savednadesfileName = "MatchZy/savednades.json";
                string savednadesPath = Path.Join(Server.GameDirectory + "/csgo/cfg", savednadesfileName);

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                        ?? [];

                    bool lineupFound = false;
                    bool lineupOnWrongMap = false;

                    // Check for the lineup in the player's steamID and the fixed steamID
                    foreach (string currentSteamID in (string[])[playerSteamID, "default"])
                    {
                        if (savedNadesDict.TryGetValue(currentSteamID, out var currentDict))
                        {
                            // Filter nade names based on the current map
                            var nadeNamesOnCurrentMap = currentDict
                                .Where(n => n.Value.TryGetValue("Map", out var m) && m == Server.MapName)
                                .Select(n => n.Key)
                                .ToList();

                            // Find the nearest matching name
                            string nearestName = StringSimilarity.FindNearestName(loadNadeName, nadeNamesOnCurrentMap);

                            if (currentDict.TryGetValue(nearestName, out var lineupInfo))
                            {
                                // Check if the lineup contains the "Map" key and if it matches the current map
                                if (lineupInfo.TryGetValue("Map", out var map) && map == Server.MapName)
                                {
                                    // Extract position and angle from the lineup information
                                    string[] posArray = lineupInfo["LineupPos"].Split(' ');
                                    string[] angArray = lineupInfo["LineupAng"].Split(' ');

                                    // Parse position and angle
                                    Vector loadedPlayerPos = new(float.Parse(posArray[0]), float.Parse(posArray[1]), float.Parse(posArray[2]));
                                    QAngle loadedPlayerAngle = new(float.Parse(angArray[0]), float.Parse(angArray[1]), float.Parse(angArray[2]));

                                    // Teleport player
                                    player.PlayerPawn.Value?.Teleport(loadedPlayerPos, loadedPlayerAngle, new(0, 0, 0));

                                    // Change player inv slot
                                    string nadeType = lineupInfo.TryGetValue("Type", out var typeVal) ? typeVal : "";
                                    switch (nadeType)
                                    {
                                        case "Flash":
                                            player.ExecuteClientCommand("slot7");
                                            break;
                                        case "Smoke":
                                            player.ExecuteClientCommand("slot8");
                                            break;
                                        case "HE":
                                            player.ExecuteClientCommand("slot6");
                                            break;
                                        case "Decoy":
                                            player.ExecuteClientCommand("slot9");
                                            break;
                                        case "Molly":
                                            player.ExecuteClientCommand("slot10");
                                            break;
                                        default:
                                            player.ExecuteClientCommand("slot8");
                                            break;
                                    }

                                    // Extract description, if available
                                    string? lineupDesc = lineupInfo.TryGetValue("Desc", out var descVal) ? descVal : null;

                                    ReplyToUserCommand(player, Localizer["matchzy.pm.lineuploadedsuccess", nearestName]);

                                    if (!string.IsNullOrWhiteSpace(lineupDesc))
                                    {
                                        player.PrintToCenter($"{lineupDesc}");
                                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupdesc", lineupDesc]);
                                    }

                                    lineupFound = true;
                                    break;
                                }
                                else
                                {
                                    ReplyToUserCommand(player, Localizer["matchzy.pm.nadenotfoundonmap", nearestName]);
                                    lineupOnWrongMap = true;
                                }
                            }
                        }
                    }

                    if (!lineupFound && !lineupOnWrongMap)
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.pm.nadenotfound", loadNadeName]);
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.pm.loadnadenotfound"]);
            }
        }

        public void ShowSpawnBeam(Position spawn, Color color)
        {
            CBeam? beam = Utilities.CreateEntityByName<CBeam>("beam");
            if (beam is null)
            {
                Log($"Failed to create beam for the spawn");
                return;
            }

            beam.LifeState = 1;
            beam.Width = 5;
            beam.Render = color;

            beam.EndPos.X = spawn.PlayerPosition.X;
            beam.EndPos.Y = spawn.PlayerPosition.Y;
            beam.EndPos.Z = spawn.PlayerPosition.Z + 100.0f;

            beam.Teleport(spawn.PlayerPosition, new(0, 0, 0), new(0, 0, 0));

            beam.DispatchSpawn();
        }

        public void RemoveSpawnBeams()
        {
            var beams = Utilities.FindAllEntitiesByDesignerName<CEntityInstance>("beam");
            foreach (var beam in beams)
            {
                if (beam is null) continue;
                beam.Remove();
            }
        }

        [ConsoleCommand("css_god", "Sets Infinite health for player")]
        public void OnGodCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player is null || !IsPlayerValid(player)) return;
            if (player.PlayerPawn.Value is not { } playerPawn) return;

            int currentHP = playerPawn.Health;
            
            if (currentHP > 100)
            {
                playerPawn.Health = 100;
                ReplyToUserCommand(player, "God is " + Localizer["matchzy.cc.disabled"]);
            }
            else
            {
                playerPawn.Health = 2147483647; // max 32bit int
                ReplyToUserCommand(player, "God is " + Localizer["matchzy.cc.enabled"]);
            }
        }

        [ConsoleCommand("css_prac999", "Starts practice mode")]
        [ConsoleCommand("css_tactics999", "Starts practice mode")]
        public void OnPracCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_prac999", "@css/map", "@custom/prac")) {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer["matchzy.pm.pracmatchstarted"]);
                return;
            }
    
            StartPracticeMode();
        }

        [ConsoleCommand("css_dry", "Starts dryrun in practice mode")]
        [ConsoleCommand("css_dryrun", "Starts dryrun in practice mode")]
        public void OnDryRunCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_prac", "@css/map", "@custom/prac")) {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer["matchzy.pm.dryrunmatchstarted"]);
                return;
            }
            if (!isPractice)
            {
                ReplyToUserCommand(player, Localizer["matchzy.pm.dryrunnopractice"]);
                return;
            }

            Server.ExecuteCommand("bot_kick");
            pracUsedBots = [];
            noFlashList = [];

            ExecUnpracCommands();
            ExecDryRunCFG();

            isDryRun = true;
        }

        [ConsoleCommand("css_spawn", "Teleport to provided spawn")]
        public void OnSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice) return;
            // Checking if any of the Position List is empty
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            if (player is not { IsValid: true } || !player.PlayerPawn.IsValid) return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, player.TeamNum, "spawn");
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!spawn <round>"]);
            }
        }

        [ConsoleCommand("css_ctspawn", "Teleport to provided CT spawn")]
        public void OnCtSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice) return;
            // Checking if any of the Position List is empty
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            if (player is not { IsValid: true } || !player.PlayerPawn.IsValid) return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!ctspawn <round>"]);
            }
        }

        [ConsoleCommand("css_tspawn", "Teleport to provided T spawn")]
        public void OnTSpawnCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice) return;
            // Checking if any of the Position List is empty
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            if (player is not { IsValid: true } || !player.PlayerPawn.IsValid) return;

            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleSpawnCommand(player, commandArg, (byte)CsTeam.Terrorist, "tspawn");
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!ctspawn <round>"]);
            }
        }

        [ConsoleCommand("css_bot", "Spawns a bot at the player's position")]
        public void OnBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            AddBot(player, false);
        }

        [ConsoleCommand("css_cbot", "Spawns a crouched bot at the player's position")]
        [ConsoleCommand("css_crouchbot", "Spawns a crouched bot at the player's position")]
        public void OnCrouchBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            AddBot(player, true);
        }

        [ConsoleCommand("css_boost", "Spawns a bot at the player's position and boost the player on it")]
        public void OnBoostBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice) return;
            AddBot(player, false);
            AddTimer(0.2f, () => ElevatePlayer(player));
        }

        [ConsoleCommand("css_crouchboost", "Spawns a crouched bot at the player's position and boost the player on it")]
        public void OnCrouchBoostBotCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice) return;
            AddBot(player, true);
            AddTimer(0.2f, () => ElevatePlayer(player));
        }

        private void AddBot(CCSPlayerController? player, bool crouch)
        {
            try
            {
                if (!isPractice || player is not { IsValid: true } || !player.PlayerPawn.IsValid || player.PlayerPawn.Value is not { } playerPawn) return;
                if (playerPawn.MovementServices is null) return;
                
                CCSPlayer_MovementServices movementService = new(playerPawn.MovementServices.Handle);

                if ((int)movementService.DuckAmount == 1)
                {
                    // Player was crouching while using .bot command
                    crouch = true;
                }
                isSpawningBot = true;

                if (player.TeamNum == (byte)CsTeam.CounterTerrorist)
                {
                    Server.ExecuteCommand("bot_join_team T");
                    Server.ExecuteCommand("bot_add_t");
                }
                else if (player.TeamNum == (byte)CsTeam.Terrorist)
                {
                    Server.ExecuteCommand("bot_join_team CT");
                    Server.ExecuteCommand("bot_add_ct");
                }
                
                // Once bot is added, we teleport it to the requested position
                AddTimer(0.1f, () => SpawnBot(player, crouch));
                Server.ExecuteCommand("bot_stop 1");
                Server.ExecuteCommand("bot_freeze 1");
                Server.ExecuteCommand("bot_zombie 1");
            }
            catch (JsonException ex)
            {
                Log($"[AddBot - FATAL] Error: {ex.Message}");
            }
        }

        private void SpawnBot(CCSPlayerController botOwner, bool crouch)
        {
            try 
            {
                if (!IsPlayerValid(botOwner) || botOwner.PlayerPawn.Value?.CBodyComponent?.SceneNode is not { AbsOrigin: not null, AbsRotation: not null } ownerSceneNode) return;
                
                var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
                bool unusedBotFound = false;
                foreach (var tempPlayer in playerEntities)
                {
                    if (!IsPlayerValid(tempPlayer)) continue;
                    if (!tempPlayer.IsBot || tempPlayer.IsHLTV) continue;
                    if (tempPlayer.UserId.HasValue)
                    {
                        int botUserId = tempPlayer.UserId.Value;
                        if (!pracUsedBots.ContainsKey(botUserId) && unusedBotFound)
                        {
                            Log($"UNUSED BOT FOUND: {botUserId} EXECUTING: kickid {botUserId}");
                            Server.ExecuteCommand($"kickid {botUserId}");
                            continue;
                        }
                        if (pracUsedBots.ContainsKey(botUserId))
                        {
                            continue;
                        }
                        pracUsedBots[botUserId] = [];

                        Position botOwnerPosition = new(ownerSceneNode.AbsOrigin, ownerSceneNode.AbsRotation);
                        
                        pracUsedBots[botUserId]["controller"] = tempPlayer;
                        pracUsedBots[botUserId]["position"] = botOwnerPosition;
                        pracUsedBots[botUserId]["owner"] = botOwner;
                        pracUsedBots[botUserId]["crouchstate"] = crouch;

                        if (tempPlayer.PlayerPawn.Value is { } tempPawn)
                        {
                            if (crouch && tempPawn.MovementServices is not null)
                            {
                                CCSPlayer_MovementServices movementService = new(tempPawn.MovementServices.Handle);
                                AddTimer(0.1f, () => movementService.DuckAmount = 1);
                                AddTimer(0.2f, () => {
                                    if (tempPawn.Bot is not null) tempPawn.Bot.IsCrouching = true;
                                });
                            }

                            tempPawn.Teleport(botOwnerPosition.PlayerPosition, botOwnerPosition.PlayerAngle, new(0, 0, 0));
                        }

                        TemporarilyDisableCollisions(botOwner, tempPlayer);
                        unusedBotFound = true;
                    }
                }
                if (!unusedBotFound) {
                    PrintToAllChat(Localizer["matchzy.pm.botlimit"]);
                }

                isSpawningBot = false;
            }
            catch (JsonException ex)
            {
                Log($"[SpawnBot - FATAL] Error: {ex.Message}");
            }
        }

        public void TemporarilyDisableCollisions(CCSPlayerController p1, CCSPlayerController p2)
        {
            Log($"[TemporarilyDisableCollisions] Disabling {p1.PlayerName} {p2.PlayerName}");
            
            if (p1.PlayerPawn.Value is not { } p1Pawn || p2.PlayerPawn.Value is not { } p2Pawn) return;

            p1Pawn.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            p1Pawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            p2Pawn.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;
            p2Pawn.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEBRIS;

            var p1p = p1.PlayerPawn;
            var p2p = p2.PlayerPawn;
            collisionGroupTimer?.Kill();
            collisionGroupTimer = AddTimer(0.1f, () =>
            {
                if (!p1p.IsValid || !p2p.IsValid || p1p.Value is not { IsValid: true } validP1 || p2p.Value is not { IsValid: true } validP2)
                {
                    Log($"player handle invalid");
                    collisionGroupTimer?.Kill();
                    return;
                }

                if (!DoPlayersCollide(validP1, validP2))
                {
                    // Once they no longer collide 
                    validP1.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                    validP1.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                    validP2.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;
                    validP2.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PLAYER_MOVEMENT;

                    collisionGroupTimer?.Kill();
                }

            }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        public bool DoPlayersCollide(CCSPlayerPawn p1, CCSPlayerPawn p2)
        {
            var p1pos = p1.AbsOrigin;
            var p2pos = p2.AbsOrigin;

            if (p1pos is null || p2pos is null) return false;

            Vector p1min = p1.Collision.Mins + p1pos;
            Vector p1max = p1.Collision.Maxs + p1pos;
            Vector p2min = p2.Collision.Mins + p2pos;
            Vector p2max = p2.Collision.Maxs + p2pos;

            return p1min.X <= p2max.X && p1max.X >= p2min.X &&
                    p1min.Y <= p2max.Y && p1max.Y >= p2min.Y &&
                    p1min.Z <= p2max.Z && p1max.Z >= p2min.Z;
        }

        private static void ElevatePlayer(CCSPlayerController? player)
        {
            if (player is not { IsValid: true } || !player.PlayerPawn.IsValid || player.PlayerPawn.Value is not { } playerPawn) return;
            if (playerPawn.CBodyComponent?.SceneNode?.AbsOrigin is not { } absOrigin) return;

            playerPawn.Teleport(new(absOrigin.X, absOrigin.Y, absOrigin.Z + 80.0f), playerPawn.EyeAngles, new(0, 0, 0));
        }

        [GameEventHandler]
        public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (!IsPlayerValid(player) || player is null) return HookResult.Continue;

            if (player.PlayerPawn.Value is not { } pawn) return HookResult.Continue;

            if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP) {
                pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            }

            if (matchStarted && (matchzyTeam1.coach.Contains(player) || matchzyTeam2.coach.Contains(player)))
            {
                if (player.InGameMoneyServices is not null)
                {
                    player.InGameMoneyServices.Account = 0;
                    Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
                }
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                
                return HookResult.Continue;
            }

            // Respawing a bot where it was actually spawned during practice session
            if (isPractice && player.IsValid && player.IsBot && player.UserId.HasValue)
            {
                int botUserId = player.UserId.Value;
                if (pracUsedBots.TryGetValue(botUserId, out var botData))
                {
                    if (botData.TryGetValue("position", out var posObj) && posObj is Position botPosition)
                    {
                        pawn.Teleport(botPosition.PlayerPosition, botPosition.PlayerAngle, new(0, 0, 0));

                        if (botData.TryGetValue("crouchstate", out var crouchObj) && crouchObj is bool isCrouched && isCrouched)
                        {
                            pawn.Flags |= (uint)PlayerFlags.FL_DUCKING;
                            if (pawn.MovementServices is not null)
                            {
                                CCSPlayer_MovementServices movementService = new(pawn.MovementServices.Handle);
                                AddTimer(0.1f, () => movementService.DuckAmount = 1);
                                AddTimer(0.2f, () => {
                                    if (pawn.Bot is not null) pawn.Bot.IsCrouching = true;
                                });
                            }
                        }

                        if (botData.TryGetValue("owner", out var ownerObj) && ownerObj is CCSPlayerController botOwner && IsPlayerValid(botOwner)) 
                        {
                            AddTimer(0.2f, () => TemporarilyDisableCollisions(botOwner, player));
                        } 
                    }
                }
                else if (!isSpawningBot && !player.IsHLTV)
                {
                    Log($"Kicking bot {player.PlayerName} due to erroneous spawning");
                    AddTimer(2.5f, () =>
                    {
                        Server.ExecuteCommand($"bot_kick {player.PlayerName}");
                    });
                }
            }

            return HookResult.Continue;
        }

        [ConsoleCommand("css_nobots", "Removes bots from the practice session")]
        public void OnNoBotsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player is null) return;
            Server.ExecuteCommand("bot_kick");
            pracUsedBots = [];
        }

        [ConsoleCommand("css_ff", "Fast forwards the timescale to 20 seconds")]
        public void OnFFCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player is null) return;

            Dictionary<int, MoveType_t> preFastForwardMoveTypes = [];

            foreach (var key in playerData.Keys) {
                if (!IsPlayerValid(playerData[key]) || playerData[key].PlayerPawn.Value is not { } pawn) continue;
                preFastForwardMoveTypes[key] = pawn.MoveType;
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
            }

            Server.PrintToChatAll($"{chatPrefix} Fastforwarding 20 seconds!");
            Server.ExecuteCommand("host_timescale 10");
            AddTimer(20.0f, () => {
                ResetFastForward(preFastForwardMoveTypes);
            });
        }

        [ConsoleCommand("css_fastforward", "Fast forwards the timescale to 20 seconds")]
        public void OnFastForwardCommand(CCSPlayerController? player, CommandInfo? command)
        {
            OnFFCommand(player, command);
        }

        public void ResetFastForward(Dictionary<int, MoveType_t> preFastForwardMoveTypes) {
            if (!isPractice) return;
            Server.ExecuteCommand("host_timescale 1");
            foreach (var (key, value) in preFastForwardMoveTypes) {
                if (!playerData.TryGetValue(key, out var pController) || !IsPlayerValid(pController) || pController.PlayerPawn.Value is not { } pawn) continue;
                pawn.MoveType = value;
            }
        }

        [ConsoleCommand("css_clear", "Removes all the available granades")]
        public void OnClearCommand(CCSPlayerController? player, CommandInfo? command)
        {
            RemoveGrenadeEntities();
        }

        [ConsoleCommand("css_spec", "Switches team to Spectator")]
        public void OnSpecCommand(CCSPlayerController? player, CommandInfo? command) {
            if (!isPractice || player is null) return;

            SideSwitchCommand(player, CsTeam.Spectator);
        }

        [ConsoleCommand("css_fas", "Switches all other players to spectator")]
        [ConsoleCommand("css_watchme", "Switches all other players to spectator")]
        public void OnFASCommand(CCSPlayerController? player, CommandInfo? command) {
            if (!isPractice || player is null) return;

            SideSwitchCommand(player, CsTeam.None);
        }

        [ConsoleCommand("css_noblind", "Disables flash effect for the player")]
        [ConsoleCommand("css_noflash", "Disables flash effect for the player")]
        public void OnNoFlashCommand(CCSPlayerController? player, CommandInfo? command) {
            if (!isPractice || player is null || player.UserId is null) return;

            int userId = player.UserId.Value;

            if (noFlashList.Contains(userId))
            {
                noFlashList.Remove(userId);
                ReplyToUserCommand(player, "Disabled noflash.");
            } else {
                noFlashList.Add(userId);
                ReplyToUserCommand(player, "Enabled noflash. Use .noflash again to disable.");
                Server.NextFrame(() => KillFlashEffect(player));
            }
        }

        [ConsoleCommand("css_break", "Breaks the breakable entities")]
        public void OnBreakCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice) return;
            var entities = Utilities.FindAllEntitiesByDesignerName<CBreakable>("prop_dynamic")
                .Concat(Utilities.FindAllEntitiesByDesignerName<CBreakable>("func_breakable"));
            foreach (var entity in entities)
            {
                entity.AcceptInput("Break");
            }
        }

        public void KillFlashEffect(CCSPlayerController player) {
            if (player.PlayerPawn.Value is not { } playerPawn) return;
            Log($"[KillFlashEffect] Killing flash effect for player: {player.PlayerName}");
            playerPawn.FlashMaxAlpha = 0.5f;
        }

        // CsTeam.None is a special value to mean force all other players to spectator
        private void SideSwitchCommand(CCSPlayerController player, CsTeam team) {
          if (team > CsTeam.None) {
            if (player.TeamNum == (byte)CsTeam.Spectator) {
              ReplyToUserCommand(player, Localizer["matchzy.pm.spectatorbroken"]);
              return;
            }
            player.ChangeTeam(team);
            return;
          }
          
          foreach (var x in Utilities.GetPlayers())
          {
              if (x is { IsValid: true, IsBot: false } && x.UserId != player.UserId)
              {
                  x.ChangeTeam(CsTeam.Spectator);
              }
          }
        }

        public void RemoveGrenadeEntities()
        {
            if (!isPractice) return;
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("smokegrenade_projectile"))
            {
                entity?.Remove();
            }
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("molotov_projectile"))
            {
                entity?.Remove();
            }
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("inferno"))
            {
                entity?.Remove();
            }
        }

        public void ExecDryRunCFG()
        {
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", dryrunCfgPath);
    
            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(absolutePath)) {
                Log($"[ExecDryRunCFG] Starting Dryrun! Executing Dryrun CFG from {dryrunCfgPath}");
                Server.ExecuteCommand($"exec {dryrunCfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            } else {
                Log($"[ExecDryRunCFG] Starting Dryrun! Dryrun CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 6;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 16000;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 3;mp_team_timeout_ot_max 1;mp_team_timeout_ot_add_each 1;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
            }
        }

        public void ExecUnpracCommands() {
            Server.ExecuteCommand("sv_cheats false;sv_grenade_trajectory_prac_pipreview false;sv_grenade_trajectory_prac_trailtime 0; mp_ct_default_grenades \"\"; mp_ct_default_primary \"\"; mp_t_default_grenades\"\"; mp_t_default_primary\"\"; mp_teammates_are_enemies false;");
            Server.ExecuteCommand("mp_death_drop_breachcharge true; mp_death_drop_defuser true; mp_death_drop_taser true; mp_drop_knife_enable false; mp_death_drop_grenade 2; ammo_grenade_limit_total 4; mp_defuser_allocation 0; sv_infinite_ammo 0; mp_force_pick_time 15");
        }

        public bool IsValidPositionForLastGrenade(CCSPlayerController player, int position)
        {
            if (player.UserId is null) return false;
            int userId = player.UserId.Value;
            if (!lastGrenadesData.TryGetValue(userId, out var list) || list.Count <= 0)
            {
                PrintToPlayerChat(player, Localizer["matchzy.pm.nothrownnades"]);
                return false;
            }

            if (list.Count < position)
            {
                PrintToPlayerChat(player, Localizer["matchzy.pm.grenadehistory", $"{list.Count}"]);
                return false;
            }

            return true;
        }

        public void RethrowSpecificNade(CCSPlayerController player, string nadeType)
        {
            if (!isPractice || player.UserId is null) return;
            int userId = player.UserId.Value;
            if (!nadeSpecificLastGrenadeData.TryGetValue(userId, out var nadeDict) || !nadeDict.TryGetValue(nadeType, out var grenadeThrown))
            {
                PrintToPlayerChat(player, Localizer["matchzy.pm.nothrownnadestype", nadeType]);
                return;
            }
            AddTimer(grenadeThrown.Delay, () => grenadeThrown.Throw(player));
        }

        public void HandleBackCommand(CCSPlayerController player, string number)
        {
            if (!isPractice || player.UserId is null) return;
            int userId = player.UserId.Value;
            if (!string.IsNullOrWhiteSpace(number))
            {
                if (int.TryParse(number, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        if (lastGrenadesData.TryGetValue(userId, out var list))
                        {
                            list[positionNumber].LoadPosition(player);
                            PrintToPlayerChat(player, Localizer["matchzy.pm.tptogrenade", $"{positionNumber + 1}/{list.Count}"]);
                        }
                    }
                }
                else
                {
                    PrintToPlayerChat(player, Localizer["matchzy.pm.backinvalidvalue"]);
                    return;
                }
            }
            else
            {
                int thrownCount = lastGrenadesData.TryGetValue(userId, out var list) ? list.Count : 0;
                ReplyToUserCommand(player, Localizer["matchzy.pm.backtonumber", thrownCount]);
            }
        }

        public void HandleThrowIndexCommand(CCSPlayerController player, string argString)
        {
            if (!isPractice || !IsPlayerValid(player) || player.UserId is null) return;
            int userId = player.UserId.Value;

            if (string.IsNullOrEmpty(argString))
            {
                int thrownCount = lastGrenadesData.TryGetValue(userId, out var list) ? list.Count : 0;
                ReplyToUserCommand(player, Localizer["matchzy.pm.throwindextonumber", thrownCount]);
                return;
            }

            string[] argsList = argString.Split();

            foreach (string arg in argsList)
            {
                if (int.TryParse(arg, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        if (lastGrenadesData.TryGetValue(userId, out var list))
                        {
                            GrenadeThrownData grenadeThrown = list[positionNumber];
                            AddTimer(grenadeThrown.Delay, () => grenadeThrown.Throw(player));
                            PrintToPlayerChat(player, Localizer["matchzy.pm.throwgrenadehistory", $"{positionNumber + 1}/{list.Count}"]);
                        }
                    }
                }
                else
                {
                    PrintToPlayerChat(player, Localizer["matchzy.pm.backnegativenumber", arg]);
                }
            }
        }

        public void HandleDelayCommand(CCSPlayerController player, string delay)
        {
            if (!isPractice || !IsPlayerValid(player) || player.UserId is null) return;
            int userId = player.UserId.Value;
            if (string.IsNullOrWhiteSpace(delay))
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!delay <delay_in_seconds>"]);
                return;
            }
            
            if (float.TryParse(delay, out float delayInSeconds) && delayInSeconds > 0)
            {
                if (IsValidPositionForLastGrenade(player, 0) && lastGrenadesData.TryGetValue(userId, out var list) && list.Count > 0)
                {
                    list.Last().Delay = delayInSeconds;
                    PrintToPlayerChat(player, Localizer["matchzy.pm.delaygrenade", $"{delayInSeconds:0.00}", $"{list.Count}"]);
                }
            }
            else
            {
                int count = lastGrenadesData.TryGetValue(userId, out var list) ? list.Count : 0;
                PrintToPlayerChat(player, Localizer["matchzy.pm.delayvalidnumber", $"{delayInSeconds:0.00}", $"{count}"]);
                return;
            }
        }

        public void DisplayPracticeTimerCenter(int userId)
        {
            if (!playerData.TryGetValue(userId, out var pController) || !playerTimers.TryGetValue(userId, out var pTimer)) return;
            if (!IsPlayerValid(pController)) return;
            pTimer.DisplayTimerCenter(pController);
        }

        [ConsoleCommand("css_throw", "Throws the last thrown grenade")]
        [ConsoleCommand("css_rethrow", "Throws the last thrown grenade")]
        public void OnRethrowCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player is null || player.UserId is null) return;
            int userId = player.UserId.Value;
            if (!lastGrenadesData.TryGetValue(userId, out var list) || list.Count <= 0)
            {
                PrintToPlayerChat(player, Localizer["matchzy.pm.notthrownnade"]);
                return;
            }
            GrenadeThrownData lastGrenade = list.Last();
            AddTimer(lastGrenade.Delay, () => lastGrenade.Throw(player));
        }

        [ConsoleCommand("css_savepos", "Saves the player location")]
        public void OnSavePosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player is null || player.UserId is null || player.PlayerPawn.Value is not { } pawn) return;
            if (pawn.AbsOrigin is not { } absOrigin || pawn.EyeAngles is not { } eyeAngles) return;

            int userId = player.UserId.Value;
            Vector position = new(absOrigin.X, absOrigin.Y, absOrigin.Z);
            QAngle angle = new(eyeAngles.X, eyeAngles.Y, eyeAngles.Z);
            
            savedPlayerLocationData[userId] = new(position, angle);
            Log($"[SavePos] Saved position for UserID {userId}, Position: {position}, Angle: {angle}!");
            PrintToPlayerChat(player, Localizer["matchzy.pm.savepos"]);
        }

        [ConsoleCommand("css_loadpos", "Loads the last saved player location")]
        public void OnLoadPosCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player is null || player.UserId is null) return;
            
            int userId = player.UserId.Value;
            if (!savedPlayerLocationData.TryGetValue(userId, out var playerLocationData))
            {
                PrintToPlayerChat(player, Localizer["matchzy.pm.notsavedpos"]);
                return;
            }
            
            Log($"[LoadPos] LoadPos position for UserID {userId}, Position: {playerLocationData.Position}, Angles: {playerLocationData.Angle}!");
            playerLocationData.LoadPosition(player);
            PrintToPlayerChat(player, Localizer["matchzy.pm.loadpos"]);
        }

        [ConsoleCommand("css_throwsmoke", "Throws the last thrown smoke")]
        [ConsoleCommand("css_rethrowsmoke", "Throws the last thrown smoke")]
        public void OnRethrowSmokeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null) return;
            RethrowSpecificNade(player, "smoke");
        }

        [ConsoleCommand("css_throwflash", "Throws the last thrown flash")]
        [ConsoleCommand("css_rethrowflash", "Throws the last thrown flash")]
        public void OnRethrowFlashCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null) return;
            RethrowSpecificNade(player, "flash");
        }

        [ConsoleCommand("css_throwgrenade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_rethrowgrenade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_thrownade", "Throws the last thrown he grenade")]
        [ConsoleCommand("css_rethrownade", "Throws the last thrown he grenade")]
        public void OnRethrowGrenadeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null) return;
            RethrowSpecificNade(player, "hegrenade");
        }

        [ConsoleCommand("css_throwmolotov", "Throws the last thrown molotov")]
        [ConsoleCommand("css_rethrowmolotov", "Throws the last thrown molotov")]
        public void OnRethrowMolotovCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null) return;
            RethrowSpecificNade(player, "molotov");
        }

        [ConsoleCommand("css_throwdecoy", "Throws the last thrown decoy")]
        [ConsoleCommand("css_rethrowdecoy", "Throws the last thrown decoy")]
        public void OnRethrowDecoyCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null) return;
            RethrowSpecificNade(player, "decoy");
        }

        [ConsoleCommand("css_last", "Teleports to the last thrown grenade position")]
        public void OnLastCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || player is null || player.UserId is null) return;
            int userId = player.UserId.Value;
            if (!lastGrenadesData.TryGetValue(userId, out var list) || list.Count <= 0)
            {
                PrintToPlayerChat(player, Localizer["matchzy.pm.notthrownnade"]);
                return;
            }
            list.Last().LoadPosition(player);
        }

        [ConsoleCommand("css_back", "Teleports to the provided position in grenade thrown history")]
        public void OnBackCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || player is null || player.UserId is null) return;
            if (command.ArgCount >= 2) 
            {
                string commandArg = command.ArgByIndex(1);
                HandleBackCommand(player, commandArg);
            }
            else 
            {
                int userId = player.UserId.Value;
                int thrownCount = lastGrenadesData.TryGetValue(userId, out var list) ? list.Count : 0;
                ReplyToUserCommand(player, Localizer["matchzy.pm.backtonumber", thrownCount]);
            }      
        }

        [ConsoleCommand("css_throwidx", "Throws grenade of provided position in grenade thrown history")]
        [ConsoleCommand("css_throwindex", "Throws grenade of provided position in grenade thrown history")]
        public void OnThrowIndexCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null || player.UserId is null) return;
            if (command.ArgCount >= 2) 
            {
                HandleThrowIndexCommand(player, command.ArgString);
            }
            else 
            {
                int userId = player.UserId.Value;
                int thrownCount = lastGrenadesData.TryGetValue(userId, out var list) ? list.Count : 0;
                ReplyToUserCommand(player, Localizer["matchzy.pm.throwindextonumber", thrownCount]);
            }      
        }

        [ConsoleCommand("css_lastindex", "Returns index of the last thrown grenade")]
        public void OnLastIndexCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null || player.UserId is null) return;
            if (IsValidPositionForLastGrenade(player, 1) && lastGrenadesData.TryGetValue(player.UserId.Value, out var list))
            {
                PrintToPlayerChat(player, Localizer["matchzy.pm.indexlastgrenade", $"{list.Count}"]);
            } 
        }

        [ConsoleCommand("css_delay", "Adds a delay to the last thrown grenade. Usage: !delay <delay_in_seconds>")]
        public void OnDelayCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null) return;
            if (command.ArgCount >= 2) 
            {
                HandleDelayCommand(player, command.ArgByIndex(1));
            }
            else 
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!delay <delay_in_seconds>"]);
            }      
        }

        [ConsoleCommand("css_timer", "Starts a timer, use .timer again to stop it.")]
        public void OnTimerCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null || player.UserId is null) return;
            int userId = player.UserId.Value;
            if (playerTimers.TryGetValue(userId, out var pTimer))
            {
                pTimer.KillTimer();
                double timerResult = pTimer.GetTimerResult();
                player.PrintToCenter($"Timer: {timerResult}s");
                PrintToPlayerChat(player, $"Timer stopped! Result: {timerResult}s");
                playerTimers.Remove(userId);
            }
            else
            {
                playerTimers[userId] = new(PracticeTimerType.Immediate)
                {
                    StartTime = DateTime.Now,
                    Timer = AddTimer(0.1f, () => DisplayPracticeTimerCenter(userId), TimerFlags.REPEAT)
                };
                PrintToPlayerChat(player, $"Timer started! User !timer to stop it.");
            }
        }

        [ConsoleCommand("css_sn", "Saves current nade position")]
        [ConsoleCommand("css_savenade", "Saves current nade position")]
        public void OnSaveNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleSaveNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_ln", "Loades the nade with provided filter")]
        [ConsoleCommand("css_loadnade", "Loades the nade with provided filter")]
        public void OnLoadNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleLoadNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_lin", "Lists the nade with provided filter")]
        [ConsoleCommand("css_listnades", "Lists the nade with provided filter")]
        public void OnListNadesCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleListNadesCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_importnade", "Imports the nade with the given code")]
        [ConsoleCommand("css_in", "Imports the nade with the given code")]
        public void OnImportNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleImportNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_deletenade", "Deletes the nade by name")]
        [ConsoleCommand("css_delnade", "Deletes the nade by name")]
        [ConsoleCommand("css_dn", "Deletes the nade by name")]
        public void OnDeleteNadeCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            HandleDeleteNadeCommand(player, command.ArgString);
        }

        [ConsoleCommand("css_solid", "Toggles mp_solid_teammates in practice mode")]
        public void OnSolidCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            var cvar = ConVar.Find("mp_solid_teammates");
            if (cvar is null) return;

            int solidValue = cvar.GetPrimitiveValue<int>();
            int newSolidValue = (solidValue is 0 or 1) ? 2 : 1;

            cvar.SetValue(newSolidValue);

            PrintToAllChat($"mp_solid_teammates is now set to {newSolidValue}");
        }

        [ConsoleCommand("css_impacts", "Toggles sv_showimpacts in practice mode")]
        public void OnImpactsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            var cvar = ConVar.Find("sv_showimpacts");
            if (cvar is null) return;

            int impactValue = cvar.GetPrimitiveValue<int>();
            int newImpactValue = 1 - impactValue;

            Server.ExecuteCommand($"sv_showimpacts {newImpactValue}");

            PrintToAllChat($"sv_showimpacts is now set to {newImpactValue}");
        }

        [ConsoleCommand("css_traj", "Toggles sv_grenade_trajectory_prac_pipreview in practice mode")]
        [ConsoleCommand("css_pip", "Toggles sv_grenade_trajectory_prac_pipreview in practice mode")]
        public void OnTrajCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;

            var cvar = ConVar.Find("sv_grenade_trajectory_prac_pipreview");
            if (cvar is null) return;

            bool trajValue = cvar.GetPrimitiveValue<bool>();

            Server.ExecuteCommand($"sv_grenade_trajectory_prac_pipreview {!trajValue}");

            PrintToAllChat($"sv_grenade_trajectory_prac_pipreview is now set to {!trajValue}");
        }

        [ConsoleCommand("css_bestspawn", "Teleports you to your team's closest spawn from your current position")]
        public void OnBestSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null) return;
            TeleportPlayerToBestSpawn(player, player.TeamNum);
        }

        [ConsoleCommand("css_worstspawn", "Teleports you to your team's furthest spawn from your current position")]
        public void OnWorstSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null) return;
            TeleportPlayerToWorstSpawn(player, player.TeamNum);
        }

        [ConsoleCommand("css_bestctspawn", "Teleports you to CT team's closest spawn from your current position")]
        public void OnBestCTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null) return;
            TeleportPlayerToBestSpawn(player, (byte)CsTeam.CounterTerrorist);
        }

        [ConsoleCommand("css_worstctspawn", "Teleports you to CT team's furthest spawn from your current position")]
        public void OnWorstCTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null) return;
            TeleportPlayerToWorstSpawn(player, (byte)CsTeam.CounterTerrorist);
        }

        [ConsoleCommand("css_besttspawn", "Teleports you to T team's closest spawn from your current position")]
        public void OnBestTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null) return;
            TeleportPlayerToBestSpawn(player, (byte)CsTeam.Terrorist);
        }

        [ConsoleCommand("css_worsttspawn", "Teleports you to T team's furthest spawn from your current position")]
        public void OnWorstTSpawnCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player) || player is null) return;
            TeleportPlayerToWorstSpawn(player, (byte)CsTeam.Terrorist);
        }

        [ConsoleCommand("css_showspawns", "Highlights all the competitive spawns")]
        public void OnShowSpawnsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            RemoveSpawnBeams();
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            
            if (spawnsData.TryGetValue((byte)CsTeam.CounterTerrorist, out var ctSpawns))
            {
                foreach (Position spawn in ctSpawns)
                {
                    ShowSpawnBeam(spawn, Color.Blue);
                }
            }
            if (spawnsData.TryGetValue((byte)CsTeam.Terrorist, out var tSpawns))
            {
                foreach (Position spawn in tSpawns)
                {
                    ShowSpawnBeam(spawn, Color.Orange);
                }
            }
        }

        [ConsoleCommand("css_hidespawns", "Hides the highlighted spawns")]
        public void OnHideSpawnsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isPractice || !IsPlayerValid(player)) return;
            RemoveSpawnBeams();
        }

        public void TeleportPlayerToBestSpawn(CCSPlayerController player, byte teamNum)
        {
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns) || teamSpawns is null or { Count: 0 }) return;
            if (player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin is not { } playerPosition) return;
            if (player.PlayerPawn.Value is not { } playerPawn) return;

            int closestIndex = -1;
            double minDistance = double.MaxValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = index;
                }
            }
            if (closestIndex != -1)
            {
                playerPawn.Teleport(teamSpawns[closestIndex].PlayerPosition, teamSpawns[closestIndex].PlayerAngle, new(0, 0, 0));
            }
        }

        public void TeleportPlayerToWorstSpawn(CCSPlayerController player, byte teamNum)
        {
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns) || teamSpawns is null or { Count: 0 }) return;
            if (player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin is not { } playerPosition) return;
            if (player.PlayerPawn.Value is not { } playerPawn) return;

            int farthestIndex = -1;
            double maxDistance = double.MinValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthestIndex = index;
                }
            }
            if (farthestIndex != -1)
            {
                playerPawn.Teleport(teamSpawns[farthestIndex].PlayerPosition, teamSpawns[farthestIndex].PlayerAngle, new(0, 0, 0));
            }
        }
    }
}
