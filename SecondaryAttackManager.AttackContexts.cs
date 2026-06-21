using System;
using UnityEngine;

namespace CaptainValheim;

internal static partial class SecondaryAttackManager
{
    public static bool BeginProjectileHitContext(Projectile projectile, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        return SecondaryAttackRuntimeFacade.BeginProjectileHitContext(projectile, collider, hitPoint, water, normal);
    }

    public static void EndProjectileHitContext(bool active)
    {
        SecondaryAttackRuntimeFacade.EndProjectileHitContext(active);
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
        context.ProjectileContext = SecondaryAttackRuntimeContext.TryPeekProjectileHitContext(out ProjectileHitContext? projectileContext)
            ? projectileContext
            : null;

        BlockCostAnalysis costAnalysis = AnalyzeBlockCost(player, blocker, hit, blockTimer);
        context.PostResistanceBlockableDamage = costAnalysis.PostResistanceBlockableDamage;
        context.VanillaBlockStaminaCost = costAnalysis.StaminaCost;
        return context;
    }

    internal static void FinalizeBlockAttack(Humanoid humanoid, bool result, HitData hit, BlockAttackContext context)
    {
        if (!result || context.Player == null || context.Blocker == null || context.Definition == null || context.ProjectileContext == null)
        {
            return;
        }

        Projectile projectile = context.ProjectileContext.Projectile;
        if (projectile == null || context.ProjectileContext.Water || !projectile.m_blockable || ShieldRuntimeSystem.IsReflectedProjectile(projectile))
        {
            return;
        }

        float staminaDelta = context.VanillaBlockStaminaCost * (Mathf.Max(0f, context.Definition.ShieldProjectileReflectStaminaFactor) - 1f);
        if (staminaDelta > 0f && !context.Player.HaveStamina(staminaDelta))
        {
            return;
        }

        if (!TryReflectShieldProjectile(context.Player, context.Blocker, context.Definition, context.ProjectileContext))
        {
            return;
        }

        if (staminaDelta > 0f)
        {
            context.Player.UseStamina(staminaDelta);
        }
        else if (staminaDelta < 0f)
        {
            context.Player.AddStamina(-staminaDelta);
        }
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
}
