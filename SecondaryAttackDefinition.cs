namespace CaptainValheim;

internal sealed class SecondaryAttackDefinition
{
    public ShieldSpecialSecondaryBehavior? ShieldSpecial { get; set; }

    public bool AppliesSecondaryOverride => ShieldSpecial != null;

    public bool ShieldProjectileReflect { get; set; }

    public float ShieldProjectileReflectStaminaFactor { get; set; } = 1f;

    public float ShieldProjectileReflectionFactor { get; set; }

    public bool ShieldBlockCharge { get; set; }

    public int? ShieldBlockChargeCount { get; set; }

    public float? ShieldBlockChargeDecayTime { get; set; }

    public float? ShieldBlockChargeBlockingDecayFactor { get; set; }
}
