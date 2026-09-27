using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using YamlDotNet.Serialization;

namespace CaptainValheim;

internal static class CaptainValheimLocalization
{
    private const string EnglishLanguage = "English";
    private const string ResourcePrefix = "CaptainValheim.translations.";
    private const string ExternalFilePrefix = "CaptainValheim.";
    private const string TranslationFileExtension = ".yml";
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreFields()
        .Build();
    private static readonly Dictionary<string, Dictionary<string, string>?> TranslationCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _reportedMissingEnglish;

    internal static void Load()
    {
        if (ReadEmbeddedTranslations(EnglishLanguage) == null)
        {
            ReportMissingEnglish();
        }

        if (Localization.instance != null)
        {
            ApplyLanguage(Localization.instance);
        }
    }

    internal static string Text(string key)
    {
        return Localize("$" + key);
    }

    internal static string Localize(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return token ?? string.Empty;
        }

        string localized = Localization.instance?.Localize(token) ?? token;
        if (!IsMissingTranslation(localized, token))
        {
            return localized;
        }

        string key = GetTranslationKey(token);
        if (ReadEmbeddedTranslations(EnglishLanguage) is { } english &&
            english.TryGetValue(key, out string englishText))
        {
            return englishText;
        }

        return localized;
    }

    internal static void ApplyLanguage(Localization localization)
    {
        if (localization == null)
        {
            return;
        }

        string selectedLanguage = localization.GetSelectedLanguage();
        string language = string.IsNullOrWhiteSpace(selectedLanguage)
            ? EnglishLanguage
            : selectedLanguage;
        try
        {
            Dictionary<string, string>? english = ReadEmbeddedTranslations(EnglishLanguage);
            if (english == null)
            {
                ReportMissingEnglish();
                return;
            }

            Dictionary<string, string> translations = new(english, StringComparer.Ordinal);
            if (!string.Equals(language, EnglishLanguage, StringComparison.OrdinalIgnoreCase) &&
                ReadEmbeddedTranslations(language) is { } embeddedSelectedLanguage)
            {
                OverlayTranslations(translations, embeddedSelectedLanguage);
            }

            if (ReadExternalTranslations(language) is { } externalTranslations)
            {
                OverlayTranslations(translations, externalTranslations);
            }

            foreach (KeyValuePair<string, string> entry in translations)
            {
                GameAccess.AddWord(localization, entry.Key, entry.Value);
            }
        }
        catch (Exception exception)
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to load localization for '{language}': {exception.Message}");
        }
    }

    private static Dictionary<string, string>? ReadExternalTranslations(string language)
    {
        if (!IsSafeLanguageFileComponent(language))
        {
            CaptainValheimPlugin.ModLogger.LogWarning(
                $"Skipped external localization for unsafe language name '{language}'.");
            return null;
        }

        string logicalFileName = $"{ExternalFilePrefix}{language}{TranslationFileExtension}";
        string? assemblyDirectory;
        try
        {
            assemblyDirectory = Path.GetDirectoryName(typeof(CaptainValheimPlugin).Assembly.Location);
        }
        catch (Exception exception)
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to resolve the plugin directory for external localization: {exception.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(assemblyDirectory) || !Directory.Exists(assemblyDirectory))
        {
            return null;
        }

        string? filePath;
        try
        {
            string exactPath = Path.Combine(assemblyDirectory, logicalFileName);
            filePath = File.Exists(exactPath)
                ? exactPath
                : Directory
                    .EnumerateFiles(assemblyDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Where(path =>
                        string.Equals(
                            Path.GetFileName(path),
                            logicalFileName,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                    .FirstOrDefault();
        }
        catch (Exception exception)
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to search for external localization '{logicalFileName}': {exception.Message}");
            return null;
        }

        if (filePath == null)
        {
            return null;
        }

        try
        {
            using StreamReader reader = new(
                filePath,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            Dictionary<string, string>? translations =
                Deserializer.Deserialize<Dictionary<string, string>?>(reader.ReadToEnd());
            if (translations == null)
            {
                throw new InvalidDataException("the document root must be a translation mapping.");
            }

            foreach (KeyValuePair<string, string> entry in translations)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
                {
                    throw new InvalidDataException(
                        "translation keys must be non-empty and values cannot be null.");
                }
            }

            return translations;
        }
        catch (Exception exception)
        {
            CaptainValheimPlugin.ModLogger.LogError(
                $"Failed to read external localization '{logicalFileName}': {exception.Message}");
            return null;
        }
    }

    private static bool IsSafeLanguageFileComponent(string language)
    {
        return !string.IsNullOrWhiteSpace(language) &&
               language.IndexOf("..", StringComparison.Ordinal) < 0 &&
               language.IndexOf('/') < 0 &&
               language.IndexOf('\\') < 0 &&
               language.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static void OverlayTranslations(
        Dictionary<string, string> target,
        Dictionary<string, string> overlay)
    {
        foreach (KeyValuePair<string, string> entry in overlay)
        {
            if (target.ContainsKey(entry.Key))
            {
                target[entry.Key] = entry.Value;
            }
            else
            {
                CaptainValheimPlugin.ModLogger.LogWarning(
                    $"Ignored unknown localization key '{entry.Key}'.");
            }
        }
    }

    private static Dictionary<string, string>? ReadEmbeddedTranslations(string language)
    {
        if (TranslationCache.TryGetValue(language, out Dictionary<string, string>? cached))
        {
            return cached;
        }

        Assembly assembly = typeof(CaptainValheimPlugin).Assembly;
        string resourceSuffix = $"{ResourcePrefix}{language}.yml";
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
        {
            TranslationCache[language] = null;
            return null;
        }

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            TranslationCache[language] = null;
            return null;
        }

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        Dictionary<string, string>? translations =
            Deserializer.Deserialize<Dictionary<string, string>?>(reader.ReadToEnd());
        TranslationCache[language] = translations;
        return translations;
    }

    private static bool IsMissingTranslation(string localized, string token)
    {
        if (string.IsNullOrWhiteSpace(localized) ||
            string.Equals(localized, token, StringComparison.Ordinal))
        {
            return true;
        }

        return token[0] == '$' &&
               string.Equals(localized, $"[{GetTranslationKey(token)}]", StringComparison.Ordinal);
    }

    private static string GetTranslationKey(string token)
    {
        return token.Length > 0 && token[0] == '$' ? token.Substring(1) : token;
    }

    private static void ReportMissingEnglish()
    {
        if (_reportedMissingEnglish)
        {
            return;
        }

        _reportedMissingEnglish = true;
        CaptainValheimPlugin.ModLogger.LogError("Embedded English localization resource was not found.");
    }
}

[HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
internal static class LocalizationSetupLanguageCaptainValheimPatch
{
    private static void Postfix(Localization __instance)
    {
        CaptainValheimLocalization.ApplyLanguage(__instance);
    }
}
