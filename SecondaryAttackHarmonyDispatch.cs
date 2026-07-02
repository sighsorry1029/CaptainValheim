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
        SecondaryAttackManager.TrySendShieldReflectRequest(projectile, collider, hitPoint, water, normal);
        return true;
    }

    internal static void ProjectileOnHitPostfix(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal,
        ProjectileOnHitState state)
    {
        SecondaryAttackRuntimeFacade.EndProjectileHitContext(state.RuntimeContext);
    }

    internal static void PlayerUpdatePostfix(Player player, bool primaryAttackHold, bool secondaryAttackHold, bool secondaryAttackPressed, ref bool blocking)
    {
        if (player == Player.m_localPlayer)
        {
            SecondaryAttackFacade.TryApplyPendingConfig();
            ShieldRuntimeSystem.UpdateReturnedShieldAutoEquip(player);
            ShieldOnlyKeyHintSystem.RefreshKeyHintUi();
        }
    }
}
