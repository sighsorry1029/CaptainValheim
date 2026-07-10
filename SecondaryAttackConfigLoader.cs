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

    public static void EnsureLocalFileExists()
    {
        Directory.CreateDirectory(SecondaryAttackYamlConfig.DirectoryPath);
        if (!File.Exists(SecondaryAttackYamlConfig.FilePath))
        {
            File.WriteAllText(
                SecondaryAttackYamlConfig.FilePath,
                SecondaryAttackDefaultYamlResources.Load(SecondaryAttackYamlConfig.FileName));
        }
    }

    public static string ReadLocalYamlText()
    {
        return File.ReadAllText(SecondaryAttackYamlConfig.FilePath);
    }

    public static bool TryCompileSnapshot(
        string yamlText,
        out SecondaryAttackCompiledSnapshot? snapshot)
    {
        snapshot = null;
        if (!TryParseDictionary(yamlText, out Dictionary<string, ShieldWeaponConfig>? shields))
        {
            return false;
        }

        snapshot = new SecondaryAttackCompiledSnapshot(
            SecondaryAttackWeaponConfigNormalizer.Normalize(shields!));
        return true;
    }

    private static bool TryParseDictionary(
        string yamlText,
        out Dictionary<string, ShieldWeaponConfig>? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            parsed = new Dictionary<string, ShieldWeaponConfig>(StringComparer.OrdinalIgnoreCase);
            return true;
        }

        try
        {
            parsed = new Dictionary<string, ShieldWeaponConfig>(StringComparer.OrdinalIgnoreCase);
            YamlStream stream = new();
            stream.Load(new StringReader(yamlText));
            if (stream.Documents.Count == 0 ||
                stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return true;
            }

            foreach (KeyValuePair<YamlNode, YamlNode> entry in root.Children)
            {
                string rootKey = (entry.Key as YamlScalarNode)?.Value?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(rootKey))
                {
                    continue;
                }

                try
                {
                    parsed[rootKey] =
                        DeserializeYamlNode(entry.Value) ?? new ShieldWeaponConfig();
                }
                catch (Exception entryException)
                {
                    CaptainValheimPlugin.ModLogger.LogWarning(
                        $"Skipping {SecondaryAttackYamlConfig.FileName} block '{rootKey}': {entryException.Message}");
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to parse {SecondaryAttackYamlConfig.FileName}: {exception.Message}");
            return false;
        }
    }

    private static ShieldWeaponConfig? DeserializeYamlNode(YamlNode node)
    {
        using StringWriter writer = new();
        YamlStream stream = new(new YamlDocument(node));
        stream.Save(writer, assignAnchors: false);
        return Deserializer.Deserialize<ShieldWeaponConfig>(writer.ToString());
    }
}
