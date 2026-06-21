using System;
using System.Collections.Generic;

namespace CaptainValheim;

internal sealed class NormalizedSecondaryAttackConfigFile
{
    public Dictionary<string, EffectBehaviorConfig> Effects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, NormalizedWeaponConfig> Weapons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public NormalizedWeaponConfig? GlobalShieldFallback { get; set; }
}
