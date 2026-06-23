namespace CaptainValheim;

internal static partial class SecondaryAttackDefinitionCompiler
{
    private readonly struct DefinitionFeatures
    {
        public DefinitionFeatures(
            bool hasShieldConfig,
            bool usesShieldSpecials,
            bool usesShieldReflect,
            bool usesShieldBlockCharge)
        {
            HasShieldConfig = hasShieldConfig;
            UsesShieldSpecials = usesShieldSpecials;
            UsesShieldReflect = usesShieldReflect;
            UsesShieldBlockCharge = usesShieldBlockCharge;
        }

        public bool HasShieldConfig { get; }

        public bool UsesShieldSpecials { get; }

        public bool UsesShieldReflect { get; }

        public bool UsesShieldBlockCharge { get; }

        public bool WantsSecondaryOverride => UsesShieldSpecials;
    }

    private static DefinitionFeatures AnalyzeDefinitionFeatures(NormalizedWeaponConfig weaponConfig)
    {
        bool hasShieldConfig = weaponConfig.Shield != null;
        bool usesShieldSpecials = weaponConfig.Shield?.PrimaryAttack != null ||
                                  weaponConfig.Shield?.Throw != null ||
                                  weaponConfig.Shield?.Charge != null;
        bool usesShieldReflect = weaponConfig.Shield?.Reflect != null;
        bool usesShieldBlockCharge = weaponConfig.Shield?.BlockCharge != null;

        return new DefinitionFeatures(
            hasShieldConfig,
            usesShieldSpecials,
            usesShieldReflect,
            usesShieldBlockCharge);
    }

    private static bool TryCreateValidatedDefinition(
        SecondaryAttackDefinitionBuildContext buildContext,
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        Attack primaryAttack,
        NormalizedWeaponConfig weaponConfig,
        DefinitionFeatures features,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;

        if (features.UsesShieldSpecials)
        {
            return SecondaryAttackManager.TryCreateShieldSpecialDefinition(prefabName, sharedData, weaponConfig, out definition);
        }

        if (features.UsesShieldReflect || features.UsesShieldBlockCharge)
        {
            definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig);
            return true;
        }

        return false;
    }
}
