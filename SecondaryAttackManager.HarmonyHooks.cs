using HarmonyLib;
using UnityEngine;

namespace CaptainValheim;

[HarmonyPatch(typeof(Projectile), "UpdateVisual")]
internal static class ProjectileUpdateVisualPatch
{
    private static void Prefix(Projectile __instance)
    {
        ShieldRuntimeSystem.PrepareShieldThrowProjectileIfNeeded(__instance);
    }

    private static void Postfix(Projectile __instance)
    {
        ShieldRuntimeSystem.EnsureShieldThrowProjectileVisualSpinIfNeeded(__instance);
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
internal static class ProjectileOnHitPatch
{
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(
        Projectile __instance,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal,
        out SecondaryAttackHarmonyDispatch.ProjectileOnHitState __state)
    {
        return SecondaryAttackHarmonyDispatch.ProjectileOnHitPrefix(__instance, collider, hitPoint, water, normal, out __state);
    }

    [HarmonyPriority(Priority.First)]
    private static void Postfix(
        Projectile __instance,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal,
        SecondaryAttackHarmonyDispatch.ProjectileOnHitState __state)
    {
        SecondaryAttackHarmonyDispatch.ProjectileOnHitPostfix(__instance, collider, hitPoint, water, normal, __state);
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class PlayerUpdatePendingConfigPatch
{
    private static void Postfix(Player __instance, bool ___m_attackHold, bool ___m_secondaryAttackHold, bool ___m_secondaryAttack, ref bool ___m_blocking)
    {
        SecondaryAttackHarmonyDispatch.PlayerUpdatePostfix(__instance, ___m_attackHold, ___m_secondaryAttackHold, ___m_secondaryAttack, ref ___m_blocking);
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
internal static class ObjectDbAwakePatch
{
    private static void Postfix(ObjectDB __instance)
    {
        SecondaryAttackFacade.ApplyPendingConfigToObjectDb(__instance, emitMissingWarnings: false);
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
internal static class ObjectDbCopyOtherDbPatch
{
    private static void Postfix(ObjectDB __instance)
    {
        SecondaryAttackFacade.ApplyPendingConfigToObjectDb(__instance, emitMissingWarnings: true);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetCurrentWeapon))]
internal static class HumanoidGetCurrentWeaponPatch
{
    private static void Postfix(Humanoid __instance, ref ItemDrop.ItemData __result)
    {
        if (ShieldRuntimeSystem.TryGetScopedCurrentWeaponOverride(__instance, out ItemDrop.ItemData weapon))
        {
            __result = weapon;
        }
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
internal static class HumanoidPickupThrownShieldPatch
{
    private static void Prefix(GameObject go, ref ItemDrop.ItemData __state)
    {
        __state = null!;
        ShieldRuntimeSystem.TryGetAutoEquipThrownShieldState(go, out __state);
    }

    private static void Postfix(Humanoid __instance, bool __result, ItemDrop.ItemData __state)
    {
        if (!__result || __state == null || __instance is not Player player)
        {
            return;
        }

        if (player.LeftItem != __state)
        {
            player.EquipItem(__state);
        }
    }
}

[HarmonyPatch(typeof(Humanoid), "BlockAttack")]
internal static class HumanoidBlockAttackPatch
{
    private static void Prefix(Humanoid __instance, HitData hit, ItemDrop.ItemData ___m_leftItem, float ___m_blockTimer, out SecondaryAttackManager.BlockAttackContext __state)
    {
        __state = SecondaryAttackManager.CaptureBlockAttackContext(__instance, hit, ___m_leftItem, ___m_blockTimer);
    }

    private static void Postfix(Humanoid __instance, bool __result, HitData hit, SecondaryAttackManager.BlockAttackContext __state)
    {
        SecondaryAttackManager.FinalizeBlockAttack(__instance, __result, hit, __state);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
internal static class HumanoidStartAttackPatch
{
    private static bool Prefix(
        Humanoid __instance,
        bool secondaryAttack,
        ref bool __result,
        ItemDrop.ItemData ___m_leftItem,
        ItemDrop.ItemData ___m_rightItem,
        out SecondaryAttackStartAttackDispatch.StartAttackState __state)
    {
        return SecondaryAttackStartAttackDispatch.Prefix(
            __instance,
            secondaryAttack,
            ref __result,
            ___m_leftItem,
            ___m_rightItem,
            out __state);
    }

    private static void Postfix(
        Humanoid __instance,
        bool secondaryAttack,
        bool __result,
        SecondaryAttackStartAttackDispatch.StartAttackState __state)
    {
        SecondaryAttackStartAttackDispatch.Postfix(__instance, secondaryAttack, __result, __state);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
internal static class AttackOnAttackTriggerPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Attack __instance)
    {
        return !SecondaryAttackRuntimeFacade.TryHandleCustomAttackTrigger(__instance);
    }

    private static void Postfix()
    {
        ShieldRuntimeSystem.EndShieldPrimaryVanillaTrigger();
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
internal static class AttackDoMeleeAttackSecondaryDurabilityFactorPatch
{
    private static void Prefix(Attack __instance, out SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        __state = SecondaryAttackManager.BeginSecondaryAttackDurabilityAdjustment(__instance);
    }

    private static void Postfix(SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(__state);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
internal static class AttackDoAreaAttackSecondaryDurabilityFactorPatch
{
    private static void Prefix(Attack __instance, out SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        __state = SecondaryAttackManager.BeginSecondaryAttackDurabilityAdjustment(__instance);
    }

    private static void Postfix(SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(__state);
    }
}

[HarmonyPatch(typeof(Attack), "ProjectileAttackTriggered")]
internal static class AttackProjectileAttackTriggeredSecondaryDurabilityFactorPatch
{
    private static void Prefix(Attack __instance, out SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        __state = SecondaryAttackManager.BeginSecondaryAttackDurabilityAdjustment(__instance);
    }

    private static void Postfix(SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(__state);
    }
}
