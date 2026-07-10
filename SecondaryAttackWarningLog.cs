using System;
using System.Collections.Generic;

namespace CaptainValheim;

internal static class SecondaryAttackWarningLog
{
    private static readonly HashSet<string> ReportedWarnings = new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryMarkWarning(string key)
    {
        return ReportedWarnings.Add(key);
    }
}
