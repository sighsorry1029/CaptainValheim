using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CaptainValheim;

internal static partial class SecondaryAttackManager
{
    private const float ShieldReflectPendingContextLifetime = 0.5f;
    private const float ShieldReflectPendingHitPointMaxDistanceSqr = 9f;
    private const int ShieldReflectMaxPendingContextsPerPlayer = 8;
    private const float ShieldReflectDebugThrottleSeconds = 0.5f;
    private static readonly ConditionalWeakTable<Player, PendingShieldReflectState> ShieldReflectPendingContexts = new();
    private static readonly Dictionary<string, float> ShieldReflectDebugNextLogTimes = new(StringComparer.Ordinal);

    // Public compatibility bridge for external integrations. Internal runtime code should call SecondaryAttackRuntimeFacade directly.
    public static bool BeginProjectileHitContext(Projectile projectile, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        return SecondaryAttackRuntimeFacade.BeginProjectileHitContext(projectile, collider, hitPoint, water, normal);
    }

    public static void EndProjectileHitContext(bool active)
    {
        SecondaryAttackRuntimeFacade.EndProjectileHitContext(active);
    }

    internal static void TrySendShieldReflectRequest(Projectile projectile, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        if (projectile == null || collider == null || water || !projectile.m_blockable || ShieldRuntimeSystem.IsReflectedProjectile(projectile))
        {
            return;
        }

        if (GetHitCharacter(collider) is not Player targetPlayer)
        {
            return;
        }

        if (!TryGetProjectileZdo(projectile, out ZNetView? projectileNView, out ZDO? projectileZdo))
        {
            LogShieldReflectDebug(
                "request.skip.projectileZdo",
                () => $"request.skip reason=no-projectile-zdo projectile={projectile.name} target={targetPlayer.name} frame={Time.frameCount}");
            return;
        }

        if (!projectileNView!.IsOwner())
        {
            LogShieldReflectDebug(
                "request.skip.projectileOwner",
                () => $"request.skip reason=not-projectile-owner projectile={projectile.name} projectileId={projectileZdo!.m_uid} target={targetPlayer.name} frame={Time.frameCount}");
            return;
        }

        ZDOID projectileId = projectileZdo!.m_uid;
        if (projectileId == ZDOID.None)
        {
            return;
        }

        if (!TryGetCharacterZdo(targetPlayer, out ZNetView? targetNView, out ZDO? targetZdo))
        {
            LogShieldReflectDebug(
                "request.skip.targetZdo",
                () => $"request.skip reason=no-target-zdo projectile={projectile.name} projectileId={projectileId} target={targetPlayer.name} frame={Time.frameCount}");
            return;
        }

        if (targetNView!.IsOwner())
        {
            LogShieldReflectDebug(
                "request.skip.localTarget",
                () => $"request.skip reason=local-target-context projectile={projectile.name} projectileId={projectileId} target={targetPlayer.name} targetOwner={targetZdo!.GetOwner()} frame={Time.frameCount}");
            return;
        }

        CaptainValheimCharacterRpc.SendShieldReflectRequest(targetNView, projectileId, hitPoint, normal, water);
        LogShieldReflectDebug(
            "request.sent",
            () => $"request.sent projectile={projectile.name} projectileId={projectileId} projectileOwner={projectileZdo.GetOwner()} target={targetPlayer.name} targetOwner={targetZdo!.GetOwner()} frame={Time.frameCount}");
    }

    internal static void StorePendingShieldReflectContext(Player player, ZDOID projectileId, Vector3 hitPoint, Vector3 normal, bool water)
    {
        if (player == null || projectileId == ZDOID.None)
        {
            return;
        }

        GameObject? projectileObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(projectileId) : null;
        Projectile? projectile = projectileObject != null ? projectileObject.GetComponent<Projectile>() : null;
        if (projectile == null)
        {
            LogShieldReflectDebug(
                "pending.skip.projectile",
                () => $"pending.skip reason=projectile-not-found projectileId={projectileId} player={player.name} frame={Time.frameCount}");
            return;
        }

        Collider? collider = projectileObject!.GetComponent<Collider>() ??
                             projectileObject.GetComponentInChildren<Collider>() ??
                             player.GetComponent<Collider>();
        if (collider == null)
        {
            LogShieldReflectDebug(
                "pending.skip.collider",
                () => $"pending.skip reason=no-collider projectile={projectile.name} projectileId={projectileId} player={player.name} frame={Time.frameCount}");
            return;
        }

        if (normal.sqrMagnitude <= 0.001f)
        {
            normal = ResolveFallbackProjectileNormal(projectile, player);
        }

        float now = GetNetworkTimeSeconds();
        PendingShieldReflectState state = ShieldReflectPendingContexts.GetValue(player, _ => new PendingShieldReflectState());
        PruneExpiredShieldReflectContexts(state, now);
        while (state.Contexts.Count >= ShieldReflectMaxPendingContextsPerPlayer)
        {
            state.Contexts.RemoveAt(0);
        }

        ProjectileHitContext context = new(projectile, collider, hitPoint, water, normal, attribution: null);
        state.Contexts.Add(new PendingShieldReflectContext(projectileId, context, now + ShieldReflectPendingContextLifetime));
        LogShieldReflectDebug(
            "pending.stored",
            () => $"pending.stored projectile={projectile.name} projectileId={projectileId} player={player.name} count={state.Contexts.Count} frame={Time.frameCount}");
    }

    internal static void LogShieldReflectDebug(string key, Func<string> messageFactory)
    {
        if (CaptainValheimPlugin.Settings.General.ShieldReflectDebugLogging.Value != CaptainValheimPlugin.Toggle.On)
        {
            return;
        }

        float now = Time.time;
        if (ShieldReflectDebugNextLogTimes.TryGetValue(key, out float nextAllowedTime) && now < nextAllowedTime)
        {
            return;
        }

        ShieldReflectDebugNextLogTimes[key] = now + ShieldReflectDebugThrottleSeconds;
        CaptainValheimPlugin.ModLogger.LogInfo("[ShieldReflect] " + messageFactory());
    }

    internal static BlockAttackContext CaptureBlockAttackContext(Humanoid humanoid, HitData hit, ItemDrop.ItemData blocker, float blockTimer)
    {
        BlockAttackContext context = new();
        if (humanoid is not Player player || blocker == null)
        {
            return context;
        }

        if (!TryGetDefinition(blocker, out SecondaryAttackDefinition definition) || !definition.ShieldProjectileReflect)
        {
            return context;
        }

        context.Player = player;
        context.Blocker = blocker;
        context.Definition = definition;
        if (SecondaryAttackRuntimeContext.TryPeekProjectileHitContext(out ProjectileHitContext? projectileContext))
        {
            context.ProjectileContext = projectileContext;
            context.ProjectileContextSource = "local";
        }
        else if (TryConsumePendingShieldReflectContext(player, hit, out ProjectileHitContext? pendingContext))
        {
            context.ProjectileContext = pendingContext;
            context.ProjectileContextSource = "rpc";
        }
        else
        {
            LogShieldReflectDebug(
                "capture.noContext",
                () => $"capture.noContext player={player.name} blocker={blocker.m_dropPrefab?.name ?? blocker.m_shared?.m_name ?? "<unknown>"} frame={Time.frameCount}");
        }

        BlockCostAnalysis costAnalysis = AnalyzeBlockCost(player, blocker, hit, blockTimer);
        context.PostResistanceBlockableDamage = costAnalysis.PostResistanceBlockableDamage;
        context.VanillaBlockStaminaCost = costAnalysis.StaminaCost;
        return context;
    }

    internal static void FinalizeBlockAttack(Humanoid humanoid, bool result, HitData hit, BlockAttackContext context)
    {
        if (!result || context.Player == null || context.Blocker == null || context.Definition == null || !context.ProjectileContext.HasValue)
        {
            if (context.Player != null && context.Definition != null)
            {
                LogShieldReflectDebug(
                    "finalize.skip.context",
                    () => $"finalize.skip reason=no-success-or-context result={result} player={context.Player.name} source={context.ProjectileContextSource} frame={Time.frameCount}");
            }

            return;
        }

        ProjectileHitContext projectileContext = context.ProjectileContext.Value;
        Projectile projectile = projectileContext.Projectile;
        if (projectile == null || projectileContext.Water || !projectile.m_blockable || ShieldRuntimeSystem.IsReflectedProjectile(projectile))
        {
            LogShieldReflectDebug(
                "finalize.skip.projectile",
                () => $"finalize.skip reason=invalid-projectile player={context.Player.name} projectile={(projectile != null ? projectile.name : "<null>")} water={projectileContext.Water} source={context.ProjectileContextSource} frame={Time.frameCount}");
            return;
        }

        float staminaDelta = context.VanillaBlockStaminaCost * (Mathf.Max(0f, context.Definition.ShieldProjectileReflectStaminaFactor) - 1f);
        if (staminaDelta > 0f && !context.Player.HaveStamina(staminaDelta))
        {
            LogShieldReflectDebug(
                "finalize.skip.stamina",
                () => $"finalize.skip reason=stamina player={context.Player.name} projectile={projectile.name} staminaDelta={staminaDelta:0.###} source={context.ProjectileContextSource} frame={Time.frameCount}");
            return;
        }

        if (!TryReflectShieldProjectile(context.Player, context.Blocker, context.Definition, projectileContext))
        {
            LogShieldReflectDebug(
                "finalize.skip.reflect",
                () => $"finalize.skip reason=reflect-failed player={context.Player.name} projectile={projectile.name} source={context.ProjectileContextSource} frame={Time.frameCount}");
            return;
        }

        LogShieldReflectDebug(
            "finalize.success",
            () => $"finalize.success player={context.Player.name} projectile={projectile.name} source={context.ProjectileContextSource} staminaDelta={staminaDelta:0.###} frame={Time.frameCount}");

        if (staminaDelta > 0f)
        {
            context.Player.UseStamina(staminaDelta);
        }
        else if (staminaDelta < 0f)
        {
            context.Player.AddStamina(-staminaDelta);
        }
    }

    private static bool TryGetProjectileZdo(Projectile projectile, out ZNetView? nview, out ZDO? zdo)
    {
        nview = projectile != null ? projectile.GetComponent<ZNetView>() : null;
        zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
        return nview != null && nview.IsValid() && zdo != null;
    }

    private static bool TryConsumePendingShieldReflectContext(Player player, HitData hit, out ProjectileHitContext? context)
    {
        context = null;
        if (player == null)
        {
            return false;
        }

        if (!hit.m_ranged)
        {
            LogShieldReflectDebug(
                "pending.skip.notRanged",
                () => $"pending.skip reason=not-ranged player={player.name} frame={Time.frameCount}");
            return false;
        }

        if (!ShieldReflectPendingContexts.TryGetValue(player, out PendingShieldReflectState? state))
        {
            return false;
        }

        PruneExpiredShieldReflectContexts(state, GetNetworkTimeSeconds());
        if (state.Contexts.Count == 0)
        {
            return false;
        }

        int index = SelectPendingShieldReflectContext(state, hit);
        if (index < 0)
        {
            LogShieldReflectDebug(
                "pending.skip.distance",
                () => $"pending.skip reason=hit-point-distance player={player.name} pending={state.Contexts.Count} frame={Time.frameCount}");
            return false;
        }

        PendingShieldReflectContext pending = state.Contexts[index];
        state.Contexts.RemoveAt(index);
        context = pending.Context;
        LogShieldReflectDebug(
            "pending.consumed",
            () => $"pending.consumed projectile={pending.Context.Projectile.name} projectileId={pending.ProjectileId} player={player.name} remaining={state.Contexts.Count} frame={Time.frameCount}");
        return true;
    }

    private static int SelectPendingShieldReflectContext(PendingShieldReflectState state, HitData hit)
    {
        if (state.Contexts.Count <= 1)
        {
            return IsPendingShieldReflectHitPointClose(state.Contexts[0], hit.m_point) ? 0 : -1;
        }

        Vector3 hitPoint = hit.m_point;
        int bestIndex = 0;
        float bestDistance = float.PositiveInfinity;
        for (int index = 0; index < state.Contexts.Count; index++)
        {
            float distance = (state.Contexts[index].Context.HitPoint - hitPoint).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestDistance <= ShieldReflectPendingHitPointMaxDistanceSqr ? bestIndex : -1;
    }

    private static bool IsPendingShieldReflectHitPointClose(PendingShieldReflectContext pending, Vector3 hitPoint)
    {
        return (pending.Context.HitPoint - hitPoint).sqrMagnitude <= ShieldReflectPendingHitPointMaxDistanceSqr;
    }

    private static void PruneExpiredShieldReflectContexts(PendingShieldReflectState state, float now)
    {
        for (int index = state.Contexts.Count - 1; index >= 0; index--)
        {
            if (state.Contexts[index].ExpiresAt <= now)
            {
                state.Contexts.RemoveAt(index);
            }
        }
    }

    private static Vector3 ResolveFallbackProjectileNormal(Projectile projectile, Player player)
    {
        Vector3 velocity = projectile.GetVelocity();
        if (velocity.sqrMagnitude > 0.001f)
        {
            return -velocity.normalized;
        }

        Vector3 fromProjectile = player.transform.position - projectile.transform.position;
        return fromProjectile.sqrMagnitude > 0.001f ? fromProjectile.normalized : -player.GetLookDir();
    }

    private static BlockCostAnalysis AnalyzeBlockCost(Player player, ItemDrop.ItemData blocker, HitData hit, float blockTimer)
    {
        HitData hitData = hit.Clone();
        bool timedBlock = blocker.m_shared.m_timedBlockBonus > 1f && blockTimer != -1f && blockTimer < 0.25f;
        float skillFactor = player.GetSkillFactor(Skills.SkillType.Blocking);
        float blockPower = blocker.GetBlockPower(skillFactor);
        if (timedBlock)
        {
            blockPower *= blocker.m_shared.m_timedBlockBonus;
            player.GetSEMan().ModifyTimedBlockBonus(ref blockPower);
        }

        if (blocker.m_shared.m_damageModifiers.Count > 0)
        {
            HitData.DamageModifiers modifiers = default;
            modifiers.Apply(blocker.m_shared.m_damageModifiers);
            hitData.ApplyResistance(modifiers, out _);
        }

        HitData.DamageTypes blockedDamage = hitData.m_damage.Clone();
        blockedDamage.ApplyArmor(blockPower);
        float totalBlockableDamage = hitData.GetTotalBlockableDamage();
        float postArmorBlockableDamage = blockedDamage.GetTotalBlockableDamage();
        float blockedAmount = totalBlockableDamage - postArmorBlockableDamage;
        float blockUsageRatio = blockPower > 0f ? Mathf.Clamp01(blockedAmount / blockPower) : 0f;
        float staminaCost = timedBlock ? player.m_perfectBlockStaminaDrain : player.m_blockStaminaDrain * blockUsageRatio;
        return new BlockCostAnalysis(totalBlockableDamage, staminaCost);
    }

    private static bool TryReflectShieldProjectile(
        Player player,
        ItemDrop.ItemData blocker,
        SecondaryAttackDefinition definition,
        ProjectileHitContext projectileContext)
    {
        Projectile originalProjectile = projectileContext.Projectile;
        if (originalProjectile == null)
        {
            return false;
        }

        Vector3 spawnPoint = projectileContext.HitPoint + projectileContext.Normal.normalized * 0.15f;
        GameObject reflectedObject = UnityEngine.Object.Instantiate(
            originalProjectile.gameObject,
            spawnPoint,
            Quaternion.identity);
        Projectile? reflectedProjectile = reflectedObject.GetComponent<Projectile>();
        IProjectile? reflectedProjectileInterface = reflectedObject.GetComponent<IProjectile>();
        if (reflectedProjectile == null || reflectedProjectileInterface == null)
        {
            DestroyProjectileObject(reflectedObject);
            return false;
        }

        Vector3 incomingVelocity = originalProjectile.GetVelocity();
        Vector3 fallbackDirection = incomingVelocity.sqrMagnitude > 0.001f
            ? Vector3.Reflect(incomingVelocity.normalized, projectileContext.Normal)
            : player.GetLookDir();
        Vector3 aimDirection = ShieldRuntimeSystem.ResolvePlayerAimDirectionForReflection(player, spawnPoint, fallbackDirection, maxTravelDistance: 60f);
        if (aimDirection.sqrMagnitude <= 0.001f)
        {
            aimDirection = fallbackDirection.sqrMagnitude > 0.001f ? fallbackDirection.normalized : player.GetLookDir();
        }

        float speed = Mathf.Max(incomingVelocity.magnitude, 10f);
        HitData reflectedHit = BuildReflectedProjectileHitData(player, blocker, definition, originalProjectile);
        reflectedProjectileInterface.Setup(
            player,
            aimDirection.normalized * speed,
            originalProjectile.m_hitNoise,
            reflectedHit,
            blocker,
            ProjectileAccess.GetAmmo(originalProjectile));

        RegisterProjectileAttackAttribution(reflectedProjectile, disableCurrentAttackFallback: true);
        ShieldRuntimeSystem.MarkReflectedProjectile(reflectedProjectile);
        return true;
    }

    private static HitData BuildReflectedProjectileHitData(
        Player player,
        ItemDrop.ItemData blocker,
        SecondaryAttackDefinition definition,
        Projectile originalProjectile)
    {
        HitData hitData = new();
        hitData.m_damage = originalProjectile.m_damage.Clone();
        float powerMultiplier = Mathf.Max(
            0f,
            blocker.GetDeflectionForce() * definition.ShieldProjectileReflectionFactor);
        hitData.ApplyModifier(powerMultiplier);
        hitData.m_pushForce = originalProjectile.m_attackForce * powerMultiplier;
        hitData.m_backstabBonus = originalProjectile.m_backstabBonus;
        hitData.m_statusEffectHash = ProjectileAccess.GetStatusEffectHash(originalProjectile);
        hitData.m_skill = Skills.SkillType.Blocking;
        hitData.m_skillRaiseAmount = 0f;
        hitData.m_blockable = originalProjectile.m_blockable;
        hitData.m_dodgeable = originalProjectile.m_dodgeable;
        hitData.SetAttacker(player);
        return hitData;
    }

    private sealed class PendingShieldReflectState
    {
        public List<PendingShieldReflectContext> Contexts { get; } = new(ShieldReflectMaxPendingContextsPerPlayer);
    }

    private readonly struct PendingShieldReflectContext
    {
        public PendingShieldReflectContext(ZDOID projectileId, ProjectileHitContext context, float expiresAt)
        {
            ProjectileId = projectileId;
            Context = context;
            ExpiresAt = expiresAt;
        }

        public ZDOID ProjectileId { get; }

        public ProjectileHitContext Context { get; }

        public float ExpiresAt { get; }
    }
}
