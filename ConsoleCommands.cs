using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using System;

namespace MatchZy
{
    public partial class MatchZy
    {
        // ▼▼▼ 新增：針對熱身時間的跨版本安全檢查器 ▼▼▼
        private bool IsWarmupPeriodSafe(CCSGameRules? gameRules)
        {
            if (gameRules == null) return false;
            string val = gameRules.WarmupPeriod.ToString() ?? "";
            return val == "True" || val == "1";
        }
        // ▲▲▲ ▲▲▲ ▲▲▲

        [ConsoleCommand("css_whitelist", "Toggles Whitelisting of players")]
        [ConsoleCommand("css_wl", "Toggles Whitelisting of players")]
        public void OnWLCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_whitelist", "@css/config"))
            {
                isWhitelistRequired = !isWhitelistRequired;
                string WLStatus = isWhitelistRequired ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                if (player is null)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.wl", WLStatus]);
                }
                else
                {
                    PrintToPlayerChat(player, Localizer["matchzy.cc.wl", WLStatus]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_save_nades_as_global", "Toggles Global Lineups for players")]
        [ConsoleCommand("css_globalnades", "Toggles Global Lineups for players")]
        public void OnSaveNadesAsGlobalCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_save_nades_as_global", "@css/config"))
            {
                isSaveNadesAsGlobalEnabled = !isSaveNadesAsGlobalEnabled;
                string GlobalNadesStatus = isSaveNadesAsGlobalEnabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                if (player is null)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.globalnades", GlobalNadesStatus]);
                }
                else
                {
                    PrintToPlayerChat(player, Localizer["matchzy.cc.globalnades", GlobalNadesStatus]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_ready", "Marks the player ready")]
        public void OnPlayerReady(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null) return;
            if (matchzyTeam1.coach.Contains(player) || matchzyTeam2.coach.Contains(player)) return;
            Log($"[!ready command] Sent by: {player.UserId} readyAvailable: {readyAvailable} matchStarted: {matchStarted}");
            if (readyAvailable && !matchStarted)
            {
                if (player.UserId is int userId)
                {
                    if (!playerReadyStatus.ContainsKey(userId))
                    {
                        playerReadyStatus[userId] = false;
                    }
                    if (playerReadyStatus[userId])
                    {
                        PrintToPlayerChat(player, Localizer["matchzy.ready.markedready"]);
                    }
                    else
                    {
                        playerReadyStatus[userId] = true;
                        PrintToPlayerChat(player, Localizer["matchzy.ready.markedready"]);
                    }
                    CheckLiveRequired();
                    HandleClanTags();
                }
            }
        }

        [ConsoleCommand("css_unready", "Marks the player unready")]
        [ConsoleCommand("css_notready", "Marks the player unready")]
        public void OnPlayerUnReady(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null) return;
            if (matchzyTeam1.coach.Contains(player) || matchzyTeam2.coach.Contains(player)) return;
            Log($"[!unready command] {player.UserId}");
            if (readyAvailable && !matchStarted)
            {
                if (player.UserId is int userId)
                {
                    if (!playerReadyStatus.ContainsKey(userId))
                    {
                        playerReadyStatus[userId] = false;
                    }
                    if (!playerReadyStatus[userId])
                    {
                        PrintToPlayerChat(player, Localizer["matchzy.ready.markedunready"]);
                    }
                    else
                    {
                        playerReadyStatus[userId] = false;
                        PrintToPlayerChat(player, Localizer["matchzy.ready.markedunready"]);
                    }
                    HandleClanTags();
                }
            }
        }

        [ConsoleCommand("css_stay", "Stays after knife round")]
        public void OnTeamStay(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null || !isSideSelectionPhase) return;

            Log($"[!stay command] {player.UserId}, TeamNum: {player.TeamNum}, knifeWinner: {knifeWinner}, isSideSelectionPhase: {isSideSelectionPhase}");
            if (player.TeamNum == knifeWinner)
            {
                PrintToAllChat(Localizer["matchzy.knife.decidedtostay", knifeWinnerName]);
                StartLive();
            }
        }

        [ConsoleCommand("css_switch", "Switch after knife round")]
        [ConsoleCommand("css_swap", "Switch after knife round")]
        public void OnTeamSwitch(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null || !isSideSelectionPhase) return;

            Log($"[!switch command] {player.UserId}, TeamNum: {player.TeamNum}, knifeWinner: {knifeWinner}, isSideSelectionPhase: {isSideSelectionPhase}");

            if (player.TeamNum == knifeWinner)
            {
                Server.ExecuteCommand("mp_swapteams;");
                SwapSidesInTeamData(true);
                PrintToAllChat(Localizer["matchzy.knife.decidedtoswitch", knifeWinnerName]);
                StartLive();
            }
        }

        [ConsoleCommand("css_t", "Switches team to Terrorist")]
        public void OnTCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player?.UserId is not int userId) return;
            
            if (isVeto) {
                HandleSideChoice(CsTeam.Terrorist, userId);
                return;
            }

            if (isSideSelectionPhase && player.TeamNum == knifeWinner) {
                if (player.Team == CsTeam.Terrorist) {
                    OnTeamStay(player, command);
                } else {
                    OnTeamSwitch(player, command);
                }
            }

            if (!isPractice) return;
            SideSwitchCommand(player, CsTeam.Terrorist);
        }

        [ConsoleCommand("css_ct", "Switches team to Counter-Terrorist")]
        public void OnCTCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player?.UserId is not int userId) return;
            
            if (isVeto) {
                HandleSideChoice(CsTeam.CounterTerrorist, userId);
                return;
            }

            if (isSideSelectionPhase && player.TeamNum == knifeWinner) {
                if (player.Team == CsTeam.CounterTerrorist) {
                    OnTeamStay(player, command);
                } else {
                    OnTeamSwitch(player, command);
                }
                return;
            }

            if (!isPractice) return;
            SideSwitchCommand(player, CsTeam.CounterTerrorist);
        }

        [ConsoleCommand("css_tech", "Pause the match")]
        public void OnTechCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!isMatchLive) return;

            CCSGameRules? gameRules = null;
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
            {
                if (entity is { GameRules: not null } proxy)
                {
                    gameRules = proxy.GameRules;
                    break;
                }
            }
            
            // ▼ 修復：替換為跨版本檢查器 IsOfficialTacActiveSafe
            if (IsOfficialTacActiveSafe(gameRules) || isPaused || techPauseAutoUnpauseTimer is not null)
            {
                if (player is not null)
                {
                    PrintToPlayerChat(player, $" 已 處 於 暫 停 狀 態，無 法 使 用 {ChatColors.Default}技 術 暫 停");
                }
                return; 
            }

            // ▼ 修復：替換為跨版本檢查器 IsFreezePeriodSafe 與 IsWarmupPeriodSafe
            if (player is not null && !IsFreezePeriodSafe(gameRules) && !IsWarmupPeriodSafe(gameRules))
            {
                PrintToPlayerChat(player, $" 回 合 已 開 始，無 法 使 用 {ChatColors.Default}技 術 暫 停");
                return; 
            }

            TechPause(player, command); 
        }

        [ConsoleCommand("css_pause", "Pause the match")]
        public void OnPauseCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (isPauseCommandForTactical)
            {
                OnTacCommand(player, command);
            }
            else
            {
                PauseMatch(player, command);
            }
        }

        [ConsoleCommand("css_tac", "Starts a tactical timeout for the requested team")]
        public void OnTacCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null) return;

            if (matchStarted && isMatchLive)
            {
                Log($"[.tac command sent via chat] Sent by: {player.UserId}, connectedPlayers: {connectedPlayers}");
                
                if (isPaused)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.matchpaused"]);
                    return;
                }
                
                CCSGameRules? gameRules = null;
                foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
                {
                    if (entity is { GameRules: not null } proxy)
                    {
                        gameRules = proxy.GameRules;
                        break;
                    }
                }

                if (gameRules is null) return;

                if (player.TeamNum == 2)
                {
                    if (gameRules.TerroristTimeOuts > 0)
                    {
                        Server.ExecuteCommand("timeout_terrorist_start");
                    }
                    else
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.cc.nomorepauses"]);
                    }
                }
                else if (player.TeamNum == 3)
                {
                    if (gameRules.CTTimeOuts > 0)
                    {
                        Server.ExecuteCommand("timeout_ct_start");
                    }
                    else
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.cc.nomorepauses"]);
                    }
                }
            }
        }

        [ConsoleCommand("css_fp", "Pause the match an admin")]
        [ConsoleCommand("css_forcepause", "Pause the match as an admin")]
        [ConsoleCommand("sm_pause", "Pause the match as an admin")]
        public void OnForcePauseCommand(CCSPlayerController? player, CommandInfo? command)
        {
            ForcePauseMatch(player, command);
        }

        [ConsoleCommand("css_fup", "Unpause the match an admin")]
        [ConsoleCommand("css_forceunpause", "Unpause the match as an admin")]
        [ConsoleCommand("sm_unpause", "Unpause the match as an admin")]
        public void OnForceUnpauseCommand(CCSPlayerController? player, CommandInfo? command)
        {
            ForceUnpauseMatch(player, command);
            techPauseAutoUnpauseTimer?.Kill();
            techPauseAutoUnpauseTimer = null;
        }

        [ConsoleCommand("css_unpause", "Unpause the match")]
        public void OnUnpauseCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (isMatchLive && isPaused)
            {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin" && player is not null)
                {
                    PrintToPlayerChat(player, Localizer["matchzy.pause.onlyadmincanunpause"]);
                    return;
                }

                string unpauseTeamName = "Admin";
                string remainingUnpauseTeam = "Admin";
                if (player?.TeamNum == 2)
                {
                    unpauseTeamName = reverseTeamSides["TERRORIST"].teamName;
                    remainingUnpauseTeam = reverseTeamSides["CT"].teamName;
                    if (!(bool)unpauseData["t"])
                    {
                        unpauseData["t"] = true;
                    }
                }
                else if (player?.TeamNum == 3)
                {
                    unpauseTeamName = reverseTeamSides["CT"].teamName;
                    remainingUnpauseTeam = reverseTeamSides["TERRORIST"].teamName;
                    if (!(bool)unpauseData["ct"])
                    {
                        unpauseData["ct"] = true;
                    }
                }
                else
                {
                    return;
                }
                if ((bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamsunpausedthematch"]);
                    Server.ExecuteCommand("mp_unpause_match;");
                    isPaused = false;
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;

                    techPauseAutoUnpauseTimer?.Kill();
                    techPauseAutoUnpauseTimer = null;
                }
                else if (unpauseTeamName == "Admin")
                {
                    PrintToAllChat(Localizer["matchzy.pause.adminunpausedthematch"]);
                    Server.ExecuteCommand("mp_unpause_match;");
                    isPaused = false;
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;

                    techPauseAutoUnpauseTimer?.Kill();
                    techPauseAutoUnpauseTimer = null;
                }
                else
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", unpauseTeamName, remainingUnpauseTeam]);
                }
                if (!isPaused && pausedStateTimer is not null)
                {
                    pausedStateTimer.Kill();
                    pausedStateTimer = null;
                }
            }
        }

        [ConsoleCommand("css_skipveto", "Skips the current veto phase")]
        [ConsoleCommand("css_sv", "Skips the current veto phase")]
        public void OnSkipVetoCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_skipveto", "@css/config"))
            {
                if (matchStarted)
                {
                    if (player is null)
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.cc.skipvetomatchstarted"]);
                    }
                    else
                    {
                        PrintToPlayerChat(player, Localizer["matchzy.cc.skipvetomatchstarted"]);
                    }
                }
                else
                {
                    SkipVeto();
                    if (player is null)
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.cc.skipveto"]);
                    }
                    else
                    {
                        PrintToPlayerChat(player, Localizer["matchzy.cc.skipveto"]);
                    }
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_roundknife", "Toggles knife round for the match")]
        [ConsoleCommand("css_rk", "Toggles knife round for the match")]
        public void OnKnifeCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_roundknife", "@css/config"))
            {
                isKnifeRequired = !isKnifeRequired;
                string knifeStatus = isKnifeRequired ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                if (player is null)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.roundknife", knifeStatus]);
                }
                else
                {
                    PrintToPlayerChat(player, Localizer["matchzy.cc.roundknife", knifeStatus]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_readyrequired", "Sets number of ready players required to start the match")]
        public void OnReadyRequiredCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (IsPlayerAdmin(player, "css_readyrequired", "@css/config"))
            {
                if (command.ArgCount >= 2)
                {
                    string commandArg = command.ArgByIndex(1);
                    HandleReadyRequiredCommand(player, commandArg);
                }
                else
                {
                    string minimumReadyRequiredFormatted = (player is null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                    ReplyToUserCommand(player, Localizer["matchzy.cc.minreadyrequired", minimumReadyRequiredFormatted]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_settings", "Shows the current match configuration/settings")]
        public void OnMatchSettingsCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player is null) return;

            if (IsPlayerAdmin(player, "css_settings", "@css/config"))
            {
                string knifeStatus = isKnifeRequired ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                string playoutStatus = isPlayOutEnabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                PrintToPlayerChat(player, Localizer["matchzy.cc.currentsettings"]);
                PrintToPlayerChat(player, Localizer["matchzy.cc.knifestatus", knifeStatus]);
                if (isMatchSetup)
                {
                    PrintToPlayerChat(player, Localizer["matchzy.cc.minreadyplayersperteam", matchConfig.MinPlayersToReady]);
                    PrintToPlayerChat(player, Localizer["matchzy.cc.minreadyspecs", matchConfig.MinSpectatorsToReady]);
                }
                else
                {
                    PrintToPlayerChat(player, Localizer["matchzy.cc.minreadyplayers", minimumReadyRequired]);
                }
                PrintToPlayerChat(player, Localizer["matchzy.cc.playoutstatus", playoutStatus]);
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_endmatch", "Ends and resets the current match")]
        [ConsoleCommand("get5_endmatch", "Ends and resets the current match")]
        [ConsoleCommand("css_forceend", "Ends and resets the current match")]
        public void OnEndMatchCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_endmatch", "@css/config"))
            {
                if (!isPractice)
                {
                    PrintToAllChat(Localizer["matchzy.cc.endmatch"]);
                    ResetMatch();
                    ResetTechPauseCount(); 
                }
                else
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.endmatchispracc"]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_restart", "Restarts the match")]
        [ConsoleCommand("css_rr", "Restarts the match")]
        public void OnRestartMatchCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_restart", "@css/config"))
            {
                if (!isPractice)
                {
                    ResetMatch();
                    ResetTechPauseCount(); 
                }
                else
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.rrispracc"]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_map", "Changes the map using changelevel")]
        public void OnChangeMapCommand(CCSPlayerController? player, CommandInfo command)
        {
            var mapName = command.ArgByIndex(1);
            HandleMapChangeCommand(player, mapName);
            ResetTechPauseCount(); 
        }

        [ConsoleCommand("css_rmap", "Reloads the current map")]
        private void OnMapReloadCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            string currentMapName = Server.MapName;
            if (long.TryParse(currentMapName, out _))
            { 
                Server.ExecuteCommand($"bot_kick");
                Server.ExecuteCommand($"host_workshop_map \"{currentMapName}\"");
            }
            else if (Server.IsMapValid(currentMapName))
            {
                Server.ExecuteCommand($"bot_kick");
                Server.ExecuteCommand($"changelevel \"{currentMapName}\"");
            }
            else
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.invalidmap"]);
            }
            ResetTechPauseCount(); 
        }

        [ConsoleCommand("css_start", "Force starts the match")]
        [ConsoleCommand("css_force", "Force starts the match")]
        [ConsoleCommand("css_forcestart", "Force starts the match")]
        public void OnStartCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_start", "@css/config"))
            {
                if (isPractice)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.startisprac"]);
                    return;
                }
                if (matchStarted)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.startmatchstarted"]);
                }
                else
                {
                    PrintToAllChat(Localizer["matchzy.cc.gamestarted"]);
                    
                    // ▼▼▼ 修正 1：明確標記這是由 .start 觸發的強制洗牌 ▼▼▼
                    if (isShufflePending)
                    {
                        ExecuteShuffleLogicWithReady(null, true); // 帶入 true 表示這是管理員強制的
                    }
                    else
                    {
                        StartMatchCountdown(); 
                    }
                    // ▲▲▲ ▲▲▲ ▲▲▲
                    
                    ResetTechPauseCount(); 
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_asay", "Say as an admin")]
        public void OnAdminSay(CCSPlayerController? player, CommandInfo? command)
        {
            if (command is null) return;
            if (player is null)
            {
                Server.PrintToChatAll($"{adminChatPrefix} {command.ArgString}");
                return;
            }
            if (!IsPlayerAdmin(player, "css_asay", "@css/chat"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            string message = "";
            for (int i = 1; i < command.ArgCount; i++)
            {
                message += command.ArgByIndex(i) + " ";
            }
            Server.PrintToChatAll($"{adminChatPrefix} {message}");
        }

        [ConsoleCommand("reload_admins", "Reload admins of MatchZy")]
        public void OnReloadAdmins(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "reload_admins", "@css/config"))
            {
                LoadAdmins();
                UpdatePlayersMap();
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("css_match", "Starts match mode")]
        public void OnMatchCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_match", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.match"]);
                return;
            }

            StartMatchMode();
        }

        [ConsoleCommand("css_exitprac", "Starts match mode")]
        public void OnExitPracCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (!IsPlayerAdmin(player, "css_exitprac", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                ReplyToUserCommand(player, Localizer["matchzy.cc.exitprac"]);
                return;
            }

            StartMatchMode();
        }

       [ConsoleCommand("css_rcon", "Triggers provided command on the server")]
        public void OnRconCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!IsPlayerAdmin(player, "css_rcon", "@css/rcon"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            Log($"[RCON] {player?.PlayerName ?? "Console"} ({player?.SteamID.ToString() ?? "-"}) executed: {MatchZySecurity.RedactConsoleCommand(command.ArgString)}");
            Server.ExecuteCommand(command.ArgString);
            ReplyToUserCommand(player, Localizer["matchzy.cc.rcon"]);
        }

        [ConsoleCommand("css_help", "Triggers provided command on the server")]
        public void OnHelpCommand(CCSPlayerController? player, CommandInfo? command)
        {
            SendAvailableCommandsMessage(player);
        }

        [ConsoleCommand("css_playout", "Toggles playout (Playing of max rounds)")]
        public void OnPlayoutCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (IsPlayerAdmin(player, "css_playout", "@css/config"))
            {
                isPlayOutEnabled = !isPlayOutEnabled;
                string playoutStatus = isPlayOutEnabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                if (player is null)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.playout", playoutStatus]);
                }
                else
                {
                    PrintToPlayerChat(player, Localizer["matchzy.cc.playout", playoutStatus]);
                }

                HandlePlayoutConfig();
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [ConsoleCommand("version", "Returns server version")]
        public void OnVersionCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (command is null) return;
            string steamInfFilePath = Path.Combine(Server.GameDirectory, "csgo", "steam.inf");

            if (!File.Exists(steamInfFilePath))
            {
                command.ReplyToCommand("Unable to locate steam.inf file!");
            }
            var steamInfContent = File.ReadAllText(steamInfFilePath);

            Regex regex = new(@"ServerVersion=(\d+)");
            Match match = regex.Match(steamInfContent);

            string? serverVersion = match.Success ? match.Groups[1].Value : null;

            command.ReplyToCommand((serverVersion is not null) ? $"Protocol version {serverVersion} [{serverVersion}/{serverVersion}]" : "Unable to get server version");
        }

        public HookResult OnConsoleNoClip(CCSPlayerController? player, CommandInfo? cmd) {
            if (player is not { PawnIsAlive: true } || player.Team is CsTeam.Spectator or CsTeam.None)
                return HookResult.Stop;
                
            bool cheatsEnabled = false;
            var cheatsCvar = ConVar.Find("sv_cheats");
            if (cheatsCvar != null)
            {
                try { cheatsEnabled = cheatsCvar.GetPrimitiveValue<bool>(); }
                catch {
                    try { cheatsEnabled = cheatsCvar.GetPrimitiveValue<int>() == 1; }
                    catch { }
                }
            }

            if (!cheatsEnabled) {
                return HookResult.Stop;
            }

            if (player.PlayerPawn.Value is { } pawn) {
                if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP) {
                    pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                    pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                    Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
                } else {
                    pawn.MoveType = MoveType_t.MOVETYPE_NOCLIP;
                    pawn.ActualMoveType = MoveType_t.MOVETYPE_OBSERVER;
                    Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
                }
            }

            return HookResult.Stop;
        }

        // ==========================================
        // ▼ GG 認輸投票系統 ▼
        // ==========================================
        public bool isGGEnabled = true;       // 預設開啟
        public int ggMinScoreDifference = 6;  // 預設落後 6 分才能投降

        [ConsoleCommand("matchzy_allow_gg", "Enable or disable GG system")]
        public void OnGGConfigCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (command == null || command.ArgCount < 2) return;
            if (player != null && !IsPlayerAdmin(player)) return; 

            string arg = command.ArgByIndex(1).ToLower();
            isGGEnabled = (arg == "true" || arg == "1");
        }

        [ConsoleCommand("matchzy_gg_min_score_difference", "Minimum score difference to allow GG")]
        public void OnGGScoreConfigCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (command == null || command.ArgCount < 2) return;
            if (player != null && !IsPlayerAdmin(player)) return; 

            if (int.TryParse(command.ArgByIndex(1), out int diff))
            {
                ggMinScoreDifference = diff;
            }
        }

        private Dictionary<CsTeam, HashSet<int>> ggVotes = new() { 
            { CsTeam.CounterTerrorist, new HashSet<int>() }, 
            { CsTeam.Terrorist, new HashSet<int>() } 
        };
        
        private readonly Dictionary<CsTeam, CounterStrikeSharp.API.Modules.Timers.Timer?> ggResetTimers = new();
        private readonly Dictionary<CsTeam, int> ggTimerSeconds = new(); 
        
        // ▼▼▼ 新增：紀錄隊伍最後一次投票失敗的時間，用來計算 3 分鐘冷卻 ▼▼▼
        private readonly Dictionary<CsTeam, DateTime> lastFailedGGVote = new();

        // 本地宣告教練檢查，解決跨檔案 private 存取限制
        private bool IsPlayerCoach(CCSPlayerController? player)
        {
            if (player == null) return false;
            try {
                return (matchzyTeam1 != null && matchzyTeam1.coach.Contains(player)) || 
                       (matchzyTeam2 != null && matchzyTeam2.coach.Contains(player));
            } catch {
                return false;
            }
        }

        [ConsoleCommand("css_gg", "Vote to surrender the match")]
        [ConsoleCommand(".gg", "Vote to surrender the match")]
        public void OnGGCommand(CCSPlayerController? player, CommandInfo? command)
        {
            if (player == null || !player.IsValid) return;

            if (!isGGEnabled)
            {
                PrintToPlayerChat(player, $" 本 伺 服 器 尚 未 開 放 {ChatColors.Green}投 降 指 令{ChatColors.Default}");
                return;
            }
            
            if (!isMatchLive)
            {
                PrintToPlayerChat(player, $" 比 賽 尚 未 開 始，無 法 使 用 {ChatColors.Green}投 降 指 令{ChatColors.Default}");
                return;
            }

            if (IsHalfTimePhase())
            {
                PrintToPlayerChat(player, $" 中 場 休 息 期 間，無 法 使 用 {ChatColors.Green}投 降 指 令{ChatColors.Default}");
                return;
            }

            // ▼ 修復：替換為跨版本檢查器 IsOfficialTacActiveSafe
            CCSGameRules? gameRules = null;
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
            {
                if (entity is { GameRules: not null } proxy)
                {
                    gameRules = proxy.GameRules;
                    break;
                }
            }
            
            if (isPaused || IsOfficialTacActiveSafe(gameRules))
            {
                PrintToPlayerChat(player, $" 比 賽 暫 停 期 間，無 法 發 起 投 降 投 票");
                return;
            }
            // ▲▲▲ ▲▲▲ ▲▲▲

            var playerTeam = player.Team;
            if (playerTeam != CsTeam.Terrorist && playerTeam != CsTeam.CounterTerrorist) return;

            // ▼▼▼ 神級防護 2：失敗冷卻時間 (3 分鐘防洗頻) ▼▼▼
            if (lastFailedGGVote.TryGetValue(playerTeam, out DateTime lastFailedTime))
            {
                var timePassed = DateTime.Now - lastFailedTime;
                if (timePassed.TotalMinutes < 3)
                {
                    int remainingSeconds = (int)(180 - timePassed.TotalSeconds);
                    PrintToPlayerChat(player, $" 投 降 投 票 失 敗，請 等 待 {ChatColors.Green}{remainingSeconds}{ChatColors.Default} 秒 後 再 次 發 起");
                    return;
                }
            }
            // ▲▲▲ ▲▲▲ ▲▲▲

            if (IsPlayerCoach(player))
            {
                PrintToPlayerChat(player, $" 教 練 無 法 使 用 {ChatColors.Green}投 降 指 令{ChatColors.Default}");
                return;
            }

            if (isMatchSetup && matchConfig.NumMaps > 1)
            {
                PrintToPlayerChat(player, $" 多 圖 系 列 賽 (BO{matchConfig.NumMaps}) 中 不 允 許 投 降");
                return;
            }

            (int t1score, int t2score) = GetTeamsScore();
            int playerTeamScore = (playerTeam == CsTeam.CounterTerrorist && reverseTeamSides["CT"] == matchzyTeam1) || (playerTeam == CsTeam.Terrorist && reverseTeamSides["TERRORIST"] == matchzyTeam1) ? t1score : t2score;
            int opponentTeamScore = (playerTeamScore == t1score) ? t2score : t1score;
            
            if (opponentTeamScore - playerTeamScore < ggMinScoreDifference)
            {
                PrintToPlayerChat(player, $" 你隊伍落後至少 {ChatColors.Green}{ggMinScoreDifference} 分{ChatColors.Default} 才能發起投降");
                return;
            }

            if (!player.UserId.HasValue) return;
            int userId = player.UserId.Value;

            if (ggVotes[playerTeam].Contains(userId))
            {
                PrintToPlayerChat(player, $" {ChatColors.Orange}你已經投過票了{ChatColors.Default}");
                return;
            }

            ggVotes[playerTeam].Add(userId);
            
            int teamSize = 0;
            foreach (var p in playerData.Values) {
                if (p != null && p.IsValid && p.Team == playerTeam && !IsPlayerCoach(p)) teamSize++;
            }
            
            int votesNeeded = teamSize <= 2 ? Math.Max(1, teamSize) : teamSize - 1;
            int currentVotes = ggVotes[playerTeam].Count;
            
            string teamName = playerTeam == CsTeam.CounterTerrorist ? "反恐小組" : "恐怖分子";

            PrintToAllChat($" {ChatColors.Green}{teamName} 隊伍{ChatColors.Default} 發起了投降投票({ChatColors.Yellow}{currentVotes}{ChatColors.Default}/{votesNeeded})");
            
            if (currentVotes >= votesNeeded)
            {
                ExecuteSurrender(playerTeam, teamName);
            }
            else
            {
                if (ggResetTimers.TryGetValue(playerTeam, out var oldTimer)) oldTimer?.Kill();
                
                ggTimerSeconds[playerTeam] = 60; 
                
                // 第一秒的初始 HUD
                foreach (var p in playerData.Values) {
                    if (p != null && p.IsValid) 
                    {
                        if (p.Team == playerTeam)
                            p.PrintToCenter($"{teamName} 隊伍 ({currentVotes}/{votesNeeded}) 倒數：60秒");
                        else
                            p.PrintToCenter($"對手 {teamName} 發起投降 ({currentVotes}/{votesNeeded}) 倒數：60秒");
                    }
                }

                ggResetTimers[playerTeam] = AddTimer(1.0f, () => {
                    ggTimerSeconds[playerTeam]--; 
                    
                    if (ggTimerSeconds[playerTeam] > 0) 
                    {
                        // 倒數期間的 HUD 刷新
                        foreach (var p in playerData.Values) {
                            if (p != null && p.IsValid) 
                            {
                                if (p.Team == playerTeam)
                                    p.PrintToCenter($"{teamName} 隊伍 ({ggVotes[playerTeam].Count }/ {votesNeeded}) 倒數：{ggTimerSeconds[playerTeam]}秒");
                                else
                                    p.PrintToCenter($"{teamName} 發起投降 ({ggVotes[playerTeam].Count} / {votesNeeded}) 倒數：{ggTimerSeconds[playerTeam]}秒");
                            }
                        }
                    }
                    else 
                    {
                        // 投票超時失敗的處理
                        PrintToAllChat($" {ChatColors.Red}{teamName} 隊伍{ChatColors.Default} 的投降投票已過期");
                        
                        foreach (var p in playerData.Values) {
                            if (p != null && p.IsValid) 
                            {
                                if (p.Team == playerTeam)
                                    p.PrintToCenter($"{teamName} 隊伍的投降投票已過期");
                                else
                                    p.PrintToCenter($"對手 {teamName} 的投降投票已過期");
                            }
                        }
                        
                        ggVotes[playerTeam].Clear();
                        ggResetTimers[playerTeam]?.Kill();
                        ggTimerSeconds[playerTeam] = 0;
                        
                        // ▼ 投票失敗時，記錄失敗時間，該隊伍進入 3 分鐘冷卻期 ▼
                        lastFailedGGVote[playerTeam] = DateTime.Now;
                    }
                }, TimerFlags.REPEAT); 
            }
        }

        // ▼▼▼ 將執行投降邏輯獨立封裝，方便多處調用 (採用極致效能的傳統 foreach 寫法) ▼▼▼
        private void ExecuteSurrender(CsTeam playerTeam, string teamName)
        {
            PrintToAllChat($" {ChatColors.Red}{teamName} 隊伍{ChatColors.Default} 已經投降");
            
            foreach (var p in playerData.Values) {
                if (p != null && p.IsValid) 
                {
                    if (p.Team == playerTeam)
                        p.PrintToCenter($"{teamName} 隊伍 已經投降");
                    else
                        p.PrintToCenter($"對手 {teamName} 隊伍 已經投降");
                }
            }
            
            CCSGameRules? gameRules = null;
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
            {
                if (entity is { GameRules: not null } proxy)
                {
                    gameRules = proxy.GameRules;
                    break;
                }
            }
            
            if (gameRules != null)
            {
                RoundEndReason surrenderReason = playerTeam == CsTeam.CounterTerrorist 
                    ? RoundEndReason.CTsSurrender 
                    : RoundEndReason.TerroristsSurrender;

                float defaultDelay = ConVar.Find("mp_round_restart_delay")?.GetPrimitiveValue<float>() ?? 5.0f;
                gameRules.TerminateRound(defaultDelay, surrenderReason);
            }
            
            if (ggResetTimers.TryGetValue(playerTeam, out var oldTimer)) oldTimer?.Kill();
            ggTimerSeconds[playerTeam] = 0;
            ResetGGVotes();
        }
        // ▲▲▲ ▲▲▲ ▲▲▲

        // ▼▼▼ 神級防護 3：中離斷線自動過關（動態計算門檻） ▼▼▼
        [GameEventHandler]
        public HookResult OnPlayerDisconnectForGG(EventPlayerDisconnect @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return HookResult.Continue;
            
            var team = player.Team;
            if (team != CsTeam.CounterTerrorist && team != CsTeam.Terrorist) return HookResult.Continue;

            // 1. 如果該玩家有參與投票，將他的票從陣列移除
            if (player.UserId.HasValue)
            {
                int uid = player.UserId.Value;
                if (ggVotes[team].Contains(uid))
                {
                    ggVotes[team].Remove(uid);
                }
            }

            // 2. 如果該隊伍正在進行 60 秒倒數，則重新計算門檻
            if (ggTimerSeconds.TryGetValue(team, out int seconds) && seconds > 0)
            {
                // 延遲一幀確保斷線玩家已被系統排除
                Server.NextFrame(() => {
                    int teamSize = 0;
                    foreach (var p in playerData.Values) {
                        if (p != null && p.IsValid && p.Team == team && !IsPlayerCoach(p)) teamSize++;
                    }
                    
                    if (teamSize == 0) return; // 全部退光就不管了

                    int votesNeeded = teamSize <= 2 ? Math.Max(1, teamSize) : teamSize - 1;
                    int currentVotes = ggVotes[team].Count;
                    string teamName = team == CsTeam.CounterTerrorist ? "反恐小組" : "恐怖份子";

                    // 如果因為玩家退服，導致剩下的同意票已經滿足門檻，直接截斷計時並通過投降！
                    if (currentVotes >= votesNeeded)
                    {
                        ExecuteSurrender(team, teamName);
                    }
                });
            }
            
            return HookResult.Continue;
        }
        // ▲▲▲ ▲▲▲ ▲▲▲

        private void ResetGGVotes()
        {
            foreach (var timer in ggResetTimers.Values) timer?.Kill();
            ggResetTimers.Clear();
            ggVotes[CsTeam.CounterTerrorist].Clear();
            ggVotes[CsTeam.Terrorist].Clear();
        }
        // ==========================================
        // ▲ GG 認輸投票系統結束 ▲
        // ==========================================
    }
}
