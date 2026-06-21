namespace CaptainValheim;

internal static class SecondaryAttackWorldApplyContributors
{
    internal static void BeforeDefinitions(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        CaptureOriginalObjectDbState(objectDb);
        RestoreOriginalObjectDbState(objectDb);
        ApplyObjectDbPreDefinitionSystems(objectDb, compiledSnapshot);
    }

    internal static void AfterDefinitions(
        ObjectDB objectDb,
        SecondaryAttackAppliedWorldSnapshot appliedWorldSnapshot,
        bool emitMissingWarnings)
    {
        ShieldChargeCooldownStatusSystem.RegisterStatusEffect(objectDb);
    }

    internal static void ApplyToZNetScene(
        ZNetScene scene,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
    }

    private static void CaptureOriginalObjectDbState(ObjectDB objectDb)
    {
        SecondaryAttackObjectDbStateStore.Capture(objectDb);
    }

    private static void RestoreOriginalObjectDbState(ObjectDB objectDb)
    {
        SecondaryAttackObjectDbStateStore.Restore(objectDb);
    }

    private static void ApplyObjectDbPreDefinitionSystems(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot)
    {
    }
}
