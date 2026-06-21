using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using ProjectileLaunchData = CaptainValheim.ProjectileRuntimeSystem.ProjectileLaunchData;

namespace CaptainValheim;

internal static partial class ShieldRuntimeSystem
{
    private const string ShieldThrowCatapultProjectilePrefabName = "Catapult_Ammo_Projectile";
    private const string ShieldThrowProjectileMarkerKey = "CaptainValheim_ShieldThrowProjectile";
    private const string ShieldThrowProjectileVisualRootName = "CaptainValheim_ShieldThrowVisualRoot";
    private const string ThrownShieldPickupMarkerKey = "CaptainValheim_ThrownShieldPickup";
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

    private static readonly ConditionalWeakTable<Projectile, ShieldProjectileController> ShieldProjectileControllers = new();
    private static readonly ConditionalWeakTable<Projectile, ReflectedProjectileState> ReflectedProjectiles = new();
    private static readonly ConditionalWeakTable<Attack, ShieldChargeAttackState> ShieldChargeAttackStates = new();
    private static readonly ConditionalWeakTable<Character, ShieldChargeRuntimeState> ShieldChargeRuntimeStates = new();
    private static readonly ConditionalWeakTable<Humanoid, ShieldStartOverrideState> ShieldStartOverrides = new();
    private static readonly ConditionalWeakTable<Humanoid, ReturnedShieldEquipState> ReturnedShieldEquipStates = new();
    private static readonly List<ShieldPrimarySharedOverrideState> ShieldPrimarySharedOverrides = [];
    private static readonly Collider[] ShieldChargeImpactHits = new Collider[128];
    private static ProjectileLaunchData _shieldThrowTemplateLaunchData = ProjectileLaunchData.Invalid;
    private static string _shieldThrowTemplateSource = string.Empty;

    internal static void ResetTransientState()
    {
        _shieldThrowTemplateLaunchData = ProjectileLaunchData.Invalid;
        _shieldThrowTemplateSource = string.Empty;
    }

    internal static bool CanStartShieldCharge(Humanoid humanoid, SecondaryAttackDefinition? definition = null)
    {
        if (humanoid == null || IsShieldChargeActive(humanoid))
        {
            return false;
        }

        if (definition == null ||
            !ShieldChargeRuntimeStates.TryGetValue(humanoid, out ShieldChargeRuntimeState? state))
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

        if (Time.time < state.NextRetry)
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
        humanoid.EquipItem(shield);
        if (shield.m_equipped)
        {
            return;
        }

        ReturnedShieldEquipStates.Add(humanoid, new ReturnedShieldEquipState(shield));
    }

    internal static void BeginShieldPrimaryStart(Humanoid humanoid, ItemDrop.ItemData shieldWeapon)
    {
        ShieldStartOverrides.Remove(humanoid);
        ShieldStartOverrideState state = new(shieldWeapon, ShieldSpecialMode.PrimaryAttack, secondaryAttack: false);
        Attack sourceAttack = humanoid.m_unarmedWeapon != null
            ? SecondaryAttackManager.CloneAttack(humanoid.m_unarmedWeapon.m_itemData.m_shared.m_attack)
            : SecondaryAttackManager.CloneAttack(shieldWeapon.m_shared.m_attack);

        if (SecondaryAttackRuntimeFacade.TryGetDefinition(shieldWeapon, out SecondaryAttackDefinition definition) &&
            TryCalculateShieldSpecialRawStaminaCost(shieldWeapon, definition, ShieldSpecialMode.PrimaryAttack, out float rawAttackStamina))
        {
            sourceAttack.m_attackStamina = rawAttackStamina;
        }

        state.ApplyAttackOverride(sourceAttack);
        ShieldStartOverrides.Add(humanoid, state);
    }

    internal static void BeginShieldSecondaryStart(Humanoid humanoid, ItemDrop.ItemData shieldWeapon, ShieldSpecialMode mode)
    {
        BeginShieldAttackStart(humanoid, shieldWeapon, mode, secondaryAttack: true);
    }

    internal static bool TryStartShieldChargeDirect(Humanoid humanoid, ItemDrop.ItemData shieldWeapon, SecondaryAttackDefinition definition)
    {
        if (humanoid == null || shieldWeapon == null || definition?.Behavior is not ShieldSpecialSecondaryBehavior behavior || !behavior.HasShieldCharge)
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
        SecondaryAttackAdrenalineSystem.Reset(attack);
        SecondaryAttackManager.PlayTriggeredAttackEffects(attack, behavior.ShieldChargeDurabilityFactor);
        StartShieldCharge(attack, definition);
        return true;
    }

    internal static bool EndShieldAttackStart(Humanoid humanoid, bool startedAttack)
    {
        if (!ShieldStartOverrides.TryGetValue(humanoid, out ShieldStartOverrideState? state))
        {
            return false;
        }

        state.RestoreAnimationOverride();
        if (startedAttack && humanoid.m_currentAttack != null)
        {
            SecondaryAttackRuntimeFacade.RegisterActiveAttack(humanoid.m_currentAttack, state.Weapon, state.Mode);
        }

        ShieldStartOverrides.Remove(humanoid);
        return true;
    }

    internal static bool TryGetShieldOnlyPrimary(Humanoid humanoid, ItemDrop.ItemData? leftItem, ItemDrop.ItemData? rightItem, out ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        if (!TryGetShieldOnlyWeapon(humanoid, leftItem, rightItem, out weapon, out definition))
        {
            return false;
        }

        return (definition.Behavior as ShieldSpecialSecondaryBehavior)?.HasShieldPrimaryAttack ?? false;
    }

    internal static bool TryGetShieldOnlySecondary(Humanoid humanoid, ItemDrop.ItemData? leftItem, ItemDrop.ItemData? rightItem, out ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        if (!TryGetShieldOnlyWeapon(humanoid, leftItem, rightItem, out weapon, out definition))
        {
            return false;
        }

        ShieldSpecialSecondaryBehavior? behavior = definition.Behavior as ShieldSpecialSecondaryBehavior;
        return behavior != null && (behavior.HasShieldThrow || behavior.HasShieldCharge);
    }

    internal static ShieldSpecialMode ResolveShieldSpecialMode(Player player, SecondaryAttackDefinition definition)
    {
        ShieldSpecialSecondaryBehavior? shieldBehavior = definition.Behavior as ShieldSpecialSecondaryBehavior;
        if (shieldBehavior == null)
        {
            return ShieldSpecialMode.Throw;
        }

        bool canThrow = shieldBehavior.HasShieldThrow;
        bool canCharge = shieldBehavior.HasShieldCharge && shieldBehavior.ShieldChargeDistance > 0f;
        if (canCharge && player.IsBlocking())
        {
            SecondaryAttackManager.LogShieldDebug("Shield special resolved to Charge: player is currently blocking.");
            return ShieldSpecialMode.Charge;
        }

        if (canThrow)
        {
            SecondaryAttackManager.LogShieldDebug("Shield special resolved to Throw: player is not currently blocking and throw is configured.");
            return ShieldSpecialMode.Throw;
        }

        if (canCharge)
        {
            SecondaryAttackManager.LogShieldDebug("Shield special resolved to Charge: throw is not configured.");
            return ShieldSpecialMode.Charge;
        }

        SecondaryAttackManager.LogShieldDebug("Shield special resolved to Throw: no secondary shield mode is configured.");
        return ShieldSpecialMode.Throw;
    }

    internal static bool IsShieldChargeActiveForDebug(Humanoid humanoid)
    {
        return IsShieldChargeActive(humanoid);
    }

    internal static bool IsShieldChargeCooldownActiveForDebug(Humanoid humanoid)
    {
        return humanoid != null &&
               ShieldChargeRuntimeStates.TryGetValue(humanoid, out ShieldChargeRuntimeState? state) &&
               state.CooldownUntil > Time.time;
    }

    internal static void TriggerShieldSpecialFromRuntimeFacade(Attack attack, ActiveSecondaryAttack activeAttack)
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

    internal static void BeginShieldPrimaryVanillaTrigger(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        if (attack?.m_weapon?.m_shared == null ||
            activeAttack?.Definition?.Behavior is not ShieldSpecialSecondaryBehavior behavior ||
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
        ShieldPrimarySharedOverrideState state = new(attack.m_weapon.m_shared, hitEffectFallback);
        state.Apply(baseDamage, basePush);
        ShieldPrimarySharedOverrides.Add(state);
    }

    internal static void EndShieldPrimaryVanillaTrigger()
    {
        if (ShieldPrimarySharedOverrides.Count == 0)
        {
            return;
        }

        int index = ShieldPrimarySharedOverrides.Count - 1;
        ShieldPrimarySharedOverrideState state = ShieldPrimarySharedOverrides[index];
        ShieldPrimarySharedOverrides.RemoveAt(index);
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

    internal static Vector3 ResolvePlayerAimDirectionForReflection(Player player, Vector3 spawnPoint, Vector3 fallbackAimDirection, float maxTravelDistance)
    {
        return ResolvePlayerAimDirection(player, spawnPoint, fallbackAimDirection, maxTravelDistance);
    }

    private static void BeginShieldAttackStart(Humanoid humanoid, ItemDrop.ItemData shieldWeapon, ShieldSpecialMode mode, bool secondaryAttack)
    {
        ShieldStartOverrides.Remove(humanoid);
        ShieldStartOverrideState state = new(shieldWeapon, mode, secondaryAttack);
        SecondaryAttackRuntimeFacade.TryGetDefinition(shieldWeapon, out SecondaryAttackDefinition definition);
        string animationOverride = ResolveShieldAttackAnimationOverride(definition, mode);
        if (!string.IsNullOrWhiteSpace(animationOverride))
        {
            state.ApplyAnimationOverride(animationOverride);
        }

        if (definition != null &&
            TryCalculateShieldSpecialRawStaminaCost(shieldWeapon, definition, mode, out float rawAttackStamina))
        {
            state.ApplyAttackStaminaOverride(rawAttackStamina);
        }

        ShieldStartOverrides.Add(humanoid, state);
    }

    private static string ResolveShieldAttackAnimationOverride(SecondaryAttackDefinition? definition, ShieldSpecialMode mode)
    {
        if (mode != ShieldSpecialMode.Throw)
        {
            return string.Empty;
        }

        ShieldSpecialSecondaryBehavior? behavior = definition?.Behavior as ShieldSpecialSecondaryBehavior;
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
            sourceAttack = SecondaryAttackManager.ResolveSourceAttack(ObjectDB.instance, prefabItemDrop, definition);
        }

        attack = SecondaryAttackManager.BuildSecondaryAttack(sourceAttack, definition);
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

        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(leftItem, out definition) || definition.BehaviorType != SecondaryAttackBehaviorType.ShieldSpecial)
        {
            return false;
        }

        weapon = leftItem;
        return true;
    }

    internal static bool IsReflectedProjectile(Projectile projectile)
    {
        return projectile != null && ReflectedProjectiles.TryGetValue(projectile, out _);
    }

    internal static void MarkReflectedProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        ReflectedProjectiles.Remove(projectile);
        ReflectedProjectiles.Add(projectile, new ReflectedProjectileState());
    }

    private static bool IsShieldChargeActive(Humanoid humanoid)
    {
        return humanoid != null &&
               ShieldChargeRuntimeStates.TryGetValue(humanoid, out ShieldChargeRuntimeState? state) &&
               state.Active;
    }

    private static Sprite? ResolveShieldIcon(ItemDrop.ItemData? shield)
    {
        return shield?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private sealed class ReflectedProjectileState
    {
    }

    private sealed class ShieldChargeAttackState
    {
        public System.Collections.Generic.HashSet<Character> HitTargets { get; } = new();
    }

    private sealed class ShieldChargeRuntimeState
    {
        public bool Active { get; set; }
        public float CooldownUntil { get; set; }
        public float ChargeCooldownDuration { get; set; }
        public Sprite? ShieldIcon { get; set; }
    }

    private sealed class ReturnedShieldEquipState
    {
        public ReturnedShieldEquipState(ItemDrop.ItemData shield)
        {
            Shield = shield;
            RetryUntil = Time.time + ShieldThrowReturnedShieldEquipRetrySeconds;
            NextRetry = Time.time;
        }

        public ItemDrop.ItemData Shield { get; }

        public float RetryUntil { get; }

        public float NextRetry { get; set; }
    }

    private readonly struct ShieldPrimarySharedOverrideState
    {
        private readonly ItemDrop.ItemData.SharedData? _sharedData;
        private readonly HitData.DamageTypes _originalDamages;
        private readonly HitData.DamageTypes _originalDamagesPerLevel;
        private readonly float _originalAttackForce;
        private readonly EffectList? _originalHitEffect;
        private readonly EffectList? _hitEffectFallback;
        private readonly bool _overrideHitEffect;

        public ShieldPrimarySharedOverrideState(ItemDrop.ItemData.SharedData sharedData, EffectList? hitEffectFallback)
        {
            _sharedData = sharedData;
            _originalDamages = sharedData.m_damages;
            _originalDamagesPerLevel = sharedData.m_damagesPerLevel;
            _originalAttackForce = sharedData.m_attackForce;
            _originalHitEffect = sharedData.m_hitEffect;
            _hitEffectFallback = hitEffectFallback;
            _overrideHitEffect = hitEffectFallback != null && !HasEffect(sharedData.m_hitEffect);
        }

        public void Apply(float bluntDamage, float attackForce)
        {
            if (_sharedData == null)
            {
                return;
            }

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

        public void Restore()
        {
            if (_sharedData == null)
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
        public ShieldStartOverrideState(ItemDrop.ItemData weapon, ShieldSpecialMode mode, bool secondaryAttack)
        {
            Weapon = weapon;
            Mode = mode;
            SecondaryAttack = secondaryAttack;
        }

        public ItemDrop.ItemData Weapon { get; }
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

        public void RestoreAnimationOverride()
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
