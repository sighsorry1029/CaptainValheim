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
}

internal readonly struct ProjectileHitContext
{
    public ProjectileHitContext(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal)
    {
        Projectile = projectile;
        Collider = collider;
        HitPoint = hitPoint;
        Water = water;
        Normal = normal;
    }

    public Projectile Projectile { get; }

    public Collider Collider { get; }

    public Vector3 HitPoint { get; }

    public bool Water { get; }

    public Vector3 Normal { get; }
}
