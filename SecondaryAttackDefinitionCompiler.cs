using UnityEngine;

namespace CaptainValheim;

internal static class SecondaryAttackDefinitionCompiler
{
    internal static bool TryCreateDefinition(
        string prefabName,
        ItemDrop itemDrop,
        NormalizedShieldModeConfig shieldConfig,
        bool emitWarnings,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        if (sharedData == null)
        {
            return false;
        }

        if (sharedData.m_itemType != ItemDrop.ItemData.ItemType.Shield)
        {
            if (emitWarnings &&
                SecondaryAttackManager.TryMarkCompatibilityWarningReported($"non_shield_prefab:{prefabName}"))
            {
                CaptainValheimPlugin.ModLogger.LogWarning(
                    $"Skipping {prefabName}: CaptainValheim shield features can only be used on shield prefabs.");
            }

            return false;
        }

        bool hasShieldSpecial = shieldConfig.PrimaryAttack != null ||
                                shieldConfig.Throw != null ||
                                shieldConfig.Charge is
                                {
                                    Distance: > 0f
                                };
        if (!hasShieldSpecial && shieldConfig.Reflect == null && shieldConfig.BlockCharge == null)
        {
            return false;
        }

        definition = CreateDefinition(shieldConfig);
        if (hasShieldSpecial)
        {
            definition.ShieldSpecial = CreateShieldSpecialBehavior(shieldConfig);
        }

        return true;
    }

    private static SecondaryAttackDefinition CreateDefinition(NormalizedShieldModeConfig shieldConfig)
    {
        return new SecondaryAttackDefinition
        {
            ShieldProjectileReflect = shieldConfig.Reflect != null,
            ShieldProjectileReflectStaminaFactor = Mathf.Max(0f, shieldConfig.Reflect?.StaminaFactor ?? 1f),
            ShieldProjectileReflectionFactor = shieldConfig.Reflect?.ReflectionFactor ?? 0f,
            ShieldBlockCharge = shieldConfig.BlockCharge != null,
            ShieldBlockChargeCount = shieldConfig.BlockCharge?.ChargeCount,
            ShieldBlockChargeDecayTime = shieldConfig.BlockCharge?.DecayTime,
            ShieldBlockChargeBlockingDecayFactor = shieldConfig.BlockCharge?.BlockingDecayFactor
        };
    }

    private static ShieldSpecialSecondaryBehavior CreateShieldSpecialBehavior(
        NormalizedShieldModeConfig shieldConfig)
    {
        NormalizedShieldPrimaryAttackConfig? primaryAttackConfig = shieldConfig.PrimaryAttack;
        NormalizedShieldThrowConfig? throwConfig = shieldConfig.Throw;
        NormalizedShieldChargeConfig? chargeConfig = shieldConfig.Charge;
        bool hasPrimaryAttack = primaryAttackConfig != null;
        bool hasThrow = throwConfig != null;
        bool hasCharge = chargeConfig is
        {
            Distance: > 0f
        };

        return new ShieldSpecialSecondaryBehavior
        {
            HasShieldPrimaryAttack = hasPrimaryAttack,
            ShieldPrimaryAttackDamageFactor = hasPrimaryAttack ? Mathf.Max(0f, primaryAttackConfig!.DamageFactor) : 0f,
            ShieldPrimaryAttackPushFactor = hasPrimaryAttack ? Mathf.Max(0f, primaryAttackConfig!.PushFactor) : 0f,
            ShieldPrimaryAttackStaminaFactor = hasPrimaryAttack ? Mathf.Max(0f, primaryAttackConfig!.StaminaFactor) : 0f,
            ShieldPrimaryAttackDurabilityFactor = hasPrimaryAttack ? Mathf.Max(0f, primaryAttackConfig!.DurabilityFactor) : 1f,
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
            HasShieldCharge = hasCharge,
            ShieldChargeDamageFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.DamageFactor) : 0f,
            ShieldChargePushFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.PushFactor) : 0f,
            ShieldChargeStaminaFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.StaminaFactor) : 0f,
            ShieldChargeDistance = hasCharge ? Mathf.Max(0f, chargeConfig!.Distance) : 0f,
            ShieldChargeSpeed = hasCharge ? Mathf.Max(0f, chargeConfig!.Speed) : 0f,
            ShieldChargeCooldown = hasCharge ? Mathf.Max(0f, chargeConfig!.Cooldown) : 0f,
            ShieldChargeCooldownReductionFactor = hasCharge ? Mathf.Clamp01(chargeConfig!.CooldownReductionFactor) : 0f,
            ShieldChargeDurabilityFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.DurabilityFactor) : 1f,
            ShieldChargeHitRadiusFactor = hasCharge ? Mathf.Max(0f, chargeConfig!.HitRadiusFactor) : 0f
        };
    }
}
