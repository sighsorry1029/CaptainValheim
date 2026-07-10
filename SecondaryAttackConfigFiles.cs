using System;
using System.IO;
using BepInEx;

namespace CaptainValheim;

internal static class SecondaryAttackYamlConfig
{
    internal const string FileName = "CaptainValheim.yml";
    internal const string SyncedIdentifier = "captain_valheim_yaml";
    internal const long ReloadDelayTicks = TimeSpan.TicksPerSecond;

    internal static readonly string DirectoryPath = Paths.ConfigPath;
    internal static readonly string FilePath = Path.Combine(DirectoryPath, FileName);
}
