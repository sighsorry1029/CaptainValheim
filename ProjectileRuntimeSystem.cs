using UnityEngine;

namespace CaptainValheim;

internal static class ProjectileRuntimeSystem
{
    internal readonly struct ProjectileLaunchData
    {
        public static readonly ProjectileLaunchData Invalid = new(
            null,
            0f,
            0f,
            0f,
            false);

        public ProjectileLaunchData(
            GameObject? projectilePrefab,
            float projectileVelocity,
            float projectileVelocityMin,
            float attackHitNoise,
            bool useRandomVelocity)
        {
            ProjectilePrefab = projectilePrefab;
            ProjectileVelocity = projectileVelocity;
            ProjectileVelocityMin = projectileVelocityMin;
            AttackHitNoise = attackHitNoise;
            UseRandomVelocity = useRandomVelocity;
        }

        public GameObject? ProjectilePrefab { get; }

        public float ProjectileVelocity { get; }

        public float ProjectileVelocityMin { get; }

        public float AttackHitNoise { get; }

        public bool UseRandomVelocity { get; }

        public bool IsValid => ProjectilePrefab != null;
    }

    internal static Character? GetHitCharacter(Collider collider)
    {
        return Projectile.FindHitObject(collider)?.GetComponent<Character>();
    }
}
