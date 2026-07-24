using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CaptainValheim;

internal static class SecondaryAttackConfigLoader
{
    private const string DefaultResourcePrefix = "CaptainValheim.Resources.Defaults.";
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
                LoadEmbeddedDefault(SecondaryAttackYamlConfig.FileName));
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

        if (!TryValidateFiniteValues(shields!, out string invalidValuePath))
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to parse {SecondaryAttackYamlConfig.FileName}: '{invalidValuePath}' must be a finite number.");
            return false;
        }

        try
        {
            snapshot = SecondaryAttackWeaponConfigNormalizer.Normalize(shields!);
            return true;
        }
        catch (Exception exception)
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to compile {SecondaryAttackYamlConfig.FileName}: {exception.Message}");
            return false;
        }
    }

    private static bool TryParseDictionary(
        string yamlText,
        out Dictionary<string, ShieldWeaponConfig>? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to parse {SecondaryAttackYamlConfig.FileName}: the document cannot be empty.");
            return false;
        }

        try
        {
            parsed = new Dictionary<string, ShieldWeaponConfig>(StringComparer.OrdinalIgnoreCase);
            YamlStream stream = new();
            stream.Load(new StringReader(yamlText));
            if (stream.Documents.Count != 1 ||
                stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                CaptainValheimPlugin.ModLogger.LogError(
                    $"Failed to parse {SecondaryAttackYamlConfig.FileName}: the document root must be a single mapping.");
                return false;
            }

            foreach (KeyValuePair<YamlNode, YamlNode> entry in root.Children)
            {
                string rootKey = (entry.Key as YamlScalarNode)?.Value?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(rootKey))
                {
                    CaptainValheimPlugin.ModLogger.LogError(
                        $"Failed to parse {SecondaryAttackYamlConfig.FileName}: every root block must have a non-empty scalar key.");
                    return false;
                }

                if (entry.Value is not YamlMappingNode)
                {
                    CaptainValheimPlugin.ModLogger.LogError(
                        $"Failed to parse {SecondaryAttackYamlConfig.FileName} block '{rootKey}': the block must be a mapping.");
                    return false;
                }

                if (parsed.ContainsKey(rootKey))
                {
                    CaptainValheimPlugin.ModLogger.LogError(
                        $"Failed to parse {SecondaryAttackYamlConfig.FileName}: duplicate root block '{rootKey}'.");
                    return false;
                }

                try
                {
                    ShieldWeaponConfig? shieldConfig = DeserializeYamlNode(entry.Value);
                    if (shieldConfig == null)
                    {
                        CaptainValheimPlugin.ModLogger.LogError(
                            $"Failed to parse {SecondaryAttackYamlConfig.FileName} block '{rootKey}': the block is empty or invalid.");
                        return false;
                    }

                    parsed.Add(rootKey, shieldConfig);
                }
                catch (Exception entryException)
                {
                    CaptainValheimPlugin.ModLogger.LogError(
                        $"Failed to parse {SecondaryAttackYamlConfig.FileName} block '{rootKey}': {entryException.Message}");
                    return false;
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

    private static bool TryValidateFiniteValues(
        IReadOnlyDictionary<string, ShieldWeaponConfig> shields,
        out string invalidValuePath)
    {
        foreach ((string prefabName, ShieldWeaponConfig shield) in shields)
        {
            if (TryFindNonFiniteValue(
                    out invalidValuePath,
                    ($"{prefabName}.primaryAttack.damageFactor", shield.PrimaryAttack?.DamageFactor),
                    ($"{prefabName}.primaryAttack.pushFactor", shield.PrimaryAttack?.PushFactor),
                    ($"{prefabName}.primaryAttack.staminaFactor", shield.PrimaryAttack?.StaminaFactor),
                    ($"{prefabName}.primaryAttack.durabilityFactor", shield.PrimaryAttack?.DurabilityFactor),
                    ($"{prefabName}.throw.damageFactor", shield.Throw?.DamageFactor),
                    ($"{prefabName}.throw.pushFactor", shield.Throw?.PushFactor),
                    ($"{prefabName}.throw.staminaFactor", shield.Throw?.StaminaFactor),
                    ($"{prefabName}.throw.durabilityFactor", shield.Throw?.DurabilityFactor),
                    ($"{prefabName}.throw.damageDecay", shield.Throw?.DamageDecay),
                    ($"{prefabName}.throw.radiusFactor", shield.Throw?.RadiusFactor),
                    ($"{prefabName}.throw.ttlFactor", shield.Throw?.TtlFactor),
                    ($"{prefabName}.charge.damageFactor", shield.Charge?.DamageFactor),
                    ($"{prefabName}.charge.pushFactor", shield.Charge?.PushFactor),
                    ($"{prefabName}.charge.staminaFactor", shield.Charge?.StaminaFactor),
                    ($"{prefabName}.charge.distance", shield.Charge?.Distance),
                    ($"{prefabName}.charge.speed", shield.Charge?.Speed),
                    ($"{prefabName}.charge.cooldown", shield.Charge?.Cooldown),
                    ($"{prefabName}.charge.cooldownReductionFactor", shield.Charge?.CooldownReductionFactor),
                    ($"{prefabName}.charge.durabilityFactor", shield.Charge?.DurabilityFactor),
                    ($"{prefabName}.charge.hitRadiusFactor", shield.Charge?.HitRadiusFactor),
                    ($"{prefabName}.reflect.staminaFactor", shield.Reflect?.StaminaFactor),
                    ($"{prefabName}.reflect.reflectionFactor", shield.Reflect?.ReflectionFactor),
                    ($"{prefabName}.blockCharge.decayTime", shield.BlockCharge?.DecayTime),
                    ($"{prefabName}.blockCharge.blockingDecayFactor", shield.BlockCharge?.BlockingDecayFactor)))
            {
                return false;
            }
        }

        invalidValuePath = string.Empty;
        return true;
    }

    private static bool TryFindNonFiniteValue(
        out string invalidValuePath,
        params (string Path, float? Value)[] values)
    {
        foreach ((string path, float? value) in values)
        {
            if (value.HasValue && (float.IsNaN(value.Value) || float.IsInfinity(value.Value)))
            {
                invalidValuePath = path;
                return true;
            }
        }

        invalidValuePath = string.Empty;
        return false;
    }

    private static string LoadEmbeddedDefault(string fileName)
    {
        string resourceName = DefaultResourcePrefix + fileName;
        Assembly assembly = typeof(SecondaryAttackConfigLoader).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException(
                $"Embedded default YAML resource '{resourceName}' was not found.");
        }

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

internal static class SecondaryAttackYamlConfig
{
    internal const string FileName = "CaptainValheim.yml";
    internal const string SyncedIdentifier = "captain_valheim_yaml";
    internal const long ReloadDelayTicks = TimeSpan.TicksPerSecond;

    internal static readonly string DirectoryPath = Paths.ConfigPath;
    internal static readonly string FilePath = Path.Combine(DirectoryPath, FileName);
}
