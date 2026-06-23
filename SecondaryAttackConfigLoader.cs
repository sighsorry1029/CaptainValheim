using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CaptainValheim;

internal static class SecondaryAttackConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static void EnsureLocalFilesExist()
    {
        Directory.CreateDirectory(SecondaryAttackYamlDomainRegistry.ConfigDirectoryPath);
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            if (!File.Exists(domain.FilePath))
            {
                File.WriteAllText(domain.FilePath, domain.GetDefaultContents());
            }
        }
    }

    public static SecondaryAttackYamlTexts ReadLocalYamlTexts()
    {
        Dictionary<SecondaryAttackYamlDomainId, string> texts = new();
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            texts[domain.Id] = File.ReadAllText(domain.FilePath);
        }

        return new SecondaryAttackYamlTexts(texts);
    }

    public static bool TryCompileSnapshot(
        int snapshotId,
        SecondaryAttackYamlTexts yamlTexts,
        out SecondaryAttackCompiledSnapshot? snapshot)
    {
        snapshot = null;
        if (!TryParseYamlTexts(yamlTexts, out SecondaryAttackParsedYaml? parsedYaml))
        {
            return false;
        }

        snapshot = SecondaryAttackConfigCompiler.Compile(snapshotId, parsedYaml!);
        return true;
    }

    private static bool TryParseYamlTexts(SecondaryAttackYamlTexts yamlTexts, out SecondaryAttackParsedYaml? parsedYaml)
    {
        parsedYaml = null;
        if (!TryParseDictionary<ShieldWeaponConfig>(
                SecondaryAttackYamlDomainId.Shields,
                yamlTexts.Get(SecondaryAttackYamlDomainId.Shields),
                out Dictionary<string, ShieldWeaponConfig>? shields))
        {
            return false;
        }

        parsedYaml = new SecondaryAttackParsedYaml
        {
            Shields = shields!
        };
        return true;
    }

    private static bool TryParseDictionary<T>(
        SecondaryAttackYamlDomainId domainId,
        string yamlText,
        out Dictionary<string, T>? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            parsed = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            return true;
        }

        try
        {
            parsed = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            YamlStream stream = new();
            stream.Load(new StringReader(yamlText));
            if (stream.Documents.Count == 0 ||
                stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return true;
            }

            SecondaryAttackYamlDomain domain = SecondaryAttackYamlDomainRegistry.Get(domainId);
            foreach (KeyValuePair<YamlNode, YamlNode> entry in root.Children)
            {
                string rootKey = (entry.Key as YamlScalarNode)?.Value?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(rootKey))
                {
                    continue;
                }

                try
                {
                    parsed[rootKey] = DeserializeYamlNode<T>(entry.Value) ?? Activator.CreateInstance<T>();
                }
                catch (Exception entryException)
                {
                    CaptainValheimPlugin.ModLogger.LogWarning($"Skipping {domain.FileName} block '{rootKey}': {entryException.Message}");
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            SecondaryAttackYamlDomain domain = SecondaryAttackYamlDomainRegistry.Get(domainId);
            CaptainValheimPlugin.ModLogger.LogError($"Failed to parse {domain.FileName}: {exception.Message}");
            return false;
        }
    }

    private static T? DeserializeYamlNode<T>(YamlNode node)
    {
        using StringWriter writer = new();
        YamlStream stream = new(new YamlDocument(node));
        stream.Save(writer, assignAnchors: false);
        return Deserializer.Deserialize<T>(writer.ToString());
    }
}
