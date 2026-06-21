namespace CaptainValheim;

internal enum ShieldSpecialMode
{
    PrimaryAttack,
    Throw,
    Charge
}

internal enum SecondaryAttackBehaviorType
{
    EffectOnly,
    ShieldSpecial
}

internal abstract class SecondaryAttackBehavior
{
    public abstract SecondaryAttackBehaviorType BehaviorType { get; }
}

internal sealed class EffectOnlySecondaryBehavior : SecondaryAttackBehavior
{
    public override SecondaryAttackBehaviorType BehaviorType => SecondaryAttackBehaviorType.EffectOnly;
}

internal sealed class ShieldSpecialSecondaryBehavior : SecondaryAttackBehavior
{
    public override SecondaryAttackBehaviorType BehaviorType => SecondaryAttackBehaviorType.ShieldSpecial;

    public bool HasShieldPrimaryAttack { get; set; }

    public float ShieldPrimaryAttackDamageFactor { get; set; }

    public float ShieldPrimaryAttackPushFactor { get; set; }

    public float ShieldPrimaryAttackStaminaFactor { get; set; }

    public float ShieldPrimaryAttackDurabilityFactor { get; set; } = 1f;

    public float ShieldPrimaryAttackAdrenalineFactor { get; set; } = 1f;

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

    public float ShieldThrowAdrenalineFactor { get; set; } = 1f;

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

    public float ShieldChargeAdrenalineFactor { get; set; } = 1f;
}
