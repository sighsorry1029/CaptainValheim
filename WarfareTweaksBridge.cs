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

        Scope scope = new(_weaponPrefabName, active: true);
        _weaponPrefabName = prefabName;
        return scope;
    }

    internal static bool TryGetWeaponPrefabName(out string weaponPrefabName)
    {
        weaponPrefabName = _weaponPrefabName ?? "";
        return !string.IsNullOrWhiteSpace(weaponPrefabName);
    }

    private static void End(Scope scope)
    {
        if (!scope.Active)
        {
            return;
        }

        _weaponPrefabName = scope.PreviousWeaponPrefabName;
    }

    internal readonly struct Scope : System.IDisposable
    {
        internal Scope(string? previousWeaponPrefabName, bool active)
        {
            PreviousWeaponPrefabName = previousWeaponPrefabName;
            Active = active;
        }

        internal string? PreviousWeaponPrefabName { get; }

        internal bool Active { get; }

        public void Dispose()
        {
            End(this);
        }
    }
}
