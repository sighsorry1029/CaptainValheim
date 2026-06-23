using UnityEngine;

namespace CaptainValheim;

internal static partial class SecondaryAttackDefinitionCompiler
{
    internal static bool TryCreateDefinition(
        SecondaryAttackDefinitionBuildContext buildContext,
        string prefabName,
        ItemDrop itemDrop,
        NormalizedWeaponConfig weaponConfig,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        if (sharedData == null)
        {
            return false;
        }

        DefinitionFeatures features = AnalyzeDefinitionFeatures(weaponConfig);
        DefinitionValidationResult validation = ValidateDefinitionRequest(prefabName, sharedData, weaponConfig, features);
        switch (validation.Disposition)
        {
            case DefinitionValidationDisposition.EffectOnly:
                definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig);
                return true;
            case DefinitionValidationDisposition.Skip:
                return false;
            default:
                return TryCreateValidatedDefinition(
                    buildContext,
                    prefabName,
                    sharedData,
                    validation.PrimaryAttack ?? new Attack(),
                    weaponConfig,
                    features,
                    out definition);
        }
    }
}
