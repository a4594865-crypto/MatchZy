using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;


namespace MatchZy
{

    public partial class MatchZy
    {
        // --- 核心修正：攔截換隊事件，解決觀戰者卡開賽與暖身洗牌報錯問題 ---
        [GameEventHandler]
        public HookResult EventPlayerTeamHandler(EventPlayerTeam @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;
            // 【.NET 10 升級】：現代化屬性模式匹配
            if (player is not { IsValid: true }) return HookResult.Continue;

            int userId = (int)player.UserId!.Value;

            // --- 核心修正：觀戰者不應佔用「已準備」名額 ---
            if (@event.Team == 1) // 進入觀戰
            {
                // 關鍵修正：不再只是設為 false，而是直接從名單中「擦除」
                playerReadyStatus.Remove(userId); 
                return HookResult.Continue; 
            }
            else if (@event.Team == 2 || @event.Team == 3) // 進入選手隊伍
            {
                // 當玩家從觀戰回歸選手位，系統才重新把他寫入點名單，預設為未準備 (false)
                if (!playerReadyStatus.ContainsKey(userId)) {
                    playerReadyStatus[userId] = false;
                }
            }

            // --- 原有的攔截邏輯：禁止比賽中互換隊伍 ---
            if (!isWarmup && (matchStarted || isKnifeRequired))
            {
                if (!@event.Silent)
                {
                    // 這裡刪除了 ReplyToUserCommand
                    return HookResult.Stop; // 依然保留攔截功能，讓換隊無效
                }
            }
            return HookResult.Continue;
        }
        
        public MatchConfig matchConfig = new();

        public bool isMatchSetup = false;

        public bool matchModeOnly = false;

        public bool resetCvarsOnSeriesEnd = true;

        public string loadedConfigFile = "";

        public Team matchzyTeam1 = new() {
            teamName = "COUNTER-TERRORISTS"
        };
        public Team matchzyTeam2 = new() {
            teamName = "TERRORISTS"
        };

        public Dictionary<Team, string> teamSides = new();
        public Dictionary<string, Team> reverseTeamSides = new();

        [ConsoleCommand("css_team1", "Sets team name for team1")]
        public void OnTeam1Command(CCSPlayerController? player, CommandInfo command) {
            HandleTeamNameChangeCommand(player, command.ArgString, 1);
        }

        [ConsoleCommand("css_team2", "Sets team name for team2")]
        public void OnTeam2Command(CCSPlayerController? player, CommandInfo command) {
            HandleTeamNameChangeCommand(player, command.ArgString, 2);
        }

        [ConsoleCommand("matchzy_loadmatch", "Loads a match from the given JSON file path (relative to the csgo/ directory)")]
        public void LoadMatch(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                if (player is null) return;
                if (isMatchSetup)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.matchisalreadysetup", liveMatchId]);
                    Log($"[LoadMatch] A match is already setup with id: {liveMatchId}, cannot load a new match!");
                    return;
                }
                string fileName = command.ArgString;
                string filePath = Path.Join(Server.GameDirectory + "/csgo", fileName);
                if (!File.Exists(filePath)) 
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.filedoesntexist"]);
                    Log($"[LoadMatch] Provided file does not exist! Usage: matchzy_loadmatch <filename>");
                    return;
                }
                string jsonData = File.ReadAllText(filePath);
                bool success = LoadMatchFromJSON(jsonData);
                if (!success)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.matchloadfailed"]);
                    ResetMatch();
                }
                loadedConfigFile = fileName;
            }
            catch (Exception e)
            {
                Log($"[LoadMatch - FATAL] An error occured: {e.Message}");
                return;
            }
        }

        [ConsoleCommand("get5_loadmatch_url", "Loads a match from the given URL")]
        [ConsoleCommand("matchzy_loadmatch_url", "Loads a match from the given URL")]
        public void LoadMatchFromURL(CCSPlayerController? player, CommandInfo command)
        {
            if (player is null) return;
            if (isMatchSetup)
            {
                ReplyToUserCommand(player, Localizer["matchzy.mm.get5matchisalreadysetup", liveMatchId]);
                Log($"[LoadMatchDataCommand] A match is already setup with id: {liveMatchId}, cannot load a new match!");
                return;
            }
            string url = command.ArgByIndex(1);

            string headerName = command.ArgCount > 3 ? command.ArgByIndex(2) : "";
            string headerValue = command.ArgCount > 3 ? command.ArgByIndex(3) : "";

            Log($"[LoadMatchDataCommand] Match setup request received with URL: {url} headerName: {headerName} and headerValue: {headerValue}");

            if (!IsValidUrl(url))
            {
                ReplyToUserCommand(player, Localizer["matchzy.mm.invalidurl", url]);
                Log($"[LoadMatchDataCommand] Invalid URL: {url}. Please provide a valid URL to load the match!");
                return;
            }
            try
            {
                HttpClient httpClient = new();
                if (headerName != "")
                {
                    httpClient.DefaultRequestHeaders.Add(headerName, headerValue);
                }
                HttpResponseMessage response = httpClient.GetAsync(url).Result;

                if (response.IsSuccessStatusCode)
                {
                    string jsonData = response.Content.ReadAsStringAsync().Result;
                    Log($"[LoadMatchFromURL] Received following data: {jsonData}");

                    bool success = LoadMatchFromJSON(jsonData);
                    if (!success)
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.mm.matchloadfailed"]);
                        ResetMatch();
                    }
                    loadedConfigFile = url;
                }
                else
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.httprequestfailed", response.StatusCode]);
                    Log($"[LoadMatchFromURL] HTTP request failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception e)
            {
                Log($"[LoadMatchFromURL - FATAL] An error occured: {e.Message}");
                return;
            }
        }

        static string ValidateMatchJsonStructure(JObject jsonData)
        {
            // 【.NET 10 升級】：集合表達式 []
            string[] requiredFields = ["maplist", "team1", "team2", "num_maps"];

            foreach (string field in requiredFields)
            {
                if (jsonData[field] is null)
                {
                    return $"Missing mandatory field: {field}";
                }
            }

            foreach (var property in jsonData.Properties())
            {
                string field = property.Name;

                switch (field)
                {
                    case "matchid":
                    case "players_per_team":
                    case "min_players_to_ready":
                    case "min_spectators_to_ready":
                    case "num_maps":
                        // 【.NET 10 升級】：Inline Out 變數宣告
                        if (!int.TryParse(jsonData[field]!.ToString(), out int numMaps))
                        {
                            return $"{field} should be an integer!";
                        }
                        if (field == "num_maps" && numMaps > jsonData["maplist"]!.ToObject<List<string>>()!.Count)
                        {
                            return $"{field} should be equal to or greater than maplist!";
                        }
                        
                        break;
                    
                    case "cvars":
                        if (jsonData[field]!.Type != JTokenType.Object)
                        {
                            return $"{field} should be a JSON structure!";
                        }
                        break;

                    case "team1":
                    case "team2":
                    case "spectators":
                        if (jsonData[field]!.Type != JTokenType.Object)
                        {
                            return $"{field} should be a JSON structure!";
                        }
                        if ((field != "spectators") && (jsonData[field]!["players"] is null || jsonData[field]!["players"]!.Type != JTokenType.Object)) 
                        {
                            return $"{field} should have 'players' JSON!";
                        }
                        break;

                    case "veto_mode":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }
                        break;

                    case "maplist":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }
                        // 【.NET 10 升級】：拔除 LINQ .Any()，改用 JToken 屬性 HasValues，0 記憶體分配
                        if (!jsonData[field]!.HasValues)
                        {
                            return $"{field} should contain atleast 1 map!";
                        }

                        break;
                    case "map_sides":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }
                        
                        // 【.NET 10 升級】：拔除 LINQ .All() 與 .Contains()，改用高速 foreach，0 記憶體分配
                        string[] allowedValues = ["team1_ct", "team1_t", "team2_ct", "team2_t", "knife"];
                        bool allElementsValid = true;
                        
                        foreach (var element in jsonData[field]!)
                        {
                            bool isValid = false;
                            string elementStr = element.ToString();
                            foreach (var allowed in allowedValues)
                            {
                                if (elementStr == allowed)
                                {
                                    isValid = true;
                                    break;
                                }
                            }
                            if (!isValid)
                            {
                                allElementsValid = false;
                                break;
                            }
                        }

                        if (!allElementsValid) {
                            return $"{field} should be \"team1_ct\", \"team1_t\", or \"knife\"!";
                        }
                        
                        if (jsonData[field]!.ToObject<List<string>>()!.Count < jsonData["num_maps"]!.Value<int>()) {
                            return $"{field} should be equal to or greater than num_maps!";
                        }
                        break;

                    case "skip_veto":
                    case "clinch_series":
                    case "wingman":
                        if (!bool.TryParse(jsonData[field]!.ToString(), out bool result))
                        {
                            return $"{field} should be a boolean!";
                        }
                        break;
                }
            }

            return "";
        }

        public bool LoadMatchFromJSON(string jsonData)
        {
            
            JObject jsonDataObject = JObject.Parse(jsonData);

            string validationError = ValidateMatchJsonStructure(jsonDataObject);

            if (validationError != "")
            {
                Log($"[LoadMatchDataCommand] {validationError}");
                return false;
            }

            if(jsonDataObject["matchid"] is not null)
            {
                liveMatchId = (long)jsonDataObject["matchid"]!;
            }
            JToken team1 = jsonDataObject["team1"]!;
            JToken team2 = jsonDataObject["team2"]!;
            JToken maplist = jsonDataObject["maplist"]!;

            if (team1["id"] is not null) matchzyTeam1.id = team1["id"]!.ToString();
            if (team2["id"] is not null) matchzyTeam2.id = team2["id"]!.ToString();

            matchzyTeam1.teamName = RemoveSpecialCharacters(team1["name"]!.ToString());
            matchzyTeam2.teamName = RemoveSpecialCharacters(team2["name"]!.ToString());
            matchzyTeam1.teamPlayers = team1["players"];
            matchzyTeam2.teamPlayers = team2["players"];

            matchConfig = new()
            {
                MatchId = liveMatchId,
                MapsPool = maplist.ToObject<List<string>>()!,
                MapsLeftInVetoPool = maplist.ToObject<List<string>>()!,
                NumMaps = jsonDataObject["num_maps"]!.Value<int>(),
                MinPlayersToReady = minimumReadyRequired
            };

            GetOptionalMatchValues(jsonDataObject);

            if (matchConfig.MapsPool.Count == matchConfig.NumMaps)
            {
                matchConfig.SkipVeto = true;
                isPreVeto = false;
            }
            else if (matchConfig.MapsPool.Count < matchConfig.NumMaps)
            {
                Log($"[LOADMATCH] The map pool {matchConfig.MapsPool.Count} is not large enough to play a series of {matchConfig.NumMaps} maps.");
                return false;
            }

            if (!matchConfig.SkipVeto)
            {
                if (matchConfig.MapBanOrder.Count != 0)
                {
                    if (!ValidateMapBanLogic()) return false;
                }
                else
                {
                    GenerateDefaultVetoSetup();
                }
            }

            GetCvarValues(jsonDataObject);

            Log($"[LOADMATCH] MinPlayersToReady: {matchConfig.MinPlayersToReady} SeriesClinch: {matchConfig.SeriesCanClinch}");
            Log($"[LOADMATCH] MapsPool: {string.Join(", ", matchConfig.MapsPool)} MapsLeftInVetoPool: {string.Join(", ", matchConfig.MapsLeftInVetoPool)}");

            LoadClientNames();

            if (matchConfig.SkipVeto)
            {
                for (int i = 0; i < matchConfig.NumMaps; i++) 
                {
                    matchConfig.Maplist.Add(matchConfig.MapsPool[i]);

                    if (matchConfig.MapSides.Count < matchConfig.Maplist.Count) {
                        if (matchConfig.MatchSideType == "standard" || matchConfig.MatchSideType == "always_knife") {
                            matchConfig.MapSides.Add("knife");
                        } else if (matchConfig.MatchSideType == "random") {
                            matchConfig.MapSides.Add(new Random().Next(0, 2) == 0 ? "team1_ct" : "team1_t");
                        } else {
                            matchConfig.MapSides.Add("team1_ct");
                        }
                    }
                }
                string currentMapName = Server.MapName;
                string mapName = matchConfig.Maplist[0].ToString();

                if (IsMapReloadRequiredForGameMode(matchConfig.Wingman) || mapReloadRequired || currentMapName != mapName) 
                {
                    SetCorrectGameMode();
                    ChangeMap(mapName, 0);
                }
            }
            else
            {
                isPreVeto = true;
            } 

            readyAvailable = true;

            ExecuteChangedConvars();

            StartWarmup();

            isMatchSetup = true;

            if(matchConfig.SkipVeto) SetMapSides();

            SetTeamNames();
            UpdatePlayersMap();
            UpdateHostname();

            var seriesStartedEvent = new MatchZySeriesStartedEvent
            {
                MatchId = liveMatchId,
                NumberOfMaps = matchConfig.NumMaps,
                Team1 = new(matchzyTeam1.id, matchzyTeam1.teamName),
                Team2 = new(matchzyTeam2.id, matchzyTeam2.teamName),
            };

            Task.Run(async () => {
                await SendEventAsync(seriesStartedEvent);
            });

            Log($"[LoadMatchFromJSON] Success with matchid: {liveMatchId}!");
            return true;
        }

        public void SetMapSides() {
            int mapNumber = matchConfig.CurrentMapNumber;
            
            teamSides[matchzyTeam1] = "CT";
            teamSides[matchzyTeam2] = "TERRORIST";
            reverseTeamSides["CT"] = matchzyTeam1;
            reverseTeamSides["TERRORIST"] = matchzyTeam2;
            
            if (matchConfig.MapSides.Count > mapNumber) {
                if (matchConfig.MapSides[mapNumber] == "team2_ct" || matchConfig.MapSides[mapNumber] == "team1_t") {
                    (teamSides[matchzyTeam1], teamSides[matchzyTeam2]) = (teamSides[matchzyTeam2], teamSides[matchzyTeam1]);
                    (reverseTeamSides["CT"], reverseTeamSides["TERRORIST"]) = (reverseTeamSides["TERRORIST"], reverseTeamSides["CT"]);
                }
            }
            
            SetTeamNames();
        }

     public void SetTeamNames()
        {
            // 取得目前暫存的隊伍名稱
            string ctName = reverseTeamSides["CT"].teamName;
            string tName = reverseTeamSides["TERRORIST"].teamName;

            // ==========================================
            // 1. 檢查 CT 陣營 (team1)
            // ==========================================
            // 使用 AsSpan() 進行 0 GC 的高效能字元驗證
            if (string.IsNullOrWhiteSpace(ctName) || ctName == "team_" || !HasValidChar(ctName.AsSpan()))
            {
                foreach (var p in Utilities.GetPlayers())
                {
                    // ⚠️ 注意這裡！已經把 IsBot: false 刪除了，現在會抓 BOT 的名字了！
                    if (p is { IsValid: true, TeamNum: 3 } && !string.IsNullOrWhiteSpace(p.PlayerName))
                    {
                        if (HasValidChar(p.PlayerName.AsSpan()))
                        {
                            ctName = $"Team {p.PlayerName}"; // 字串插值自動優化
                            break;
                        }
                    }
                }
                
                // 極端防護：萬一整隊都是符號哥
                if (string.IsNullOrWhiteSpace(ctName) || ctName == "team_" || !HasValidChar(ctName.AsSpan()))
                {
                    ctName = "CTs";
                }
                
                reverseTeamSides["CT"].teamName = ctName; 
            }

            // ==========================================
            // 2. 檢查 T 陣營 (team2)
            // ==========================================
            if (string.IsNullOrWhiteSpace(tName) || tName == "team_" || !HasValidChar(tName.AsSpan()))
            {
                foreach (var p in Utilities.GetPlayers())
                {
                    // ⚠️ 注意這裡！也把 IsBot: false 刪除了！
                    if (p is { IsValid: true, TeamNum: 2 } && !string.IsNullOrWhiteSpace(p.PlayerName))
                    {
                        if (HasValidChar(p.PlayerName.AsSpan()))
                        {
                            tName = $"Team {p.PlayerName}";
                            break;
                        }
                    }
                }
                
                if (string.IsNullOrWhiteSpace(tName) || tName == "team_" || !HasValidChar(tName.AsSpan()))
                {
                    tName = "TERRORISTS";
                }
                
                reverseTeamSides["TERRORIST"].teamName = tName;
            }

            // ==========================================
            // 3. 執行指令 (加上終極雙引號過濾！)
            // ==========================================
            // 確保玩家名字裡的雙引號被清空，防止 CS2 控制台語法錯亂
            ctName = ctName.Replace("\"", "");
            tName = tName.Replace("\"", "");
            
            Server.ExecuteCommand($"mp_teamname_1 \"{ctName}\"");
            Server.ExecuteCommand($"mp_teamname_2 \"{tName}\"");

            // ==========================================
            // 【內嵌區域函數】0 GC 記憶體分配的字元驗證器
            // ==========================================
            bool HasValidChar(ReadOnlySpan<char> nameSpan)
            {
                if (nameSpan.IsEmpty) return false;
                
                // 直接遍歷 Span，完全不產生任何 Array 或 String 的垃圾回收
                foreach (char c in nameSpan)
                {
                    // C# 高效邏輯模式匹配：判斷 ASCII 英數 或 繁簡中文字元區間
                    if (char.IsAsciiLetterOrDigit(c) || c is >= '\u4E00' and <= '\u9FA5')
                    {
                        return true;
                    }
                }
                return false;
            }
        }
        public void GetCvarValues(JObject jsonDataObject)
        {
            try
            {
                if (jsonDataObject["cvars"] is null) return;

                foreach (JProperty cvarData in jsonDataObject["cvars"]!)
                {
                    string cvarName = cvarData.Name;
                    string cvarValue = cvarData.Value.ToString();

                    var cvar = ConVar.Find(cvarName);
                    matchConfig.ChangedCvars[cvarName] = cvarValue;
                    if (cvar != null)
                    {
                        matchConfig.OriginalCvars[cvarName] = GetConvarStringValue(cvar);
                    }
                }

            }
            catch (Exception e)
            {
                Log($"[GetCvarValues FATAL] An error occurred: {e.Message}");
            }
        }

        public void GetOptionalMatchValues(JObject jsonDataObject)
        {
            if(jsonDataObject["map_sides"] is not null)
            {
                matchConfig.MapSides = jsonDataObject["map_sides"]!.ToObject<List<string>>()!;
            }
            if(jsonDataObject["players_per_team"] is not null)
            {
                matchConfig.PlayersPerTeam = jsonDataObject["players_per_team"]!.Value<int>();
            }
            if(jsonDataObject["min_players_to_ready"] is not null)
            {
                matchConfig.MinPlayersToReady = jsonDataObject["min_players_to_ready"]!.Value<int>();
            }
            if(jsonDataObject["min_spectators_to_ready"] is not null)
            {
                matchConfig.MinSpectatorsToReady = jsonDataObject["min_spectators_to_ready"]!.Value<int>();
            }
            if (jsonDataObject["spectators"] is not null && jsonDataObject["spectators"]!["players"] is not null)
            {
                matchConfig.Spectators = jsonDataObject["spectators"]!["players"]!;
                if (matchConfig.Spectators is JArray spectatorsArray && spectatorsArray.Count == 0)
                {
                    matchConfig.Spectators = new JObject();
                }
            }
            if (jsonDataObject["clinch_series"] is not null)
            {
                matchConfig.SeriesCanClinch = bool.Parse(jsonDataObject["clinch_series"]!.ToString());
            }
            if (jsonDataObject["skip_veto"] is not null)
            {
                matchConfig.SkipVeto = bool.Parse(jsonDataObject["skip_veto"]!.ToString());
            }
            if (jsonDataObject["wingman"] is not null)
            {
                matchConfig.Wingman = bool.Parse(jsonDataObject["wingman"]!.ToString());
            }
            if (jsonDataObject["veto_mode"] is not null)
            {
                matchConfig.MapBanOrder = jsonDataObject["veto_mode"]!.ToObject<List<string>>()!;
            }
            
        }

        public void HandleTeamNameChangeCommand(CCSPlayerController? player, string teamName, int teamNum) {
            if (!IsPlayerAdmin(player, "css_team", "@css/config")) {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (matchStarted) {
                ReplyToUserCommand(player, Localizer["matchzy.mm.teamcannotbechanged"]);
                return;
            }
            teamName = RemoveSpecialCharacters(teamName.Trim());
            if (teamName == "") {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!team{teamNum} <name>"]);
            }

            if (teamNum == 1) {
                matchzyTeam1.teamName = teamName;
                teamSides[matchzyTeam1] = "CT";
                reverseTeamSides["CT"] = matchzyTeam1;
                foreach (var coach in matchzyTeam1.coach)
                {
                    coach.Clan = $"[{matchzyTeam1.teamName} COACH]";
                }
            } else if (teamNum == 2) {
                matchzyTeam2.teamName = teamName;
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
                foreach (var coach in matchzyTeam2.coach)
                {
                    coach.Clan = $"[{matchzyTeam2.teamName} COACH]";
                }
            }
            Server.ExecuteCommand($"mp_teamname_{teamNum} {teamName};");
        }

        public void SwapSidesInTeamData(bool swapTeams) {
            (teamSides[matchzyTeam1], teamSides[matchzyTeam2]) = (teamSides[matchzyTeam2], teamSides[matchzyTeam1]);
            (reverseTeamSides["CT"], reverseTeamSides["TERRORIST"]) = (reverseTeamSides["TERRORIST"], reverseTeamSides["CT"]);
        }

        private CsTeam GetPlayerTeam(CCSPlayerController player)
        {
            // 【.NET 10 升級】：現代化屬性模式匹配
            if (player is not { IsValid: true }) return CsTeam.None;
            
            return player.TeamNum switch {
                3 => CsTeam.CounterTerrorist,
                2 => CsTeam.Terrorist,
                1 => CsTeam.Spectator,
                _ => CsTeam.None
            };
        }

        public void EndSeries(string? winnerName, int restartDelay, int t1score, int t2score)
        {
            long matchId = liveMatchId;
            (int team1Score, int team2Score) = (matchzyTeam1.seriesScore, matchzyTeam2.seriesScore);
            if (winnerName is null)
            {
                PrintToAllChat($"{ChatColors.Green}雙 方 最 終 戰 平{ChatColors.Default}");
            }
            else
            {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{winnerName}{ChatColors.Default} 贏 得 了 最 終 勝 利");
            }

            string winnerTeam = (winnerName is null) ? "none" : matchzyTeam1.seriesScore > matchzyTeam2.seriesScore ? "team1" : "team2";

            var seriesResultEvent = new MatchZySeriesResultEvent()
            {
                MatchId = matchId,
                Winner = new Winner(t1score > t2score && reverseTeamSides["CT"] == matchzyTeam1 ? "3" : "2", winnerTeam),
                Team1SeriesScore = team1Score,
                Team2SeriesScore = team2Score,
                TimeUntilRestore = 10,
            };

            Task.Run(async () => {
                await database.SetMatchEndData(matchId, winnerName ?? "Draw", team1Score, team2Score);
                await Task.Delay(2000);
                await SendEventAsync(seriesResultEvent);
            });

            if (resetCvarsOnSeriesEnd) ResetChangedConvars();
            isMatchLive = false;
            AddTimer(restartDelay, () => {
                ResetMatch(false);
            });
        }

        public void HandlePlayoutConfig()
        {
            if (isPlayOutEnabled) {
                Server.ExecuteCommand("mp_overtime_enable 0");
                Server.ExecuteCommand("mp_match_can_clinch false");
            } else {
                var absoluteCfgPath = Path.Join(Server.GameDirectory + "/csgo/cfg", GetGameMode() == 1 ? liveCfgPath : liveWingmanCfgPath);
                string? matchCanClinch = GetConvarValueFromCFGFile(absoluteCfgPath, "mp_match_can_clinch");
                string? overtimeEnabled = GetConvarValueFromCFGFile(absoluteCfgPath, "mp_overtime_enable");
                Server.ExecuteCommand($"mp_match_can_clinch {matchCanClinch ?? "1"}");
                Server.ExecuteCommand($"mp_overtime_enable {overtimeEnabled ?? "1"}");
            }
        }

        public string GetTeamNameFromSide(int teamNum) {
            if (teamNum == 3) return reverseTeamSides["CT"].teamName;
            if (teamNum == 2) return reverseTeamSides["TERRORIST"].teamName;
            return "Unknown";
        } // 結束 GetTeamNameFromSide 函數
    } // 結束 public partial class MatchZy
} // 結束 namespace MatchZy
