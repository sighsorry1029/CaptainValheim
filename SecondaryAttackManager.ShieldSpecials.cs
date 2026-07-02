using UnityEngine;

namespace CaptainValheim;

internal static partial class SecondaryAttackManager
{
    // Public compatibility bridge for external integrations. Internal runtime code should call ShieldRuntimeSystem directly.
    public static bool CanStartShieldCharge(Humanoid humanoid, SecondaryAttackDefinition? definition = null)
    {
        return ShieldRuntimeSystem.CanStartShieldCharge(humanoid, definition);
    }

    public static bool TryGetScopedCurrentWeaponOverride(Humanoid humanoid, out ItemDrop.ItemData weapon)
    {
        return ShieldRuntimeSystem.TryGetScopedCurrentWeaponOverride(humanoid, out weapon);
    }

    public static void BeginShieldPrimaryStart(Humanoid humanoid, ItemDrop.ItemData shieldWeapon)
    {
        ShieldRuntimeSystem.BeginShieldPrimaryStart(humanoid, shieldWeapon);
    }

    public static void BeginShieldSecondaryStart(Humanoid humanoid, ItemDrop.ItemData shieldWeapon, ShieldSpecialMode mode)
    {
        ShieldRuntimeSystem.BeginShieldSecondaryStart(humanoid, shieldWeapon, mode);
    }

    public static bool EndShieldAttackStart(Humanoid humanoid, bool startedAttack)
    {
        return ShieldRuntimeSystem.EndShieldAttackStart(humanoid, startedAttack);
    }

    public static bool TryGetShieldOnlyPrimary(Humanoid humanoid, ItemDrop.ItemData? leftItem, ItemDrop.ItemData? rightItem, out ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        return ShieldRuntimeSystem.TryGetShieldOnlyPrimary(humanoid, leftItem, rightItem, out weapon, out definition);
    }

    public static bool TryGetShieldOnlySecondary(Humanoid humanoid, ItemDrop.ItemData? leftItem, ItemDrop.ItemData? rightItem, out ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        return ShieldRuntimeSystem.TryGetShieldOnlySecondary(humanoid, leftItem, rightItem, out weapon, out definition);
    }

    public static ShieldSpecialMode ResolveShieldSpecialMode(Player player, SecondaryAttackDefinition definition)
    {
        return ShieldRuntimeSystem.ResolveShieldSpecialMode(player, definition);
    }

    internal static bool TryCreateShieldSpecialDefinition(
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        NormalizedWeaponConfig weaponConfig,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;
        Attack sourceAttack = sharedData.m_secondaryAttack ?? new Attack();
        NormalizedShieldPrimaryAttackConfig? primaryAttackConfig = weaponConfig.Shield?.PrimaryAttack;
        NormalizedShieldThrowConfig? throwConfig = weaponConfig.Shield?.Throw;
        NormalizedShieldChargeConfig? chargeConfig = weaponConfig.Shield?.Charge;
        bool hasPrimaryAttack = primaryAttackConfig != null;
        bool hasThrow = throwConfig != null;
        bool hasCharge = chargeConfig != null;

        definition = new SecondaryAttackDefinition
        {
            PrefabName = prefabName,
            AppliesSecondaryOverride = true,
            Behavior = new ShieldSpecialSecondaryBehavior
            {
                HasShieldPrimaryAttack = hasPrimaryAttack,
                ShieldPrimaryAttackDamageFactor = hasPrimaryAttack ? Mathf.Max(0f, primaryAttackConfig!.DamageFactor) : 0f,
                ShieldPrimaryAttackPushFactor = hasPrimaryAttack ? Mathf.Max(0f, primaryAttackConfig!.PushFactor) : 0f,
                ShieldPrimaryAttackStaminaFactor = hasPrimaryAttack ? Mathf.Max(0f, primaryAttackConfig!.StaminaFactor) : 0f,
                ShieldPrimaryAttackDurabilityFactor = hasPrimaryAttack ? Mathf.Max(0f, primaryAttackConfig!.DurabilityFactor) : 1f,
                ShieldPrimaryAttackAdrenalineFactor = hasPrimaryAttack ? Mathf.Max(0f, primaryAttackConfig!.AdrenalineFactor) : 0f,
                HasShieldThrow = hasThrow,
                ShieldThrowAnimation = hasThrow ? throwConfig!.Animation : string.Empty,
                ShieldThrowTargets = hasThrow ? Mathf.Max(0, throwConfig!.Targets) : 0,
                ShieldThrowDamageFactor = hasThrow ? Mathf.Max(0f, throwConfig!.DamageFactor) : 0f,
                ShieldThrowPushFactor = hasThrow ? Mathf.Max(0f, throwConfig!.PushFactor) : 0f,
                ShieldThrowStaminaFactor = hasThrow ? Mathf.Max(0f, throwConfig!.StaminaFactor) : 0f,
                ShieldThrowDurabilityFactor = hasThrow ? Mathf.Max(0f, throwConfig!.DurabilityFactor) : 1f,
                ShieldThrowDamageDecay = hasThrow ? Mathf.Clamp01(throwConfig!.DamageDecay) : 0f,
                ShieldThrowRadiusFactor = hasThrow ? Mathf.Max(0f, throwConfig!.RadiusFactor) : 0f,
                ShieldThrowTtlFactor = hasThrow ? Mathf.Max(0f, throwConfig!.TtlFactor) : 0f,
                ShieldThrowAdrenalineFactor = hasThrow ? Mathf.Max(0f, throwConfig!.AdrenalineFactor) : 0f,
                HasShieldCharge = hasCharge,
                ShieldChargeDamageFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.DamageFactor) : 0f,
                ShieldChargePushFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.PushFactor) : 0f,
                ShieldChargeStaminaFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.StaminaFactor) : 0f,
                ShieldChargeDistance = hasCharge ? Mathf.Max(0f, chargeConfig!.Distance) : 0f,
                ShieldChargeSpeed = hasCharge ? Mathf.Max(0f, chargeConfig!.Speed) : 0f,
                ShieldChargeCooldown = hasCharge ? Mathf.Max(0f, chargeConfig!.Cooldown) : 0f,
                ShieldChargeCooldownReductionFactor = hasCharge ? Mathf.Clamp01(chargeConfig!.CooldownReductionFactor) : 0f,
                ShieldChargeDurabilityFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.DurabilityFactor) : 1f,
                ShieldChargeHitRadiusFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.HitRadiusFactor) : 0f,
                ShieldChargeAdrenalineFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.AdrenalineFactor) : 0f
            },
            AttackAnimation = sourceAttack.m_attackAnimation,
            HasCustomAttackAnimation = false,
            ShieldProjectileReflect = GetNormalizedShieldReflectEnabled(weaponConfig),
            ShieldProjectileReflectStaminaFactor = Mathf.Max(0f, GetNormalizedShieldReflectStaminaFactor(weaponConfig)),
            ShieldProjectileReflectionFactor = GetNormalizedShieldReflectionFactor(weaponConfig),
            ShieldBlockCharge = GetNormalizedShieldBlockChargeEnabled(weaponConfig),
            ShieldBlockChargeCount = GetNormalizedShieldBlockChargeCount(weaponConfig),
            ShieldBlockChargeDecayTime = GetNormalizedShieldBlockChargeDecayTime(weaponConfig),
            ShieldBlockChargeBlockingDecayFactor = GetNormalizedShieldBlockChargeBlockingDecayFactor(weaponConfig)
        };
        ApplyAttackResourceScaling(definition, sourceAttack, 1f);
        return true;
    }

    internal static void TriggerShieldSpecialForRuntimeFacade(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        ShieldRuntimeSystem.TriggerShieldSpecialFromRuntimeFacade(attack, activeAttack);
    }

    internal sealed class BlockAttackContext
    {
        public Player? Player { get; set; }
        public ItemDrop.ItemData? Blocker { get; set; }
        public SecondaryAttackDefinition? Definition { get; set; }
        public ProjectileHitContext? ProjectileContext { get; set; }
        public string ProjectileContextSource { get; set; } = string.Empty;
        public float PostResistanceBlockableDamage { get; set; }
        public float VanillaBlockStaminaCost { get; set; }
    }

    private readonly struct BlockCostAnalysis
    {
        public BlockCostAnalysis(float postResistanceBlockableDamage, float staminaCost)
        {
            PostResistanceBlockableDamage = postResistanceBlockableDamage;
            StaminaCost = staminaCost;
        }

        public float PostResistanceBlockableDamage { get; }
        public float StaminaCost { get; }
    }

}
