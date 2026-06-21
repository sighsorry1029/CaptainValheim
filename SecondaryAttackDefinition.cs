using System.Collections.Generic;

namespace CaptainValheim;

internal sealed class SecondaryAttackDefinition
{
    public string PrefabName { get; set; } = "";

    public bool AppliesSecondaryOverride { get; set; }

    public SecondaryAttackBehavior Behavior { get; set; } = new EffectOnlySecondaryBehavior();

    public SecondaryAttackBehaviorType BehaviorType => Behavior.BehaviorType;

    public string AttackAnimation { get; set; } = "";

    public bool HasCustomAttackAnimation { get; set; }

    public Attack? ConfiguredSecondaryAttack { get; set; }

    public float ResourceMultiplier { get; set; } = 1f;

    public float OutputMultiplier { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public float RawAttackHealth { get; set; }

    public float RawAttackHealthPercentage { get; set; }

    public float RawAttackStamina { get; set; }

    public float RawAttackEitr { get; set; }

    public float RawDrawStamina { get; set; }

    public float RawDrawEitr { get; set; }

    public float RawReloadStamina { get; set; }

    public float RawReloadEitr { get; set; }

    public List<ConfiguredWeaponEffectDefinition> ConfiguredEffects { get; set; } = new();

    public bool ShieldProjectileReflect { get; set; }

    public float ShieldProjectileReflectStaminaFactor { get; set; } = 1f;

    public float ShieldProjectileReflectionFactor { get; set; }

    public bool ShieldBlockCharge { get; set; }

    public int? ShieldBlockChargeCount { get; set; }

    public float? ShieldBlockChargeDecayTime { get; set; }

    public float? ShieldBlockChargeBlockingDecayFactor { get; set; }
}

internal sealed class ConfiguredWeaponEffectDefinition
{
}
