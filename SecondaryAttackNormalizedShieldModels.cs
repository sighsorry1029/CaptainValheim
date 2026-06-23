namespace CaptainValheim;

internal sealed class NormalizedShieldModeConfig
{
    public NormalizedShieldPrimaryAttackConfig? PrimaryAttack { get; set; }

    public NormalizedShieldThrowConfig? Throw { get; set; }

    public NormalizedShieldChargeConfig? Charge { get; set; }

    public NormalizedShieldReflectConfig? Reflect { get; set; }

    public NormalizedShieldBlockChargeConfig? BlockCharge { get; set; }
}

internal sealed class NormalizedShieldPrimaryAttackConfig
{
    public float DamageFactor { get; set; } = 0.4f;

    public float PushFactor { get; set; } = 1f;

    public float StaminaFactor { get; set; } = 0.8f;

    public float DurabilityFactor { get; set; } = 1f;

    public float AdrenalineFactor { get; set; } = ShieldAdrenalineFactors.PrimaryAttack;
}

internal sealed class NormalizedShieldThrowConfig
{
    public string Animation { get; set; } = "battleaxe_attack1";

    public float DamageFactor { get; set; } = 0.8f;

    public float PushFactor { get; set; } = 1f;

    public float StaminaFactor { get; set; } = 2f;

    public float DurabilityFactor { get; set; } = 1f;

    public int Targets { get; set; } = 3;

    public float DamageDecay { get; set; } = 0.5f;

    public float RadiusFactor { get; set; } = 6f;

    public float TtlFactor { get; set; } = 1f;

    public float AdrenalineFactor { get; set; } = ShieldAdrenalineFactors.Throw;
}

internal sealed class NormalizedShieldChargeConfig
{
    public float DamageFactor { get; set; } = 1.2f;

    public float PushFactor { get; set; } = 2f;

    public float StaminaFactor { get; set; } = 3f;

    public float Distance { get; set; } = 4f;

    public float Speed { get; set; } = 12f;

    public float Cooldown { get; set; } = 10f;

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float DurabilityFactor { get; set; } = 2f;

    public float HitRadiusFactor { get; set; } = 0.4f;

    public float AdrenalineFactor { get; set; } = ShieldAdrenalineFactors.Charge;
}

internal static class ShieldAdrenalineFactors
{
    public const float PrimaryAttack = 1f;

    public const float Throw = 1f;

    public const float Charge = 5f;
}

internal sealed class NormalizedShieldReflectConfig
{
    public float StaminaFactor { get; set; } = 2f;

    public float ReflectionFactor { get; set; } = 0.01f;
}

internal sealed class NormalizedShieldBlockChargeConfig
{
    public int? ChargeCount { get; set; }

    public float? DecayTime { get; set; }

    public float? BlockingDecayFactor { get; set; }
}
