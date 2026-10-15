using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;

public partial class MatchZy
{
    // 【0-GC 優化】：靜態共用零向量，避免在教練傳送時重複 new Vector(0, 0, 0)
    private static readonly Vector _zeroVector = new(0, 0, 0);

    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            if (!IsPlayerValid(player)) return HookResult.Continue;
            Log($"[FULL CONNECT] Player ID: {player!.UserId}, Name: {player.PlayerName} has connected!");

           // --- 坑位識別系統修正版：根據隊伍自動判定準備狀態 ---
            // 【修正】：直接使用 is int 完成非空檢查與拆箱
            if (player.UserId is int userId)
            {
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
            
            // 【修正】：直接使用 is not int 攔截 null
            if (player!.UserId is not int userId) return HookResult.Continue;

            // 【0-GC 優化】：單次雜湊查找完成移除與判斷，免除 ContainsKey + Remove 雙重查找
            if (playerReadyStatus.Remove(userId))
            {
                connectedPlayers--;
            }
            playerData.Remove(userId);

            if (!matchzyTeam1.coach.Remove(player))
            {
                matchzyTeam2.coach.Remove(player);
            }
            
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

            // 【0-GC 優化】：若兩隊皆無教練，直接跳出，避免每局凍結結束瞬間呼叫 GetAllCoaches() 配置新 HashSet
            if (matchzyTeam1.coach.Count == 0 && matchzyTeam2.coach.Count == 0) return HookResult.Continue;

            HashSet<CCSPlayerController> coaches = GetAllCoaches();

            foreach (var coach in coaches)
            {
                if (!IsPlayerValid(coach)) continue;
                var pawn = coach.PlayerPawn.Value;
                if (pawn == null || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;
                var sceneNode = pawn.CBodyComponent?.SceneNode;
                if (sceneNode == null) continue;

                Position coachPosition = new(sceneNode.AbsOrigin, sceneNode.AbsRotation);
                Vector elevatedPos = new(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f);
                pawn.Teleport(elevatedPos, coachPosition.PlayerAngle, _zeroVector);

                AddTimer(1.5f, () =>
                {
                    if (!IsPlayerValid(coach) || coach.PlayerPawn.Value == null) return;
                    coach.PlayerPawn.Value.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, _zeroVector);
                    CsTeam oldTeam = GetCoachTeam(coach);
                    coach.ChangeTeam(CsTeam.Spectator);
                    AddTimer(0.01f, () =>
                    {
                        if (IsPlayerValid(coach)) coach.ChangeTeam(oldTeam);
                    });
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
            if (!reverseTeamSides.TryGetValue("TERRORIST", out var tTeam) || tTeam.coach.Count == 0) return HookResult.Continue;

            var recv = @event.Userid;
            if (recv is null) return HookResult.Continue;

            if (tTeam.coach.Contains(recv)) {
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
            if (!isPractice || entity is not { Entity: not null }) return;
            // 【0-GC 優化】：單次 TryGetValue 取得 nadeType，避免在 NextFrame 內重複跨 C++ 轉換 DesignerName 字串與二次查表
            if (!Constants.ProjectileTypeMap.TryGetValue(entity.Entity.DesignerName, out string? nadeType)) return;

            Server.NextFrame(() => {
                if (!entity.IsValid) return;
                CBaseCSGrenadeProjectile projectile = new(entity.Handle);

                if (projectile is not { IsValid: true, Thrower: { IsValid: true, Value.Controller.Value: not null }, Globalname: not "custom" }) return;

                CCSPlayerController player = new(projectile.Thrower.Value.Controller.Value.Handle);
                
                if (player is not { IsValid: true, PlayerPawn: { IsValid: true, Value: not null } }) return;
                if (player.UserId is not int client) return;
                
                Vector position = new(projectile.AbsOrigin!.X, projectile.AbsOrigin.Y, projectile.AbsOrigin.Z);
                QAngle angle = new(projectile.AbsRotation!.X, projectile.AbsRotation.Y, projectile.AbsRotation.Z);
                Vector velocity = new(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z);

                if (!lastGrenadesData.TryGetValue(client, out var playerLastGrenades)) {
                    playerLastGrenades = [];
                    lastGrenadesData[client] = playerLastGrenades;
                }

                if (!nadeSpecificLastGrenadeData.TryGetValue(client, out var playerNadeSpecific))
                {
                    playerNadeSpecific = [];
                    nadeSpecificLastGrenadeData[client] = playerNadeSpecific;
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

                playerNadeSpecific[nadeType] = lastGrenadeThrown;
                playerLastGrenades.Add(lastGrenadeThrown);

                if (maxLastGrenadesSavedLimit != 0 && playerLastGrenades.Count > maxLastGrenadesSavedLimit)
                {
                    playerLastGrenades.RemoveAt(0);
                }

                lastGrenadeThrownTime[(int)projectile.Index] = DateTime.Now;
                if (smokeColorEnabled.Value && nadeType == "smoke")
                {
                    CSmokeGrenadeProjectile smokeProjectile = new(entity.Handle);
                    var teamColor = GetPlayerTeammateColor(player);
                    smokeProjectile.SmokeColor.X = teamColor.R;
                    smokeProjectile.SmokeColor.Y = teamColor.G;
                    smokeProjectile.SmokeColor.Z = teamColor.B;
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
            // 【核心 0-GC 優化】：若場上無任何教練，直接返回，免除每次擊殺時跨 C++ 底層包裝 @event.Attacker 與 @event.Userid 物件！
            if (matchzyTeam1.coach.Count == 0 && matchzyTeam2.coach.Count == 0) return HookResult.Continue;

            CCSPlayerController? attacker = @event.Attacker;
            if (attacker != null && attacker == @event.Userid)
            {
                if (matchzyTeam1.coach.Contains(attacker) || matchzyTeam2.coach.Contains(attacker))
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
