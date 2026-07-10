using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CaptainValheim;

internal static class SecondaryAttackRuntimeContext
{
    private static readonly ConditionalWeakTable<Attack, ActiveSecondaryAttack> ActiveAttacks = new();
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

    internal static void PushProjectileHitContext(ProjectileHitContext context)
    {
        ActiveProjectileHitContexts.Add(context);
    }

    internal static void PopProjectileHitContext()
    {
        if (ActiveProjectileHitContexts.Count == 0)
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
}
