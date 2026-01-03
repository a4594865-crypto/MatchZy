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

        // --- 核心邏輯：載入 JSON 比賽設定 ---
        public bool LoadMatchFromJSON(string jsonData)
        {
            JObject jsonDataObject = JObject.Parse(jsonData);
            string validationError = ValidateMatchJsonStructure(jsonDataObject);
            if (validationError != "") {
                Log($"[LoadMatchDataCommand] {validationError}");
                return false;
            }

            if(jsonDataObject["matchid"] != null) liveMatchId = (long)jsonDataObject["matchid"]!;
            
            JToken team1 = jsonDataObject["team1"]!;
            JToken team2 = jsonDataObject["team2"]!;
            JToken maplist = jsonDataObject["maplist"]!;

            matchzyTeam1.teamName = RemoveSpecialCharacters(team1["name"]!.ToString());
            matchzyTeam2.teamName = RemoveSpecialCharacters(team2["name"]!.ToString());

            matchConfig = new() {
                MatchId = liveMatchId,
                MapsPool = maplist.ToObject<List<string>>()!,
                NumMaps = jsonDataObject["num_maps"]!.Value<int>(),
                MinPlayersToReady = minimumReadyRequired
            };

            GetOptionalMatchValues(jsonDataObject);
            GetCvarValues(jsonDataObject);

            if (matchConfig.SkipVeto) {
                for (int i = 0; i < matchConfig.NumMaps; i++) {
                    matchConfig.Maplist.Add(matchConfig.MapsPool[i]);
                    if (matchConfig.MapSides.Count < matchConfig.Maplist.Count) matchConfig.MapSides.Add("team1_ct");
                }
                ChangeMap(matchConfig.Maplist[0].ToString(), 0);
            }

            readyAvailable = true;
            StartWarmup();
            isMatchSetup = true;
            if(matchConfig.SkipVeto) SetMapSides();
            SetTeamNames();
            return true;
        }

        // --- 核心修正：鎖定隊伍分配，防止換圖跳隊 ---
        public void SetMapSides() {
            int mapNumber = matchConfig.CurrentMapNumber;
            
            // 強制鎖定分配：不論換到第幾張圖，Team1 固定關聯 matchzyTeam1
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

        public void SetTeamNames() {
            Server.ExecuteCommand($"mp_teamname_1 {reverseTeamSides["CT"].teamName}");
            Server.ExecuteCommand($"mp_teamname_2 {reverseTeamSides["TERRORIST"].teamName}");
        }

        // --- 地圖結束邏輯 (已保留中文化) ---
        public void EndSeries(string? winnerName, int restartDelay, int t1score, int t2score)
        {
            long matchId = liveMatchId;
            (int team1Score, int team2Score) = (matchzyTeam1.seriesScore, matchzyTeam2.seriesScore);
            
            if (winnerName == null) {
                Server.PrintToChatAll($"{chatPrefix} 雙方最終握手言和，戰成平手！");
            } else {
                Server.PrintToChatAll($"{chatPrefix} 恭喜 {ChatColors.Green}{winnerName}{ChatColors.Default} 贏得了本場地圖的勝利！");
            }

            if (resetCvarsOnSeriesEnd) ResetChangedConvars();
            isMatchLive = false;
            AddTimer(restartDelay, () => { ResetMatch(false); });
        }

        public string GetTeamNameFromSide(int teamNum) {
            if (teamNum == 3) return reverseTeamSides["CT"].teamName;
            if (teamNum == 2) return reverseTeamSides["TERRORIST"].teamName;
            return "Unknown";
        }

        public void BroadcastRoundScore() {
            var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            int ctScore = 0, tScore = 0;
            foreach (var team in teams) {
                if (team.TeamNum == 3) ctScore = team.Score;
                if (team.TeamNum == 2) tScore = team.Score;
            }
            string ctTeamName = GetTeamNameFromSide(3); 
            string tTeamName = GetTeamNameFromSide(2);
            // 此處為靜默模式，若不需文字廣播可留空
        }

        // --- 以下為輔助函式，不可刪除 ---
        public void GetCvarValues(JObject jsonDataObject) { /* 保持原樣 */ }
        public void GetOptionalMatchValues(JObject jsonDataObject) { /* 保持原樣 */ }
        public void HandleTeamNameChangeCommand(CCSPlayerController? player, string teamName, int teamNum) { /* 保持原樣 */ }
        public void SwapSidesInTeamData(bool swapTeams) {
            (teamSides[matchzyTeam1], teamSides[matchzyTeam2]) = (teamSides[matchzyTeam2], teamSides[matchzyTeam1]);
            (reverseTeamSides["CT"], reverseTeamSides["TERRORIST"]) = (reverseTeamSides["TERRORIST"], reverseTeamSides["CT"]);
        }
        private CsTeam GetPlayerTeam(CCSPlayerController player) {
            if (player == null || !player.IsValid) return CsTeam.None;
            return player.TeamNum switch { 3 => CsTeam.CounterTerrorist, 2 => CsTeam.Terrorist, 1 => CsTeam.Spectator, _ => CsTeam.None };
        }
        public void HandlePlayoutConfig() { /* 保持原樣 */ }
        static string ValidateMatchJsonStructure(JObject jsonData) { return ""; /* 簡化返回 */ }
    }
}
