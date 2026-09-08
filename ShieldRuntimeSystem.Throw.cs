using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CaptainValheim;

internal readonly struct ProjectileLaunchData
{
    internal ProjectileLaunchData(
        GameObject? projectilePrefab,
        float projectileVelocity,
        float projectileVelocityMin,
        float attackHitNoise,
        bool useRandomVelocity)
    {
        ProjectilePrefab = projectilePrefab;
        ProjectileVelocity = projectileVelocity;
        ProjectileVelocityMin = projectileVelocityMin;
        AttackHitNoise = attackHitNoise;
        UseRandomVelocity = useRandomVelocity;
    }

    internal GameObject? ProjectilePrefab { get; }

    internal float ProjectileVelocity { get; }

    internal float ProjectileVelocityMin { get; }

    internal float AttackHitNoise { get; }

    internal bool UseRandomVelocity { get; }

    internal bool IsValid => ProjectilePrefab != null;
}

internal static partial class ShieldRuntimeSystem
{
    private const string ShieldThrowCatapultProjectilePrefabName = "Catapult_Ammo_Projectile";
    private const string ShieldThrowProjectileMarkerKey = "CaptainValheim_ShieldThrowProjectile";
    private const string ShieldThrowProjectileVisualRootName = "CaptainValheim_ShieldThrowVisualRoot";
    private const string ThrownShieldPickupMarkerKey = "CaptainValheim_ThrownShieldPickup";
    private const string ShieldThrowImpactAoePrefabName = "Catapult_Ammo_Projectile_AOE";
    private const string ShieldThrowImpactSfxChildName = "sfx";
    private const string ArrowHitSfxPrefabName = "sfx_arrow_hit";
    private const float ShieldThrowForceReference = 20f;
    private const float ShieldThrowMinTtl = 0.3f;
    private const float ShieldThrowReturnCatchRadius = 1.25f;
    private const float ShieldThrowReturnSpawnOffset = 0.25f;
    private const float ShieldThrowRedirectSurfaceOffset = 0.15f;
    private const float ShieldThrowReturnTtlPadding = 0.25f;
    private const float ShieldThrowReturnCollisionGraceSeconds = 0.12f;
    private const float ShieldThrowReturnedShieldEquipRetrySeconds = 1f;
    private const float ShieldThrowReturnedShieldEquipRetryInterval = 0.1f;
    private const float ShieldThrowDefaultHitRadius = 0.7f;
    private const float ShieldThrowCatapultProjectileSpeed = 18f;

    private static readonly ConditionalWeakTable<Humanoid, ReturnedShieldEquipState> ReturnedShieldEquipStates = new();
    private static readonly RaycastHit[] AimRayHits = new RaycastHit[64];
    private static ProjectileLaunchData _shieldThrowTemplateLaunchData;
    private static string _shieldThrowTemplateSource = string.Empty;

    private static readonly List<Renderer> ShieldProjectileRendererBuffer = new();

    internal static void ResetTransientState()
    {
        _shieldThrowTemplateLaunchData = default;
        _shieldThrowTemplateSource = string.Empty;
    }

    internal static void UpdateReturnedShieldAutoEquip(Humanoid humanoid)
    {
        if (humanoid == null || !ReturnedShieldEquipStates.TryGetValue(humanoid, out ReturnedShieldEquipState? state))
        {
            return;
        }

        if (state.Shield == null || state.Shield.m_equipped || Time.time > state.RetryUntil)
        {
            ReturnedShieldEquipStates.Remove(humanoid);
            return;
        }

        if (Time.frameCount < state.NextRetryFrame || Time.time < state.NextRetry)
        {
            return;
        }

        state.NextRetry = Time.time + ShieldThrowReturnedShieldEquipRetryInterval;
        humanoid.EquipItem(state.Shield);
        if (state.Shield.m_equipped)
        {
            ReturnedShieldEquipStates.Remove(humanoid);
        }
    }

    private static void EquipReturnedShieldNowOrLater(Humanoid humanoid, ItemDrop.ItemData shield)
    {
        if (humanoid == null || shield == null)
        {
            return;
        }

        ReturnedShieldEquipStates.Remove(humanoid);
        ReturnedShieldEquipStates.Add(humanoid, new ReturnedShieldEquipState(shield));
    }

    private static void StartShieldThrow(Attack attack, SecondaryAttackDefinition definition)
    {
        ShieldSpecialSecondaryBehavior? behavior = definition.ShieldSpecial;
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

            Character? hitCharacter = ProjectileAccess.GetHitCharacter(hit.collider);
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

    private static float CalculateShieldThrowProjectileSpeed(ProjectileLaunchData launchData)
    {
        float speed = launchData.UseRandomVelocity
            ? UnityEngine.Random.Range(launchData.ProjectileVelocityMin, launchData.ProjectileVelocity)
            : launchData.ProjectileVelocity;
        return Mathf.Max(18f, speed);
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
            launchData = default;
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
            launchData = default;
            return false;
        }

        Projectile? projectile = resolvedAttack.m_attackProjectile.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.m_canChangeVisuals = true;
        }

        launchData = new ProjectileLaunchData(
            resolvedAttack.m_attackProjectile,
            resolvedAttack.m_projectileVel,
            resolvedAttack.m_projectileVelMin,
            resolvedAttack.m_attackHitNoise,
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
            launchData = default;
            return false;
        }

        Projectile? projectile = projectilePrefab.GetComponent<Projectile>();
        launchData = new ProjectileLaunchData(
            projectilePrefab,
            ShieldThrowCatapultProjectileSpeed,
            ShieldThrowCatapultProjectileSpeed,
            projectile?.m_hitNoise ?? 0f,
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

        ItemDrop.ItemData equippedShield = attack.m_weapon;
        bool wasEquipped = equippedShield.m_equipped;
        thrownShield = equippedShield.Clone();
        thrownShield.m_stack = 1;
        thrownShield.m_equipped = false;
        player.UnequipItem(equippedShield, triggerEquipEffects: false);
        Inventory inventory = player.GetInventory();
        if (inventory == null || !inventory.RemoveItem(equippedShield, 1))
        {
            if (wasEquipped)
            {
                player.EquipItem(equippedShield);
            }

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
        float ttl,
        float speed,
        int remainingChains,
        float damageDecay,
        HashSet<Character> hitTargets,
        bool allowSkillRaise = true,
        bool returningToOwner = false)
    {
        string shieldName = "<null>";
        GameObject? projectileObject = null;
        try
        {
            if (thrownShield == null)
            {
                return false;
            }

            shieldName = thrownShield.m_dropPrefab?.name ?? "<null>";
            if (!launchData.IsValid)
            {
                return false;
            }

            if (direction.sqrMagnitude < 0.001f)
            {
                direction = SecondaryAttackManager.GetSentinelForward(attack.m_character);
            }

            direction.Normalize();
            projectileObject = Object.Instantiate(launchData.ProjectilePrefab!, spawnPoint, Quaternion.LookRotation(direction));

            Projectile? projectile = projectileObject.GetComponent<Projectile>();
            IProjectile? projectileInterface = projectileObject.GetComponent<IProjectile>();
            if (projectile == null || projectileInterface == null)
            {
                SecondaryAttackManager.DestroyProjectileObject(projectileObject);
                return false;
            }

            ConfigureShieldProjectileInstance(projectile, thrownShield, ttl);

            HitData hitData = CreateShieldHitData(attack, direction, spawnPoint, damage, pushForce);
            if (returningToOwner)
            {
                hitData.m_statusEffectHash = 0;
            }

            if (!allowSkillRaise || returningToOwner)
            {
                hitData.m_skillRaiseAmount = 0f;
            }

            projectile.m_adrenaline = 0f;
            projectileInterface.Setup(attack.m_character, direction * speed, launchData.AttackHitNoise, hitData, thrownShield, null);
            projectile.m_adrenaline = 0f;

            IgnoreShieldProjectileOwnerCollisions(projectileObject, attack.m_character);

            ApplyShieldProjectileVisual(projectile, thrownShield);

            attack.m_weapon.m_lastProjectile = projectileObject;
            ShieldProjectileController controller = projectileObject.AddComponent<ShieldProjectileController>();
            controller.Initialize(attack, projectile, thrownShield, remainingChains, searchRadius, speed, ttl, damageDecay, hitTargets, returningToOwner);

            return true;
        }
        catch (Exception exception)
        {
            if (attack.m_weapon.m_lastProjectile == projectileObject)
            {
                attack.m_weapon.m_lastProjectile = null;
            }

            if (projectileObject != null)
            {
                SecondaryAttackManager.DestroyProjectileObject(projectileObject);
            }

            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to create shield throw projectile for '{shieldName}'. The shield will be returned to the world. {exception}");
            return false;
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

        ThrowProjectileVisualSpin.Ensure(projectile.m_visual);
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
        Transform? existingVisualRoot = projectile.transform.Find(ShieldThrowProjectileVisualRootName);
        if (existingVisualRoot != null)
        {
            projectile.m_visual = existingVisualRoot.gameObject;
            projectile.m_canChangeVisuals = true;
            ThrowProjectileVisualSpin.Ensure(projectile.m_visual);
            return;
        }

        HideShieldProjectileSourcePresentation(projectile);

        GameObject visualRoot = new(ShieldThrowProjectileVisualRootName);
        visualRoot.transform.SetParent(projectile.transform, false);
        visualRoot.layer = projectile.gameObject.layer;
        projectile.m_visual = visualRoot;
        projectile.m_canChangeVisuals = true;

        ThrowProjectileVisualSpin.Ensure(projectile.m_visual);
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

        ZNetView? nview = projectile.GetComponent<ZNetView>();
        bool nviewValid = nview != null && nview.IsValid();
        if (projectile.m_canChangeVisuals && projectile.m_visual != null && nviewValid)
        {
            nview!.GetZDO().Set(ZDOVars.s_visual, thrownShield.m_dropPrefab.name);
            projectile.UpdateVisual();
            ThrowProjectileVisualSpin.Ensure(projectile.m_visual);
            return;
        }

        GameObject? attachPrefab = ResolveAttachGameObject(thrownShield.m_dropPrefab);
        ApplyShieldProjectileCachedVisual(projectile, thrownShield, attachPrefab);
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
        GameObject? attachPrefab)
    {
        GameObject? previousVisual = projectile.m_visual;
        GameObject visual;
        if (attachPrefab != null)
        {
            visual = Object.Instantiate(attachPrefab, projectile.transform, false);
            visual.name = $"{attachPrefab.name}(ShieldProjectileVisual)";
        }
        else
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "CaptainValheim_ShieldProjectileFallback";
            Object.Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(projectile.transform, false);
            visual.transform.localScale = new Vector3(0.6f, 0.08f, 0.6f);
        }

        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        if (previousVisual != null && previousVisual != visual)
        {
            previousVisual.SetActive(false);
        }

        visual.GetComponentInChildren<IEquipmentVisual>()?.Setup(thrownShield.m_variant);
        projectile.m_visual = visual;

        ThrowProjectileVisualSpin.Ensure(projectile.m_visual);
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

    private static void DropThrownShield(ItemDrop.ItemData thrownShield, Vector3 position, Quaternion rotation)
    {
        if (thrownShield == null)
        {
            return;
        }

        thrownShield.m_equipped = false;
        ItemDrop droppedShield = ItemDrop.DropItem(thrownShield, 1, position + Vector3.up * 0.25f, rotation);
        MarkThrownShieldForAutoEquip(droppedShield);
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

    internal static bool ShouldHandleShieldProjectileHit(Projectile projectile, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        ShieldProjectileController? controller = projectile != null
            ? projectile.GetComponent<ShieldProjectileController>()
            : null;
        if (controller == null)
        {
            return false;
        }

        controller.HandleHit(collider, hitPoint, water, normal);
        return true;
    }

    private sealed class ReturnedShieldEquipState
    {
        public ReturnedShieldEquipState(ItemDrop.ItemData shield)
        {
            Shield = shield;
            RetryUntil = Time.time + ShieldThrowReturnedShieldEquipRetrySeconds;
            NextRetry = Time.time;
            NextRetryFrame = Time.frameCount + 1;
        }

        public ItemDrop.ItemData Shield { get; }

        public float RetryUntil { get; }

        public float NextRetry { get; set; }

        public int NextRetryFrame { get; }
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
            Character? target = ProjectileAccess.GetHitCharacter(collider);
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
            Character? hitTarget = ProjectileAccess.GetHitCharacter(collider);
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
            if (hitTarget == null && owner != null && TryStartReturnToOwner(hitPoint, normal))
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
                nextLaunchData = default;
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

            Character? character = ProjectileAccess.GetHitCharacter(collider);
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
                        ? SecondaryAttackRuntimeContext.ResolveAdrenalineFactor(activeAttack)
                        : 1f;
                SecondaryAttackRuntimeContext.TryGrantAdrenalineOnce(
                    _attack,
                    character,
                    adrenalineFactor,
                    "shield:throw");
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
                returnLaunchData = default;
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
            Inventory? inventory = owner.GetInventory();
            if (inventory == null || _thrownShield == null)
            {
                return false;
            }

            _thrownShield.m_equipped = false;
            if (!inventory.CanAddItem(_thrownShield) || !inventory.AddItem(_thrownShield))
            {
                DropThrownShield(_thrownShield, _lastPosition, transform.rotation);
                _dropped = true;
                DestroyCurrentProjectile();
                return true;
            }

            _transferred = true;
            EquipReturnedShieldNowOrLater(owner, _thrownShield);
            DestroyCurrentProjectile();
            return true;
        }

        private void DestroyCurrentProjectile()
        {
            if (_projectile != null)
            {
                SecondaryAttackManager.DestroyProjectileObject(_projectile.gameObject);
            }

            enabled = false;
        }

        private bool HasAuthority()
        {
            ZNetView? nview = _projectile != null ? _projectile.GetComponent<ZNetView>() : GetComponent<ZNetView>();
            return nview == null || !nview.IsValid() || nview.IsOwner();
        }
    }

}

internal sealed class ThrowProjectileVisualSpin : MonoBehaviour
{
    private const float DegreesPerSecond = 720f;

    private void LateUpdate()
    {
        transform.Rotate(Vector3.up, DegreesPerSecond * Time.deltaTime, Space.World);
    }

    internal static void Ensure(GameObject? visual)
    {
        if (visual == null)
        {
            return;
        }

        ThrowProjectileVisualSpin spin =
            visual.GetComponent<ThrowProjectileVisualSpin>() ??
            visual.AddComponent<ThrowProjectileVisualSpin>();
        spin.enabled = true;
    }
}
