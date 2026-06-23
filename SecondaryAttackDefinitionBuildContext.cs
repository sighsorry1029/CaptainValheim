using UnityEngine;

namespace CaptainValheim;

internal readonly struct SecondaryAttackDefinitionBuildContext
{
    public SecondaryAttackDefinitionBuildContext(
        ObjectDB objectDb,
        bool emitMissingWarnings)
    {
        ObjectDb = objectDb;
        EmitMissingWarnings = emitMissingWarnings;
    }

    public ObjectDB ObjectDb { get; }

    public bool EmitMissingWarnings { get; }
}
