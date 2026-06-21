using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace CaptainValheim;

internal static class SecondaryAttackDefaultYamlResources
{
    private const string ResourcePrefix = "CaptainValheim.Resources.Defaults.";

    internal static string Load(string fileName)
    {
        string resourceName = ResourcePrefix + fileName;
        Assembly assembly = typeof(SecondaryAttackDefaultYamlResources).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded default YAML resource '{resourceName}' was not found.");
        }

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
