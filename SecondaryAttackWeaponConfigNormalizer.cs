using System;
using System.Collections.Generic;

namespace CaptainValheim;

internal sealed class SecondaryAttackWeaponNormalizationResult
{
    public Dictionary<string, NormalizedWeaponConfig> Weapons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public NormalizedWeaponConfig? GlobalShieldFallback { get; set; }
}

internal static class SecondaryAttackWeaponConfigNormalizer
{
    private const string GlobalFallbackKey = "Global";

    internal static SecondaryAttackWeaponNormalizationResult Normalize(
        IReadOnlyDictionary<string, ShieldWeaponConfig> shields)
    {
        Dictionary<string, NormalizedWeaponConfig> normalizedWeapons = new(StringComparer.OrdinalIgnoreCase);
        NormalizedWeaponConfig builtInShieldFallback = CreateGlobalDefaultShieldFallback();

        ShieldWeaponConfig? rawGlobalShieldFallback = null;
        foreach ((string prefabName, ShieldWeaponConfig shieldConfig) in shields)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || shieldConfig == null)
            {
                continue;
            }

            if (prefabName.Trim().Equals(GlobalFallbackKey, StringComparison.OrdinalIgnoreCase))
            {
                rawGlobalShieldFallback = shieldConfig;
                break;
            }
        }

        NormalizedWeaponConfig? globalShieldFallback = rawGlobalShieldFallback != null
            ? FromShieldRaw(rawGlobalShieldFallback, builtInShieldFallback)
            : null;
        NormalizedWeaponConfig shieldFallback = globalShieldFallback ?? builtInShieldFallback;

        foreach ((string prefabName, ShieldWeaponConfig shieldConfig) in shields)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || shieldConfig == null)
            {
                continue;
            }

            string normalizedPrefabName = prefabName.Trim();
            if (normalizedPrefabName.Equals(GlobalFallbackKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            normalizedWeapons[normalizedPrefabName] = FromShieldRaw(shieldConfig, shieldFallback);
        }

        return new SecondaryAttackWeaponNormalizationResult
        {
            Weapons = normalizedWeapons,
            GlobalShieldFallback = globalShieldFallback
        };
    }

    public static NormalizedWeaponConfig FromShieldRaw(ShieldWeaponConfig raw, NormalizedWeaponConfig? fallback = null)
    {
        fallback ??= CreateGlobalDefaultShieldFallback();
        return new NormalizedWeaponConfig
        {
            Shield = NormalizeShield(raw, fallback.Shield ?? new NormalizedShieldModeConfig())
        };
    }

    public static NormalizedWeaponConfig CreateGlobalDefaultShieldFallback()
    {
        return new NormalizedWeaponConfig
        {
            Shield = new NormalizedShieldModeConfig
            {
                PrimaryAttack = new NormalizedShieldPrimaryAttackConfig(),
                Throw = new NormalizedShieldThrowConfig(),
                Charge = new NormalizedShieldChargeConfig(),
                Reflect = new NormalizedShieldReflectConfig(),
                BlockCharge = new NormalizedShieldBlockChargeConfig()
            }
        };
    }

    private static NormalizedShieldModeConfig NormalizeShield(ShieldWeaponConfig rawShield, NormalizedShieldModeConfig fallback)
    {
        return new NormalizedShieldModeConfig
        {
            PrimaryAttack = NormalizeShieldPrimaryAttack(rawShield.PrimaryAttack, fallback.PrimaryAttack),
            Throw = NormalizeShieldThrow(rawShield.Throw, fallback.Throw),
            Charge = NormalizeShieldCharge(rawShield.Charge, fallback.Charge),
            Reflect = NormalizeShieldReflect(rawShield.Reflect, fallback.Reflect),
            BlockCharge = NormalizeShieldBlockCharge(rawShield.BlockCharge, fallback.BlockCharge)
        };
    }

    private static NormalizedShieldPrimaryAttackConfig? NormalizeShieldPrimaryAttack(
        ShieldPrimaryAttackConfig? rawPrimaryAttack,
        NormalizedShieldPrimaryAttackConfig? fallback)
    {
        if (rawPrimaryAttack == null)
        {
            return fallback;
        }

        if (rawPrimaryAttack.Enabled == false)
        {
            return null;
        }

        NormalizedShieldPrimaryAttackConfig baseConfig = fallback ?? new NormalizedShieldPrimaryAttackConfig();
        return new NormalizedShieldPrimaryAttackConfig
        {
            DamageFactor = rawPrimaryAttack.DamageFactor ?? baseConfig.DamageFactor,
            PushFactor = rawPrimaryAttack.PushFactor ?? baseConfig.PushFactor,
            StaminaFactor = rawPrimaryAttack.StaminaFactor ?? baseConfig.StaminaFactor,
            DurabilityFactor = rawPrimaryAttack.DurabilityFactor ?? baseConfig.DurabilityFactor,
            AdrenalineFactor = ShieldAdrenalineFactors.PrimaryAttack
        };
    }

    private static NormalizedShieldThrowConfig? NormalizeShieldThrow(
        ShieldThrowConfig? rawThrow,
        NormalizedShieldThrowConfig? fallback)
    {
        if (rawThrow == null)
        {
            return fallback;
        }

        if (rawThrow.Enabled == false)
        {
            return null;
        }

        NormalizedShieldThrowConfig baseConfig = fallback ?? new NormalizedShieldThrowConfig();
        return new NormalizedShieldThrowConfig
        {
            Animation = !string.IsNullOrWhiteSpace(rawThrow.Animation)
                ? rawThrow.Animation!.Trim()
                : baseConfig.Animation,
            DamageFactor = rawThrow.DamageFactor ?? baseConfig.DamageFactor,
            PushFactor = rawThrow.PushFactor ?? baseConfig.PushFactor,
            StaminaFactor = rawThrow.StaminaFactor ?? baseConfig.StaminaFactor,
            DurabilityFactor = rawThrow.DurabilityFactor ?? baseConfig.DurabilityFactor,
            Targets = rawThrow.Targets ?? baseConfig.Targets,
            DamageDecay = rawThrow.DamageDecay ?? baseConfig.DamageDecay,
            RadiusFactor = rawThrow.RadiusFactor ?? baseConfig.RadiusFactor,
            TtlFactor = rawThrow.TtlFactor ?? baseConfig.TtlFactor,
            AdrenalineFactor = ShieldAdrenalineFactors.Throw
        };
    }

    private static NormalizedShieldChargeConfig? NormalizeShieldCharge(
        ShieldChargeConfig? rawCharge,
        NormalizedShieldChargeConfig? fallback)
    {
        if (rawCharge == null)
        {
            return fallback;
        }

        if (rawCharge.Enabled == false)
        {
            return null;
        }

        NormalizedShieldChargeConfig baseConfig = fallback ?? new NormalizedShieldChargeConfig();
        return new NormalizedShieldChargeConfig
        {
            DamageFactor = rawCharge.DamageFactor ?? baseConfig.DamageFactor,
            PushFactor = rawCharge.PushFactor ?? baseConfig.PushFactor,
            StaminaFactor = rawCharge.StaminaFactor ?? baseConfig.StaminaFactor,
            Distance = rawCharge.Distance ?? baseConfig.Distance,
            Speed = rawCharge.Speed ?? baseConfig.Speed,
            Cooldown = rawCharge.Cooldown ?? baseConfig.Cooldown,
            CooldownReductionFactor = rawCharge.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
            DurabilityFactor = rawCharge.DurabilityFactor ?? baseConfig.DurabilityFactor,
            HitRadiusFactor = rawCharge.HitRadiusFactor ?? baseConfig.HitRadiusFactor,
            AdrenalineFactor = ShieldAdrenalineFactors.Charge
        };
    }

    private static NormalizedShieldReflectConfig? NormalizeShieldReflect(
        ShieldReflectConfig? rawReflect,
        NormalizedShieldReflectConfig? fallback)
    {
        if (rawReflect == null)
        {
            return fallback;
        }

        if (rawReflect.Enabled == false)
        {
            return null;
        }

        NormalizedShieldReflectConfig baseConfig = fallback ?? new NormalizedShieldReflectConfig();
        return new NormalizedShieldReflectConfig
        {
            StaminaFactor = rawReflect.StaminaFactor ?? baseConfig.StaminaFactor,
            ReflectionFactor = rawReflect.ReflectionFactor ?? baseConfig.ReflectionFactor
        };
    }

    private static NormalizedShieldBlockChargeConfig? NormalizeShieldBlockCharge(
        ShieldBlockChargeConfig? rawBlockCharge,
        NormalizedShieldBlockChargeConfig? fallback)
    {
        if (rawBlockCharge == null)
        {
            return fallback;
        }

        if (rawBlockCharge.Enabled == false)
        {
            return null;
        }

        return new NormalizedShieldBlockChargeConfig
        {
            ChargeCount = rawBlockCharge.ChargeCount ?? fallback?.ChargeCount,
            DecayTime = rawBlockCharge.DecayTime ?? fallback?.DecayTime,
            BlockingDecayFactor = rawBlockCharge.BlockingDecayFactor ?? fallback?.BlockingDecayFactor
        };
    }
}
