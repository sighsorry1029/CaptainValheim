using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

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
    private const string ReflectedProjectileMarkerKey = "CaptainValheim_ReflectedProjectile";
    private const string ShieldThrowImpactAoePrefabName = "Catapult_Ammo_Projectile_AOE";
    private const string ShieldThrowImpactSfxChildName = "sfx";
    private const string ArrowHitSfxPrefabName = "sfx_arrow_hit";
    private const string ShieldChargeBullseyeEffectPrefabName = "vfx_archerytarget_bullseye";
    private const string ShieldThrowChargeStartSfxPrefabName = "sfx_trollfire_attack_club_swing_up";
    private const string ShieldChargeStartVfxPrefabName = "vfx_blocked";
    private const float ShieldChargeHitRadiusReferenceForce = 20f;
    private const float ShieldChargeHitPointForwardOffsetFactor = 0.5f;
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

    private static readonly ConditionalWeakTable<Projectile, ReflectedProjectileState> ReflectedProjectiles = new();
    private static readonly ConditionalWeakTable<Character, ShieldChargeRuntimeState> ShieldChargeRuntimeStates = new();
    private static readonly ConditionalWeakTable<Humanoid, ShieldStartOverrideState> ShieldStartOverrides = new();
    private static readonly ConditionalWeakTable<Humanoid, ReturnedShieldEquipState> ReturnedShieldEquipStates = new();
    private static readonly Collider[] ShieldChargeImpactHits = new Collider[128];
    private static readonly Collider[] ShieldChargeScanHits = new Collider[128];
    private static readonly HashSet<IDestructible> ShieldChargeImpactedTargets = new();
    private static readonly HashSet<Character> ShieldChargeScanCandidates = new();
    private static readonly List<ShieldImpactTarget> ShieldChargeImpactTargets = [];
    private static readonly List<Collider> ShieldChargeTargetColliders = [];
    private static readonly RaycastHit[] AimRayHits = new RaycastHit[64];
    private static ProjectileLaunchData _shieldThrowTemplateLaunchData;
    private static string _shieldThrowTemplateSource = string.Empty;

    internal static void ResetTransientState()
    {
        _shieldThrowTemplateLaunchData = default;
        _shieldThrowTemplateSource = string.Empty;
    }

    internal static bool CanStartShieldCharge(Humanoid humanoid)
    {
        if (humanoid == null || IsShieldChargeActive(humanoid))
        {
            return false;
        }

        if (!ShieldChargeRuntimeStates.TryGetValue(humanoid, out ShieldChargeRuntimeState? state))
        {
            return true;
        }

        return Time.time >= state.CooldownUntil;
    }

    internal static bool TryGetScopedCurrentWeaponOverride(Humanoid humanoid, out ItemDrop.ItemData weapon)
    {
        weapon = null!;
        if (!ShieldStartOverrides.TryGetValue(humanoid, out ShieldStartOverrideState? state))
        {
            return false;
        }

        weapon = state.Weapon;
        return true;
    }

    internal static bool TryGetDefinition(
        ItemDrop.ItemData weapon,
        out SecondaryAttackDefinition definition)
    {
        definition = null!;
        return weapon?.m_dropPrefab != null &&
               SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.TryGetValue(
                   weapon.m_dropPrefab.name,
                   out definition!);
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

    internal static bool HandleStartAttackPrefix(
        Humanoid humanoid,
        bool secondaryAttack,
        ref bool result,
        ItemDrop.ItemData leftItem,
        ItemDrop.ItemData rightItem)
    {
        if (IsShieldChargeActive(humanoid))
        {
            result = false;
            return false;
        }

        if (!secondaryAttack)
        {
            if (TryGetShieldOnlyPrimary(
                    humanoid,
                    leftItem,
                    rightItem,
                    out ItemDrop.ItemData shieldWeapon,
                    out SecondaryAttackDefinition definition))
            {
                BeginShieldPrimaryStart(humanoid, shieldWeapon, definition);
            }

            return true;
        }

        if (!TryGetShieldOnlySecondary(
                humanoid,
                leftItem,
                rightItem,
                out ItemDrop.ItemData secondaryShieldWeapon,
                out SecondaryAttackDefinition secondaryDefinition))
        {
            return true;
        }

        ShieldSpecialMode mode = humanoid is Player player
            ? ResolveShieldSpecialMode(player, secondaryDefinition)
            : ShieldSpecialMode.Throw;

        if (mode != ShieldSpecialMode.Charge)
        {
            BeginShieldSecondaryStart(
                humanoid,
                secondaryShieldWeapon,
                secondaryDefinition,
                mode);
            return true;
        }

        if (!CanStartShieldCharge(humanoid))
        {
            result = false;
            return false;
        }

        result = TryStartShieldChargeDirect(
            humanoid,
            secondaryShieldWeapon,
            secondaryDefinition);
        return false;
    }

    private static void BeginShieldPrimaryStart(
        Humanoid humanoid,
        ItemDrop.ItemData shieldWeapon,
        SecondaryAttackDefinition definition)
    {
        ShieldStartOverrides.Remove(humanoid);
        ShieldStartOverrideState state = new(
            shieldWeapon,
            definition,
            ShieldSpecialMode.PrimaryAttack,
            secondaryAttack: false);
        Attack sourceAttack = humanoid.m_unarmedWeapon != null
            ? SecondaryAttackManager.CloneAttack(humanoid.m_unarmedWeapon.m_itemData.m_shared.m_attack)
            : SecondaryAttackManager.CloneAttack(shieldWeapon.m_shared.m_attack);

        if (TryCalculateShieldSpecialRawStaminaCost(
                shieldWeapon,
                definition,
                ShieldSpecialMode.PrimaryAttack,
                out float rawAttackStamina))
        {
            sourceAttack.m_attackStamina = rawAttackStamina;
        }

        state.ApplyAttackOverride(sourceAttack);
        ShieldStartOverrides.Add(humanoid, state);
    }

    private static void BeginShieldSecondaryStart(
        Humanoid humanoid,
        ItemDrop.ItemData shieldWeapon,
        SecondaryAttackDefinition definition,
        ShieldSpecialMode mode)
    {
        BeginShieldAttackStart(humanoid, shieldWeapon, definition, mode, secondaryAttack: true);
    }

    internal static bool TryStartShieldChargeDirect(Humanoid humanoid, ItemDrop.ItemData shieldWeapon, SecondaryAttackDefinition definition)
    {
        if (humanoid == null ||
            shieldWeapon == null ||
            definition?.ShieldSpecial is not { } behavior ||
            !behavior.HasShieldCharge ||
            behavior.ShieldChargeDistance <= 0f)
        {
            return false;
        }

        if (!TryCreateDirectShieldChargeAttack(humanoid, shieldWeapon, definition, out Attack? attack))
        {
            return false;
        }

        if (TryCalculateShieldSpecialRawStaminaCost(shieldWeapon, definition, ShieldSpecialMode.Charge, out float rawAttackStamina))
        {
            attack.m_attackStamina = rawAttackStamina;
        }

        float staminaCost = attack.GetAttackStamina();
        if (staminaCost > 0f && !humanoid.HaveStamina(staminaCost))
        {
            return false;
        }

        SecondaryAttackRuntimeContext.SetActiveAttack(attack, new ActiveSecondaryAttack(definition, ShieldSpecialMode.Charge));
        SecondaryAttackRuntimeContext.ResetAdrenaline(attack);
        SecondaryAttackManager.PlayTriggeredAttackEffects(attack, behavior.ShieldChargeDurabilityFactor);
        StartShieldCharge(attack, definition);
        return true;
    }

    internal static void EndShieldAttackStart(Humanoid humanoid, bool startedAttack)
    {
        if (!ShieldStartOverrides.TryGetValue(humanoid, out ShieldStartOverrideState? state))
        {
            return;
        }

        state.Restore();
        if (startedAttack && humanoid.m_currentAttack != null)
        {
            RegisterActiveAttack(humanoid.m_currentAttack, state.Definition, state.Mode);
        }

        ShieldStartOverrides.Remove(humanoid);
    }

    internal static bool TryGetShieldOnlyPrimary(Humanoid humanoid, ItemDrop.ItemData? leftItem, ItemDrop.ItemData? rightItem, out ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        if (!TryGetShieldOnlyWeapon(humanoid, leftItem, rightItem, out weapon, out definition))
        {
            return false;
        }

        return definition.ShieldSpecial?.HasShieldPrimaryAttack ?? false;
    }

    internal static bool TryGetShieldOnlySecondary(Humanoid humanoid, ItemDrop.ItemData? leftItem, ItemDrop.ItemData? rightItem, out ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        if (!TryGetShieldOnlyWeapon(humanoid, leftItem, rightItem, out weapon, out definition))
        {
            return false;
        }

        ShieldSpecialSecondaryBehavior? behavior = definition.ShieldSpecial;
        return behavior != null &&
               (behavior.HasShieldThrow ||
                (behavior.HasShieldCharge && behavior.ShieldChargeDistance > 0f));
    }

    internal static ShieldSpecialMode ResolveShieldSpecialMode(Player player, SecondaryAttackDefinition definition)
    {
        ShieldSpecialSecondaryBehavior? shieldBehavior = definition.ShieldSpecial;
        if (shieldBehavior == null)
        {
            return ShieldSpecialMode.Throw;
        }

        bool canThrow = shieldBehavior.HasShieldThrow;
        bool canCharge = shieldBehavior.HasShieldCharge && shieldBehavior.ShieldChargeDistance > 0f;
        if (canCharge && player.IsBlocking())
        {
            return ShieldSpecialMode.Charge;
        }

        if (canThrow)
        {
            return ShieldSpecialMode.Throw;
        }

        if (canCharge)
        {
            return ShieldSpecialMode.Charge;
        }

        return ShieldSpecialMode.Throw;
    }

    internal static void RegisterActiveAttack(
        Attack attack,
        SecondaryAttackDefinition definition,
        ShieldSpecialMode shieldMode)
    {
        if (attack == null || definition?.ShieldSpecial == null)
        {
            return;
        }

        ActiveSecondaryAttack activeAttack = new(definition, shieldMode);
        SecondaryAttackRuntimeContext.SetActiveAttack(attack, activeAttack);
        SecondaryAttackRuntimeContext.ResetAdrenaline(attack);
        if (shieldMode == ShieldSpecialMode.Charge)
        {
            TriggerShieldSpecial(attack, activeAttack);
        }
    }

    internal static bool TryHandleCustomAttackTrigger(
        Attack attack,
        out ShieldPrimaryTriggerState primaryTriggerState)
    {
        primaryTriggerState = default;
        if (!SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
            activeAttack == null ||
            activeAttack.Definition.ShieldSpecial == null)
        {
            return false;
        }

        if (attack.m_character.IsStaggering())
        {
            return true;
        }

        if (activeAttack.ShieldMode == ShieldSpecialMode.PrimaryAttack)
        {
            BeginShieldPrimaryVanillaTrigger(attack, activeAttack, out primaryTriggerState);
            return false;
        }

        if (!activeAttack.Triggered)
        {
            TriggerShieldSpecial(attack, activeAttack);
        }

        return true;
    }

    private static void TriggerShieldSpecial(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        if (activeAttack.Triggered)
        {
            return;
        }

        if (activeAttack.ShieldMode == ShieldSpecialMode.PrimaryAttack)
        {
            return;
        }

        activeAttack.Triggered = true;
        SecondaryAttackManager.PlayTriggeredAttackEffects(attack, SecondaryAttackManager.ResolveActiveAttackDurabilityFactor(activeAttack));

        switch (activeAttack.ShieldMode)
        {
            case ShieldSpecialMode.Charge:
                StartShieldCharge(attack, activeAttack.Definition);
                break;
            default:
                StartShieldThrow(attack, activeAttack.Definition);
                break;
        }
    }

    internal static void BeginShieldPrimaryVanillaTrigger(
        Attack attack,
        ActiveSecondaryAttack activeAttack,
        out ShieldPrimaryTriggerState state)
    {
        state = default;
        if (attack?.m_weapon?.m_shared == null ||
            activeAttack?.Definition?.ShieldSpecial is not { } behavior ||
            !behavior.HasShieldPrimaryAttack)
        {
            return;
        }

        activeAttack.Triggered = true;
        float expectedSkillFactor = ResolveExpectedVanillaShieldPrimarySkillFactor(attack);
        float baseDamage = Mathf.Max(0f, GetShieldBlockPower(attack) * behavior.ShieldPrimaryAttackDamageFactor);
        float basePush = Mathf.Max(0f, attack.m_weapon.GetDeflectionForce() * behavior.ShieldPrimaryAttackPushFactor);
        if (expectedSkillFactor > 0.001f)
        {
            baseDamage /= expectedSkillFactor;
            basePush /= expectedSkillFactor;
        }

        EffectList? hitEffectFallback = !HasEffect(attack.m_weapon.m_shared.m_hitEffect) && !HasEffect(attack.m_hitEffect)
            ? ResolveShieldHitEffectFallback(attack)
            : null;
        state = new ShieldPrimaryTriggerState(attack.m_weapon.m_shared, hitEffectFallback);
        state.Apply(baseDamage, basePush);
    }

    internal static void EndShieldPrimaryVanillaTrigger(ref ShieldPrimaryTriggerState state)
    {
        state.Restore();
    }

    private static float ResolveExpectedVanillaShieldPrimarySkillFactor(Attack attack)
    {
        if (attack?.m_character == null || attack.m_weapon?.m_shared == null)
        {
            return 1f;
        }

        float skillFactor = Mathf.Clamp01(attack.m_character.GetSkillFactor(attack.m_weapon.m_shared.m_skillType));
        return Mathf.Lerp(0.4f, 1f, skillFactor);
    }

    private static void CreateShieldHitEffects(Attack attack, Vector3 point, Quaternion rotation)
    {
        bool created = CreateEffectIfAvailable(attack?.m_weapon?.m_shared?.m_hitEffect, point, rotation);
        created |= CreateEffectIfAvailable(attack?.m_hitEffect, point, rotation);
        if (!created)
        {
            CreateEffectIfAvailable(ResolveShieldHitEffectFallback(attack), point, rotation);
        }
    }

    private static EffectList? ResolveShieldHitEffectFallback(Attack? attack)
    {
        EffectList? blockEffect = attack?.m_weapon?.m_shared?.m_blockEffect;
        if (HasEffect(blockEffect))
        {
            return blockEffect;
        }

        EffectList? unarmedHitEffect = null;
        if (attack?.m_character is Humanoid humanoid &&
            humanoid.m_unarmedWeapon?.m_itemData?.m_shared != null)
        {
            unarmedHitEffect = humanoid.m_unarmedWeapon.m_itemData.m_shared.m_hitEffect;
        }

        if (HasEffect(unarmedHitEffect))
        {
            return unarmedHitEffect;
        }

        return null;
    }

    private static bool CreateEffectIfAvailable(EffectList? effects, Vector3 point, Quaternion rotation)
    {
        if (!HasEffect(effects))
        {
            return false;
        }

        effects!.Create(point, rotation);
        return true;
    }

    private static bool HasEffect(EffectList? effects)
    {
        return effects != null && effects.HasEffects();
    }

    private static void BeginShieldAttackStart(
        Humanoid humanoid,
        ItemDrop.ItemData shieldWeapon,
        SecondaryAttackDefinition definition,
        ShieldSpecialMode mode,
        bool secondaryAttack)
    {
        ShieldStartOverrides.Remove(humanoid);
        ShieldStartOverrideState state = new(shieldWeapon, definition, mode, secondaryAttack);
        string animationOverride = ResolveShieldAttackAnimationOverride(definition, mode);
        if (!string.IsNullOrWhiteSpace(animationOverride))
        {
            state.ApplyAnimationOverride(animationOverride);
        }

        if (TryCalculateShieldSpecialRawStaminaCost(
                shieldWeapon,
                definition,
                mode,
                out float rawAttackStamina))
        {
            state.ApplyAttackStaminaOverride(rawAttackStamina);
        }

        ShieldStartOverrides.Add(humanoid, state);
    }

    private static string ResolveShieldAttackAnimationOverride(SecondaryAttackDefinition definition, ShieldSpecialMode mode)
    {
        if (mode != ShieldSpecialMode.Throw)
        {
            return string.Empty;
        }

        ShieldSpecialSecondaryBehavior? behavior = definition.ShieldSpecial;
        return behavior != null && !string.IsNullOrWhiteSpace(behavior.ShieldThrowAnimation)
            ? behavior.ShieldThrowAnimation
            : "battleaxe_attack1";
    }

    private static bool TryCreateDirectShieldChargeAttack(Humanoid humanoid, ItemDrop.ItemData shieldWeapon, SecondaryAttackDefinition definition, out Attack attack)
    {
        attack = null!;
        if (humanoid == null || shieldWeapon == null)
        {
            return false;
        }

        Attack sourceAttack = shieldWeapon.m_shared?.m_secondaryAttack ?? new Attack();
        ItemDrop? prefabItemDrop = shieldWeapon.m_dropPrefab != null ? shieldWeapon.m_dropPrefab.GetComponent<ItemDrop>() : null;
        if (ObjectDB.instance != null && prefabItemDrop != null)
        {
            sourceAttack = SecondaryAttackManager.ResolveSourceAttack(prefabItemDrop);
        }

        attack = SecondaryAttackManager.BuildSecondaryAttack(sourceAttack);
        attack.m_character = humanoid;
        attack.m_weapon = shieldWeapon;
        return true;
    }

    private static bool TryGetShieldOnlyWeapon(Humanoid humanoid, ItemDrop.ItemData? leftItem, ItemDrop.ItemData? rightItem, out ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        weapon = null!;
        definition = null!;
        if (humanoid is not Player player || player != Player.m_localPlayer)
        {
            return false;
        }

        if (rightItem != null || leftItem == null || leftItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
        {
            return false;
        }

        if (!TryGetDefinition(leftItem, out definition) || definition.ShieldSpecial == null)
        {
            return false;
        }

        weapon = leftItem;
        return true;
    }

    internal static bool IsReflectedProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return false;
        }

        if (ReflectedProjectiles.TryGetValue(projectile, out _))
        {
            return true;
        }

        ZNetView? nview = projectile.GetComponent<ZNetView>();
        return nview != null &&
               nview.IsValid() &&
               nview.GetZDO() != null &&
               nview.GetZDO().GetBool(ReflectedProjectileMarkerKey, false);
    }

    internal static void MarkReflectedProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        ReflectedProjectiles.Remove(projectile);
        ReflectedProjectiles.Add(projectile, new ReflectedProjectileState());

        ZNetView? nview = projectile.GetComponent<ZNetView>();
        if (nview != null && nview.IsValid() && nview.IsOwner() && nview.GetZDO() != null)
        {
            nview.GetZDO().Set(ReflectedProjectileMarkerKey, true);
        }
    }

    internal static bool IsShieldChargeActive(Humanoid humanoid)
    {
        return humanoid != null &&
               ShieldChargeRuntimeStates.TryGetValue(humanoid, out ShieldChargeRuntimeState? state) &&
               state.Active;
    }

    private static float GetShieldBlockPower(Attack attack)
    {
        return attack.m_weapon.GetBlockPower(
            attack.m_character.GetSkillFactor(Skills.SkillType.Blocking));
    }

    private static bool CanShieldAttackHitCharacter(Attack attack, Character target)
    {
        if (attack == null ||
            attack.m_character == null ||
            attack.m_weapon == null ||
            target == null ||
            target == attack.m_character)
        {
            return false;
        }

        Character attacker = attack.m_character;
        bool isEnemy = BaseAI.IsEnemy(attacker, target) ||
                       (target.GetBaseAI() is { } targetAi &&
                        targetAi.IsAggravatable() &&
                        attacker.IsPlayer());
        if (((!attack.m_hitFriendly || attacker.IsTamed()) && !attacker.IsPlayer() && !isEnemy) ||
            (!attack.m_weapon.m_shared.m_tamedOnly &&
             attacker.IsPlayer() &&
             !attacker.IsPVPEnabled() &&
             !isEnemy) ||
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

    private static HitData CreateShieldHitData(
        Attack attack,
        Vector3 direction,
        Vector3 hitPoint,
        float damage,
        float pushForce)
    {
        HitData hitData = new()
        {
            m_toolTier = (short)attack.m_weapon.m_shared.m_toolTier,
            m_pushForce = pushForce,
            m_backstabBonus = attack.m_weapon.m_shared.m_backstabBonus,
            m_staggerMultiplier = 1f,
            m_blockable = attack.m_weapon.m_shared.m_blockable,
            m_dodgeable = attack.m_weapon.m_shared.m_dodgeable,
            m_skill = attack.m_weapon.m_shared.m_skillType,
            m_skillRaiseAmount = attack.m_raiseSkillAmount,
            m_skillLevel = attack.m_character.GetSkillLevel(attack.m_weapon.m_shared.m_skillType),
            m_itemLevel = (short)attack.m_weapon.m_quality,
            m_itemWorldLevel = (byte)attack.m_weapon.m_worldLevel,
            m_point = hitPoint,
            m_dir = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : SecondaryAttackManager.GetSentinelForward(attack.m_character),
            m_healthReturn = attack.m_attackHealthReturnHit
        };
        hitData.m_damage.m_blunt = damage;
        hitData.m_statusEffectHash = ResolveAttackStatusEffectHash(attack.m_weapon);
        hitData.SetAttacker(attack.m_character);
        hitData.m_hitType = attack.m_character is Player
            ? HitData.HitType.PlayerHit
            : HitData.HitType.EnemyHit;
        attack.m_character.GetSEMan().ModifyAttack(
            attack.m_weapon.m_shared.m_skillType,
            ref hitData);
        return hitData;
    }

    private static int ResolveAttackStatusEffectHash(ItemDrop.ItemData weapon)
    {
        StatusEffect statusEffect = weapon.m_shared.m_attackStatusEffect;
        if (statusEffect == null)
        {
            return 0;
        }

        return weapon.m_shared.m_attackStatusEffectChance >= 1f ||
               UnityEngine.Random.Range(0f, 1f) <
               weapon.m_shared.m_attackStatusEffectChance
            ? statusEffect.NameHash()
            : 0;
    }

    private static void PlayShieldThrowChargeStartSfx(Attack attack)
    {
        if (attack?.m_character == null)
        {
            return;
        }

        GameObject? sfxPrefab =
            ZNetScene.instance?.GetPrefab(ShieldThrowChargeStartSfxPrefabName);
        if (sfxPrefab == null)
        {
            if (SecondaryAttackManager.TryMarkCompatibilityWarningReported(
                    "shield_throw_charge_start_sfx_missing"))
            {
                CaptainValheimPlugin.ModLogger.LogWarning(
                    $"Shield throw/charge start SFX prefab " +
                    $"'{ShieldThrowChargeStartSfxPrefabName}' was not found.");
            }

            return;
        }

        Transform origin = attack.m_character.transform;
        GameObject sfxInstance =
            UnityEngine.Object.Instantiate(sfxPrefab, origin.position, origin.rotation);
        UnityEngine.Object.Destroy(sfxInstance, 6f);
    }

    private sealed class ReflectedProjectileState
    {
    }

    private sealed class ShieldChargeRuntimeState
    {
        public bool Active { get; set; }
        public float CooldownUntil { get; set; }
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

    internal struct ShieldPrimaryTriggerState
    {
        private readonly ItemDrop.ItemData.SharedData? _sharedData;
        private readonly HitData.DamageTypes _originalDamages;
        private readonly HitData.DamageTypes _originalDamagesPerLevel;
        private readonly float _originalAttackForce;
        private readonly EffectList? _originalHitEffect;
        private readonly EffectList? _hitEffectFallback;
        private readonly bool _overrideHitEffect;
        private bool _applied;

        internal ShieldPrimaryTriggerState(ItemDrop.ItemData.SharedData sharedData, EffectList? hitEffectFallback)
        {
            _sharedData = sharedData;
            _originalDamages = sharedData.m_damages;
            _originalDamagesPerLevel = sharedData.m_damagesPerLevel;
            _originalAttackForce = sharedData.m_attackForce;
            _originalHitEffect = sharedData.m_hitEffect;
            _hitEffectFallback = hitEffectFallback;
            _overrideHitEffect = hitEffectFallback != null && !HasEffect(sharedData.m_hitEffect);
            _applied = false;
        }

        internal void Apply(float bluntDamage, float attackForce)
        {
            if (_sharedData == null)
            {
                return;
            }

            _applied = true;
            _sharedData.m_damages = new HitData.DamageTypes
            {
                m_blunt = Mathf.Max(0f, bluntDamage)
            };
            _sharedData.m_damagesPerLevel = new HitData.DamageTypes();
            _sharedData.m_attackForce = Mathf.Max(0f, attackForce);
            if (_overrideHitEffect && _hitEffectFallback != null)
            {
                _sharedData.m_hitEffect = _hitEffectFallback;
            }
        }

        internal void Restore()
        {
            if (!_applied || _sharedData == null)
            {
                return;
            }

            _sharedData.m_damages = _originalDamages;
            _sharedData.m_damagesPerLevel = _originalDamagesPerLevel;
            _sharedData.m_attackForce = _originalAttackForce;
            if (_overrideHitEffect)
            {
                _sharedData.m_hitEffect = _originalHitEffect;
            }

            _applied = false;
        }
    }

    private readonly struct ShieldImpactTarget
    {
        public ShieldImpactTarget(IDestructible destructible, Collider collider)
        {
            Destructible = destructible;
            Collider = collider;
        }

        public IDestructible Destructible { get; }
        public Collider Collider { get; }
    }

    private sealed class ShieldStartOverrideState
    {
        public ShieldStartOverrideState(
            ItemDrop.ItemData weapon,
            SecondaryAttackDefinition definition,
            ShieldSpecialMode mode,
            bool secondaryAttack)
        {
            Weapon = weapon;
            Definition = definition;
            Mode = mode;
            SecondaryAttack = secondaryAttack;
        }

        public ItemDrop.ItemData Weapon { get; }
        public SecondaryAttackDefinition Definition { get; }
        public ShieldSpecialMode Mode { get; }
        public bool SecondaryAttack { get; }

        private string? OriginalAnimation { get; set; }
        private float? OriginalAttackStamina { get; set; }
        private Attack? OriginalAttack { get; set; }
        private Attack TargetAttack => SecondaryAttack ? Weapon.m_shared.m_secondaryAttack : Weapon.m_shared.m_attack;

        public void ApplyAttackOverride(Attack attackOverride)
        {
            OriginalAttack = SecondaryAttackManager.CloneAttack(TargetAttack);
            if (SecondaryAttack)
            {
                Weapon.m_shared.m_secondaryAttack = SecondaryAttackManager.CloneAttack(attackOverride);
                return;
            }

            Weapon.m_shared.m_attack = SecondaryAttackManager.CloneAttack(attackOverride);
        }

        public void ApplyAnimationOverride(string attackAnimation)
        {
            OriginalAnimation = TargetAttack.m_attackAnimation;
            TargetAttack.m_attackAnimation = attackAnimation;
        }

        public void ApplyAttackStaminaOverride(float rawAttackStamina)
        {
            OriginalAttackStamina = TargetAttack.m_attackStamina;
            TargetAttack.m_attackStamina = rawAttackStamina;
        }

        public void Restore()
        {
            if (OriginalAttack != null)
            {
                if (SecondaryAttack)
                {
                    Weapon.m_shared.m_secondaryAttack = SecondaryAttackManager.CloneAttack(OriginalAttack);
                }
                else
                {
                    Weapon.m_shared.m_attack = SecondaryAttackManager.CloneAttack(OriginalAttack);
                }

                return;
            }

            if (OriginalAnimation != null)
            {
                TargetAttack.m_attackAnimation = OriginalAnimation;
            }

            if (OriginalAttackStamina.HasValue)
            {
                TargetAttack.m_attackStamina = OriginalAttackStamina.Value;
            }
        }
    }
}
