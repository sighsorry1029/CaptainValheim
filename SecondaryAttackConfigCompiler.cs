using System.Collections.Generic;

namespace CaptainValheim;

internal static class SecondaryAttackConfigCompiler
{
    public static SecondaryAttackCompiledSnapshot Compile(
        int snapshotId,
        SecondaryAttackParsedYaml parsedYaml)
    {
        return Compile(snapshotId, parsedYaml.Shields);
    }

    public static SecondaryAttackCompiledSnapshot Compile(
        int snapshotId,
        IReadOnlyDictionary<string, ShieldWeaponConfig> parsedShields)
    {
        return new SecondaryAttackCompiledSnapshot(
            snapshotId,
            SecondaryAttackNormalizedConfigFacade.FromParsed(parsedShields));
    }
}
