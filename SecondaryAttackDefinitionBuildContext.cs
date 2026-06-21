using System.Collections.Generic;
using UnityEngine;

namespace CaptainValheim;

internal readonly struct SecondaryAttackDefinitionBuildContext
{
    public SecondaryAttackDefinitionBuildContext(
        ObjectDB objectDb,
        IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs,
        bool emitMissingWarnings)
    {
        ObjectDb = objectDb;
        EffectConfigs = effectConfigs;
        EmitMissingWarnings = emitMissingWarnings;
    }

    public ObjectDB ObjectDb { get; }

    public IReadOnlyDictionary<string, EffectBehaviorConfig> EffectConfigs { get; }

    public bool EmitMissingWarnings { get; }
}
