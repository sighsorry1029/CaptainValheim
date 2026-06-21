using UnityEngine;

namespace CaptainValheim;

internal static partial class ProjectileRuntimeSystem
{
    internal readonly struct ProjectileLaunchData
    {
        public static readonly ProjectileLaunchData Invalid = new(
            null,
            null,
            0f,
            0f,
            0f,
            0f,
            0f,
            1f,
            1f,
            1f,
            1f,
            false);

        public ProjectileLaunchData(
            GameObject? projectilePrefab,
            ItemDrop.ItemData? ammoItem,
            float projectileVelocity,
            float projectileVelocityMin,
            float projectileAccuracy,
            float projectileAccuracyMin,
            float attackHitNoise,
            float damageFactor,
            float configuredDamageFactor,
            float configuredSkillRaiseFactor,
            float configuredAdrenalineFactor,
            bool useRandomVelocity)
        {
            ProjectilePrefab = projectilePrefab;
            AmmoItem = ammoItem;
            ProjectileVelocity = projectileVelocity;
            ProjectileVelocityMin = projectileVelocityMin;
            ProjectileAccuracy = projectileAccuracy;
            ProjectileAccuracyMin = projectileAccuracyMin;
            AttackHitNoise = attackHitNoise;
            DamageFactor = damageFactor;
            ConfiguredDamageFactor = configuredDamageFactor;
            ConfiguredSkillRaiseFactor = configuredSkillRaiseFactor;
            ConfiguredAdrenalineFactor = configuredAdrenalineFactor;
            UseRandomVelocity = useRandomVelocity;
        }

        public GameObject? ProjectilePrefab { get; }

        public ItemDrop.ItemData? AmmoItem { get; }

        public float ProjectileVelocity { get; }

        public float ProjectileVelocityMin { get; }

        public float ProjectileAccuracy { get; }

        public float ProjectileAccuracyMin { get; }

        public float AttackHitNoise { get; }

        public float DamageFactor { get; }

        public float ConfiguredDamageFactor { get; }

        public float ConfiguredSkillRaiseFactor { get; }

        public float ConfiguredAdrenalineFactor { get; }

        public bool UseRandomVelocity { get; }

        public bool IsValid => ProjectilePrefab != null;
    }

    internal static Character? GetHitCharacter(Collider collider)
    {
        return Projectile.FindHitObject(collider)?.GetComponent<Character>();
    }
}
