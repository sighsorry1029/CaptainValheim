using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CaptainValheim;

internal static class SecondaryAttackRuntimeContext
{
    private const float DefaultAdrenalineFactor = 1f;
    private const float ShieldChargeAdrenalineFactor = 5f;
    private static readonly ConditionalWeakTable<Attack, ActiveSecondaryAttack> ActiveAttacks = new();
    private static readonly ConditionalWeakTable<Attack, AttackAdrenalineState> AttackAdrenalineStates = new();
    private static readonly List<ProjectileHitContext> ActiveProjectileHitContexts = new(4);

    internal static void SetActiveAttack(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        ActiveAttacks.Remove(attack);
        ActiveAttacks.Add(attack, activeAttack);
    }

    internal static bool TryGetActiveAttack(Attack attack, out ActiveSecondaryAttack? activeAttack)
    {
        return ActiveAttacks.TryGetValue(attack, out activeAttack);
    }

    internal static void ResetAdrenaline(Attack attack)
    {
        if (attack != null)
        {
            AttackAdrenalineStates.Remove(attack);
        }
    }

    internal static float ResolveAdrenalineFactor(ActiveSecondaryAttack activeAttack)
    {
        return activeAttack.ShieldMode == ShieldSpecialMode.Charge
            ? ShieldChargeAdrenalineFactor
            : DefaultAdrenalineFactor;
    }

    internal static bool TryGrantAdrenalineOnce(
        Attack attack,
        Character target,
        float factor,
        string key)
    {
        if (attack?.m_character == null ||
            target == null ||
            target.m_enemyAdrenalineMultiplier <= 0f ||
            factor <= 0f)
        {
            return false;
        }

        AttackAdrenalineState state = AttackAdrenalineStates.GetValue(
            attack,
            _ => new AttackAdrenalineState());
        if (!state.GrantedKeys.Add(key))
        {
            return false;
        }

        attack.m_character.AddAdrenaline(factor * target.m_enemyAdrenalineMultiplier);
        return true;
    }

    internal static bool BeginProjectileHitContext(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal)
    {
        if (projectile == null || collider == null)
        {
            return false;
        }

        ActiveProjectileHitContexts.Add(new ProjectileHitContext(projectile, collider, hitPoint, water, normal));
        return true;
    }

    internal static void EndProjectileHitContext(bool active)
    {
        if (!active || ActiveProjectileHitContexts.Count == 0)
        {
            return;
        }

        ActiveProjectileHitContexts.RemoveAt(ActiveProjectileHitContexts.Count - 1);
    }

    internal static bool TryPeekProjectileHitContext(out ProjectileHitContext? context)
    {
        if (ActiveProjectileHitContexts.Count == 0)
        {
            context = null;
            return false;
        }

        context = ActiveProjectileHitContexts[ActiveProjectileHitContexts.Count - 1];
        return true;
    }

    private sealed class AttackAdrenalineState
    {
        internal readonly HashSet<string> GrantedKeys = new();
    }
}

internal sealed class ActiveSecondaryAttack
{
    internal ActiveSecondaryAttack(SecondaryAttackDefinition definition, ShieldSpecialMode shieldMode)
    {
        Definition = definition;
        ShieldMode = shieldMode;
    }

    internal SecondaryAttackDefinition Definition { get; }

    internal ShieldSpecialMode ShieldMode { get; }

    internal bool Triggered { get; set; }
}

internal readonly struct ProjectileHitContext
{
    internal ProjectileHitContext(
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

    internal Projectile Projectile { get; }

    internal Collider Collider { get; }

    internal Vector3 HitPoint { get; }

    internal bool Water { get; }

    internal Vector3 Normal { get; }
}
