namespace CaptainValheim;

internal enum ShieldSpecialMode
{
    PrimaryAttack,
    Throw,
    Charge
}

internal sealed class SecondaryAttackDefinition
{
    public ShieldSpecialSecondaryBehavior? ShieldSpecial { get; set; }

    public bool AppliesSecondaryOverride => ShieldSpecial?.HasShieldThrow == true;

    public bool ShieldProjectileReflect { get; set; }

    public float ShieldProjectileReflectStaminaFactor { get; set; } = 1f;

    public float ShieldProjectileReflectionFactor { get; set; }

    public bool ShieldBlockCharge { get; set; }

    public int? ShieldBlockChargeCount { get; set; }

    public float? ShieldBlockChargeDecayTime { get; set; }

    public float? ShieldBlockChargeBlockingDecayFactor { get; set; }
}

internal sealed class ShieldSpecialSecondaryBehavior
{
    public bool HasShieldPrimaryAttack { get; set; }

    public float ShieldPrimaryAttackDamageFactor { get; set; }

    public float ShieldPrimaryAttackPushFactor { get; set; }

    public float ShieldPrimaryAttackStaminaFactor { get; set; }

    public float ShieldPrimaryAttackDurabilityFactor { get; set; } = 1f;

    public bool HasShieldThrow { get; set; }

    public string ShieldThrowAnimation { get; set; } = "battleaxe_attack1";

    public int ShieldThrowTargets { get; set; }

    public float ShieldThrowDamageFactor { get; set; }

    public float ShieldThrowPushFactor { get; set; }

    public float ShieldThrowStaminaFactor { get; set; }

    public float ShieldThrowDurabilityFactor { get; set; } = 1f;

    public float ShieldThrowDamageDecay { get; set; }

    public float ShieldThrowRadiusFactor { get; set; }

    public float ShieldThrowTtlFactor { get; set; }

    public bool HasShieldCharge { get; set; }

    public float ShieldChargeDamageFactor { get; set; }

    public float ShieldChargePushFactor { get; set; }

    public float ShieldChargeStaminaFactor { get; set; }

    public float ShieldChargeDistance { get; set; }

    public float ShieldChargeSpeed { get; set; }

    public float ShieldChargeCooldown { get; set; }

    public float ShieldChargeCooldownReductionFactor { get; set; }

    public float ShieldChargeDurabilityFactor { get; set; } = 1f;

    public float ShieldChargeHitRadiusFactor { get; set; }
}
