using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;

namespace CaptainValheim;

internal enum SecondaryAttackYamlDomainId
{
    Shields
}

internal sealed class SecondaryAttackYamlDomain
{
    internal SecondaryAttackYamlDomain(
        SecondaryAttackYamlDomainId id,
        string fileName,
        string filePath,
        string syncedIdentifier,
        Func<string> getDefaultContents)
    {
        Id = id;
        FileName = fileName;
        FilePath = filePath;
        SyncedIdentifier = syncedIdentifier;
        GetDefaultContents = getDefaultContents;
    }

    public SecondaryAttackYamlDomainId Id { get; }

    public string FileName { get; }

    public string FilePath { get; }

    public string SyncedIdentifier { get; }

    public Func<string> GetDefaultContents { get; }
}

internal static class SecondaryAttackYamlDomainRegistry
{
    internal const string ShieldsYamlFileName = "CaptainValheim.yml";
    private const string SyncedShieldsYamlIdentifier = "captain_valheim_yaml";
    internal const long ReloadDelayTicks = TimeSpan.TicksPerSecond;

    internal static readonly string ConfigDirectoryPath = Paths.ConfigPath;
    internal static readonly string ShieldsYamlFilePath = Path.Combine(ConfigDirectoryPath, ShieldsYamlFileName);

    private static readonly SecondaryAttackYamlDomain[] OrderedDomains =
    {
        new(
            SecondaryAttackYamlDomainId.Shields,
            ShieldsYamlFileName,
            ShieldsYamlFilePath,
            SyncedShieldsYamlIdentifier,
            () => SecondaryAttackDefaultYamlResources.Load(ShieldsYamlFileName)),
    };

    private static readonly Dictionary<SecondaryAttackYamlDomainId, SecondaryAttackYamlDomain> DomainsById =
        OrderedDomains.ToDictionary(domain => domain.Id);

    public static IReadOnlyList<SecondaryAttackYamlDomain> Domains => OrderedDomains;

    public static SecondaryAttackYamlDomain Get(SecondaryAttackYamlDomainId id)
    {
        return DomainsById[id];
    }
}

internal sealed class SecondaryAttackYamlTexts
{
    private readonly Dictionary<SecondaryAttackYamlDomainId, string> _texts;

    public SecondaryAttackYamlTexts(IReadOnlyDictionary<SecondaryAttackYamlDomainId, string> texts)
    {
        _texts = new Dictionary<SecondaryAttackYamlDomainId, string>(texts);
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            _texts.TryAdd(domain.Id, string.Empty);
        }
    }

    public IReadOnlyDictionary<SecondaryAttackYamlDomainId, string> All => _texts;

    public string Get(SecondaryAttackYamlDomainId id)
    {
        return _texts.TryGetValue(id, out string? text) ? text : string.Empty;
    }

    public string GetContentFingerprint()
    {
        StringBuilder builder = new();
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            string text = Get(domain.Id);
            builder.Append((int)domain.Id)
                .Append(':')
                .Append(text.Length)
                .Append(':')
                .Append(text)
                .Append('\n');
        }

        return builder.ToString();
    }
}

internal sealed class SecondaryAttackParsedYaml
{
    public IReadOnlyDictionary<string, ShieldWeaponConfig> Shields { get; set; } =
        new Dictionary<string, ShieldWeaponConfig>(StringComparer.OrdinalIgnoreCase);
}
