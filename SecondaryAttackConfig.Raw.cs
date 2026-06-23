namespace CaptainValheim;

internal sealed class ShieldWeaponConfig
{
    public ShieldPrimaryAttackConfig? PrimaryAttack { get; set; }

    public ShieldThrowConfig? Throw { get; set; }

    public ShieldChargeConfig? Charge { get; set; }

    public ShieldReflectConfig? Reflect { get; set; }

    public ShieldBlockChargeConfig? BlockCharge { get; set; }
}

internal sealed class ShieldPrimaryAttackConfig
{
    public bool? Enabled { get; set; }

    public float? DamageFactor { get; set; }

    public float? PushFactor { get; set; }

    public float? StaminaFactor { get; set; }

    public float? DurabilityFactor { get; set; }
}

internal sealed class ShieldThrowConfig
{
    public bool? Enabled { get; set; }

    public string? Animation { get; set; }

    public float? DamageFactor { get; set; }

    public float? PushFactor { get; set; }

    public float? StaminaFactor { get; set; }

    public float? DurabilityFactor { get; set; }

    public int? Targets { get; set; }

    public float? DamageDecay { get; set; }

    public float? RadiusFactor { get; set; }

    public float? TtlFactor { get; set; }
}

internal sealed class ShieldChargeConfig
{
    public bool? Enabled { get; set; }

    public float? DamageFactor { get; set; }

    public float? PushFactor { get; set; }

    public float? StaminaFactor { get; set; }

    public float? Distance { get; set; }

    public float? Speed { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? DurabilityFactor { get; set; }

    public float? HitRadiusFactor { get; set; }
}

internal sealed class ShieldReflectConfig
{
    public bool? Enabled { get; set; }

    public float? StaminaFactor { get; set; }

    public float? ReflectionFactor { get; set; }
}

internal sealed class ShieldBlockChargeConfig
{
    public bool? Enabled { get; set; }

    public int? ChargeCount { get; set; }

    public float? DecayTime { get; set; }

    public float? BlockingDecayFactor { get; set; }
}
