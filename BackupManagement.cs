using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Timers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MatchZy
{
    public partial class MatchZy
    {
        // ▼ 新增變數：TXT 備份檔名前綴，預設保持為 matchzy
        public string txtBackupPrefix = "matchzy";

        public bool isStopCommandAvailable = true;
        public bool pauseAfterRoundRestore = true;
        public string lastBackupFileName = "";
        public string lastMatchZyBackupFileName = "";

        public bool isRoundRestoring = false;
        public bool isRoundRestorePending = false;
        public string pendingRestoreFileName = "";

        // 【.NET 10】: 使用 Target-typed new
        public Dictionary<string, bool> stopData = new()
        {
            { "ct", false },
            { "t", false }
        };

        public string backupUploadURL = "";
        public string backupUploadHeaderKey = "";
        public string backupUploadHeaderValue = "";

        public void SetupRoundBackupFile()
        {
            // 建立並確認 MatchZyTXT 資料夾
            string backupDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyTXT");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            // 使用絕對路徑強制寫入 MatchZyTXT (將 \ 轉 / 確保引擎讀取正常)
            string backupFilePrefix = Path.Combine(backupDir, $"{txtBackupPrefix}_{liveMatchId}_{matchConfig.CurrentMapNumber}").Replace("\\", "/");
            Server.ExecuteCommand($"mp_backup_round_file \"{backupFilePrefix}\"");
        }

        // ▼ 新增指令：用來讀取 cfg 設定並更改 TXT 前綴名稱
        [ConsoleCommand("matchzy_txt_prefix", "設定 TXT 備份檔案的開頭名稱")]
        public void OnSetTxtPrefixCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null && !IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                return;
            }

            if (command.ArgCount >= 2)
            {
                txtBackupPrefix = command.ArgByIndex(1);
                Log($"[MatchZy] TXT 備份檔名前綴已更改為: {txtBackupPrefix}");
            }
        }

        [ConsoleCommand("css_stop", "Restore the backup of the current round (Both teams need to type .stop to restore the current round)")]
        public void OnStopCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null) return;

            Log($"[!stop command] Sent by: {player.UserId}, TeamNum: {player.TeamNum}, connectedPlayers: {connectedPlayers}");
            if (isStopCommandAvailable && isMatchLive)
            {
                if (IsHalfTimePhase())
                {
                    ReplyToUserCommand(player, Localizer["matchzy.backup.stopduringhalftime"]);
                    return;
                }
                if (IsPostGamePhase())
                {
                    ReplyToUserCommand(player, Localizer["matchzy.backup.stopmatchended"]);
                    return;
                }
                if (IsTacticalTimeoutActive())
                {
                    ReplyToUserCommand(player, Localizer["matchzy.backup.stoptacticaltimeout"]);
                    return;
                }
                if (playerHasTakenDamage && stopCommandNoDamage.Value)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.restore.stopcommandrequiresnodamage"]);
                    return;
                }
                string stopTeamName = "";
                string remainingStopTeam = "";
                if (player.TeamNum == 2)
                {
                    stopTeamName = reverseTeamSides["TERRORIST"].teamName;
                    remainingStopTeam = reverseTeamSides["CT"].teamName;
                    if (!stopData["t"])
                    {
                        stopData["t"] = true;
                    }
                }
                else if (player.TeamNum == 3)
                {
                    stopTeamName = reverseTeamSides["CT"].teamName;
                    remainingStopTeam = reverseTeamSides["TERRORIST"].teamName;
                    if (!stopData["ct"])
                    {
                        stopData["ct"] = true;
                    }
                }
                else
                {
                    return;
                }
                
                if (stopData["t"] && stopData["ct"])
                {
                    if (lastMatchZyBackupFileName != "")
                    {
                        RestoreRoundBackup(player, lastMatchZyBackupFileName);
                    }
                    else
                    {
                        Log($"[OnStopCommand] lastMatchZyBackupFileName not found, unable to restore round!");
                    }
                }
                else
                {
                    PrintToAllChat(Localizer["matchzy.restore.teamwantstorestore", stopTeamName, remainingStopTeam]);
                }
            }
        }

        [ConsoleCommand("css_restore", "Restores the specified round")]
        public void OnRestoreCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (command.ArgCount >= 2)
            {
                string commandArg = command.ArgByIndex(1);
                HandleRestoreCommand(player, commandArg);
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", "!restore <round>"]);
            }
        }

        private void HandleRestoreCommand(CCSPlayerController? player, string commandArg)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (!isMatchLive) return;

            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int roundNumber) && roundNumber >= 0)
                {
                    string round = roundNumber.ToString("D2");
                    string requiredBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                    RestoreRoundBackup(player, requiredBackupFileName);
                }
                else
                {
                    ReplyToUserCommand(player, Localizer["matchzy.backup.restoreinvalidvalue"]);
                }
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", "!restore <round>"]);
            }
        }

        public static string ExtractJsonFileName(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            if (!input.Contains('\\') && !input.Contains('/')) return input;

            int jsonIndex = input.IndexOf(".json", StringComparison.OrdinalIgnoreCase);
            if (jsonIndex != -1)
            {
                int startIndex = input.LastIndexOfAny(['\\', '/'], jsonIndex);

                if (startIndex >= 0)
                {
                    int length = jsonIndex - startIndex + 5;
                    if (length > 0 && startIndex + 1 + length <= input.Length)
                    {
                        return input.Substring(startIndex + 1, length);
                    }
                }
            }
            return string.Empty;
        }

        private void RestoreRoundBackup(CCSPlayerController? player, string fileName)
        {
            if (IsHalfTimePhase())
            {
                ReplyToUserCommand(player, Localizer["matchzy.backup.restoreduringhalftime"]);
                return;
            }
            if (IsPostGamePhase())
            {
                ReplyToUserCommand(player, Localizer["matchzy.backup.restorematchended"]);
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                ReplyToUserCommand(player, Localizer["matchzy.backup.restoretacticaltimeout"]);
                return;
            }
            
            string backupFolder = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");
            string filePath = Path.Combine(backupFolder, fileName);

            if (!File.Exists(filePath))
            {
                ReplyToUserCommand(player, Localizer["matchzy.backup.restoredoesntexist", fileName]);
                Log($"[RestoreRoundBackup FATAL] Required backup data file does not exist! File: {filePath}");
                return;
            }

            var gameRules = GetGameRules();
            bool liveSetupRequired = false;

            gameRules.CTTimeOutActive = gameRules.TerroristTimeOutActive = false;

            Dictionary<string, string> backupData = [];
            try
            {
                using (StreamReader fileReader = File.OpenText(filePath))
                {
                    string jsonContent = fileReader.ReadToEnd();
                    if (!string.IsNullOrEmpty(jsonContent))
                    {
                        JsonSerializerOptions options = new() { AllowTrailingCommas = true };
                        backupData = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? [];
                    }
                }

                isRoundRestoring = true;

                if (backupData.TryGetValue("matchid", out var matchId))
                {
                    liveMatchId = long.Parse(matchId);
                }
                if (backupData.TryGetValue("match_loaded", out var matchLoaded))
                {
                    isMatchSetup = bool.Parse(matchLoaded);
                }
                if (backupData.TryGetValue("match_config", out var matchConfigValue))
                {
                    matchConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<MatchConfig>(matchConfigValue)!;
                    SetupRoundBackupFile();
                }
                if (backupData.TryGetValue("team1", out var team1config))
                {
                    matchzyTeam1 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team1config)!;
                }
                if (backupData.TryGetValue("team2", out var team2config))
                {
                    matchzyTeam2 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team2config)!;
                }
                if (backupData.TryGetValue("team1_side", out var team1Side))
                {
                    if (team1Side == "CT")
                    {
                        teamSides[matchzyTeam1] = "CT";
                        reverseTeamSides["CT"] = matchzyTeam1;
                        teamSides[matchzyTeam2] = "TERRORIST";
                        reverseTeamSides["TERRORIST"] = matchzyTeam2;
                    }
                    else if (team1Side == "TERRORIST")
                    {
                        teamSides[matchzyTeam1] = "TERRORIST";
                        reverseTeamSides["TERRORIST"] = matchzyTeam1;
                        teamSides[matchzyTeam2] = "CT";
                        reverseTeamSides["CT"] = matchzyTeam2;
                    }
                }
                if (backupData.TryGetValue("map_name", out var map_name))
                {
                    if (map_name != Server.MapName)
                    {
                        ChangeMap(map_name, 0);
                        isRoundRestorePending = true;
                        pendingRestoreFileName = fileName;
                        return;
                    }
                }

                if (gameRules.WarmupPeriod)
                {
                    if (!isRoundRestorePending)
                    {
                        isRoundRestorePending = true;
                        pendingRestoreFileName = fileName;
                        PrintToAllChat(Localizer["matchzy.restore.loadedsuccessfully", fileName]);
                        return;
                    }
                    else
                    {
                        liveSetupRequired = true;
                    }
                }
                if (backupData.TryGetValue("TerroristTimeOuts", out var terroristTimeouts))
                {
                    gameRules.TerroristTimeOuts = int.Parse(terroristTimeouts);
                }
                if (backupData.TryGetValue("CTTimeOuts", out var ctTimeouts))
                {
                    gameRules.CTTimeOuts = int.Parse(ctTimeouts);
                }
                
                if (backupData.TryGetValue("valve_backup", out var valveBackup))
                {
                    string tempFileName = fileName.Replace(".json", ".txt");
                    if (backupData.TryGetValue("round", out var roundNumber))
                    {
                        // 讀取時套用 txtBackupPrefix
                        tempFileName = $"{txtBackupPrefix}_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{roundNumber}.txt";
                    }
                    
                    // 確保去 MatchZyTXT 裡面找檔案
                    string backupDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyTXT");
                    if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                    
                    string tempFilePath = Path.Combine(backupDir, tempFileName);

                    if (!File.Exists(tempFilePath))
                    {
                        File.WriteAllText(tempFilePath, valveBackup);
                    }
                    int restoreTimer = liveSetupRequired ? 2 : 0;
                    if (liveSetupRequired)
                    {
                        Log($"Game was in warmup, setting up Live!");
                        SetupLiveFlagsAndCfg();
                    }
                    AddTimer(restoreTimer, () => {
                        // 使用絕對路徑呼叫 CS2 底層引擎去 MatchZyTXT 內讀取
                        string absoluteLoadPath = tempFilePath.Replace("\\", "/");
                        Server.ExecuteCommand($"mp_backup_restore_load_file \"{absoluteLoadPath}\"");
                        StartDemoRecording();
                    });
                }
            }
            catch (Exception e)
            {
                Log($"[RestoreRoundBackup FATAL] An error occurred: {e.Message}");
                return;
            }

            PrintToAllChat(Localizer["matchzy.restore.restoredsuccessfully", fileName]);
            if (pauseAfterRoundRestore)
            {
                Server.ExecuteCommand("mp_pause_match;");
                stopData["ct"] = false;
                stopData["t"] = false;
                isPaused = true;
                unpauseData["pauseTeam"] = "RoundRestore";
                pausedStateTimer ??= AddTimer(chatTimerDelay, SendPausedStateMessage, TimerFlags.REPEAT);
            }
        }

        // ======================================================================================
        // 【核心優化】: 非同步卸載 I/O，主執行緒 0 延遲，徹底消滅伺服器回合結束時的抖動 (Micro-stutter)
        // ======================================================================================
        public void CreateMatchZyRoundDataBackup()
        {
            if (!isMatchLive || isRoundRestoring) return;

            try
            {
                // 1. [主執行緒] 瞬間抓取所有遊戲狀態快照 (Snapshot)
                // 絕對不能在 Task.Run 裡面呼叫 CS2 引擎 API，否則會引發 ThreadStateException
                (int t1score, int t2score) = GetTeamsScore();
                int roundNumber = t1score + t2score;
                string round = roundNumber.ToString("D2");

                long currentMatchId = liveMatchId;
                int currentMapNumber = matchConfig.CurrentMapNumber;
                string currentMapName = Server.MapName;
                
                string t1Name = matchzyTeam1.teamName;
                string t1Flag = matchzyTeam1.teamFlag;
                string t1Tag = matchzyTeam1.teamTag;
                string t1Side = teamSides[matchzyTeam1];
                int t1SeriesScore = matchzyTeam1.seriesScore;

                string t2Name = matchzyTeam2.teamName;
                string t2Flag = matchzyTeam2.teamFlag;
                string t2Tag = matchzyTeam2.teamTag;
                string t2Side = teamSides[matchzyTeam2];
                int t2SeriesScore = matchzyTeam2.seriesScore;

                // 移除危險的 .First()，改用安全的遍歷與模式匹配
                int tTimeOuts = 0, ctTimeOuts = 0;
                foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
                {
                    if (entity is { GameRules: not null } proxy)
                    {
                        tTimeOuts = proxy.GameRules.TerroristTimeOuts;
                        ctTimeOuts = proxy.GameRules.CTTimeOuts;
                        break;
                    }
                }

                bool isLoaded = isMatchSetup;
                string team1ConfigStr = GetTeamConfig("team1");
                string team2ConfigStr = GetTeamConfig("team2");
                string matchConfigStr = GetMatchConfig();

                // 準備寫入路徑
                string backupDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyTXT");
                string lastBackupFileName = $"{txtBackupPrefix}_{currentMatchId}_{currentMapNumber}_round{round}.txt";
                string lastBackupFilePath = Path.Combine(backupDir, lastBackupFileName);

                string jsonBackupDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");
                string matchZyBackupFileName = $"matchzy_{currentMatchId}_{currentMapNumber}_round{round}.json";
                string jsonFilePath = Path.Combine(jsonBackupDir, matchZyBackupFileName);

                string bUploadURL = backupUploadURL;
                string bUploadHeaderKey = backupUploadHeaderKey;
                string bUploadHeaderValue = backupUploadHeaderValue;

                // 2. [背景執行緒] 將耗時的硬碟 I/O 與 JSON 序列化交由底層 ThreadPool 處理
                Task.Run(async () =>
                {
                    try
                    {
                        if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                        if (!Directory.Exists(jsonBackupDir)) Directory.CreateDirectory(jsonBackupDir);

                        // 使用 Async 非同步讀取 CS2 引擎生成的 TXT 檔
                        string valveBackupContent = File.Exists(lastBackupFilePath) 
                            ? await File.ReadAllTextAsync(lastBackupFilePath) 
                            : "";

                        Dictionary<string, string> roundData = new()
                        {
                            { "matchid", currentMatchId.ToString() },
                            { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                            { "map_name", currentMapName },
                            { "mapnumber", currentMapNumber.ToString() },
                            { "round", round },
                            { "team1", team1ConfigStr },
                            { "team2", team2ConfigStr },
                            { "team1_name", t1Name },
                            { "team1_flag", t1Flag },
                            { "team1_tag", t1Tag },
                            { "team1_side", t1Side },
                            { "team2_name", t2Name },
                            { "team2_flag", t2Flag },
                            { "team2_tag", t2Tag },
                            { "team2_side", t2Side },
                            { "team1_score", t1score.ToString() },
                            { "team2_score", t2score.ToString() },
                            { "team1_series_score", t1SeriesScore.ToString() },
                            { "team2_series_score", t2SeriesScore.ToString() },
                            { "TerroristTimeOuts", tTimeOuts.ToString() },
                            { "CTTimeOuts", ctTimeOuts.ToString() },
                            { "match_loaded", isLoaded.ToString() },
                            { "match_config", matchConfigStr },
                            { "valve_backup", valveBackupContent }
                        };

                        // 非同步寫入 JSON，徹底解放主執行緒
                        JsonSerializerOptions options = new() { WriteIndented = true };
                        string defaultJson = JsonSerializer.Serialize(roundData, options);

                        await File.WriteAllTextAsync(jsonFilePath, defaultJson);

                        // 上傳備份
                        await UploadFileAsync(jsonFilePath, bUploadURL, bUploadHeaderKey, bUploadHeaderValue, currentMatchId, currentMapNumber, roundNumber);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CreateMatchZyRoundDataBackup Background Task FATAL] {ex.Message}");
                    }
                });
            }
            catch (Exception e)
            {
                Log($"[CreateMatchZyRoundDataBackup FATAL] Error: {e.Message}");
            }
        }

        public List<string> GetBackups(string matchID)
        {
            string backupDir = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup");

            // 【.NET 10】: 集合表達式 []
            if (!Directory.Exists(backupDir)) return [];

            var directoryInfo = new DirectoryInfo(backupDir);
            var files = directoryInfo.GetFiles();

            var pattern = $"matchzy_{matchID}_";
            List<string> backups = []; // 【.NET 10】: 集合表達式 []

            foreach (var file in files)
            {
                if (file.Name.Contains(pattern))
                {
                    backups.Add(file.FullName);
                }
            }

            backups.Sort((x, y) => string.Compare(y, x, StringComparison.Ordinal));
            return backups;
        }

        public string GetBackupInfo(string filePath)
        {
            string info = "";
            if (!File.Exists(filePath)) return "";

            Dictionary<string, string> backupData = []; // 【.NET 10】: 集合表達式 []
            try
            {
                using (StreamReader fileReader = File.OpenText(filePath))
                {
                    string jsonContent = fileReader.ReadToEnd();
                    if (string.IsNullOrEmpty(jsonContent))
                    {
                        return "";
                    }
                    else
                    {
                        JsonSerializerOptions options = new() { AllowTrailingCommas = true };
                        backupData = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? [];
                    }
                }

                info = $"{filePath.Split("/")[^1]} {backupData["timestamp"]} {backupData["team1_name"]} {backupData["team2_name"]} {backupData["map_name"]} {backupData["team1_score"]} {backupData["team2_score"]}";
            }
            catch (Exception e)
            {
                Log($"[GetBackupInfo FATAL] An error occurred: {e.Message}");
                return "";
            }

            return info;
        }

        public string GetMatchConfig()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(matchConfig);
        }

        public string GetTeamConfig(string team)
        {
            Team teamConfig = team == "team1" ? matchzyTeam1 : matchzyTeam2;
            return Newtonsoft.Json.JsonConvert.SerializeObject(teamConfig);
        }

        [ConsoleCommand("get5_loadbackup", "Restore the backup from the provided file")]
        [ConsoleCommand("matchzy_loadbackup", "Restore the backup from the provided file")]
        [CommandHelper(minArgs: 1, usage: "<backup_file_name>")]
        public void OnLoadBackupCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            
            var fileName = ExtractJsonFileName(command.ArgString);
            RestoreRoundBackup(player, fileName);
        }

        [ConsoleCommand("get5_loadbackup_url", "Loads a backup from the given URL")]
        [ConsoleCommand("matchzy_loadbackup_url", "Loads a backup from the given URL")]
        public void LoadBackupFromURL(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;

            string url = command.ArgByIndex(1);
            string headerName = command.ArgCount > 3 ? command.ArgByIndex(2) : "";
            string headerValue = command.ArgCount > 3 ? command.ArgByIndex(3) : "";

            Log($"[LoadBackupFromURL] Backup Restore request received with URL: {url} headerName: {headerName} and headerValue: {headerValue}");

            if (!IsValidUrl(url))
            {
                ReplyToUserCommand(player, Localizer["matchzy.mm.invalidurl", url]);
                Log($"[LoadBackupFromURL] Invalid URL: {url}. Please provide a valid URL to load the backup!");
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
                    Log($"[LoadBackupFromURL] Received following data: {jsonData}");
                    string fileName = Guid.NewGuid().ToString() + ".json";
                    string filePath = Path.Combine(Server.GameDirectory, "csgo", "MatchZyDataBackup", fileName);

                    string? directoryPath = Path.GetDirectoryName(filePath);
                    if (directoryPath != null && !Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                    File.WriteAllText(filePath, jsonData);
                    Log($"[LoadBackupFromURL] Data saved to: {filePath}");

                    RestoreRoundBackup(player, fileName);
                }
                else
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.httprequestfailed", response.StatusCode]);
                    Log($"[LoadBackupFromURL] HTTP request failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception e)
            {
                Log($"[LoadBackupFromURL - FATAL] An error occured: {e.Message}");
                return;
            }
        }

        [ConsoleCommand("get5_listbackups", "List all the backups for the provided matchid")]
        [ConsoleCommand("matchzy_listbackups", "List all the backups for the provided matchid")]
        public void OnListBackupCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            var matchId = command.ArgCount >= 2 ? command.GetArg(1) : liveMatchId.ToString();
            List<string> backups = GetBackups(matchId);

            if (backups.Count == 0)
            {
                command.ReplyToCommand("Found no backup files matching the provided parameters.");
            }

            foreach (string backup in backups)
            {
                string backupInfo = GetBackupInfo(backup);
                if (backupInfo != "")
                {
                    command.ReplyToCommand(backupInfo);
                }
                else
                {
                    command.ReplyToCommand(backup);
                }
            }
        }
    }
}
