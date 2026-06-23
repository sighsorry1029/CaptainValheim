using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using ProjectileLaunchData = CaptainValheim.ProjectileRuntimeSystem.ProjectileLaunchData;

namespace CaptainValheim;

internal static partial class ShieldRuntimeSystem
{
    private const float ShieldChargeStartVfxForwardOffset = 0f;
    private const float ShieldChargeStartVfxYOffset = 0.5f;
    private static readonly List<Renderer> ShieldProjectileRendererBuffer = new();

    private static void StartShieldThrow(Attack attack, SecondaryAttackDefinition definition)
    {
        ShieldSpecialSecondaryBehavior? behavior = definition.Behavior as ShieldSpecialSecondaryBehavior;
        if (behavior == null || !behavior.HasShieldThrow)
        {
            return;
        }

        if (!TryResolveShieldThrowTemplate(out ProjectileLaunchData launchData))
        {
            if (SecondaryAttackManager.TryMarkCompatibilityWarningReported("shield_throw_template_missing"))
            {
                CaptainValheimPlugin.ModLogger.LogWarning("Shield throw requires a projectile template, but no compatible projectile attack could be found in ObjectDB.");
            }

            return;
        }

        attack.GetProjectileSpawnPoint(out Vector3 spawnPoint, out Vector3 aimDirection);
        float blockPower = GetShieldBlockPower(attack);
        float deflectionForce = attack.m_weapon.GetDeflectionForce();
        float damage = Mathf.Max(0f, blockPower * behavior.ShieldThrowDamageFactor);
        float pushForce = Mathf.Max(0f, deflectionForce * behavior.ShieldThrowPushFactor);
        float searchRadius = CalculateShieldThrowSearchRadius(deflectionForce, behavior.ShieldThrowRadiusFactor);
        float ttl = CalculateShieldThrowTtl(deflectionForce, behavior.ShieldThrowTtlFactor);
        float speed = CalculateShieldThrowProjectileSpeed(launchData);
        float flightDistance = speed * ttl;
        int remainingChains = Mathf.Max(0, behavior.ShieldThrowTargets - 1);
        aimDirection = ResolveShieldThrowAimDirection(attack, spawnPoint, aimDirection, flightDistance);
        if (!TryConsumeShieldForThrow(attack, out ItemDrop.ItemData thrownShield))
        {
            if (SecondaryAttackManager.TryMarkCompatibilityWarningReported("shield_throw_consume_failed"))
            {
                CaptainValheimPlugin.ModLogger.LogWarning("Failed to consume the equipped shield for a shield throw. The special attack was cancelled.");
            }

            return;
        }

        PlayShieldThrowChargeStartSfx(attack);
        if (TrySpawnShieldProjectile(
            attack,
            launchData,
            thrownShield,
            spawnPoint,
            aimDirection.normalized,
            damage,
            pushForce,
            searchRadius,
            flightDistance,
            ttl,
            speed,
            remainingChains,
            behavior.ShieldThrowDamageDecay,
            new HashSet<Character>()))
        {
            return;
        }

        DropThrownShield(thrownShield, spawnPoint, Quaternion.LookRotation(aimDirection));
    }

    private static void StartShieldCharge(Attack attack, SecondaryAttackDefinition definition)
    {
        ShieldSpecialSecondaryBehavior? behavior = definition.Behavior as ShieldSpecialSecondaryBehavior;
        if (behavior == null || !behavior.HasShieldCharge)
        {
            return;
        }

        float deflectionForce = attack.m_weapon.GetDeflectionForce();
        float distance = Mathf.Max(0f, behavior.ShieldChargeDistance);
        float damage = Mathf.Max(0f, GetShieldBlockPower(attack) * behavior.ShieldChargeDamageFactor);
        float pushForce = Mathf.Max(0f, deflectionForce * behavior.ShieldChargePushFactor);
        float hitRadius = CalculateShieldChargeHitRadius(deflectionForce, behavior.ShieldChargeHitRadiusFactor);
        float cooldown = CalculateShieldChargeCooldown(attack.m_character, behavior);
        float staminaCost = attack.GetAttackStamina();
        if (staminaCost > 0f)
        {
            if (!attack.m_character.HaveStamina(staminaCost))
            {
                attack.Stop();
                return;
            }

            attack.m_character.UseStamina(staminaCost);
            attack.m_attackStamina = 0f;
        }

        PlayShieldThrowChargeStartSfx(attack);
        PlayShieldChargeStartVfx(attack);
        GameObject controllerObject = new("CaptainValheim_ShieldCharge");
        ShieldChargeController controller = controllerObject.AddComponent<ShieldChargeController>();
        controller.Initialize(
            attack,
            distance,
            damage,
            pushForce,
            hitRadius,
            behavior.ShieldChargeSpeed,
            cooldown,
            1f,
            0f);
    }

    private static float GetShieldBlockPower(Attack attack)
    {
        return attack.m_weapon.GetBlockPower(attack.m_character.GetSkillFactor(Skills.SkillType.Blocking));
    }

    private static Vector3 ResolveShieldThrowAimDirection(Attack attack, Vector3 spawnPoint, Vector3 fallbackAimDirection, float maxTravelDistance)
    {
        if (attack.m_baseAI != null)
        {
            Character? target = attack.m_baseAI.GetTargetCreature();
            if (target != null)
            {
                Vector3 targetDirection = target.GetCenterPoint() - spawnPoint;
                if (targetDirection.sqrMagnitude > 0.001f)
                {
                    return targetDirection.normalized;
                }
            }
        }

        if (attack.m_character is Player player)
        {
            return ResolvePlayerAimDirection(player, spawnPoint, fallbackAimDirection, maxTravelDistance);
        }

        if (fallbackAimDirection.sqrMagnitude > 0.001f)
        {
            return fallbackAimDirection.normalized;
        }

        return SecondaryAttackManager.GetSentinelForward(attack.m_character);
    }

    internal static Vector3 ResolvePlayerAimDirection(Player player, Vector3 spawnPoint, Vector3 fallbackAimDirection, float maxTravelDistance)
    {
        if (GameCamera.instance != null)
        {
            Vector3 rayOrigin = GameCamera.instance.transform.position;
            Vector3 rayDirection = GameCamera.instance.transform.forward;
            float rayDistance = Mathf.Max(32f, maxTravelDistance * 3f);
            int hitCount = Physics.RaycastNonAlloc(rayOrigin, rayDirection, AimRayHits, rayDistance, SecondaryAttackManager.GetAimRayMask());
            if (TryResolveNearestPlayerAimHit(player, spawnPoint, AimRayHits, hitCount, out Vector3 aimedDirection))
            {
                ClearAimRayHits(hitCount);
                return aimedDirection;
            }

            ClearAimRayHits(hitCount);
            if (hitCount >= AimRayHits.Length &&
                TryResolveNearestPlayerAimHit(player, spawnPoint, Physics.RaycastAll(rayOrigin, rayDirection, rayDistance, SecondaryAttackManager.GetAimRayMask()), out aimedDirection))
            {
                return aimedDirection;
            }

            if (rayDirection.sqrMagnitude > 0.001f)
            {
                return rayDirection.normalized;
            }
        }

        if (fallbackAimDirection.sqrMagnitude > 0.001f)
        {
            return fallbackAimDirection.normalized;
        }

        return player.transform.forward;
    }

    private static bool TryResolveNearestPlayerAimHit(
        Player player,
        Vector3 spawnPoint,
        RaycastHit[] hits,
        out Vector3 aimedDirection)
    {
        return TryResolveNearestPlayerAimHit(player, spawnPoint, hits, hits.Length, out aimedDirection);
    }

    private static bool TryResolveNearestPlayerAimHit(
        Player player,
        Vector3 spawnPoint,
        RaycastHit[] hits,
        int hitCount,
        out Vector3 aimedDirection)
    {
        aimedDirection = Vector3.zero;
        float nearestDistance = float.MaxValue;
        int count = Mathf.Min(hitCount, hits.Length);
        for (int index = 0; index < count; index++)
        {
            RaycastHit hit = hits[index];
            if (hit.collider == null ||
                hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.gameObject == player.gameObject ||
                hit.distance >= nearestDistance)
            {
                continue;
            }

            Character? hitCharacter = SecondaryAttackManager.GetHitCharacter(hit.collider);
            Vector3 targetPoint = hitCharacter != null && hitCharacter != player
                ? hitCharacter.GetCenterPoint()
                : hit.point;
            Vector3 candidateDirection = targetPoint - spawnPoint;
            if (candidateDirection.sqrMagnitude <= 0.001f)
            {
                continue;
            }

            nearestDistance = hit.distance;
            aimedDirection = candidateDirection.normalized;
        }

        return nearestDistance < float.MaxValue;
    }

    private static void ClearAimRayHits(int hitCount)
    {
        int count = Mathf.Min(hitCount, AimRayHits.Length);
        for (int index = 0; index < count; index++)
        {
            AimRayHits[index] = default;
        }
    }

    private static float CalculateShieldThrowSearchRadius(float deflectionForce, float radiusFactor)
    {
        return CalculateShieldThrowInverseForceScaledValue(deflectionForce, radiusFactor);
    }

    private static float CalculateShieldThrowTtl(float deflectionForce, float ttlFactor)
    {
        return Mathf.Max(ShieldThrowMinTtl, CalculateShieldThrowInverseForceScaledValue(deflectionForce, ttlFactor));
    }

    private static float CalculateShieldThrowInverseForceScaledValue(float deflectionForce, float factor)
    {
        float forceScale = CalculateShieldThrowForceScale(deflectionForce);
        return forceScale > 0f
            ? Mathf.Max(0f, factor) / forceScale
            : 0f;
    }

    private static float CalculateShieldThrowForceScale(float deflectionForce)
    {
        return deflectionForce > 0f
            ? Mathf.Pow(deflectionForce / ShieldThrowForceReference, 1f / 3f)
            : 0f;
    }

    private static float CalculateShieldChargeHitRadius(float deflectionForce, float hitRadiusFactor)
    {
        return Mathf.Sqrt(Mathf.Max(0f, deflectionForce) / ShieldChargeHitRadiusReferenceForce) * Mathf.Max(0f, hitRadiusFactor);
    }

    private static float CalculateShieldChargeCooldown(Character character, ShieldSpecialSecondaryBehavior behavior)
    {
        float baseCooldown = Mathf.Max(0f, behavior.ShieldChargeCooldown);
        if (baseCooldown <= 0f)
        {
            return 0f;
        }

        float blockingLevel = character != null ? Mathf.Clamp(character.GetSkillLevel(Skills.SkillType.Blocking), 0f, 100f) : 0f;
        float reduction = Mathf.Clamp01(blockingLevel / 100f) * Mathf.Clamp01(behavior.ShieldChargeCooldownReductionFactor);
        return Mathf.Max(0f, baseCooldown * (1f - reduction));
    }

    private static float CalculateShieldThrowProjectileSpeed(ProjectileLaunchData launchData)
    {
        return Mathf.Max(18f, SecondaryAttackManager.ResolveProjectileSpeed(launchData));
    }

    internal static bool TryCalculateShieldSpecialRawStaminaCost(
        ItemDrop.ItemData shieldWeapon,
        SecondaryAttackDefinition definition,
        ShieldSpecialMode mode,
        out float rawAttackStamina)
    {
        rawAttackStamina = 0f;
        if (shieldWeapon == null || definition == null)
        {
            return false;
        }

        ShieldSpecialSecondaryBehavior? behavior = definition.Behavior as ShieldSpecialSecondaryBehavior;
        if (behavior == null)
        {
            return false;
        }

        float baseBlockPower = shieldWeapon.GetBaseBlockPower(shieldWeapon.m_quality);
        float normalizedBaseBlockPower = Mathf.Sqrt(Mathf.Max(0f, baseBlockPower));
        switch (mode)
        {
            case ShieldSpecialMode.PrimaryAttack:
                if (!behavior.HasShieldPrimaryAttack ||
                    behavior.ShieldPrimaryAttackStaminaFactor <= 0f)
                {
                    return false;
                }

                rawAttackStamina = Mathf.Max(
                    0f,
                    behavior.ShieldPrimaryAttackStaminaFactor * normalizedBaseBlockPower);
                return true;
            case ShieldSpecialMode.Charge:
                if (!behavior.HasShieldCharge ||
                    behavior.ShieldChargeStaminaFactor <= 0f)
                {
                    return false;
                }

                rawAttackStamina = Mathf.Max(
                    0f,
                    behavior.ShieldChargeStaminaFactor * normalizedBaseBlockPower);
                return true;
            default:
                if (!behavior.HasShieldThrow ||
                    behavior.ShieldThrowStaminaFactor <= 0f)
                {
                    return false;
                }

                rawAttackStamina = Mathf.Max(
                    0f,
                    behavior.ShieldThrowStaminaFactor * normalizedBaseBlockPower);
                return true;
        }
    }

    private static bool TryApplyShieldHit(
        Attack attack,
        Character target,
        Vector3 direction,
        Vector3 hitPoint,
        float damage,
        float pushForce,
        float hitRadius,
        HashSet<Character> hitTargets,
        ref bool skillRaised)
    {
        if (target == null || target.IsDead() || hitTargets.Contains(target) || !CanShieldAttackHitCharacter(attack, target))
        {
            return false;
        }

        hitTargets.Add(target);
        HitData hitData = CreateShieldHitData(attack, direction, hitPoint, damage, pushForce);
        hitData.m_hitCollider = FindBestHitCollider(target, hitPoint, hitRadius);
        using (ShieldWarfareHitContext.Begin(attack))
        {
            target.Damage(hitData);
        }
        if (BaseAI.IsEnemy(attack.m_character, target))
        {
            float adrenalineFactor = SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) && activeAttack != null
                ? SecondaryAttackAdrenalineSystem.ResolveFactor(activeAttack)
                : 1f;
            SecondaryAttackAdrenalineSystem.TryGrantOnceRaw(attack, target, 1f, adrenalineFactor, "shield");
        }

        if (!skillRaised)
        {
            attack.m_character.RaiseSkill(attack.m_weapon.m_shared.m_skillType, attack.m_raiseSkillAmount);
            skillRaised = true;
        }

        return true;
    }

    private static bool TryApplyShieldChargeImpact(
        Attack attack,
        Vector3 impactPoint,
        Vector3 direction,
        float damage,
        float pushForce,
        float impactRadius,
        HashSet<Character> hitTargets,
        ref bool skillRaised,
        bool applyLowerDamagePerHit = false)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            impactPoint,
            impactRadius,
            ShieldChargeImpactHits,
            SecondaryAttackManager.GetShieldChargeImpactMask(),
            QueryTriggerInteraction.Ignore);

        try
        {
            for (int index = 0; index < hitCount; index++)
            {
                Collider collider = ShieldChargeImpactHits[index];
                ShieldChargeImpactHits[index] = null!;
                if (collider == null)
                {
                    continue;
                }

                IDestructible? destructible = ResolveShieldImpactTarget(collider);
                if (destructible == null || !ShieldChargeImpactedTargets.Add(destructible))
                {
                    continue;
                }

                if (destructible is not MonoBehaviour)
                {
                    continue;
                }

                ShieldChargeImpactTargets.Add(new ShieldImpactTarget(destructible, collider));
            }

            bool hitAny = false;
            int validTargetCount = ShieldChargeImpactTargets.Count;
            float damageScale = 1f;
            if (applyLowerDamagePerHit && validTargetCount > 1)
            {
                damageScale = 1f / (validTargetCount * 0.75f);
            }

            foreach (ShieldImpactTarget target in ShieldChargeImpactTargets)
            {
                float scaledDamage = damage * damageScale;
                float scaledPushForce = pushForce * damageScale;
                if (target.Destructible is Character candidate)
                {
                    if (TryApplyShieldHit(attack, candidate, direction, impactPoint, scaledDamage, scaledPushForce, impactRadius, hitTargets, ref skillRaised))
                    {
                        hitAny = true;
                    }

                    continue;
                }

                HitData hitData = CreateShieldHitData(attack, direction, impactPoint, scaledDamage, scaledPushForce);
                hitData.m_hitCollider = target.Collider;
                using (ShieldWarfareHitContext.Begin(attack))
                {
                    target.Destructible.Damage(hitData);
                }
                hitAny = true;
            }

            return hitAny;
        }
        finally
        {
            ShieldChargeImpactedTargets.Clear();
            ShieldChargeImpactTargets.Clear();
        }
    }

    private static bool CanShieldAttackHitCharacter(Attack attack, Character target)
    {
        if (attack == null || attack.m_character == null || attack.m_weapon == null || target == null || target == attack.m_character)
        {
            return false;
        }

        Character attacker = attack.m_character;
        bool isEnemy = BaseAI.IsEnemy(attacker, target) ||
                       (target.GetBaseAI() is { } targetAi && targetAi.IsAggravatable() && attacker.IsPlayer());
        if (((!attack.m_hitFriendly || attacker.IsTamed()) && !attacker.IsPlayer() && !isEnemy) ||
            (!attack.m_weapon.m_shared.m_tamedOnly && attacker.IsPlayer() && !attacker.IsPVPEnabled() && !isEnemy) ||
            (attack.m_weapon.m_shared.m_tamedOnly && !target.IsTamed()))
        {
            return false;
        }

        if (attack.m_weapon.m_shared.m_dodgeable && target.IsDodgeInvincible())
        {
            if (target is Player dodgingPlayer)
            {
                dodgingPlayer.HitWhileDodging();
            }

            return false;
        }

        return true;
    }

    private static HitData CreateShieldHitData(Attack attack, Vector3 direction, Vector3 hitPoint, float damage, float pushForce)
    {
        HitData hitData = new();
        hitData.m_toolTier = (short)attack.m_weapon.m_shared.m_toolTier;
        hitData.m_pushForce = pushForce;
        hitData.m_backstabBonus = attack.m_weapon.m_shared.m_backstabBonus;
        hitData.m_staggerMultiplier = 1f;
        hitData.m_blockable = attack.m_weapon.m_shared.m_blockable;
        hitData.m_dodgeable = attack.m_weapon.m_shared.m_dodgeable;
        hitData.m_skill = attack.m_weapon.m_shared.m_skillType;
        hitData.m_skillRaiseAmount = attack.m_raiseSkillAmount;
        hitData.m_skillLevel = attack.m_character.GetSkillLevel(attack.m_weapon.m_shared.m_skillType);
        hitData.m_itemLevel = (short)attack.m_weapon.m_quality;
        hitData.m_itemWorldLevel = (byte)attack.m_weapon.m_worldLevel;
        hitData.m_point = hitPoint;
        hitData.m_dir = direction.sqrMagnitude > 0.001f ? direction.normalized : SecondaryAttackManager.GetSentinelForward(attack.m_character);
        hitData.m_healthReturn = attack.m_attackHealthReturnHit;
        hitData.m_damage.m_blunt = damage;
        hitData.m_statusEffectHash = ResolveAttackStatusEffectHash(attack.m_weapon);
        hitData.SetAttacker(attack.m_character);
        hitData.m_hitType = attack.m_character is Player ? HitData.HitType.PlayerHit : HitData.HitType.EnemyHit;
        attack.m_character.GetSEMan().ModifyAttack(attack.m_weapon.m_shared.m_skillType, ref hitData);
        return hitData;
    }

    private static int ResolveAttackStatusEffectHash(ItemDrop.ItemData weapon)
    {
        StatusEffect statusEffect = weapon.m_shared.m_attackStatusEffect;
        if (statusEffect == null)
        {
            return 0;
        }

        return weapon.m_shared.m_attackStatusEffectChance >= 1f || UnityEngine.Random.Range(0f, 1f) < weapon.m_shared.m_attackStatusEffectChance
            ? statusEffect.NameHash()
            : 0;
    }

    private static Collider? FindBestHitCollider(Character target, Vector3 point, float radius)
    {
        ShieldChargeTargetColliders.Clear();
        target.GetComponentsInChildren(includeInactive: false, ShieldChargeTargetColliders);
        Collider? bestCollider = null;
        float bestDistance = float.MaxValue;
        float radiusSquared = radius * radius;
        foreach (Collider collider in ShieldChargeTargetColliders)
        {
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            Vector3 closestPoint = SecondaryAttackManager.ResolveSafeClosestPoint(collider, point);
            float distanceSquared = (closestPoint - point).sqrMagnitude;
            if (distanceSquared > radiusSquared || distanceSquared >= bestDistance)
            {
                continue;
            }

            bestDistance = distanceSquared;
            bestCollider = collider;
        }

        ShieldChargeTargetColliders.Clear();
        return bestCollider;
    }

    private static IDestructible? ResolveShieldImpactTarget(Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        GameObject hitObject = Projectile.FindHitObject(collider);
        return hitObject != null ? hitObject.GetComponent<IDestructible>() : null;
    }

    private static bool TryFindShieldChargeImpact(
        Attack attack,
        Vector3 start,
        Vector3 end,
        float hitRadius,
        HashSet<Character> hitTargets,
        out Character? impactTarget,
        out float impactProgress,
        out Vector3 impactPoint)
    {
        impactTarget = null;
        impactProgress = 0f;
        impactPoint = end;
        float closestProgress = float.MaxValue;
        float scanRadius = (end - start).magnitude * 0.5f + hitRadius;
        if (scanRadius <= 0f)
        {
            return false;
        }

        Vector3 scanCenter = (start + end) * 0.5f;
        int hitCount = Physics.OverlapSphereNonAlloc(
            scanCenter,
            scanRadius,
            ShieldChargeScanHits,
            SecondaryAttackManager.GetShieldChargeImpactMask(),
            QueryTriggerInteraction.Ignore);

        if (hitCount >= ShieldChargeScanHits.Length)
        {
            ClearShieldChargeScanHits(hitCount);
            return TryFindShieldChargeImpactByAllCharacters(
                attack,
                start,
                end,
                hitRadius,
                hitTargets,
                ref impactTarget,
                ref impactProgress,
                ref impactPoint,
                ref closestProgress);
        }

        try
        {
            for (int index = 0; index < hitCount; index++)
            {
                Collider collider = ShieldChargeScanHits[index];
                ShieldChargeScanHits[index] = null!;
                Character? candidate = collider != null ? SecondaryAttackManager.GetHitCharacter(collider) : null;
                if (candidate == null || !ShieldChargeScanCandidates.Add(candidate))
                {
                    continue;
                }

                TryConsiderShieldChargeImpactCandidate(
                    attack,
                    candidate,
                    start,
                    end,
                    hitRadius,
                    hitTargets,
                    ref impactTarget,
                    ref impactProgress,
                    ref impactPoint,
                    ref closestProgress);
            }

            return impactTarget != null;
        }
        finally
        {
            ShieldChargeScanCandidates.Clear();
        }
    }

    private static bool TryFindShieldChargeImpactByAllCharacters(
        Attack attack,
        Vector3 start,
        Vector3 end,
        float hitRadius,
        HashSet<Character> hitTargets,
        ref Character? impactTarget,
        ref float impactProgress,
        ref Vector3 impactPoint,
        ref float closestProgress)
    {
        foreach (Character candidate in Character.GetAllCharacters())
        {
            TryConsiderShieldChargeImpactCandidate(
                attack,
                candidate,
                start,
                end,
                hitRadius,
                hitTargets,
                ref impactTarget,
                ref impactProgress,
                ref impactPoint,
                ref closestProgress);
        }

        return impactTarget != null;
    }

    private static void TryConsiderShieldChargeImpactCandidate(
        Attack attack,
        Character? candidate,
        Vector3 start,
        Vector3 end,
        float hitRadius,
        HashSet<Character> hitTargets,
        ref Character? impactTarget,
        ref float impactProgress,
        ref Vector3 impactPoint,
        ref float closestProgress)
    {
        Character owner = attack.m_character;
        if (candidate == null || candidate == owner || candidate.IsDead() || hitTargets.Contains(candidate))
        {
            return;
        }

        if (!CanShieldAttackHitCharacter(attack, candidate))
        {
            return;
        }

        Vector3 targetPoint = candidate.GetCenterPoint();
        float progress = SecondaryAttackManager.ClosestSegmentProgress(start, end, targetPoint);
        Vector3 closestPoint = Vector3.Lerp(start, end, progress);
        if ((targetPoint - closestPoint).sqrMagnitude > hitRadius * hitRadius || progress >= closestProgress)
        {
            return;
        }

        closestProgress = progress;
        impactTarget = candidate;
        impactProgress = progress;
        impactPoint = closestPoint;
    }

    private static void ClearShieldChargeScanHits(int hitCount)
    {
        int count = Mathf.Min(hitCount, ShieldChargeScanHits.Length);
        for (int index = 0; index < count; index++)
        {
            ShieldChargeScanHits[index] = null!;
        }
    }

    private static Character? FindShieldBounceTarget(Character owner, Character currentTarget, float searchRadius, HashSet<Character> hitTargets)
    {
        return FindShieldBounceTarget(owner, currentTarget.GetCenterPoint(), currentTarget, searchRadius, hitTargets);
    }

    private static Character? FindShieldBounceTarget(Character owner, Vector3 origin, Character? currentTarget, float searchRadius, HashSet<Character> hitTargets)
    {
        Character? nextTarget = null;
        float closestDistanceSqr = searchRadius * searchRadius;
        foreach (Character candidate in Character.GetAllCharacters())
        {
            if (candidate == null || candidate == owner || candidate == currentTarget || candidate.IsDead() || hitTargets.Contains(candidate))
            {
                continue;
            }

            if (!BaseAI.IsEnemy(owner, candidate))
            {
                continue;
            }

            float distanceSqr = (origin - candidate.GetCenterPoint()).sqrMagnitude;
            if (distanceSqr > closestDistanceSqr)
            {
                continue;
            }

            closestDistanceSqr = distanceSqr;
            nextTarget = candidate;
        }

        return nextTarget;
    }

    private static bool TryResolveShieldThrowTemplate(out ProjectileLaunchData launchData)
    {
        if (_shieldThrowTemplateLaunchData.IsValid)
        {
            launchData = _shieldThrowTemplateLaunchData;
            return true;
        }

        if (TryResolveCatapultShieldThrowTemplate(out launchData))
        {
            _shieldThrowTemplateLaunchData = launchData;
            _shieldThrowTemplateSource = ShieldThrowCatapultProjectilePrefabName;
            CaptainValheimPlugin.ModLogger.LogInfo($"Shield throw will use projectile template from '{_shieldThrowTemplateSource}'.");
            return true;
        }

        ObjectDB? objectDb = ObjectDB.instance;
        if (objectDb == null)
        {
            launchData = ProjectileLaunchData.Invalid;
            return false;
        }

        Attack? preferredAttack = null;
        Attack? secondaryFallback = null;
        Attack? primaryFallback = null;
        string preferredSource = "";
        string secondarySource = "";
        string primarySource = "";
        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null)
            {
                continue;
            }

            ItemDrop? itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null)
            {
                continue;
            }

            ItemDrop.ItemData.SharedData sharedData = itemDrop.m_itemData.m_shared;
            ConsiderShieldThrowTemplate(sharedData.m_secondaryAttack, itemPrefab.name, ref preferredAttack, ref preferredSource, ref secondaryFallback, ref secondarySource);
            ConsiderShieldThrowTemplate(sharedData.m_attack, itemPrefab.name, ref preferredAttack, ref preferredSource, ref primaryFallback, ref primarySource);
            if (preferredAttack != null)
            {
                break;
            }
        }

        Attack? resolvedAttack = preferredAttack ?? secondaryFallback ?? primaryFallback;
        _shieldThrowTemplateSource = !string.IsNullOrWhiteSpace(preferredSource)
            ? preferredSource
            : !string.IsNullOrWhiteSpace(secondarySource)
                ? secondarySource
                : primarySource;
        if (resolvedAttack == null || resolvedAttack.m_attackProjectile == null)
        {
            launchData = ProjectileLaunchData.Invalid;
            return false;
        }

        Projectile? projectile = resolvedAttack.m_attackProjectile.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.m_canChangeVisuals = true;
        }

        launchData = new ProjectileLaunchData(
            resolvedAttack.m_attackProjectile,
            null,
            resolvedAttack.m_projectileVel,
            resolvedAttack.m_projectileVelMin,
            resolvedAttack.m_projectileAccuracy,
            resolvedAttack.m_projectileAccuracyMin,
            resolvedAttack.m_attackHitNoise,
            1f,
            1f,
            1f,
            1f,
            resolvedAttack.m_randomVelocity && !resolvedAttack.m_bowDraw);
        _shieldThrowTemplateLaunchData = launchData;
        if (!string.IsNullOrWhiteSpace(_shieldThrowTemplateSource))
        {
            CaptainValheimPlugin.ModLogger.LogInfo($"Shield throw will use projectile template from '{_shieldThrowTemplateSource}'.");
        }

        return true;
    }

    private static bool TryResolveCatapultShieldThrowTemplate(out ProjectileLaunchData launchData)
    {
        ZNetScene? scene = ZNetScene.instance;
        GameObject? projectilePrefab = scene?.GetPrefab(ShieldThrowCatapultProjectilePrefabName);
        if (projectilePrefab == null)
        {
            launchData = ProjectileLaunchData.Invalid;
            return false;
        }

        Projectile? projectile = projectilePrefab.GetComponent<Projectile>();
        launchData = new ProjectileLaunchData(
            projectilePrefab,
            null,
            ShieldThrowCatapultProjectileSpeed,
            ShieldThrowCatapultProjectileSpeed,
            0f,
            0f,
            projectile?.m_hitNoise ?? 0f,
            1f,
            1f,
            1f,
            1f,
            false);
        return true;
    }

    private static void ConsiderShieldThrowTemplate(
        Attack? candidate,
        string sourcePrefabName,
        ref Attack? preferredAttack,
        ref string preferredSource,
        ref Attack? fallbackAttack,
        ref string fallbackSource)
    {
        if (candidate == null || candidate.m_attackType != Attack.AttackType.Projectile || candidate.m_attackProjectile == null)
        {
            return;
        }

        if (string.Equals(candidate.m_attackAnimation, "spear_throw", StringComparison.OrdinalIgnoreCase))
        {
            preferredAttack = candidate;
            preferredSource = sourcePrefabName;
            return;
        }

        if (fallbackAttack == null)
        {
            fallbackAttack = candidate;
            fallbackSource = sourcePrefabName;
        }
    }

    private static bool TryConsumeShieldForThrow(Attack attack, out ItemDrop.ItemData thrownShield)
    {
        thrownShield = null!;
        if (attack.m_character is not Player player || attack.m_weapon == null || attack.m_weapon.m_dropPrefab == null)
        {
            return false;
        }

        thrownShield = attack.m_weapon.Clone();
        thrownShield.m_stack = 1;
        thrownShield.m_equipped = false;
        player.UnequipItem(attack.m_weapon, triggerEquipEffects: false);
        Inventory inventory = player.GetInventory();
        if (inventory == null || !inventory.RemoveItem(attack.m_weapon, 1))
        {
            thrownShield = null!;
            return false;
        }

        return true;
    }

    private static bool TrySpawnShieldProjectile(
        Attack attack,
        ProjectileLaunchData launchData,
        ItemDrop.ItemData thrownShield,
        Vector3 spawnPoint,
        Vector3 direction,
        float damage,
        float pushForce,
        float searchRadius,
        float maxTravelDistance,
        float ttl,
        float speed,
        int remainingChains,
        float damageDecay,
        HashSet<Character> hitTargets,
        bool allowSkillRaise = true,
        bool returningToOwner = false)
    {
        Stopwatch? totalPerf = ShieldPerformanceLog.Start();
        string result = "completed";
        string shieldName = "<null>";
        GameObject? projectileObject = null;
        Projectile? projectile = null;
        try
        {
            if (thrownShield == null)
            {
                result = "missingShield";
                return false;
            }

            shieldName = thrownShield.m_dropPrefab?.name ?? "<null>";
            if (!launchData.IsValid)
            {
                result = "invalidLaunchData";
                return false;
            }

            if (direction.sqrMagnitude < 0.001f)
            {
                direction = SecondaryAttackManager.GetSentinelForward(attack.m_character);
            }

            direction.Normalize();
            Stopwatch? stepPerf = ShieldPerformanceLog.Start();
            projectileObject = Object.Instantiate(launchData.ProjectilePrefab!, spawnPoint, Quaternion.LookRotation(direction));
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.spawn.instantiate",
                () => $"shield={shieldName} projectilePrefab={launchData.ProjectilePrefab!.name} object={projectileObject.name} returning={returningToOwner}");

            stepPerf = ShieldPerformanceLog.Start();
            projectile = projectileObject.GetComponent<Projectile>();
            IProjectile? projectileInterface = projectileObject.GetComponent<IProjectile>();
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.spawn.getComponents",
                () => $"shield={shieldName} object={projectileObject.name} projectile={projectile != null} interface={projectileInterface != null}");
            if (projectile == null || projectileInterface == null)
            {
                result = "missingProjectileComponents";
                SecondaryAttackManager.DestroyProjectileObject(projectileObject);
                return false;
            }

            stepPerf = ShieldPerformanceLog.Start();
            ConfigureShieldProjectileInstance(projectile, thrownShield, ttl);
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.configure",
                () => $"shield={shieldName} projectile={projectile.name} ttl={ttl:0.###}");

            stepPerf = ShieldPerformanceLog.Start();
            HitData hitData = CreateShieldHitData(attack, direction, spawnPoint, damage, pushForce);
            if (returningToOwner)
            {
                hitData.m_statusEffectHash = 0;
            }

            if (!allowSkillRaise || returningToOwner)
            {
                hitData.m_skillRaiseAmount = 0f;
            }

            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.hitData",
                () => $"shield={shieldName} projectile={projectile.name} damage={hitData.m_damage.GetTotalDamage():0.###} push={hitData.m_pushForce:0.###} allowSkillRaise={allowSkillRaise} returning={returningToOwner}");

            projectile.m_adrenaline = 0f;
            stepPerf = ShieldPerformanceLog.Start();
            projectileInterface.Setup(attack.m_character, direction * speed, launchData.AttackHitNoise, hitData, thrownShield, null);
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.projectileSetup",
                () => $"shield={shieldName} projectile={projectile.name} speed={speed:0.###} hitNoise={launchData.AttackHitNoise:0.###}");
            projectile.m_adrenaline = 0f;

            stepPerf = ShieldPerformanceLog.Start();
            IgnoreShieldProjectileOwnerCollisions(projectileObject, attack.m_character);
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.ownerCollisionIgnore",
                () => $"shield={shieldName} projectile={projectile.name} owner={attack.m_character?.name ?? "<null>"}");

            stepPerf = ShieldPerformanceLog.Start();
            SecondaryAttackManager.RegisterProjectileAttackAttribution(projectile, attack);
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.attribution",
                () => $"shield={shieldName} projectile={projectile.name}");

            stepPerf = ShieldPerformanceLog.Start();
            ApplyShieldProjectileVisual(projectile, thrownShield);
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.visualTotal",
                () => $"shield={shieldName} projectile={projectile.name} visualObject={projectile.m_visual?.name ?? "<null>"}");

            stepPerf = ShieldPerformanceLog.Start();
            ShieldProjectileController controller = projectileObject.AddComponent<ShieldProjectileController>();
            controller.Initialize(attack, projectile, thrownShield, remainingChains, searchRadius, speed, ttl, damageDecay, hitTargets, returningToOwner);
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.controller",
                () => $"shield={shieldName} projectile={projectile.name} chains={remainingChains} returning={returningToOwner}");

            attack.m_weapon.m_lastProjectile = projectileObject;
            return true;
        }
        finally
        {
            ShieldPerformanceLog.Stop(
                totalPerf,
                "shieldThrow.spawn",
                () => $"shield={shieldName} result={result} projectile={projectile?.name ?? projectileObject?.name ?? "<null>"} returning={returningToOwner} chains={remainingChains} speed={speed:0.###} ttl={ttl:0.###}");
        }
    }

    private static void IgnoreShieldProjectileOwnerCollisions(GameObject projectileObject, Character? owner)
    {
        if (projectileObject == null || owner == null)
        {
            return;
        }

        Collider[] projectileColliders = projectileObject.GetComponentsInChildren<Collider>(includeInactive: true);
        Collider[] ownerColliders = owner.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (Collider projectileCollider in projectileColliders)
        {
            if (projectileCollider == null)
            {
                continue;
            }

            foreach (Collider ownerCollider in ownerColliders)
            {
                if (ownerCollider == null || projectileCollider == ownerCollider)
                {
                    continue;
                }

                Physics.IgnoreCollision(projectileCollider, ownerCollider, ignore: true);
            }
        }
    }

    private static void ConfigureShieldProjectileInstance(Projectile projectile, ItemDrop.ItemData thrownShield, float ttl)
    {
        PrepareShieldProjectileForVisualSwap(projectile);
        MarkShieldProjectile(projectile);
        RemoveShieldProjectileArrowHitSfx(projectile);
        projectile.m_respawnItemOnHit = false;
        projectile.m_spawnOnHit = null;
        projectile.m_spawnOnHitChance = 0f;
        projectile.m_spawnItem = null;
        projectile.m_spawnOnTtl = false;
        projectile.m_randomSpawnOnHit.Clear();
        projectile.m_randomSpawnOnHitCount = 0;
        projectile.m_attachToRigidBody = false;
        projectile.m_attachToClosestBone = false;
        projectile.m_stayAfterHitDynamic = false;
        projectile.m_stayAfterHitStatic = false;
        projectile.m_bounce = false;
        projectile.m_ttl = Mathf.Max(ShieldThrowMinTtl, ttl);
        projectile.m_stayTTL = 0.01f;
        projectile.m_rotateVisual = 0f;
        projectile.m_rotateVisualY = 0f;
        projectile.m_rotateVisualZ = 0f;
        projectile.name = $"CaptainValheim_ShieldProjectile_{thrownShield.m_dropPrefab.name}";
    }

    private static void RemoveShieldProjectileArrowHitSfx(Projectile projectile)
    {
        if (projectile == null || projectile.m_hitEffects == null || projectile.m_hitEffects.m_effectPrefabs == null)
        {
            return;
        }

        projectile.m_hitEffects.m_effectPrefabs = projectile.m_hitEffects.m_effectPrefabs
            .Where(effectData => effectData?.m_prefab == null || !effectData.m_prefab.name.Equals(ArrowHitSfxPrefabName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    internal static void PrepareShieldThrowProjectileIfNeeded(Projectile projectile)
    {
        if (!IsMarkedShieldProjectile(projectile))
        {
            return;
        }

        if (projectile.m_changedVisual)
        {
            return;
        }

        PrepareShieldProjectileForVisualSwap(projectile);
    }

    internal static void EnsureShieldThrowProjectileVisualSpinIfNeeded(Projectile projectile)
    {
        if (!IsMarkedShieldProjectile(projectile))
        {
            return;
        }

        EnsureShieldThrowVisualSpin(projectile.m_visual);
    }

    private static void EnsureShieldThrowVisualSpin(GameObject? visual)
    {
        if (ThrowProjectileVisualSpin.IsConfigured(visual, ThrowProjectileVisualSpin.AxisMode.WorldUp))
        {
            return;
        }

        ThrowProjectileVisualSpin.Ensure(visual, ThrowProjectileVisualSpin.AxisMode.WorldUp);
    }

    private static void MarkShieldProjectile(Projectile projectile)
    {
        ZNetView? nview = projectile.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid() || !nview.IsOwner() || nview.GetZDO() == null)
        {
            return;
        }

        nview.GetZDO().Set(ShieldThrowProjectileMarkerKey, true);
    }

    private static bool IsMarkedShieldProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return false;
        }

        ZNetView? nview = projectile.GetComponent<ZNetView>();
        return nview != null &&
               nview.IsValid() &&
               nview.GetZDO() != null &&
               nview.GetZDO().GetBool(ShieldThrowProjectileMarkerKey);
    }

    private static void PrepareShieldProjectileForVisualSwap(Projectile projectile)
    {
        Stopwatch? stepPerf = ShieldPerformanceLog.Start();
        Transform? existingVisualRoot = projectile.transform.Find(ShieldThrowProjectileVisualRootName);
        ShieldPerformanceLog.Stop(
            stepPerf,
            "shieldThrow.visual.prepare.findRoot",
            () => $"projectile={projectile.name} found={existingVisualRoot != null}");
        if (existingVisualRoot != null)
        {
            projectile.m_visual = existingVisualRoot.gameObject;
            projectile.m_canChangeVisuals = true;
            stepPerf = ShieldPerformanceLog.Start();
            EnsureShieldThrowVisualSpin(projectile.m_visual);
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.visual.prepare.reuseSpin",
                () => $"projectile={projectile.name} visualObject={projectile.m_visual.name}");
            return;
        }

        stepPerf = ShieldPerformanceLog.Start();
        HideShieldProjectileSourcePresentation(projectile);
        ShieldPerformanceLog.Stop(
            stepPerf,
            "shieldThrow.visual.prepare.hideSource",
            () => $"projectile={projectile.name}");

        stepPerf = ShieldPerformanceLog.Start();
        GameObject visualRoot = new(ShieldThrowProjectileVisualRootName);
        visualRoot.transform.SetParent(projectile.transform, false);
        visualRoot.layer = projectile.gameObject.layer;
        projectile.m_visual = visualRoot;
        projectile.m_canChangeVisuals = true;
        ShieldPerformanceLog.Stop(
            stepPerf,
            "shieldThrow.visual.prepare.createRoot",
            () => $"projectile={projectile.name} visualObject={visualRoot.name}");

        stepPerf = ShieldPerformanceLog.Start();
        EnsureShieldThrowVisualSpin(projectile.m_visual);
        ShieldPerformanceLog.Stop(
            stepPerf,
            "shieldThrow.visual.prepare.initialSpin",
            () => $"projectile={projectile.name} visualObject={projectile.m_visual.name}");
    }

    private static void HideShieldProjectileSourcePresentation(Projectile projectile)
    {
        ShieldProjectileRendererBuffer.Clear();
        projectile.GetComponentsInChildren(includeInactive: true, ShieldProjectileRendererBuffer);
        foreach (Renderer renderer in ShieldProjectileRendererBuffer)
        {
            if (renderer is TrailRenderer || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            renderer.enabled = false;
        }

        ShieldProjectileRendererBuffer.Clear();
    }

    private static void ApplyShieldProjectileVisual(Projectile projectile, ItemDrop.ItemData thrownShield)
    {
        if (thrownShield.m_dropPrefab == null)
        {
            return;
        }

        Stopwatch? totalPerf = ShieldPerformanceLog.Start();
        string shieldName = thrownShield.m_dropPrefab.name;
        string path = "none";
        try
        {
            Stopwatch? stepPerf = ShieldPerformanceLog.Start();
            ZNetView? nview = projectile.GetComponent<ZNetView>();
            bool nviewValid = nview != null && nview.IsValid();
            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.visual.getZNetView",
                () => $"shield={shieldName} projectile={projectile.name} valid={nviewValid}");

            if (projectile.m_canChangeVisuals && projectile.m_visual != null && nviewValid)
            {
                path = "updateVisual";
                stepPerf = ShieldPerformanceLog.Start();
                nview!.GetZDO().Set(ZDOVars.s_visual, thrownShield.m_dropPrefab.name);
                ShieldPerformanceLog.Stop(
                    stepPerf,
                    "shieldThrow.visual.zdoSet",
                    () => $"shield={shieldName} projectile={projectile.name}");

                stepPerf = ShieldPerformanceLog.Start();
                projectile.UpdateVisual();
                ShieldPerformanceLog.Stop(
                    stepPerf,
                    "shieldThrow.visual.updateVisual",
                    () => $"shield={shieldName} projectile={projectile.name} changed={projectile.m_changedVisual} visualObject={projectile.m_visual?.name ?? "<null>"}");

                stepPerf = ShieldPerformanceLog.Start();
                EnsureShieldThrowVisualSpin(projectile.m_visual);
                ShieldPerformanceLog.Stop(
                    stepPerf,
                    "shieldThrow.visual.spin",
                    () => $"shield={shieldName} projectile={projectile.name} axis=WorldUp");
                return;
            }

            path = "localFallback";
            stepPerf = ShieldPerformanceLog.Start();
            GameObject? attachPrefab = ResolveAttachGameObject(thrownShield.m_dropPrefab);

            ShieldPerformanceLog.Stop(
                stepPerf,
                "shieldThrow.visual.fallback.resolveAttach",
                () => $"shield={shieldName} projectile={projectile.name} attachPrefab={attachPrefab?.name ?? "<null>"}");

            bool createdPrimitive = false;
            if (attachPrefab == null)
            {
                stepPerf = ShieldPerformanceLog.Start();
                attachPrefab = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(attachPrefab.GetComponent<Collider>());
                attachPrefab.transform.localScale = new Vector3(0.6f, 0.08f, 0.6f);
                createdPrimitive = true;
                ShieldPerformanceLog.Stop(
                    stepPerf,
                    "shieldThrow.visual.fallback.createPrimitive",
                    () => $"shield={shieldName} projectile={projectile.name}");
            }

            ApplyShieldProjectileCachedVisual(
                projectile,
                thrownShield,
                attachPrefab,
                shieldName,
                createdPrimitive,
                perfScopePrefix: "shieldThrow.visual.fallback");
        }
        finally
        {
            ShieldPerformanceLog.Stop(
                totalPerf,
                "shieldThrow.visual",
                () => $"shield={shieldName} projectile={projectile.name} path={path} visualObject={projectile.m_visual?.name ?? "<null>"}");
        }
    }

    private static GameObject? ResolveAttachGameObject(GameObject itemPrefab)
    {
        Transform? attach = itemPrefab != null ? itemPrefab.transform.Find("attach") : null;
        if (attach == null)
        {
            return null;
        }

        Transform? attachObject = attach.Find("attachobj");
        return attachObject != null ? attachObject.gameObject : attach.gameObject;
    }

    private static void ApplyShieldProjectileCachedVisual(
        Projectile projectile,
        ItemDrop.ItemData thrownShield,
        GameObject attachPrefab,
        string shieldName,
        bool createdPrimitive,
        string perfScopePrefix)
    {
        GameObject? previousVisual = projectile.m_visual;
        Stopwatch? stepPerf = ShieldPerformanceLog.Start();
        GameObject visual = Object.Instantiate(attachPrefab, projectile.transform, false);
        visual.name = $"{attachPrefab.name}(ShieldProjectileVisual)";
        ShieldPerformanceLog.Stop(
            stepPerf,
            $"{perfScopePrefix}.instantiate",
            () => $"shield={shieldName} projectile={projectile.name} attachPrefab={attachPrefab.name} primitive={createdPrimitive}");
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        if (previousVisual != null && previousVisual != visual)
        {
            previousVisual.SetActive(false);
        }

        stepPerf = ShieldPerformanceLog.Start();
        visual.GetComponentInChildren<IEquipmentVisual>()?.Setup(thrownShield.m_variant);
        ShieldPerformanceLog.Stop(
            stepPerf,
            $"{perfScopePrefix}.equipmentSetup",
            () => $"shield={shieldName} projectile={projectile.name} variant={thrownShield.m_variant}");
        projectile.m_visual = visual;

        stepPerf = ShieldPerformanceLog.Start();
        EnsureShieldThrowVisualSpin(projectile.m_visual);
        ShieldPerformanceLog.Stop(
            stepPerf,
            $"{perfScopePrefix}.spin",
            () => $"shield={shieldName} projectile={projectile.name} axis=WorldUp");
    }

    private static void PlayShieldProjectileImpactSound(Vector3 position)
    {
        ZNetScene? scene = ZNetScene.instance;
        GameObject? impactPrefab = scene?.GetPrefab(ShieldThrowImpactAoePrefabName);
        if (impactPrefab == null)
        {
            return;
        }

        Transform? sfxTransform = impactPrefab.transform.Find(ShieldThrowImpactSfxChildName);
        GameObject? sfxPrefab = sfxTransform != null ? sfxTransform.gameObject : impactPrefab;
        if (sfxPrefab == null)
        {
            return;
        }

        GameObject sfxInstance = Object.Instantiate(sfxPrefab, position, Quaternion.identity);
        Object.Destroy(sfxInstance, 6f);
    }

    private static void PlayShieldThrowChargeStartSfx(Attack attack)
    {
        if (attack?.m_character == null)
        {
            return;
        }

        GameObject? sfxPrefab = ZNetScene.instance?.GetPrefab(ShieldThrowChargeStartSfxPrefabName);
        if (sfxPrefab == null)
        {
            if (SecondaryAttackManager.TryMarkCompatibilityWarningReported("shield_throw_charge_start_sfx_missing"))
            {
                CaptainValheimPlugin.ModLogger.LogWarning($"Shield throw/charge start SFX prefab '{ShieldThrowChargeStartSfxPrefabName}' was not found.");
            }

            return;
        }

        Transform origin = attack.m_character.transform;
        GameObject sfxInstance = Object.Instantiate(sfxPrefab, origin.position, origin.rotation);
        Object.Destroy(sfxInstance, 6f);
    }

    private static void PlayShieldChargeStartVfx(Attack attack)
    {
        if (attack?.m_character == null)
        {
            return;
        }

        GameObject? vfxPrefab = ZNetScene.instance?.GetPrefab(ShieldChargeStartVfxPrefabName);
        if (vfxPrefab == null)
        {
            if (SecondaryAttackManager.TryMarkCompatibilityWarningReported("shield_charge_start_vfx_missing"))
            {
                CaptainValheimPlugin.ModLogger.LogWarning($"Shield charge start VFX prefab '{ShieldChargeStartVfxPrefabName}' was not found.");
            }

            return;
        }

        Transform origin = attack.m_character.transform;
        Vector3 position = origin.position + origin.forward * ShieldChargeStartVfxForwardOffset + Vector3.up * ShieldChargeStartVfxYOffset;
        GameObject vfxInstance = Object.Instantiate(vfxPrefab, position, origin.rotation);
        Object.Destroy(vfxInstance, 6f);
    }

    private static void PlayShieldChargeBullseyeEffect(Character attacker, Vector3 direction, float hitHeightOffset, float forwardOffset, float extraHeightOffset, float extraForwardOffset)
    {
        if (attacker == null)
        {
            return;
        }

        GameObject? effectPrefab = ZNetScene.instance?.GetPrefab(ShieldChargeBullseyeEffectPrefabName);
        if (effectPrefab == null)
        {
            return;
        }

        Vector3 normalizedDirection = direction.sqrMagnitude > 0.001f
            ? direction.normalized
            : attacker.transform.forward;
        Vector3 effectPosition = attacker.transform.position
                                 + Vector3.up * Mathf.Max(0f, hitHeightOffset + extraHeightOffset)
                                 + normalizedDirection * Mathf.Max(0.25f, forwardOffset + extraForwardOffset);
        GameObject effectInstance = Object.Instantiate(effectPrefab, effectPosition, Quaternion.LookRotation(normalizedDirection, Vector3.up));
        Object.Destroy(effectInstance, 6f);
    }

    private static void DropThrownShield(ItemDrop.ItemData thrownShield, Vector3 position, Quaternion rotation)
    {
        if (thrownShield == null)
        {
            return;
        }

        Stopwatch? perf = ShieldPerformanceLog.Start();
        string shieldName = thrownShield.m_dropPrefab?.name ?? thrownShield.m_shared?.m_name ?? "<null>";
        thrownShield.m_equipped = false;
        ItemDrop droppedShield = ItemDrop.DropItem(thrownShield, 1, position + Vector3.up * 0.25f, rotation);
        MarkThrownShieldForAutoEquip(droppedShield);
        ShieldPerformanceLog.Stop(
            perf,
            "shieldThrow.return.drop",
            () => $"shield={shieldName} dropped={droppedShield != null} position={position}");
    }

    private static void MarkThrownShieldForAutoEquip(ItemDrop? itemDrop)
    {
        if (itemDrop == null)
        {
            return;
        }

        ZNetView? nview = itemDrop.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid() || !nview.IsOwner() || nview.GetZDO() == null)
        {
            return;
        }

        nview.GetZDO().Set(ThrownShieldPickupMarkerKey, true);
    }

    internal static bool TryGetAutoEquipThrownShieldState(GameObject go, out ItemDrop.ItemData shieldItem)
    {
        shieldItem = null!;
        if (go == null)
        {
            return false;
        }

        ItemDrop? itemDrop = go.GetComponent<ItemDrop>();
        if (itemDrop == null)
        {
            return false;
        }

        ZNetView? nview = itemDrop.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid() || nview.GetZDO() == null || !nview.GetZDO().GetBool(ThrownShieldPickupMarkerKey))
        {
            return false;
        }

        shieldItem = itemDrop.m_itemData;
        return shieldItem != null;
    }

    private static Character? ResolveProjectileHitCharacter(Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        GameObject hitObject = Projectile.FindHitObject(collider);
        return hitObject != null ? hitObject.GetComponent<Character>() : null;
    }

    private static void RegisterShieldProjectileController(Projectile projectile, ShieldProjectileController controller)
    {
        ShieldProjectileControllers.Remove(projectile);
        ShieldProjectileControllers.Add(projectile, controller);
    }

    private static void UnregisterShieldProjectileController(Projectile projectile)
    {
        ShieldProjectileControllers.Remove(projectile);
    }

    internal static bool ShouldHandleShieldProjectileHit(Projectile projectile, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        if (projectile == null || !ShieldProjectileControllers.TryGetValue(projectile, out ShieldProjectileController controller))
        {
            return false;
        }

        controller.HandleHit(collider, hitPoint, water, normal);
        return true;
    }

    private sealed class ShieldProjectileController : MonoBehaviour
    {
        private Attack _attack = null!;
        private Character? _owner;
        private Projectile _projectile = null!;
        private ItemDrop.ItemData _thrownShield = null!;
        private HashSet<Character> _hitTargets = null!;
        private float _searchRadius;
        private float _speed;
        private float _ttl;
        private float _damageDecay;
        private int _remainingChains;
        private bool _returningToOwner;
        private bool _transferred;
        private bool _dropped;
        private bool _skillRaised;
        private bool _registeredAsyncWork;
        private float _returnCollisionIgnoreUntil;
        private Vector3 _lastPosition;

        public void Initialize(
            Attack attack,
            Projectile projectile,
            ItemDrop.ItemData thrownShield,
            int remainingChains,
            float searchRadius,
            float speed,
            float ttl,
            float damageDecay,
            HashSet<Character> hitTargets,
            bool returningToOwner)
        {
            _attack = attack;
            _owner = attack.m_character;
            _projectile = projectile;
            _thrownShield = thrownShield;
            _remainingChains = Mathf.Max(0, remainingChains);
            _searchRadius = searchRadius;
            _speed = speed;
            _ttl = ttl;
            _damageDecay = Mathf.Clamp01(damageDecay);
            _hitTargets = hitTargets;
            _returningToOwner = returningToOwner;
            _returnCollisionIgnoreUntil = returningToOwner ? Time.time + ShieldThrowReturnCollisionGraceSeconds : 0f;
            _lastPosition = transform.position;
            RegisterShieldProjectileController(_projectile, this);
            _projectile.m_onHit += OnProjectileHit;
            SecondaryAttackManager.RegisterAsyncSecondaryWork(_owner);
            _registeredAsyncWork = true;
        }

        private void Update()
        {
            _lastPosition = transform.position;
            if (_returningToOwner)
            {
                TryCatchReturningShield();
            }
        }

        private void OnDestroy()
        {
            if (_registeredAsyncWork)
            {
                SecondaryAttackManager.UnregisterAsyncSecondaryWork(_owner);
                _registeredAsyncWork = false;
            }

            if (_projectile != null)
            {
                _projectile.m_onHit -= OnProjectileHit;
                UnregisterShieldProjectileController(_projectile);
            }

            if (!HasAuthority() || _transferred || _dropped || _thrownShield == null)
            {
                return;
            }

            DropThrownShield(_thrownShield, _lastPosition, transform.rotation);
            _dropped = true;
        }

        internal void HandleHit(Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
        {
            if (_transferred || _dropped || _thrownShield == null)
            {
                return;
            }

            if (ShouldIgnoreHit(collider))
            {
                return;
            }

            ApplyProjectileDamage(collider, hitPoint, water, normal);
            OnProjectileHit(collider, hitPoint, water, normal);
            if (_transferred || _dropped)
            {
                DestroyCurrentProjectile();
            }
        }

        public bool ShouldIgnoreHit(Collider collider)
        {
            Character? target = ResolveProjectileHitCharacter(collider);
            if (target == _owner)
            {
                return true;
            }

            if (_returningToOwner)
            {
                return Time.time < _returnCollisionIgnoreUntil || target != null;
            }

            return target != null && _hitTargets.Contains(target);
        }

        private void OnProjectileHit(Collider collider, Vector3 hitPoint, bool water)
        {
            OnProjectileHit(collider, hitPoint, water, Vector3.zero);
        }

        private void OnProjectileHit(Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
        {
            if (!HasAuthority() || _transferred || _dropped || _thrownShield == null)
            {
                return;
            }

            _lastPosition = hitPoint;
            PlayShieldProjectileImpactSound(hitPoint);
            Character? hitTarget = ResolveProjectileHitCharacter(collider);
            if (hitTarget != null)
            {
                _hitTargets.Add(hitTarget);
            }

            if (_returningToOwner)
            {
                DropThrownShield(_thrownShield, hitPoint, transform.rotation);
                _dropped = true;
                return;
            }

            Character? owner = _attack?.m_character;
            if (hitTarget == null && TryRedirectNonCharacterHitToCharacterOrPlayer(owner, hitPoint, normal))
            {
                return;
            }

            bool hitEnemy = !water &&
                            hitTarget != null &&
                            owner != null &&
                            BaseAI.IsEnemy(owner, hitTarget);
            if (hitEnemy && owner != null && hitTarget != null && !hitTarget.IsDead() && _remainingChains > 0)
            {
                Character? nextTarget = FindShieldBounceTarget(owner, hitTarget, _searchRadius, _hitTargets);
                float nextDamage = Mathf.Max(0f, _projectile.m_damage.m_blunt * (1f - _damageDecay));
                if (nextTarget != null &&
                    TryLaunchShieldTowardTarget(nextTarget, hitPoint, normal, nextDamage, _remainingChains - 1, allowSkillRaise: false))
                {
                    return;
                }
            }

            if (hitEnemy && TryStartReturnToOwner(hitPoint, normal))
            {
                return;
            }

            DropThrownShield(_thrownShield, hitPoint, transform.rotation);
            _dropped = true;
        }

        private bool TryRedirectNonCharacterHitToCharacterOrPlayer(Character? owner, Vector3 hitPoint, Vector3 normal)
        {
            return !_returningToOwner && owner != null && TryStartReturnToOwner(hitPoint, normal);
        }

        private bool TryLaunchShieldTowardTarget(Character target, Vector3 hitPoint, Vector3 normal, float damage, int remainingChains, bool allowSkillRaise)
        {
            Vector3 bounceDirection = target.GetCenterPoint() - hitPoint;
            if (bounceDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            ProjectileLaunchData nextLaunchData = _shieldThrowTemplateLaunchData;
            if (!nextLaunchData.IsValid && !TryResolveShieldThrowTemplate(out nextLaunchData))
            {
                nextLaunchData = ProjectileLaunchData.Invalid;
            }

            if (!nextLaunchData.IsValid)
            {
                return false;
            }

            Vector3 direction = bounceDirection.normalized;
            if (!TrySpawnShieldProjectile(
                    _attack!,
                    nextLaunchData,
                    _thrownShield,
                    ResolveShieldRedirectSpawnPoint(hitPoint, normal, direction),
                    direction,
                    Mathf.Max(0f, damage),
                    _projectile.m_attackForce,
                    _searchRadius,
                    _speed * _ttl,
                    _ttl,
                    _speed,
                    Mathf.Max(0, remainingChains),
                    _damageDecay,
                    _hitTargets,
                    allowSkillRaise: allowSkillRaise))
            {
                return false;
            }

            _transferred = true;
            return true;
        }

        private bool ApplyProjectileDamage(Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
        {
            if (water || collider == null || _projectile == null || _attack?.m_character == null)
            {
                return false;
            }

            GameObject hitObject = Projectile.FindHitObject(collider);
            if (hitObject == null ||
                hitObject == _projectile.gameObject ||
                collider.transform.IsChildOf(_projectile.transform))
            {
                return false;
            }

            Character? character = SecondaryAttackManager.GetHitCharacter(collider);
            IDestructible? destructible = character != null ? character : hitObject.GetComponent<IDestructible>();
            if (destructible == null)
            {
                return false;
            }

            if (character != null)
            {
                if (character == _owner ||
                    character.IsDead() ||
                    _hitTargets.Contains(character) ||
                    !CanShieldAttackHitCharacter(_attack, character))
                {
                    return false;
                }
            }

            HitData? hitData = ProjectileAccess.GetOriginalHitData(_projectile)?.Clone();
            if (hitData == null)
            {
                hitData = CreateFallbackProjectileHitData(hitPoint, normal);
            }

            if (hitData.m_damage.GetTotalDamage() <= 0f && hitData.m_pushForce <= 0f)
            {
                return false;
            }

            if (character != null && hitData.m_dodgeable && character.IsDodgeInvincible())
            {
                if (character is Player dodgingPlayer)
                {
                    dodgingPlayer.HitWhileDodging();
                }

                return true;
            }

            hitData.m_point = hitPoint;
            hitData.m_dir = ResolveProjectileHitDirection(hitPoint, normal);
            hitData.m_hitCollider = collider;
            hitData.SetAttacker(_attack.m_character);
            using (ShieldWarfareHitContext.Begin(_attack))
            {
                destructible.Damage(hitData);
            }
            if (character != null &&
                !_returningToOwner &&
                _owner != null &&
                BaseAI.IsEnemy(_owner, character) &&
                character.m_enemyAdrenalineMultiplier > 0f)
            {
                float adrenalineFactor =
                    SecondaryAttackRuntimeContext.TryGetActiveAttack(_attack, out ActiveSecondaryAttack? activeAttack) &&
                    activeAttack != null
                        ? SecondaryAttackAdrenalineSystem.ResolveFactor(activeAttack)
                        : 1f;
                SecondaryAttackAdrenalineSystem.TryGrantOnceRaw(_attack, character, 1f, adrenalineFactor, "shield:throw");
            }

            RaiseShieldThrowSkill(hitData);
            PlayProjectileHitEffects(hitPoint, normal);
            return true;
        }

        private HitData CreateFallbackProjectileHitData(Vector3 hitPoint, Vector3 normal)
        {
            HitData hitData = new()
            {
                m_damage = _projectile.m_damage.Clone(),
                m_pushForce = _projectile.m_attackForce,
                m_backstabBonus = _projectile.m_backstabBonus,
                m_blockable = _projectile.m_blockable,
                m_dodgeable = _projectile.m_dodgeable,
                m_statusEffectHash = ProjectileAccess.GetStatusEffectHash(_projectile),
                m_point = hitPoint,
                m_dir = ResolveProjectileHitDirection(hitPoint, normal),
                m_hitCollider = null,
                m_skillRaiseAmount = 0f
            };

            if (_attack?.m_weapon != null)
            {
                hitData.m_toolTier = (short)_attack.m_weapon.m_shared.m_toolTier;
                hitData.m_skill = _attack.m_weapon.m_shared.m_skillType;
                hitData.m_skillLevel = _attack.m_character.GetSkillLevel(_attack.m_weapon.m_shared.m_skillType);
                hitData.m_itemLevel = (short)_attack.m_weapon.m_quality;
                hitData.m_itemWorldLevel = (byte)_attack.m_weapon.m_worldLevel;
            }

            if (_attack?.m_character != null)
            {
                hitData.SetAttacker(_attack.m_character);
                hitData.m_hitType = _attack.m_character is Player ? HitData.HitType.PlayerHit : HitData.HitType.EnemyHit;
            }

            return hitData;
        }

        private Vector3 ResolveProjectileHitDirection(Vector3 hitPoint, Vector3 normal)
        {
            Vector3 velocity = ProjectileAccess.GetVelocity(_projectile);
            if (velocity.sqrMagnitude > 0.001f)
            {
                return velocity.normalized;
            }

            if (_owner != null)
            {
                Vector3 fromOwner = hitPoint - _owner.GetCenterPoint();
                if (fromOwner.sqrMagnitude > 0.001f)
                {
                    return fromOwner.normalized;
                }
            }

            return normal.sqrMagnitude > 0.001f ? -normal.normalized : transform.forward;
        }

        private void RaiseShieldThrowSkill(HitData hitData)
        {
            if (_skillRaised ||
                hitData.m_skillRaiseAmount <= 0f ||
                _attack?.m_character == null ||
                _attack.m_weapon == null)
            {
                return;
            }

            _attack.m_character.RaiseSkill(_attack.m_weapon.m_shared.m_skillType, hitData.m_skillRaiseAmount);
            _skillRaised = true;
        }

        private void PlayProjectileHitEffects(Vector3 hitPoint, Vector3 normal)
        {
            Quaternion rotation = normal.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;
            _projectile.m_hitEffects.Create(hitPoint, rotation);
            if (_owner != null && _projectile.m_hitNoise > 0f)
            {
                _owner.AddNoise(_projectile.m_hitNoise);
            }
        }

        private bool TryStartReturnToOwner(Vector3 hitPoint, Vector3 normal)
        {
            if (_attack?.m_character is not Humanoid owner || owner.IsDead() || _thrownShield == null)
            {
                return false;
            }

            Vector3 ownerPoint = owner.GetCenterPoint();
            Vector3 returnDirection = ownerPoint - hitPoint;
            if (returnDirection.sqrMagnitude <= ShieldThrowReturnCatchRadius * ShieldThrowReturnCatchRadius)
            {
                return TryReturnShieldToOwner(owner);
            }

            if (returnDirection.sqrMagnitude < 0.001f)
            {
                return false;
            }

            ProjectileLaunchData returnLaunchData = _shieldThrowTemplateLaunchData;
            if (!returnLaunchData.IsValid && !TryResolveShieldThrowTemplate(out returnLaunchData))
            {
                returnLaunchData = ProjectileLaunchData.Invalid;
            }

            if (!returnLaunchData.IsValid)
            {
                return false;
            }

            Vector3 direction = returnDirection.normalized;
            float distance = returnDirection.magnitude;
            float returnTtl = Mathf.Max(ShieldThrowMinTtl, distance / Mathf.Max(1f, _speed) + ShieldThrowReturnTtlPadding);
            if (!TrySpawnShieldProjectile(
                    _attack!,
                    returnLaunchData,
                    _thrownShield,
                    ResolveShieldRedirectSpawnPoint(hitPoint, normal, direction),
                    direction,
                    0f,
                    0f,
                    _searchRadius,
                    _speed * returnTtl,
                    returnTtl,
                    _speed,
                    0,
                    _damageDecay,
                    _hitTargets,
                    returningToOwner: true))
            {
                return false;
            }

            _transferred = true;
            return true;
        }

        private static Vector3 ResolveShieldRedirectSpawnPoint(Vector3 hitPoint, Vector3 normal, Vector3 direction)
        {
            Vector3 spawnPoint = hitPoint;
            if (normal.sqrMagnitude > 0.001f)
            {
                spawnPoint += normal.normalized * ShieldThrowRedirectSurfaceOffset;
            }

            if (direction.sqrMagnitude > 0.001f)
            {
                spawnPoint += direction.normalized * ShieldThrowReturnSpawnOffset;
            }

            return spawnPoint;
        }

        private bool TryCatchReturningShield()
        {
            if (_owner is not Humanoid owner || owner.IsDead() || _thrownShield == null)
            {
                return false;
            }

            float catchRadius = Mathf.Max(ShieldThrowReturnCatchRadius, owner.GetRadius() + 0.75f);
            if ((owner.GetCenterPoint() - transform.position).sqrMagnitude > catchRadius * catchRadius)
            {
                return false;
            }

            return TryReturnShieldToOwner(owner);
        }

        private bool TryReturnShieldToOwner(Humanoid owner)
        {
            Stopwatch? totalPerf = ShieldPerformanceLog.Start();
            string result = "completed";
            string shieldName = _thrownShield?.m_dropPrefab?.name ?? _thrownShield?.m_shared?.m_name ?? "<null>";
            Inventory? inventory = owner.GetInventory();
            try
            {
                if (inventory == null || _thrownShield == null)
                {
                    result = "missingInventoryOrShield";
                    return false;
                }

                _thrownShield.m_equipped = false;
                Stopwatch? stepPerf = ShieldPerformanceLog.Start();
                bool canAdd = inventory.CanAddItem(_thrownShield);
                ShieldPerformanceLog.Stop(
                    stepPerf,
                    "shieldThrow.return.canAdd",
                    () => $"owner={owner.name} shield={shieldName} canAdd={canAdd}");
                if (!canAdd)
                {
                    result = "dropNoInventorySpace";
                    DropThrownShield(_thrownShield, _lastPosition, transform.rotation);
                    _dropped = true;
                    DestroyCurrentProjectile();
                    return true;
                }

                stepPerf = ShieldPerformanceLog.Start();
                bool added = inventory.AddItem(_thrownShield);
                ShieldPerformanceLog.Stop(
                    stepPerf,
                    "shieldThrow.return.addItem",
                    () => $"owner={owner.name} shield={shieldName} added={added}");
                if (!added)
                {
                    result = "dropAddFailed";
                    DropThrownShield(_thrownShield, _lastPosition, transform.rotation);
                    _dropped = true;
                    DestroyCurrentProjectile();
                    return true;
                }

                _transferred = true;
                stepPerf = ShieldPerformanceLog.Start();
                EquipReturnedShieldNowOrLater(owner, _thrownShield);
                ShieldPerformanceLog.Stop(
                    stepPerf,
                    "shieldThrow.return.equipDispatch",
                    () => $"owner={owner.name} shield={shieldName} equipped={_thrownShield.m_equipped}");
                DestroyCurrentProjectile();
                return true;
            }
            finally
            {
                ShieldPerformanceLog.Stop(
                    totalPerf,
                    "shieldThrow.return.total",
                    () => $"owner={owner.name} shield={shieldName} result={result}");
            }
        }

        private void DestroyCurrentProjectile()
        {
            if (_projectile != null)
            {
                Stopwatch? perf = ShieldPerformanceLog.Start();
                SecondaryAttackManager.DestroyProjectileObject(_projectile.gameObject);
                ShieldPerformanceLog.Stop(
                    perf,
                    "shieldThrow.return.destroy",
                    () => $"projectile={_projectile.name}");
            }

            enabled = false;
        }

        private bool HasAuthority()
        {
            ZNetView? nview = _projectile != null ? _projectile.GetComponent<ZNetView>() : GetComponent<ZNetView>();
            return nview == null || !nview.IsValid() || nview.IsOwner();
        }
    }

    private static void SetShieldChargeActive(Character character, bool active, float cooldown = 0f, ItemDrop.ItemData? shield = null)
    {
        if (character == null)
        {
            return;
        }

        ShieldChargeRuntimeState state = ShieldChargeRuntimeStates.GetValue(character, _ => new ShieldChargeRuntimeState());
        state.Active = active;
        if (!active && cooldown > 0f)
        {
            state.CooldownUntil = Mathf.Max(state.CooldownUntil, Time.time + cooldown);
            state.ChargeCooldownDuration = cooldown;
            state.ShieldIcon = ResolveShieldIcon(shield) ?? state.ShieldIcon;
            ShieldChargeCooldownStatusSystem.Apply(character, shield, cooldown);
        }
    }

    private sealed class ShieldChargeController : MonoBehaviour
    {
        private Attack _attack = null!;
        private Rigidbody _body = null!;
        private Vector3 _direction;
        private HashSet<Character> _hitTargets = null!;
        private float _remainingDistance;
        private float _speed;
        private float _damage;
        private float _pushForce;
        private float _hitRadius;
        private float _hitHeightOffset;
        private float _collisionRadius;
        private float _cooldown;
        private float _vfxForwardOffset;
        private float _vfxHeightOffset;
        private bool _skillRaised;
        private bool _stopped;
        private bool _loggedFirstStep;

        public void Initialize(Attack attack, float travelDistance, float damage, float pushForce, float hitRadius, float configuredSpeed, float cooldown, float vfxForwardOffset, float vfxHeightOffset)
        {
            _attack = attack;
            _body = attack.m_character.GetComponent<Rigidbody>();
            SetShieldChargeActive(attack.m_character, true);
            _hitTargets = ShieldChargeAttackStates.GetValue(attack, _ => new ShieldChargeAttackState()).HitTargets;
            _direction = SecondaryAttackManager.GetSentinelForward(attack.m_character);
            _remainingDistance = travelDistance;
            _speed = configuredSpeed > 0f
                ? configuredSpeed
                : Mathf.Max(10f, travelDistance / 0.35f);
            _damage = damage;
            _pushForce = pushForce;
            _hitRadius = hitRadius;
            _hitHeightOffset = Mathf.Max(0.9f, attack.m_character.GetCenterPoint().y - attack.m_character.transform.position.y);
            _collisionRadius = Mathf.Max(0.2f, attack.m_character.GetRadius() * 0.85f);
            _cooldown = Mathf.Max(0f, cooldown);
            _vfxForwardOffset = vfxForwardOffset;
            _vfxHeightOffset = vfxHeightOffset;
        }

        private void FixedUpdate()
        {
            if (_stopped)
            {
                return;
            }

            if (_attack == null || _attack.m_character == null || _attack.m_character.IsDead() || _body == null)
            {
                StopChargeMotion();
                Destroy(gameObject);
                return;
            }

            if (!SecondaryAttackManager.HasCharacterAuthority(_attack.m_character))
            {
                StopChargeMotion();
                Destroy(gameObject);
                return;
            }

            if (_remainingDistance <= 0f)
            {
                StopChargeMotion();
                Destroy(gameObject);
                return;
            }

            float stepDistance = Mathf.Min(_remainingDistance, _speed * Time.fixedDeltaTime);
            Vector3 start = _body.position;
            bool blocked = TryResolveChargeEndPoint(start, stepDistance, out Vector3 end, out float traveledDistance, out Vector3 blockedImpactPoint);
            Vector3 hitPointOffset = _direction * (_hitRadius * ShieldChargeHitPointForwardOffsetFactor);
            Vector3 sweepStart = start + Vector3.up * _hitHeightOffset + hitPointOffset;
            Vector3 sweepEnd = end + Vector3.up * _hitHeightOffset + hitPointOffset;
            bool impactFound = TryFindShieldChargeImpact(_attack, sweepStart, sweepEnd, _hitRadius, _hitTargets, out Character? _, out float impactProgress, out Vector3 impactPoint);
            if (!_loggedFirstStep)
            {
                _loggedFirstStep = true;
            }

            if (impactFound)
            {
                traveledDistance *= impactProgress;
                end = start + _direction * traveledDistance;
            }

            _attack.m_character.transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
            Vector3 currentVelocity = _body.linearVelocity;
            _body.linearVelocity = new Vector3(0f, currentVelocity.y, 0f);
            _body.MovePosition(end);
            _remainingDistance -= traveledDistance;
            Vector3 resolvedImpactPoint = impactFound ? impactPoint : blockedImpactPoint;
            if (impactFound || blocked)
            {
                bool impactApplied = TryApplyShieldChargeImpact(
                    _attack,
                    resolvedImpactPoint,
                    _direction,
                    _damage,
                    _pushForce,
                    _hitRadius,
                    _hitTargets,
                    ref _skillRaised,
                    applyLowerDamagePerHit: true);
                if (impactApplied)
                {
                    PlayShieldChargeBullseyeEffect(_attack.m_character, _direction, _hitHeightOffset, _collisionRadius + 0.35f, _vfxHeightOffset, _vfxForwardOffset);
                    CreateShieldHitEffects(_attack, resolvedImpactPoint, Quaternion.identity);
                }
            }

            if (blocked || impactFound)
            {
                StopChargeMotion();
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_attack != null && _attack.m_character != null)
            {
                SetShieldChargeActive(_attack.m_character, false, _cooldown, _attack.m_weapon);
            }

            StopChargeMotion();
        }

        private void StopChargeMotion()
        {
            if (_stopped || _body == null)
            {
                return;
            }

            Vector3 currentVelocity = _body.linearVelocity;
            _body.linearVelocity = new Vector3(0f, currentVelocity.y, 0f);
            _remainingDistance = 0f;
            enabled = false;
            _stopped = true;
        }

        private bool TryResolveChargeEndPoint(Vector3 start, float requestedDistance, out Vector3 end, out float traveledDistance, out Vector3 impactPoint)
        {
            Vector3 castOrigin = start + Vector3.up * _hitHeightOffset;
            float castDistance = Mathf.Max(0f, requestedDistance) + 0.05f;
            if (castDistance > 0f &&
                Physics.SphereCast(castOrigin, _collisionRadius, _direction, out RaycastHit hit, castDistance, SecondaryAttackManager.GetShieldChargeCollisionMask(), QueryTriggerInteraction.Ignore))
            {
                float safeDistance = Mathf.Max(0f, hit.distance - 0.05f);
                traveledDistance = Mathf.Min(requestedDistance, safeDistance);
                end = start + _direction * traveledDistance;
                impactPoint = hit.point;
                return true;
            }

            traveledDistance = requestedDistance;
            end = start + _direction * requestedDistance;
            impactPoint = end + Vector3.up * _hitHeightOffset;
            return false;
        }
    }

}
