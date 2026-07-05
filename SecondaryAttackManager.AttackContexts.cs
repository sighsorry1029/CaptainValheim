using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CaptainValheim;

internal static partial class SecondaryAttackManager
{
    private const float ShieldReflectPendingContextLifetime = 1f;
    private const float ShieldReflectPendingBlockLifetime = 1f;
    private const float ShieldReflectPendingHitPointMaxDistanceSqr = 25f;
    private const int ShieldReflectMaxPendingContextsPerPlayer = 8;
    private const int ShieldReflectMaxPendingBlocksPerPlayer = 8;
    private const float ShieldReflectDebugThrottleSeconds = 0.5f;
    private static readonly ConditionalWeakTable<Player, PendingShieldReflectState> ShieldReflectPendingContexts = new();
    private static readonly ConditionalWeakTable<Player, PendingShieldReflectBlockState> ShieldReflectPendingBlocks = new();
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

        string payload = ShieldReflectProjectilePayload.FromProjectile(projectile).Serialize();
        CaptainValheimCharacterRpc.SendShieldReflectRequest(targetNView, projectileId, hitPoint, normal, payload);
        LogShieldReflectDebug(
            "request.sent",
            () => $"request.sent projectile={projectile.name} projectileId={projectileId} projectileOwner={projectileZdo.GetOwner()} target={targetPlayer.name} targetOwner={targetZdo!.GetOwner()} frame={Time.frameCount}");
    }

    internal static void StorePendingShieldReflectContext(Player player, ZDOID projectileId, Vector3 hitPoint, Vector3 normal, string payload)
    {
        if (player == null || projectileId == ZDOID.None)
        {
            return;
        }

        GameObject? projectileObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(projectileId) : null;
        Projectile? projectile = projectileObject != null ? projectileObject.GetComponent<Projectile>() : null;
        ShieldReflectProjectilePayload.TryParse(payload, out ShieldReflectProjectilePayload payloadData);
        if (projectile == null)
        {
            if (!payloadData.IsValid)
            {
                LogShieldReflectDebug(
                    "pending.skip.projectile",
                    () => $"pending.skip reason=projectile-not-found projectileId={projectileId} player={player.name} frame={Time.frameCount}");
                return;
            }

            StorePendingShieldReflectContext(
                player,
                projectileId,
                ShieldReflectProjectileContext.FromPayload(payloadData, hitPoint, normal),
                source: "payload");
            return;
        }

        if (normal.sqrMagnitude <= 0.001f)
        {
            normal = ResolveFallbackProjectileNormal(projectile, player);
        }

        StorePendingShieldReflectContext(
            player,
            projectileId,
            ShieldReflectProjectileContext.FromProjectile(projectile, hitPoint, water: false, normal),
            source: payloadData.IsValid ? "rpc" : "rpc-no-payload");
    }

    private static void StorePendingShieldReflectContext(
        Player player,
        ZDOID projectileId,
        ShieldReflectProjectileContext context,
        string source)
    {
        float now = GetNetworkTimeSeconds();
        if (TryConsumePendingShieldReflectBlock(player, projectileId, context, source, now))
        {
            return;
        }

        PendingShieldReflectState state = ShieldReflectPendingContexts.GetValue(player, _ => new PendingShieldReflectState());
        PruneExpiredShieldReflectContexts(state, now);
        while (state.Contexts.Count >= ShieldReflectMaxPendingContextsPerPlayer)
        {
            state.Contexts.RemoveAt(0);
        }

        state.Contexts.Add(new PendingShieldReflectContext(projectileId, context, now + ShieldReflectPendingContextLifetime));
        LogShieldReflectDebug(
            "pending.stored",
            () => $"pending.stored projectile={context.ProjectileName} projectileId={projectileId} source={source} player={player.name} count={state.Contexts.Count} frame={Time.frameCount}");
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
            ProjectileHitContext localProjectileContext = projectileContext.GetValueOrDefault();
            context.ProjectileContext = ShieldReflectProjectileContext.FromProjectileContext(localProjectileContext);
            context.ProjectileContextSource = "local";
        }
        else if (TryConsumePendingShieldReflectContext(player, hit, out ShieldReflectProjectileContext? pendingContext))
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
        if (!result || context.Player == null || context.Blocker == null || context.Definition == null)
        {
            if (context.Player != null && context.Definition != null)
            {
                LogShieldReflectDebug(
                    "finalize.skip.context",
                    () => $"finalize.skip reason=no-success-or-context result={result} player={context.Player.name} source={context.ProjectileContextSource} frame={Time.frameCount}");
            }

            return;
        }

        if (!context.ProjectileContext.HasValue)
        {
            StorePendingShieldReflectBlock(context, hit);
            return;
        }

        ShieldReflectProjectileContext projectileContext = context.ProjectileContext.Value;
        FinalizeShieldReflect(context.Player, context.Blocker, context.Definition, context.VanillaBlockStaminaCost, projectileContext, context.ProjectileContextSource);
    }

    private static void StorePendingShieldReflectBlock(BlockAttackContext context, HitData hit)
    {
        if (context.Player == null || context.Blocker == null || context.Definition == null)
        {
            return;
        }

        float now = GetNetworkTimeSeconds();
        PendingShieldReflectBlockState state = ShieldReflectPendingBlocks.GetValue(context.Player, _ => new PendingShieldReflectBlockState());
        PruneExpiredShieldReflectBlocks(state, now);
        while (state.Blocks.Count >= ShieldReflectMaxPendingBlocksPerPlayer)
        {
            state.Blocks.RemoveAt(0);
        }

        state.Blocks.Add(new PendingShieldReflectBlock(
            context.Blocker,
            context.Definition,
            hit.m_point,
            context.VanillaBlockStaminaCost,
            now + ShieldReflectPendingBlockLifetime));
        LogShieldReflectDebug(
            "block.pending",
            () => $"block.pending reason=no-projectile-context player={context.Player.name} blocker={context.Blocker.m_dropPrefab?.name ?? context.Blocker.m_shared?.m_name ?? "<unknown>"} count={state.Blocks.Count} frame={Time.frameCount}");
    }

    private static bool TryConsumePendingShieldReflectBlock(
        Player player,
        ZDOID projectileId,
        ShieldReflectProjectileContext projectileContext,
        string source,
        float now)
    {
        if (!ShieldReflectPendingBlocks.TryGetValue(player, out PendingShieldReflectBlockState? state))
        {
            return false;
        }

        PruneExpiredShieldReflectBlocks(state, now);
        if (state.Blocks.Count == 0)
        {
            return false;
        }

        int index = SelectPendingShieldReflectBlock(state, projectileContext.HitPoint);
        if (index < 0)
        {
            LogShieldReflectDebug(
                "block.pending.skip.distance",
                () => $"block.pending.skip reason=hit-point-distance projectile={projectileContext.ProjectileName} projectileId={projectileId} player={player.name} pending={state.Blocks.Count} frame={Time.frameCount}");
            return false;
        }

        PendingShieldReflectBlock pending = state.Blocks[index];
        state.Blocks.RemoveAt(index);
        LogShieldReflectDebug(
            "block.pending.consumed",
            () => $"block.pending.consumed projectile={projectileContext.ProjectileName} projectileId={projectileId} source={source} player={player.name} remaining={state.Blocks.Count} frame={Time.frameCount}");

        FinalizeShieldReflect(player, pending.Blocker, pending.Definition, pending.VanillaBlockStaminaCost, projectileContext, source + "-late");
        return true;
    }

    private static void FinalizeShieldReflect(
        Player player,
        ItemDrop.ItemData blocker,
        SecondaryAttackDefinition definition,
        float vanillaBlockStaminaCost,
        ShieldReflectProjectileContext projectileContext,
        string source)
    {
        Projectile? projectile = projectileContext.Projectile;
        if (projectileContext.Water ||
            !projectileContext.Blockable ||
            string.IsNullOrWhiteSpace(projectileContext.ProjectilePrefabName) ||
            (projectile != null && ShieldRuntimeSystem.IsReflectedProjectile(projectile)))
        {
            LogShieldReflectDebug(
                "finalize.skip.projectile",
                () => $"finalize.skip reason=invalid-projectile player={player.name} projectile={projectileContext.ProjectileName} water={projectileContext.Water} source={source} frame={Time.frameCount}");
            return;
        }

        float staminaDelta = vanillaBlockStaminaCost * (Mathf.Max(0f, definition.ShieldProjectileReflectStaminaFactor) - 1f);
        if (staminaDelta > 0f && !player.HaveStamina(staminaDelta))
        {
            LogShieldReflectDebug(
                "finalize.skip.stamina",
                () => $"finalize.skip reason=stamina player={player.name} projectile={projectileContext.ProjectileName} staminaDelta={staminaDelta:0.###} source={source} frame={Time.frameCount}");
            return;
        }

        if (!TryReflectShieldProjectile(player, blocker, definition, projectileContext))
        {
            LogShieldReflectDebug(
                "finalize.skip.reflect",
                () => $"finalize.skip reason=reflect-failed player={player.name} projectile={projectileContext.ProjectileName} source={source} frame={Time.frameCount}");
            return;
        }

        LogShieldReflectDebug(
            "finalize.success",
            () => $"finalize.success player={player.name} projectile={projectileContext.ProjectileName} source={source} staminaDelta={staminaDelta:0.###} frame={Time.frameCount}");

        if (staminaDelta > 0f)
        {
            player.UseStamina(staminaDelta);
        }
        else if (staminaDelta < 0f)
        {
            player.AddStamina(-staminaDelta);
        }
    }

    private static bool TryGetProjectileZdo(Projectile projectile, out ZNetView? nview, out ZDO? zdo)
    {
        nview = projectile != null ? projectile.GetComponent<ZNetView>() : null;
        zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
        return nview != null && nview.IsValid() && zdo != null;
    }

    private static bool TryConsumePendingShieldReflectContext(Player player, HitData hit, out ShieldReflectProjectileContext? context)
    {
        context = null;
        if (player == null)
        {
            return false;
        }

        if (!ShieldReflectPendingContexts.TryGetValue(player, out PendingShieldReflectState? state))
        {
            if (!hit.m_ranged)
            {
                LogShieldReflectDebug(
                    "pending.skip.notRanged",
                    () => $"pending.skip reason=not-ranged player={player.name} frame={Time.frameCount}");
            }

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

        if (!hit.m_ranged)
        {
            LogShieldReflectDebug(
                "pending.consume.notRanged",
                () => $"pending.consume reason=not-ranged-with-context player={player.name} pending={state.Contexts.Count} frame={Time.frameCount}");
        }

        PendingShieldReflectContext pending = state.Contexts[index];
        state.Contexts.RemoveAt(index);
        context = pending.Context;
        LogShieldReflectDebug(
            "pending.consumed",
            () => $"pending.consumed projectile={pending.Context.ProjectileName} projectileId={pending.ProjectileId} player={player.name} remaining={state.Contexts.Count} frame={Time.frameCount}");
        return true;
    }

    private static int SelectPendingShieldReflectContext(PendingShieldReflectState state, HitData hit)
    {
        if (state.Contexts.Count <= 1)
        {
            return 0;
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

    private static int SelectPendingShieldReflectBlock(PendingShieldReflectBlockState state, Vector3 hitPoint)
    {
        if (state.Blocks.Count <= 1)
        {
            return 0;
        }

        int bestIndex = 0;
        float bestDistance = float.PositiveInfinity;
        for (int index = 0; index < state.Blocks.Count; index++)
        {
            float distance = (state.Blocks[index].HitPoint - hitPoint).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestDistance <= ShieldReflectPendingHitPointMaxDistanceSqr ? bestIndex : -1;
    }

    private static void PruneExpiredShieldReflectBlocks(PendingShieldReflectBlockState state, float now)
    {
        for (int index = state.Blocks.Count - 1; index >= 0; index--)
        {
            if (state.Blocks[index].ExpiresAt <= now)
            {
                state.Blocks.RemoveAt(index);
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
        ShieldReflectProjectileContext projectileContext)
    {
        GameObject? sourcePrefab = projectileContext.Projectile != null
            ? projectileContext.Projectile.gameObject
            : ZNetScene.instance?.GetPrefab(projectileContext.ProjectilePrefabName);
        if (sourcePrefab == null)
        {
            return false;
        }

        Vector3 normal = projectileContext.Normal.sqrMagnitude > 0.001f
            ? projectileContext.Normal.normalized
            : -player.GetLookDir();
        Vector3 spawnPoint = projectileContext.HitPoint + normal * 0.15f;
        GameObject reflectedObject = UnityEngine.Object.Instantiate(
            sourcePrefab,
            spawnPoint,
            Quaternion.identity);
        Projectile? reflectedProjectile = reflectedObject.GetComponent<Projectile>();
        IProjectile? reflectedProjectileInterface = reflectedObject.GetComponent<IProjectile>();
        if (reflectedProjectile == null || reflectedProjectileInterface == null)
        {
            DestroyProjectileObject(reflectedObject);
            return false;
        }

        Vector3 incomingVelocity = projectileContext.Velocity;
        Vector3 fallbackDirection = incomingVelocity.sqrMagnitude > 0.001f
            ? Vector3.Reflect(incomingVelocity.normalized, normal)
            : player.GetLookDir();
        Vector3 aimDirection = ShieldRuntimeSystem.ResolvePlayerAimDirectionForReflection(player, spawnPoint, fallbackDirection, maxTravelDistance: 60f);
        if (aimDirection.sqrMagnitude <= 0.001f)
        {
            aimDirection = fallbackDirection.sqrMagnitude > 0.001f ? fallbackDirection.normalized : player.GetLookDir();
        }

        float speed = Mathf.Max(incomingVelocity.magnitude, 10f);
        HitData reflectedHit = BuildReflectedProjectileHitData(player, blocker, definition, projectileContext);
        reflectedProjectileInterface.Setup(
            player,
            aimDirection.normalized * speed,
            projectileContext.HitNoise,
            reflectedHit,
            blocker,
            projectileContext.Ammo);

        RegisterProjectileAttackAttribution(reflectedProjectile, disableCurrentAttackFallback: true);
        ShieldRuntimeSystem.MarkReflectedProjectile(reflectedProjectile);
        return true;
    }

    private static HitData BuildReflectedProjectileHitData(
        Player player,
        ItemDrop.ItemData blocker,
        SecondaryAttackDefinition definition,
        ShieldReflectProjectileContext projectileContext)
    {
        HitData hitData = new();
        hitData.m_damage = projectileContext.Damage.Clone();
        float powerMultiplier = Mathf.Max(
            0f,
            blocker.GetDeflectionForce() * definition.ShieldProjectileReflectionFactor);
        hitData.ApplyModifier(powerMultiplier);
        hitData.m_pushForce = projectileContext.AttackForce * powerMultiplier;
        hitData.m_backstabBonus = projectileContext.BackstabBonus;
        hitData.m_statusEffectHash = projectileContext.StatusEffectHash;
        hitData.m_skill = Skills.SkillType.Blocking;
        hitData.m_skillRaiseAmount = 0f;
        hitData.m_blockable = projectileContext.Blockable;
        hitData.m_dodgeable = projectileContext.Dodgeable;
        hitData.SetAttacker(player);
        return hitData;
    }

    internal readonly struct ShieldReflectProjectileContext
    {
        private ShieldReflectProjectileContext(
            Projectile? projectile,
            string projectilePrefabName,
            Vector3 hitPoint,
            bool water,
            Vector3 normal,
            Vector3 velocity,
            float hitNoise,
            HitData.DamageTypes damage,
            float attackForce,
            float backstabBonus,
            bool blockable,
            bool dodgeable,
            int statusEffectHash,
            ItemDrop.ItemData? ammo)
        {
            Projectile = projectile;
            ProjectilePrefabName = projectilePrefabName;
            HitPoint = hitPoint;
            Water = water;
            Normal = normal;
            Velocity = velocity;
            HitNoise = hitNoise;
            Damage = damage;
            AttackForce = attackForce;
            BackstabBonus = backstabBonus;
            Blockable = blockable;
            Dodgeable = dodgeable;
            StatusEffectHash = statusEffectHash;
            Ammo = ammo;
        }

        public Projectile? Projectile { get; }

        public string ProjectilePrefabName { get; }

        public string ProjectileName => Projectile != null ? Projectile.name : ProjectilePrefabName;

        public Vector3 HitPoint { get; }

        public bool Water { get; }

        public Vector3 Normal { get; }

        public Vector3 Velocity { get; }

        public float HitNoise { get; }

        public HitData.DamageTypes Damage { get; }

        public float AttackForce { get; }

        public float BackstabBonus { get; }

        public bool Blockable { get; }

        public bool Dodgeable { get; }

        public int StatusEffectHash { get; }

        public ItemDrop.ItemData? Ammo { get; }

        public static ShieldReflectProjectileContext FromProjectileContext(ProjectileHitContext context)
        {
            return FromProjectile(context.Projectile, context.HitPoint, context.Water, context.Normal);
        }

        public static ShieldReflectProjectileContext FromProjectile(Projectile projectile, Vector3 hitPoint, bool water, Vector3 normal)
        {
            return new ShieldReflectProjectileContext(
                projectile,
                ResolveProjectilePrefabName(projectile),
                hitPoint,
                water,
                normal,
                ProjectileAccess.GetVelocity(projectile),
                projectile.m_hitNoise,
                projectile.m_damage.Clone(),
                projectile.m_attackForce,
                projectile.m_backstabBonus,
                projectile.m_blockable,
                projectile.m_dodgeable,
                ProjectileAccess.GetStatusEffectHash(projectile),
                ProjectileAccess.GetAmmo(projectile));
        }

        public static ShieldReflectProjectileContext FromPayload(ShieldReflectProjectilePayload payload, Vector3 hitPoint, Vector3 normal)
        {
            return new ShieldReflectProjectileContext(
                projectile: null,
                payload.ProjectilePrefabName,
                hitPoint,
                water: false,
                normal,
                payload.Velocity,
                payload.HitNoise,
                payload.Damage.Clone(),
                payload.AttackForce,
                payload.BackstabBonus,
                payload.Blockable,
                payload.Dodgeable,
                payload.StatusEffectHash,
                ammo: null);
        }
    }

    internal readonly struct ShieldReflectProjectilePayload
    {
        private const char Separator = '|';
        private const int Version = 1;
        private const int FieldCount = 22;

        private ShieldReflectProjectilePayload(
            string projectilePrefabName,
            Vector3 velocity,
            float hitNoise,
            HitData.DamageTypes damage,
            float attackForce,
            float backstabBonus,
            bool blockable,
            bool dodgeable,
            int statusEffectHash)
        {
            ProjectilePrefabName = projectilePrefabName;
            Velocity = velocity;
            HitNoise = hitNoise;
            Damage = damage;
            AttackForce = attackForce;
            BackstabBonus = backstabBonus;
            Blockable = blockable;
            Dodgeable = dodgeable;
            StatusEffectHash = statusEffectHash;
            IsValid = !string.IsNullOrWhiteSpace(projectilePrefabName);
        }

        public bool IsValid { get; }

        public string ProjectilePrefabName { get; }

        public Vector3 Velocity { get; }

        public float HitNoise { get; }

        public HitData.DamageTypes Damage { get; }

        public float AttackForce { get; }

        public float BackstabBonus { get; }

        public bool Blockable { get; }

        public bool Dodgeable { get; }

        public int StatusEffectHash { get; }

        public static ShieldReflectProjectilePayload FromProjectile(Projectile projectile)
        {
            return new ShieldReflectProjectilePayload(
                ResolveProjectilePrefabName(projectile),
                ProjectileAccess.GetVelocity(projectile),
                projectile.m_hitNoise,
                projectile.m_damage.Clone(),
                projectile.m_attackForce,
                projectile.m_backstabBonus,
                projectile.m_blockable,
                projectile.m_dodgeable,
                ProjectileAccess.GetStatusEffectHash(projectile));
        }

        public string Serialize()
        {
            if (!IsValid)
            {
                return string.Empty;
            }

            return string.Join(
                Separator.ToString(),
                Version.ToString(CultureInfo.InvariantCulture),
                ProjectilePrefabName,
                Format(Velocity.x),
                Format(Velocity.y),
                Format(Velocity.z),
                Format(HitNoise),
                Format(Damage.m_damage),
                Format(Damage.m_blunt),
                Format(Damage.m_slash),
                Format(Damage.m_pierce),
                Format(Damage.m_chop),
                Format(Damage.m_pickaxe),
                Format(Damage.m_fire),
                Format(Damage.m_frost),
                Format(Damage.m_lightning),
                Format(Damage.m_poison),
                Format(Damage.m_spirit),
                Format(AttackForce),
                Format(BackstabBonus),
                StatusEffectHash.ToString(CultureInfo.InvariantCulture),
                Blockable ? "1" : "0",
                Dodgeable ? "1" : "0");
        }

        public static bool TryParse(string payload, out ShieldReflectProjectilePayload parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            string[] fields = payload.Split(Separator);
            if (fields.Length != FieldCount ||
                !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) ||
                version != Version ||
                string.IsNullOrWhiteSpace(fields[1]))
            {
                return false;
            }

            if (!TryParseFloat(fields[2], out float velocityX) ||
                !TryParseFloat(fields[3], out float velocityY) ||
                !TryParseFloat(fields[4], out float velocityZ) ||
                !TryParseFloat(fields[5], out float hitNoise) ||
                !TryParseFloat(fields[6], out float damage) ||
                !TryParseFloat(fields[7], out float blunt) ||
                !TryParseFloat(fields[8], out float slash) ||
                !TryParseFloat(fields[9], out float pierce) ||
                !TryParseFloat(fields[10], out float chop) ||
                !TryParseFloat(fields[11], out float pickaxe) ||
                !TryParseFloat(fields[12], out float fire) ||
                !TryParseFloat(fields[13], out float frost) ||
                !TryParseFloat(fields[14], out float lightning) ||
                !TryParseFloat(fields[15], out float poison) ||
                !TryParseFloat(fields[16], out float spirit) ||
                !TryParseFloat(fields[17], out float attackForce) ||
                !TryParseFloat(fields[18], out float backstabBonus) ||
                !int.TryParse(fields[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out int statusEffectHash))
            {
                return false;
            }

            parsed = new ShieldReflectProjectilePayload(
                fields[1],
                new Vector3(velocityX, velocityY, velocityZ),
                hitNoise,
                new HitData.DamageTypes
                {
                    m_damage = damage,
                    m_blunt = blunt,
                    m_slash = slash,
                    m_pierce = pierce,
                    m_chop = chop,
                    m_pickaxe = pickaxe,
                    m_fire = fire,
                    m_frost = frost,
                    m_lightning = lightning,
                    m_poison = poison,
                    m_spirit = spirit
                },
                attackForce,
                backstabBonus,
                fields[20] == "1",
                fields[21] == "1",
                statusEffectHash);
            return parsed.IsValid;
        }

        private static string Format(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }
    }

    private static string ResolveProjectilePrefabName(Projectile projectile)
    {
        return projectile != null ? Utils.GetPrefabName(projectile.gameObject) : string.Empty;
    }

    private sealed class PendingShieldReflectState
    {
        public List<PendingShieldReflectContext> Contexts { get; } = new(ShieldReflectMaxPendingContextsPerPlayer);
    }

    private sealed class PendingShieldReflectBlockState
    {
        public List<PendingShieldReflectBlock> Blocks { get; } = new(ShieldReflectMaxPendingBlocksPerPlayer);
    }

    private readonly struct PendingShieldReflectContext
    {
        public PendingShieldReflectContext(ZDOID projectileId, ShieldReflectProjectileContext context, float expiresAt)
        {
            ProjectileId = projectileId;
            Context = context;
            ExpiresAt = expiresAt;
        }

        public ZDOID ProjectileId { get; }

        public ShieldReflectProjectileContext Context { get; }

        public float ExpiresAt { get; }
    }

    private readonly struct PendingShieldReflectBlock
    {
        public PendingShieldReflectBlock(
            ItemDrop.ItemData blocker,
            SecondaryAttackDefinition definition,
            Vector3 hitPoint,
            float vanillaBlockStaminaCost,
            float expiresAt)
        {
            Blocker = blocker;
            Definition = definition;
            HitPoint = hitPoint;
            VanillaBlockStaminaCost = vanillaBlockStaminaCost;
            ExpiresAt = expiresAt;
        }

        public ItemDrop.ItemData Blocker { get; }

        public SecondaryAttackDefinition Definition { get; }

        public Vector3 HitPoint { get; }

        public float VanillaBlockStaminaCost { get; }

        public float ExpiresAt { get; }
    }
}
