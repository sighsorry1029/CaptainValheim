namespace CaptainValheim;

public static class WarfareTweaksBridge
{
    public static bool TryGetShieldHitWeaponPrefabName(out string weaponPrefabName)
    {
        return ShieldWarfareHitContext.TryGetWeaponPrefabName(out weaponPrefabName);
    }
}

internal static class ShieldWarfareHitContext
{
    [System.ThreadStatic]
    private static string? _weaponPrefabName;

    [System.ThreadStatic]
    private static int _depth;

    internal static Scope Begin(Attack attack)
    {
        if (attack?.m_character != Player.m_localPlayer ||
            attack.m_weapon?.m_dropPrefab == null)
        {
            return default;
        }

        string prefabName = attack.m_weapon.m_dropPrefab.name;
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return default;
        }

        Scope scope = new(_weaponPrefabName, _depth, active: true);
        _weaponPrefabName = prefabName;
        _depth++;
        return scope;
    }

    internal static bool TryGetWeaponPrefabName(out string weaponPrefabName)
    {
        weaponPrefabName = _depth > 0 ? _weaponPrefabName ?? "" : "";
        return !string.IsNullOrWhiteSpace(weaponPrefabName);
    }

    private static void End(Scope scope)
    {
        if (!scope.Active)
        {
            return;
        }

        _weaponPrefabName = scope.PreviousWeaponPrefabName;
        _depth = scope.PreviousDepth;
    }

    internal readonly struct Scope : System.IDisposable
    {
        internal Scope(string? previousWeaponPrefabName, int previousDepth, bool active)
        {
            PreviousWeaponPrefabName = previousWeaponPrefabName;
            PreviousDepth = previousDepth;
            Active = active;
        }

        internal string? PreviousWeaponPrefabName { get; }

        internal int PreviousDepth { get; }

        internal bool Active { get; }

        public void Dispose()
        {
            End(this);
        }
    }
}
