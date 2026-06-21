using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CaptainValheim;

internal static class SecondaryAttackAdrenalineSystem
{
    private sealed class AttackAdrenalineState
    {
        internal readonly HashSet<string> GrantedKeys = new();
    }

    private static readonly ConditionalWeakTable<Attack, AttackAdrenalineState> AttackStates = new();

    internal static void BeginConfiguredSecondaryStart(Humanoid humanoid, ItemDrop.ItemData weapon)
    {
    }

    internal static void EndConfiguredSecondaryStart(Humanoid humanoid)
    {
    }

    internal static void Reset(Attack attack)
    {
        if (attack != null)
        {
            AttackStates.Remove(attack);
        }
    }

    internal static float ResolveFactor(ActiveSecondaryAttack activeAttack)
    {
        if (activeAttack.Definition.Behavior is not ShieldSpecialSecondaryBehavior shield)
        {
            return 1f;
        }

        return activeAttack.ShieldMode switch
        {
            ShieldSpecialMode.PrimaryAttack => shield.ShieldPrimaryAttackAdrenalineFactor,
            ShieldSpecialMode.Charge => shield.ShieldChargeAdrenalineFactor,
            _ => shield.ShieldThrowAdrenalineFactor
        };
    }

    internal static bool TryGrantOnceRaw(Attack attack, Character target, float baseAdrenaline, float factor, string key)
    {
        if (attack?.m_character == null ||
            target == null ||
            target.m_enemyAdrenalineMultiplier <= 0f ||
            baseAdrenaline <= 0f ||
            factor <= 0f ||
            !TryMarkGranted(attack, key))
        {
            return false;
        }

        attack.m_character.AddAdrenaline(baseAdrenaline * Mathf.Max(0f, factor) * target.m_enemyAdrenalineMultiplier);
        return true;
    }

    private static bool TryMarkGranted(Attack attack, string key)
    {
        AttackAdrenalineState state = AttackStates.GetValue(attack, _ => new AttackAdrenalineState());
        return state.GrantedKeys.Add(key);
    }
}
