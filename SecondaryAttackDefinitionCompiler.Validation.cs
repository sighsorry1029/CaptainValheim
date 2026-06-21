namespace CaptainValheim;

internal static partial class SecondaryAttackDefinitionCompiler
{
    private enum DefinitionValidationDisposition
    {
        Continue,
        Skip,
        EffectOnly
    }

    private readonly struct DefinitionValidationResult
    {
        public DefinitionValidationResult(DefinitionValidationDisposition disposition, Attack? primaryAttack = null)
        {
            Disposition = disposition;
            PrimaryAttack = primaryAttack;
        }

        public DefinitionValidationDisposition Disposition { get; }

        public Attack? PrimaryAttack { get; }
    }

    private static DefinitionValidationResult ValidateDefinitionRequest(
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        NormalizedWeaponConfig weaponConfig,
        DefinitionFeatures features)
    {
        if (!features.HasShieldConfig)
        {
            return new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        if (sharedData.m_itemType != ItemDrop.ItemData.ItemType.Shield)
        {
            CaptainValheimPlugin.ModLogger.LogWarning($"Skipping {prefabName}: CaptainValheim shield features can only be used on shield prefabs.");
            return new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        if (!features.WantsSecondaryOverride)
        {
            return features.UsesShieldReflect || features.UsesShieldBlockCharge
                ? new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly)
                : new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        return new DefinitionValidationResult(
            DefinitionValidationDisposition.Continue,
            sharedData.m_attack ?? new Attack());
    }
}
