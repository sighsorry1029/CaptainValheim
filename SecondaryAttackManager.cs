using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using ProjectileLaunchData = CaptainValheim.ProjectileRuntimeSystem.ProjectileLaunchData;

namespace CaptainValheim;

internal static partial class SecondaryAttackManager
{
    private static readonly ConditionalWeakTable<Character, AsyncSecondaryActivityState> AsyncSecondaryActivityStates = new();
    private static readonly MethodInfo MemberwiseCloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone")!;
    private static int AimRayMask;
    private static int ShieldChargeCollisionMask;
    private static int ShieldChargeImpactMask;

    internal static bool TryMarkCompatibilityWarningReported(string warningKey)
    {
        return SecondaryAttackWarningLog.TryMarkWarning(warningKey);
    }

    internal static Attack CloneAttack(Attack? sourceAttack)
    {
        return sourceAttack == null
            ? new Attack()
            : (Attack)MemberwiseCloneMethod.Invoke(sourceAttack, Array.Empty<object>())!;
    }

    internal static Attack BuildSecondaryAttack(Attack sourceAttack)
    {
        Attack secondaryAttack = CloneAttack(sourceAttack);
        secondaryAttack.m_attackType = Attack.AttackType.None;
        secondaryAttack.m_bowDraw = false;
        secondaryAttack.m_requiresReload = false;
        secondaryAttack.m_projectiles = 1;
        secondaryAttack.m_projectileBursts = 1;
        secondaryAttack.m_attackChainLevels = 1;
        secondaryAttack.m_attackRandomAnimations = 0;

        return secondaryAttack;
    }

    internal static Attack ResolveSourceAttack(ItemDrop itemDrop)
    {
        ItemDrop.ItemData.SharedData sharedData = itemDrop.m_itemData.m_shared;
        return sharedData.m_secondaryAttack ?? sharedData.m_attack ?? new Attack();
    }

    internal static bool HasCharacterAuthority(Character? character)
    {
        return TryGetCharacterZdo(character, out ZNetView? nview, out _) && nview!.IsOwner();
    }

    internal static bool TryGetCharacterZdo(Character? character, out ZNetView? nview, out ZDO? zdo)
    {
        nview = character != null ? character.GetComponent<ZNetView>() : null;
        zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
        return nview != null && nview.IsValid() && zdo != null;
    }

    internal static float GetNetworkTimeSeconds()
    {
        return ZNet.instance != null ? (float)ZNet.instance.GetTimeSeconds() : Time.time;
    }

    internal static void PlayTriggeredAttackEffects(Attack attack, float durabilityFactor)
    {
        DrainAttackDurability(attack, durabilityFactor);

        Transform origin = attack.m_character.transform;
        attack.m_weapon.m_shared.m_triggerEffect.Create(origin.position, attack.m_character.transform.rotation, origin);
        attack.m_triggerEffect.Create(origin.position, attack.m_character.transform.rotation, origin);
        attack.m_character.AddNoise(attack.m_attackHitNoise);
    }

    internal static void DrainAttackDurability(Attack attack, float durabilityFactor)
    {
        if (attack?.m_weapon == null || attack.m_character == null)
        {
            return;
        }

        DrainItemDurability(attack.m_character, attack.m_weapon, durabilityFactor);
    }

    internal static void DrainItemDurability(Character character, ItemDrop.ItemData weapon, float durabilityFactor)
    {
        if (character == null ||
            weapon?.m_shared == null ||
            !weapon.m_shared.m_useDurability ||
            !character.IsPlayer())
        {
            return;
        }

        float drain = GetItemDurabilityDrain(weapon) * Mathf.Max(0f, durabilityFactor);
        if (drain <= 0f)
        {
            return;
        }

        weapon.m_durability = Mathf.Max(0f, weapon.m_durability - drain);
    }

    internal static float GetItemDurabilityDrain(ItemDrop.ItemData weapon)
    {
        float drain = weapon?.m_shared?.m_useDurabilityDrain ?? 0f;
        return drain > 0f ? drain : 1f;
    }

    internal static SecondaryAttackDurabilityAdjustmentState BeginSecondaryAttackDurabilityAdjustment(Attack attack)
    {
        if (attack?.m_weapon?.m_shared == null ||
            attack.m_character == null ||
            !attack.m_weapon.m_shared.m_useDurability ||
            !attack.m_character.IsPlayer() ||
            !SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
            activeAttack == null)
        {
            return SecondaryAttackDurabilityAdjustmentState.Empty;
        }

        float factor = Mathf.Max(0f, ResolveActiveAttackDurabilityFactor(activeAttack));
        if (Mathf.Approximately(factor, 1f))
        {
            return SecondaryAttackDurabilityAdjustmentState.Empty;
        }

        return new SecondaryAttackDurabilityAdjustmentState(attack.m_weapon, attack.m_weapon.m_durability, factor);
    }

    internal static void EndSecondaryAttackDurabilityAdjustment(ref SecondaryAttackDurabilityAdjustmentState state)
    {
        if (!state.Applies || state.Weapon?.m_shared == null)
        {
            return;
        }

        SecondaryAttackDurabilityAdjustmentState adjustment = state;
        state = SecondaryAttackDurabilityAdjustmentState.Empty;
        float actualDrain = adjustment.BeforeDurability - adjustment.Weapon!.m_durability;
        if (actualDrain <= 0.001f)
        {
            return;
        }

        float targetDrain = actualDrain * adjustment.Factor;
        adjustment.Weapon.m_durability = Mathf.Clamp(
            adjustment.BeforeDurability - targetDrain,
            0f,
            Mathf.Max(adjustment.Weapon.m_shared.m_maxDurability, adjustment.BeforeDurability));
    }

    internal static float ResolveActiveAttackDurabilityFactor(ActiveSecondaryAttack activeAttack)
    {
        if (activeAttack.Definition.ShieldSpecial is not { } shieldBehavior)
        {
            return 1f;
        }

        return activeAttack.ShieldMode switch
        {
            ShieldSpecialMode.PrimaryAttack => shieldBehavior.ShieldPrimaryAttackDurabilityFactor,
            ShieldSpecialMode.Charge => shieldBehavior.ShieldChargeDurabilityFactor,
            _ => shieldBehavior.ShieldThrowDurabilityFactor
        };
    }

    internal readonly struct SecondaryAttackDurabilityAdjustmentState
    {
        internal static readonly SecondaryAttackDurabilityAdjustmentState Empty = new(null, 0f, 1f);

        internal SecondaryAttackDurabilityAdjustmentState(ItemDrop.ItemData? weapon, float beforeDurability, float factor)
        {
            Weapon = weapon;
            BeforeDurability = beforeDurability;
            Factor = factor;
        }

        internal ItemDrop.ItemData? Weapon { get; }

        internal float BeforeDurability { get; }

        internal float Factor { get; }

        internal bool Applies => Weapon != null;
    }

    internal static int GetAimRayMask()
    {
        if (AimRayMask == 0)
        {
            AimRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
        }

        return AimRayMask;
    }

    internal static int GetShieldChargeCollisionMask()
    {
        if (ShieldChargeCollisionMask == 0)
        {
            ShieldChargeCollisionMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "blocker", "vehicle");
        }

        return ShieldChargeCollisionMask;
    }

    internal static int GetShieldChargeImpactMask()
    {
        if (ShieldChargeImpactMask == 0)
        {
            ShieldChargeImpactMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "blocker", "vehicle", "character", "character_net", "character_ghost", "hitbox", "character_noenv");
        }

        return ShieldChargeImpactMask;
    }

    internal static float ResolveProjectileSpeed(ProjectileLaunchData launchData)
    {
        float speed = launchData.UseRandomVelocity
            ? UnityEngine.Random.Range(launchData.ProjectileVelocityMin, launchData.ProjectileVelocity)
            : launchData.ProjectileVelocity;
        return Mathf.Max(0.01f, speed);
    }

    internal static Character? GetHitCharacter(Collider collider)
    {
        return ProjectileRuntimeSystem.GetHitCharacter(collider);
    }

    internal static Vector3 GetSentinelForward(Character owner)
    {
        Vector3 forward = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = owner.transform.forward;
        }

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    internal static float ClosestSegmentProgress(Vector3 start, Vector3 end, Vector3 point)
    {
        Vector3 segment = end - start;
        float lengthSq = segment.sqrMagnitude;
        if (lengthSq <= Mathf.Epsilon)
        {
            return 0f;
        }

        return Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSq);
    }

    internal static Vector3 ResolveSafeClosestPoint(Collider collider, Vector3 origin)
    {
        if (collider is MeshCollider meshCollider && !meshCollider.convex)
        {
#pragma warning disable CS0618
            return meshCollider.ClosestPointOnBounds(origin);
#pragma warning restore CS0618
        }

        return collider.ClosestPoint(origin);
    }

    internal static void DestroyProjectileObject(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return;
        }

        if (ZNetScene.instance != null && gameObject.GetComponent<ZNetView>() != null)
        {
            ZNetScene.instance.Destroy(gameObject);
            return;
        }

        Object.Destroy(gameObject);
    }

    internal static void RegisterAsyncSecondaryWork(Character? owner)
    {
        if (owner == null)
        {
            return;
        }

        AsyncSecondaryActivityState state = AsyncSecondaryActivityStates.GetValue(owner, _ => new AsyncSecondaryActivityState());
        state.ActiveCount++;
    }

    internal static void UnregisterAsyncSecondaryWork(Character? owner)
    {
        if (owner == null ||
            !AsyncSecondaryActivityStates.TryGetValue(owner, out AsyncSecondaryActivityState? state))
        {
            return;
        }

        state.ActiveCount = Mathf.Max(0, state.ActiveCount - 1);
    }

    internal static bool HasActiveAsyncSecondaryWorkForFacade(Character? owner)
    {
        return owner != null &&
               AsyncSecondaryActivityStates.TryGetValue(owner, out AsyncSecondaryActivityState? state) &&
               state.ActiveCount > 0;
    }

    private sealed class AsyncSecondaryActivityState
    {
        public int ActiveCount { get; set; }
    }
}
