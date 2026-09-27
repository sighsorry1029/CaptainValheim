using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CaptainValheim;

internal static partial class ShieldRuntimeSystem
{
    private const string ReflectedProjectileMarkerKey = "CaptainValheim_ReflectedProjectile";
    private const string ShieldThrowChargeStartSfxPrefabName = "sfx_trollfire_attack_club_swing_up";

    private static readonly ConditionalWeakTable<Projectile, ReflectedProjectileState> ReflectedProjectiles = new();
    private static readonly ConditionalWeakTable<Humanoid, ShieldStartOverrideState> ShieldStartOverrides = new();
    private static readonly ConditionalWeakTable<Player, WeaponChargeInputState> WeaponChargeInputs = new();

    internal static bool IsOneHandedMeleeWeapon(ItemDrop.ItemData? item)
    {
        Attack? primary = item?.m_shared?.m_attack;
        return item?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon &&
               primary != null && !primary.m_bowDraw &&
               primary.m_attackType is Attack.AttackType.Horizontal or Attack.AttackType.Vertical or Attack.AttackType.Area;
    }

    internal static bool IsShieldFeatureAllowed(
        Humanoid humanoid, ItemDrop.ItemData? shield, CaptainValheimPlugin.Toggle allowWithWeapon)
    {
        return shield != null && ReferenceEquals(humanoid.LeftItem, shield) &&
               IsShieldEquipmentAllowed(shield, humanoid.RightItem, allowWithWeapon);
    }

    internal static bool IsShieldEquipmentAllowed(
        ItemDrop.ItemData? leftItem, ItemDrop.ItemData? rightItem, CaptainValheimPlugin.Toggle allowWithWeapon)
    {
        return leftItem?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield &&
               (rightItem == null ||
                (allowWithWeapon == CaptainValheimPlugin.Toggle.On && IsOneHandedMeleeWeapon(rightItem)));
    }

    private static bool CanSelectWeaponCharge(Player player, out SecondaryAttackDefinition definition)
    {
        definition = null!;
        return player.RightItem != null && GameAccess.BlockingInput(player) &&
               IsShieldFeatureAllowed(player, player.LeftItem, CaptainValheimPlugin.Settings.WeaponCompatibility.AllowCharge.Value) &&
               TryGetDefinition(player.LeftItem, out definition) &&
               definition.ShieldSpecial is { HasShieldCharge: true, ShieldChargeDistance: > 0f };
    }

    // Observe the game's processed input, including toggle-block and gamepad controls.
    // An input gesture keeps its original intent until release, even when equipment,
    // block state or the server policy changes before the next physics update.
    internal static void ObserveWeaponChargeInput(Player player)
    {
        if (player != Player.m_localPlayer)
        {
            return;
        }

        bool held = GameAccess.SecondaryAttackPressed(player) || GameAccess.SecondaryAttackHeld(player);
        if (!WeaponChargeInputs.TryGetValue(player, out WeaponChargeInputState? state))
        {
            if (!held)
            {
                return;
            }

            state = new WeaponChargeInputState();
            WeaponChargeInputs.Add(player, state);
        }

        bool clearQueue = state.Claimed && !held;
        state.Observe(held, !state.Held && held && CanSelectWeaponCharge(player, out _));
        if (clearQueue)
        {
            GameAccess.QueuedSecondaryAttack(player) = 0f;
        }
    }

    internal sealed class WeaponChargeInputState
    {
        internal bool Held { get; private set; }
        internal bool Claimed { get; private set; }
        private bool _attempted;

        internal void Observe(bool held, bool wantsCharge)
        {
            if (!held)
            {
                Held = false;
                Claimed = false;
                _attempted = false;
            }
            else if (!Held)
            {
                Held = true;
                Claimed = wantsCharge;
                _attempted = false;
            }
        }

        internal bool TryBeginAttempt()
        {
            if (!Claimed || _attempted)
            {
                return false;
            }

            _attempted = true;
            return true;
        }
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

    internal static bool HandleStartAttackPrefix(
        Humanoid humanoid,
        bool secondaryAttack,
        ref bool result,
        ItemDrop.ItemData leftItem,
        ItemDrop.ItemData rightItem)
    {
        if (secondaryAttack && humanoid is Player localPlayer && localPlayer == Player.m_localPlayer &&
            WeaponChargeInputs.TryGetValue(localPlayer, out WeaponChargeInputState? input) && input.Claimed)
        {
            result = false;
            if (input.TryBeginAttempt() && localPlayer.IsBlocking() &&
                CanSelectWeaponCharge(localPlayer, out SecondaryAttackDefinition chargeDefinition) &&
                CanStartShieldCharge(localPlayer))
            {
                result = TryStartShieldChargeDirect(localPlayer, localPlayer.LeftItem, chargeDefinition);
            }

            // Consume a failed or held charge too; never route this gesture to the weapon.
            return false;
        }

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
        ShieldStartOverrides.Remove(humanoid);
        ShieldStartOverrideState state = new(shieldWeapon, definition, mode, secondaryAttack: true);
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

    internal static void EndShieldAttackStart(Humanoid humanoid, bool startedAttack)
    {
        if (!ShieldStartOverrides.TryGetValue(humanoid, out ShieldStartOverrideState? state))
        {
            return;
        }

        state.Restore();
        if (startedAttack && GameAccess.CurrentAttack(humanoid) != null)
        {
            RegisterActiveAttack(GameAccess.CurrentAttack(humanoid), state.Definition, state.Mode);
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

        if (attack.GetCharacter().IsStaggering())
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
        if (attack?.GetWeapon()?.m_shared == null ||
            activeAttack?.Definition?.ShieldSpecial is not { } behavior ||
            !behavior.HasShieldPrimaryAttack)
        {
            return;
        }

        activeAttack.Triggered = true;
        float expectedSkillFactor = ResolveExpectedVanillaShieldPrimarySkillFactor(attack);
        float baseDamage = Mathf.Max(0f, GetShieldBlockPower(attack) * behavior.ShieldPrimaryAttackDamageFactor);
        float basePush = Mathf.Max(0f, attack.GetWeapon().GetDeflectionForce() * behavior.ShieldPrimaryAttackPushFactor);
        if (expectedSkillFactor > 0.001f)
        {
            baseDamage /= expectedSkillFactor;
            basePush /= expectedSkillFactor;
        }

        EffectList? hitEffectFallback = !HasEffect(attack.GetWeapon().m_shared.m_hitEffect) && !HasEffect(attack.m_hitEffect)
            ? ResolveShieldHitEffectFallback(attack)
            : null;
        state = new ShieldPrimaryTriggerState(attack.GetWeapon().m_shared, hitEffectFallback);
        state.Apply(baseDamage, basePush);
    }

    internal static void EndShieldPrimaryVanillaTrigger(ref ShieldPrimaryTriggerState state)
    {
        state.Restore();
    }

    private static float ResolveExpectedVanillaShieldPrimarySkillFactor(Attack attack)
    {
        if (attack?.GetCharacter() == null || attack.GetWeapon()?.m_shared == null)
        {
            return 1f;
        }

        float skillFactor = Mathf.Clamp01(attack.GetCharacter().GetSkillFactor(attack.GetWeapon().m_shared.m_skillType));
        return Mathf.Lerp(0.4f, 1f, skillFactor);
    }

    private static void CreateShieldHitEffects(Attack attack, Vector3 point, Quaternion rotation)
    {
        bool created = CreateEffectIfAvailable(attack?.GetWeapon()?.m_shared?.m_hitEffect, point, rotation);
        created |= CreateEffectIfAvailable(attack?.m_hitEffect, point, rotation);
        if (!created)
        {
            CreateEffectIfAvailable(ResolveShieldHitEffectFallback(attack), point, rotation);
        }
    }

    private static EffectList? ResolveShieldHitEffectFallback(Attack? attack)
    {
        EffectList? blockEffect = attack?.GetWeapon()?.m_shared?.m_blockEffect;
        if (HasEffect(blockEffect))
        {
            return blockEffect;
        }

        EffectList? unarmedHitEffect = null;
        if (attack?.GetCharacter() is Humanoid humanoid &&
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

        ShieldSpecialSecondaryBehavior? behavior = definition.ShieldSpecial;
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

    private static float GetShieldBlockPower(Attack attack)
    {
        return attack.GetWeapon().GetBlockPower(
            attack.GetCharacter().GetSkillFactor(Skills.SkillType.Blocking));
    }

    private static bool CanShieldAttackHitCharacter(Attack attack, Character target)
    {
        if (attack == null ||
            attack.GetCharacter() == null ||
            attack.GetWeapon() == null ||
            target == null ||
            target == attack.GetCharacter())
        {
            return false;
        }

        Character attacker = attack.GetCharacter();
        bool isEnemy = BaseAI.IsEnemy(attacker, target) ||
                       (target.GetBaseAI() is { } targetAi &&
                        targetAi.IsAggravatable() &&
                        attacker.IsPlayer());
        if (((!attack.m_hitFriendly || attacker.IsTamed()) && !attacker.IsPlayer() && !isEnemy) ||
            (!attack.GetWeapon().m_shared.m_tamedOnly &&
             attacker.IsPlayer() &&
             !attacker.IsPVPEnabled() &&
             !isEnemy) ||
            (attack.GetWeapon().m_shared.m_tamedOnly && !target.IsTamed()))
        {
            return false;
        }

        if (attack.GetWeapon().m_shared.m_dodgeable && target.IsDodgeInvincible())
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
            m_toolTier = (short)attack.GetWeapon().m_shared.m_toolTier,
            m_pushForce = pushForce,
            m_backstabBonus = attack.GetWeapon().m_shared.m_backstabBonus,
            m_staggerMultiplier = 1f,
            m_blockable = attack.GetWeapon().m_shared.m_blockable,
            m_dodgeable = attack.GetWeapon().m_shared.m_dodgeable,
            m_skill = attack.GetWeapon().m_shared.m_skillType,
            m_skillRaiseAmount = attack.m_raiseSkillAmount,
            m_skillLevel = attack.GetCharacter().GetSkillLevel(attack.GetWeapon().m_shared.m_skillType),
            m_itemLevel = (short)attack.GetWeapon().m_quality,
            m_itemWorldLevel = (byte)attack.GetWeapon().m_worldLevel,
            m_point = hitPoint,
            m_dir = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : SecondaryAttackManager.GetSentinelForward(attack.GetCharacter()),
            m_healthReturn = attack.m_attackHealthReturnHit,
            m_eitrAdd = attack.m_attackEitrAdd,
            m_variant = attack.GetWeapon().m_shared.m_hitVariant
        };
        hitData.m_damage.m_blunt = damage;
        hitData.m_statusEffectHash = ResolveAttackStatusEffectHash(attack.GetWeapon());
        hitData.SetAttacker(attack.GetCharacter());
        hitData.m_hitType = attack.GetCharacter() is Player
            ? HitData.HitType.PlayerHit
            : HitData.HitType.EnemyHit;
        attack.GetCharacter().GetSEMan().ModifyAttack(
            attack.GetWeapon().m_shared.m_skillType,
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
        if (attack?.GetCharacter() == null)
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

        Transform origin = attack.GetCharacter().transform;
        GameObject sfxInstance =
            UnityEngine.Object.Instantiate(sfxPrefab, origin.position, origin.rotation);
        UnityEngine.Object.Destroy(sfxInstance, 6f);
    }

    private sealed class ReflectedProjectileState
    {
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
