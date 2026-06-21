using System;
using System.Collections.Generic;

namespace CaptainValheim;

internal static class SecondaryAttackWarningLog
{
    private static readonly HashSet<string> ReportedWarnings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ReportedIssues = new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryMarkWarning(string key)
    {
        return ReportedWarnings.Add(key);
    }

    internal static bool TryMarkIssue(string key)
    {
        return ReportedIssues.Add(key);
    }

    internal static void WarnOnce(string key, string message, bool emit = true)
    {
        if (emit && TryMarkWarning(key))
        {
            CaptainValheimPlugin.ModLogger.LogWarning(message);
        }
    }
}
