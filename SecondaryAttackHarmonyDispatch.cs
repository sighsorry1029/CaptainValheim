using UnityEngine;

namespace CaptainValheim;

internal static class SecondaryAttackHarmonyDispatch
{
    internal struct ProjectileOnHitState
    {
        internal bool RuntimeContext;
    }

    internal static bool ProjectileOnHitPrefix(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal,
        out ProjectileOnHitState state)
    {
        state = default;
        if (ShieldRuntimeSystem.ShouldHandleShieldProjectileHit(projectile, collider, hitPoint, water, normal))
        {
            return false;
        }

        state.RuntimeContext = SecondaryAttackRuntimeFacade.BeginProjectileHitContext(projectile, collider, hitPoint, water, normal);
        try
        {
            SecondaryAttackManager.TrySendShieldReflectRequest(projectile, collider, hitPoint, water, normal);
            return true;
        }
        catch
        {
            EndProjectileOnHit(ref state);
            throw;
        }
    }

    internal static void EndProjectileOnHit(ref ProjectileOnHitState state)
    {
        bool active = state.RuntimeContext;
        state.RuntimeContext = false;
        SecondaryAttackRuntimeFacade.EndProjectileHitContext(active);
    }

    internal static void PlayerUpdatePostfix(Player player)
    {
        if (player == Player.m_localPlayer)
        {
            SecondaryAttackFacade.TryApplyPendingConfig();
            ShieldRuntimeSystem.UpdateReturnedShieldAutoEquip(player);
        }
    }
}
