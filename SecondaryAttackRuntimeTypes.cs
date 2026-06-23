using UnityEngine;

namespace CaptainValheim;

internal sealed class ActiveSecondaryAttack
{
    public ActiveSecondaryAttack(SecondaryAttackDefinition definition, ShieldSpecialMode shieldMode)
    {
        Definition = definition;
        ShieldMode = shieldMode;
    }

    public SecondaryAttackDefinition Definition { get; }

    public ShieldSpecialMode ShieldMode { get; }

    public bool Triggered { get; set; }

    public bool ProjectileTriggered { get; set; }

    public float NextHoldRepeatTime { get; set; }
}

internal sealed class ProjectileAttackAttribution
{
    public ProjectileAttackAttribution(
        string weaponPrefabName,
        bool secondaryAttack,
        SecondaryAttackDefinition? definition,
        bool disableCurrentAttackFallback)
    {
        WeaponPrefabName = weaponPrefabName;
        SecondaryAttack = secondaryAttack;
        Definition = definition;
        DisableCurrentAttackFallback = disableCurrentAttackFallback;
    }

    public string WeaponPrefabName { get; }

    public bool SecondaryAttack { get; }

    public SecondaryAttackDefinition? Definition { get; }

    public bool DisableCurrentAttackFallback { get; }
}

internal readonly struct ProjectileHitContext
{
    public ProjectileHitContext(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal,
        ProjectileAttackAttribution? attribution)
    {
        Projectile = projectile;
        Collider = collider;
        HitPoint = hitPoint;
        Water = water;
        Normal = normal;
        Attribution = attribution;
    }

    public Projectile Projectile { get; }

    public Collider Collider { get; }

    public Vector3 HitPoint { get; }

    public bool Water { get; }

    public Vector3 Normal { get; }

    public ProjectileAttackAttribution? Attribution { get; }
}
