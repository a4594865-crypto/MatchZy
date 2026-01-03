using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes.Registration; 

namespace MatchZy;

public partial class MatchZy
{
    // --- 核心修正：攔截換隊事件，.r 開始後(刀局與比賽)皆生效 ---
    [GameEventHandler]
    public HookResult EventPlayerTeamHandler(EventPlayerTeam @event, GameEventInfo info)
    {
        // 判定條件：只要比賽已準備就緒 (matchStarted) 或處於刀局中 (isKnifeRequired)
        if (matchStarted || isKnifeRequired)
        {
            CCSPlayerController? player = @event.Userid;
            
            // 關鍵保險：!@event.Silent 確保只有「玩家手動按 M」被攔截
            // 刀局贏了之後插件自動幫玩家換邊是 Silent 模式，所以不會被擋住
            if (IsPlayerValid(player) && !@event.Silent)
            {
                // 回覆玩家訊息並停止動作
                ReplyToUserCommand(player, "刀局或比賽期間禁止自行更換隊伍！"); 
                return HookResult.Stop; 
            }
        }
        return HookResult.Continue;
    }

    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;
            Log($"[FULL CONNECT] Player ID: {player!.UserId}, Name: {player.PlayerName} has connected!");

            if (player.UserId.HasValue)
            {
                int userId = player.UserId.Value;
                playerData[userId] = player;
                connectedPlayers++;
                
                if (readyAvailable && !matchStarted) playerReadyStatus[userId] = false;
                else playerReadyStatus[userId] = true;
            }

            if (readyAvailable && !matchStarted && GetRealPlayersCount() == 1)
            {
                Log($"[FULL CONNECT] First player has connected, starting warmup!");
                ExecUnpracCommands();
                AutoStart();
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
            if (!IsPlayerValid(player) || !player!.UserId.HasValue) return HookResult.Continue;
            int userId = player.UserId.Value;

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
            if (!matchStarted || @event.Userid == null) return HookResult.Continue;
            var recv = @event.Userid;
            var coaches = reverseTeamSides["TERRORIST"].coach;
            if (coaches.Contains(recv)) TransferCoachBomb(recv);
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
                if (!projectile.IsValid || !projectile.Thrower.IsValid || projectile.Thrower.Value == null || projectile.Thrower.Value.Controller.Value == null || projectile.Globalname == "custom") return;
                CCSPlayerController player = new(projectile.Thrower.Value.Controller.Value.Handle);
                if(!player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid) return;
                int client = player.UserId!.Value;
                Vector position = new(projectile.AbsOrigin!.X, projectile.AbsOrigin.Y, projectile.AbsOrigin.Z);
                QAngle angle = new(projectile.AbsRotation!.X, projectile.AbsRotation.Y, projectile.AbsRotation.Z);
                Vector velocity = new(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z);
                string nadeType = Constants.ProjectileTypeMap[entity.Entity.DesignerName];
                if (!lastGrenadesData.ContainsKey(client)) lastGrenadesData[client] = new();
                if (!nadeSpecificLastGrenadeData.ContainsKey(client)) nadeSpecificLastGrenadeData[client] = new(){};
                GrenadeThrownData lastGrenadeThrown = new(position, angle, velocity, player.PlayerPawn.Value.CBodyComponent!.SceneNode!.AbsOrigin, player.PlayerPawn.Value.EyeAngles, nadeType, DateTime.Now, projectile.ItemIndex);
                nadeSpecificLastGrenadeData[client][nadeType] = lastGrenadeThrown;
                lastGrenadesData[client].Add(lastGrenadeThrown);
                if (maxLastGrenadesSavedLimit != 0 && lastGrenadesData[client].Count > maxLastGrenadesSavedLimit) lastGrenadesData[client].RemoveAt(0);
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
        catch (Exception e) { Log($"[OnEntitySpawnedHandler FATAL] An error occurred: {e.Message}"); }
    }

    public HookResult EventPlayerDeathPreHandler(EventPlayerDeath @event, GameEventInfo info)
    {
        try
        {
            if (!matchStarted) return HookResult.Continue;
            if (@event.Attacker == @event.Userid)
            {
                if (matchzyTeam1.coach.Contains(@event.Attacker!) || matchzyTeam2.coach.Contains(@event.Attacker!)) info.DontBroadcast = true;
            }
            return HookResult.Continue;
        }
        catch (Exception e) { Log($"[EventPlayerDeathPreHandler FATAL] An error occurred: {e.Message}"); return HookResult.Continue; }
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
