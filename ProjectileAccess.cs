using System.Reflection;
using HarmonyLib;

namespace CaptainValheim;

internal static class ProjectileAccess
{
    private static readonly FieldInfo? WeaponField = AccessTools.Field(typeof(Projectile), "m_weapon");
    private static readonly FieldInfo? AmmoField = AccessTools.Field(typeof(Projectile), "m_ammo");
    private static readonly FieldInfo? OwnerField = AccessTools.Field(typeof(Projectile), "m_owner");
    private static readonly FieldInfo? OriginalHitDataField = AccessTools.Field(typeof(Projectile), "m_originalHitData");
    private static readonly FieldInfo? StatusEffectHashField = AccessTools.Field(typeof(Projectile), "m_statusEffectHash");
    private static readonly FieldInfo? VelocityField = AccessTools.Field(typeof(Projectile), "m_vel");
    private static readonly FieldInfo? DidHitField = AccessTools.Field(typeof(Projectile), "m_didHit");

    internal static ItemDrop.ItemData? GetWeapon(Projectile projectile)
    {
        return WeaponField?.GetValue(projectile) as ItemDrop.ItemData;
    }

    internal static ItemDrop.ItemData? GetAmmo(Projectile projectile)
    {
        return AmmoField?.GetValue(projectile) as ItemDrop.ItemData;
    }

    internal static Character? GetOwner(Projectile projectile)
    {
        return OwnerField?.GetValue(projectile) as Character;
    }

    internal static HitData? GetOriginalHitData(Projectile projectile)
    {
        return OriginalHitDataField?.GetValue(projectile) as HitData;
    }

    internal static int GetStatusEffectHash(Projectile projectile)
    {
        return StatusEffectHashField?.GetValue(projectile) is int hash ? hash : 0;
    }

    internal static UnityEngine.Vector3 GetVelocity(Projectile projectile)
    {
        return VelocityField?.GetValue(projectile) is UnityEngine.Vector3 velocity ? velocity : UnityEngine.Vector3.zero;
    }

    internal static void SetVelocity(Projectile projectile, UnityEngine.Vector3 velocity)
    {
        VelocityField?.SetValue(projectile, velocity);
    }

    internal static void SetDidHit(Projectile projectile, bool didHit)
    {
        DidHitField?.SetValue(projectile, didHit);
    }
}
