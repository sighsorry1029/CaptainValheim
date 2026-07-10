using System;
using System.Collections.Generic;

namespace CaptainValheim;

internal sealed class SecondaryAttackCompiledSnapshot
{
    public static readonly SecondaryAttackCompiledSnapshot Empty = new(new NormalizedSecondaryAttackConfigFile());

    public SecondaryAttackCompiledSnapshot(NormalizedSecondaryAttackConfigFile config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public NormalizedSecondaryAttackConfigFile Config { get; }

    public IReadOnlyDictionary<string, NormalizedWeaponConfig> Weapons => Config.Weapons;

    public NormalizedWeaponConfig? GlobalShieldFallback => Config.GlobalShieldFallback;
}

internal sealed class SecondaryAttackAppliedWorldSnapshot
{
    public static readonly SecondaryAttackAppliedWorldSnapshot Empty =
        new(new Dictionary<string, SecondaryAttackDefinition>(StringComparer.OrdinalIgnoreCase));

    public SecondaryAttackAppliedWorldSnapshot(
        IReadOnlyDictionary<string, SecondaryAttackDefinition> definitionsByPrefabName)
    {
        DefinitionsByPrefabName = definitionsByPrefabName ?? throw new ArgumentNullException(nameof(definitionsByPrefabName));
    }

    public IReadOnlyDictionary<string, SecondaryAttackDefinition> DefinitionsByPrefabName { get; }
}
