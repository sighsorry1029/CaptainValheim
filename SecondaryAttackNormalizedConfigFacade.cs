using System.Collections.Generic;

namespace CaptainValheim;

internal static class SecondaryAttackNormalizedConfigFacade
{
    internal static NormalizedSecondaryAttackConfigFile FromParsed(
        IReadOnlyDictionary<string, ShieldWeaponConfig> shields)
    {
        SecondaryAttackWeaponNormalizationResult weaponNormalization =
            SecondaryAttackWeaponConfigNormalizer.Normalize(shields);
        return new NormalizedSecondaryAttackConfigFile
        {
            Weapons = weaponNormalization.Weapons,
            GlobalShieldFallback = weaponNormalization.GlobalShieldFallback
        };
    }
}
