using UnityEngine;

namespace CaptainValheim;

internal static partial class SecondaryAttackManager
{
    private static bool GetNormalizedShieldReflectEnabled(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Shield?.Reflect != null;
    }

    private static float GetNormalizedShieldReflectStaminaFactor(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Shield?.Reflect?.StaminaFactor ?? 1f;
    }

    private static float GetNormalizedShieldReflectionFactor(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Shield?.Reflect?.ReflectionFactor ?? 0f;
    }

    private static bool GetNormalizedShieldBlockChargeEnabled(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Shield?.BlockCharge != null;
    }

    private static int? GetNormalizedShieldBlockChargeCount(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Shield?.BlockCharge?.ChargeCount;
    }

    private static float? GetNormalizedShieldBlockChargeDecayTime(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Shield?.BlockCharge?.DecayTime;
    }

    private static float? GetNormalizedShieldBlockChargeBlockingDecayFactor(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Shield?.BlockCharge?.BlockingDecayFactor;
    }

    internal static SecondaryAttackDefinition CreateEffectOnlyDefinition(
        string prefabName,
        NormalizedWeaponConfig weaponConfig)
    {
        return new SecondaryAttackDefinition
        {
            PrefabName = prefabName,
            AppliesSecondaryOverride = false,
            Behavior = new EffectOnlySecondaryBehavior(),
            AttackAnimation = "",
            HasCustomAttackAnimation = false,
            ShieldProjectileReflect = GetNormalizedShieldReflectEnabled(weaponConfig),
            ShieldProjectileReflectStaminaFactor = Mathf.Max(0f, GetNormalizedShieldReflectStaminaFactor(weaponConfig)),
            ShieldProjectileReflectionFactor = GetNormalizedShieldReflectionFactor(weaponConfig),
            ShieldBlockCharge = GetNormalizedShieldBlockChargeEnabled(weaponConfig),
            ShieldBlockChargeCount = GetNormalizedShieldBlockChargeCount(weaponConfig),
            ShieldBlockChargeDecayTime = GetNormalizedShieldBlockChargeDecayTime(weaponConfig),
            ShieldBlockChargeBlockingDecayFactor = GetNormalizedShieldBlockChargeBlockingDecayFactor(weaponConfig)
        };
    }

}
