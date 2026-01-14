using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            if (!IsPlayerValid(player)) return HookResult.Continue;
            Log($"[FULL CONNECT] Player ID: {player!.UserId}, Name: {player.PlayerName} has connected!");

           // --- 坑位識別系統修正版：根據隊伍自動判定準備狀態 ---
            if (player.UserId.HasValue)
            {
                int userId = player.UserId.Value;
                playerData[userId] = player;
                connectedPlayers++;
                
                // 1. 如果是在準備階段 (readyAvailable 開啟且比賽還沒開始)
                if (readyAvailable && !matchStarted)
                {
                    // 核心邏輯：如果是觀戰玩家 (TeamNum 1)，自動設為 Ready (true)
                    // 如果是選手 (TeamNum 2 或 3)，設為 Not Ready (false)
                    if (player.TeamNum == 1) 
                    {
                        playerReadyStatus[userId] = true;
                        Log($"[ReadySystem] 觀戰玩家 {player.PlayerName} 自動設為已準備。");
                    } 
                    else 
                    {
                        playerReadyStatus[userId] = false;
                    }
                }
                else
                {
                    // 比賽已經開始或是沒開啟準備系統，默認設為 true
                    playerReadyStatus[userId] = true;
                }
            }

            if (readyAvailable && !matchStarted)
            {
                if (GetRealPlayersCount() == 1)
                {
                    Log($"[FULL CONNECT] First player has connected, starting warmup!");
                    ExecUnpracCommands();
                    AutoStart();
                }
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerConnectFull FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventPlayerDisconnectHandler(EventPlayerDisconnect @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            if (!IsPlayerValid(player)) return HookResult.Continue;
            if (!player!.UserId.HasValue) return HookResult.Continue;
            int userId = player.UserId.Value;

            // 斷開連線時立刻釋放 UserId 佔用的 Slot 資源
            if (playerReadyStatus.ContainsKey(userId))
            {
                playerReadyStatus.Remove(userId);
                connectedPlayers--;
            }
            playerData.Remove(userId);

            if (matchzyTeam1.coach.Contains(player)) matchzyTeam1.coach.Remove(player);
            else if (matchzyTeam2.coach.Contains(player)) matchzyTeam2.coach.Remove(player);
            
            noFlashList.Remove(userId);
            lastGrenadesData.Remove(userId);
            nadeSpecificLastGrenadeData.Remove(userId);

            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerDisconnect FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        try
        {
            // --- 解決隊伍已滿的核心：確保地圖結束時執行大掃除 ---
            HandleMatchEnd(); 
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventCsWinPanelMatch FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventCsWinPanelRoundHandler(EventCsWinPanelRound @event, GameEventInfo info)
    {
        return HookResult.Continue;
    }

    public HookResult EventRoundStartHandler(EventRoundStart @event, GameEventInfo info)
    {
        try
        {
            HandlePostRoundStartEvent(@event);
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventRoundStart FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventRoundFreezeEndHandler(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        try
        {
            if (!matchStarted) return HookResult.Continue;
            HashSet<CCSPlayerController> coaches = GetAllCoaches();

            foreach (var coach in coaches)
            {
                if (!IsPlayerValid(coach)) continue;
                if (coach.PlayerPawn.Value?.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                Position coachPosition = new(coach.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin, coach.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsRotation);
                coach!.PlayerPawn.Value!.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
                AddTimer(1.5f, () =>
                {
                    coach!.PlayerPawn.Value!.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
                    CsTeam oldTeam = GetCoachTeam(coach);
                    coach.ChangeTeam(CsTeam.Spectator);
                    AddTimer(0.01f, () => coach.ChangeTeam(oldTeam));
                });
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventRoundFreezeEnd FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventPlayerGivenC4(EventPlayerGivenC4 @event, GameEventInfo info) {
        try {
            if (!matchStarted) return HookResult.Continue;
            if (@event.Userid == null) return HookResult.Continue;
            var recv = @event.Userid;

            var coaches = reverseTeamSides["TERRORIST"].coach;
            if (coaches.Contains(recv)) {
                TransferCoachBomb(recv);
            }
        } catch (Exception e) {
            Log($"[EventPlayerGivenC4 FATAL] An error occured: {e.Message}");
        }
        return HookResult.Continue;
    }

    public void OnEntitySpawnedHandler(CEntityInstance entity)
    {
        try
        {
            if (!isPractice || entity == null || entity.Entity == null) return;
            if (!Constants.ProjectileTypeMap.ContainsKey(entity.Entity.DesignerName)) return;

            Server.NextFrame(() => {
                CBaseCSGrenadeProjectile projectile = new CBaseCSGrenadeProjectile(entity.Handle);

                if (!projectile.IsValid ||
                    !projectile.Thrower.IsValid ||
                    projectile.Thrower.Value == null ||
                    projectile.Thrower.Value.Controller.Value == null ||
                    projectile.Globalname == "custom"
                ) return;

                CCSPlayerController player = new(projectile.Thrower.Value.Controller.Value.Handle);
                if(!player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid) return;
                int client = player.UserId!.Value;
                
                Vector position = new(projectile.AbsOrigin!.X, projectile.AbsOrigin.Y, projectile.AbsOrigin.Z);
                QAngle angle = new(projectile.AbsRotation!.X, projectile.AbsRotation.Y, projectile.AbsRotation.Z);
                Vector velocity = new(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z);
                string nadeType = Constants.ProjectileTypeMap[entity.Entity.DesignerName];

                if (!lastGrenadesData.ContainsKey(client)) {
                    lastGrenadesData[client] = new();
                }

                if (!nadeSpecificLastGrenadeData.ContainsKey(client))
                {
                    nadeSpecificLastGrenadeData[client] = new(){};
                }

                GrenadeThrownData lastGrenadeThrown = new(
                    position, 
                    angle, 
                    velocity, 
                    player.PlayerPawn.Value.CBodyComponent!.SceneNode!.AbsOrigin, 
                    player.PlayerPawn.Value.EyeAngles,
                    nadeType,
                    DateTime.Now,
                    projectile.ItemIndex
                );

                nadeSpecificLastGrenadeData[client][nadeType] = lastGrenadeThrown;
                lastGrenadesData[client].Add(lastGrenadeThrown);

                if (maxLastGrenadesSavedLimit != 0 && lastGrenadesData[client].Count > maxLastGrenadesSavedLimit)
                {
                    lastGrenadesData[client].RemoveAt(0);
                }

                lastGrenadeThrownTime[(int)projectile.Index] = DateTime.Now;
                if (smokeColorEnabled.Value && nadeType == "smoke")
                {
                    CSmokeGrenadeProjectile smokeProjectile = new(entity.Handle);
                    smokeProjectile.SmokeColor.X = GetPlayerTeammateColor(player).R;
                    smokeProjectile.SmokeColor.Y = GetPlayerTeammateColor(player).G;
                    smokeProjectile.SmokeColor.Z = GetPlayerTeammateColor(player).B;
                }
            });
        }
        catch (Exception e)
        {
            Log($"[OnEntitySpawnedHandler FATAL] An error occurred: {e.Message}");
        }
    }

    public HookResult EventPlayerDeathPreHandler(EventPlayerDeath @event, GameEventInfo info)
    {
        try
        {
            if (!matchStarted) return HookResult.Continue;

            if (@event.Attacker == @event.Userid)
            {
                if (matchzyTeam1.coach.Contains(@event.Attacker!) || matchzyTeam2.coach.Contains(@event.Attacker!))
                {
                    info.DontBroadcast = true;
                }
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerDeathPreHandler FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventSmokegrenadeDetonateHandler(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) 
        {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.smoke", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }

    public HookResult EventFlashbangDetonateHandler(EventFlashbangDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) 
        {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.flash", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }

    public HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) 
        {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.grenade", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }

    public HookResult EventMolotovDetonateHandler(EventMolotovDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Get<int>("entityid"), out var thrownTime)) 
        {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.molotov", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
        }
        return HookResult.Continue;
    }

    public HookResult EventDecoyDetonateHandler(EventDecoyStarted @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) 
        {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.decoy", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }
}
