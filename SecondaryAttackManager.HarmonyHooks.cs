using System;
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
    [HarmonyBefore("sighsorry.SecondaryAttacks")]
    private static bool Prefix(
        Projectile __instance,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal,
        out bool __state)
    {
        __state = false;
        if (ShieldRuntimeSystem.ShouldHandleShieldProjectileHit(__instance, collider, hitPoint, water, normal))
        {
            return false;
        }

        __state = SecondaryAttackRuntimeContext.BeginProjectileHitContext(
            __instance,
            collider,
            hitPoint,
            water,
            normal);
        return true;
    }

    [HarmonyPriority(Priority.First)]
    [HarmonyAfter("sighsorry.SecondaryAttacks")]
    private static void Postfix(ref bool __state)
    {
        EndProjectileHitContext(ref __state);
    }

    [HarmonyAfter("sighsorry.SecondaryAttacks")]
    private static Exception? Finalizer(
        Exception? __exception,
        ref bool __state)
    {
        EndProjectileHitContext(ref __state);
        return __exception;
    }

    private static void EndProjectileHitContext(ref bool active)
    {
        bool wasActive = active;
        active = false;
        SecondaryAttackRuntimeContext.EndProjectileHitContext(wasActive);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
internal static class CharacterDamageShieldReflectRoutePatch
{
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(
        Character __instance,
        HitData hit,
        bool __runOriginal,
        out SecondaryAttackManager.ShieldReflectCharacterDamageState __state)
    {
        __state = default;
        return !__runOriginal ||
               !SecondaryAttackManager.BeginShieldReflectCharacterDamage(__instance, hit, out __state);
    }

    [HarmonyPriority(Priority.First)]
    private static void Postfix(ref SecondaryAttackManager.ShieldReflectCharacterDamageState __state)
    {
        SecondaryAttackManager.EndShieldReflectCharacterDamage(ref __state);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        ref SecondaryAttackManager.ShieldReflectCharacterDamageState __state)
    {
        SecondaryAttackManager.EndShieldReflectCharacterDamage(ref __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Character), "RPC_Damage")]
internal static class CharacterRpcDamageShieldReflectScopePatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(
        Character __instance,
        ref long sender,
        HitData hit,
        out SecondaryAttackManager.ShieldReflectRpcDamageState __state)
    {
        SecondaryAttackManager.BeginShieldReflectRpcDamage(__instance, ref sender, hit, out __state);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        ref SecondaryAttackManager.ShieldReflectRpcDamageState __state)
    {
        SecondaryAttackManager.EndShieldReflectRpcDamage(ref __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Character), "Awake")]
internal static class CharacterAwakeCaptainValheimPatch
{
    private static void Postfix(Character __instance)
    {
        if (__instance is not Player || __instance.GetComponent<ZNetView>() == null)
        {
            return;
        }

        if (__instance.GetComponent<CaptainValheimCharacterRpc>() == null)
        {
            __instance.gameObject.AddComponent<CaptainValheimCharacterRpc>();
        }
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class PlayerUpdatePendingConfigPatch
{
    private static void Postfix(Player __instance)
    {
        if (__instance == Player.m_localPlayer)
        {
            ShieldRuntimeSystem.UpdateReturnedShieldAutoEquip(__instance);
        }
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
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(Humanoid __instance, HitData hit, ItemDrop.ItemData ___m_leftItem, out SecondaryAttackManager.BlockAttackContext? __state)
    {
        __state = SecondaryAttackManager.CaptureBlockAttackContext(__instance, hit, ___m_leftItem);
    }

    [HarmonyPriority(Priority.First)]
    private static void Postfix(bool __result, HitData hit, ref SecondaryAttackManager.BlockAttackContext? __state)
    {
        try
        {
            SecondaryAttackManager.FinalizeBlockAttack(__result, hit, __state);
        }
        catch
        {
            // Reflection failures must not interrupt the completed vanilla block.
        }
        finally
        {
            SecondaryAttackManager.EndShieldReflectBlockAttack(ref __state);
        }
    }

    private static Exception? Finalizer(
        Exception? __exception,
        ref SecondaryAttackManager.BlockAttackContext? __state)
    {
        SecondaryAttackManager.EndShieldReflectBlockAttack(ref __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(HitData), nameof(HitData.BlockDamage))]
internal static class HitDataBlockDamageShieldReflectPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(HitData __instance, out float __state)
    {
        __state = __instance.GetTotalBlockableDamage();
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(HitData __instance, float __state)
    {
        SecondaryAttackManager.RecordShieldReflectBlockDamage(__instance, __state);
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
        ItemDrop.ItemData ___m_rightItem)
    {
        return ShieldRuntimeSystem.HandleStartAttackPrefix(
            __instance,
            secondaryAttack,
            ref __result,
            ___m_leftItem,
            ___m_rightItem);
    }

    private static void Postfix(Humanoid __instance, bool __result)
    {
        ShieldRuntimeSystem.EndShieldAttackStart(__instance, __result);
    }

    private static Exception? Finalizer(Exception? __exception, Humanoid __instance)
    {
        ShieldRuntimeSystem.EndShieldAttackStart(__instance, startedAttack: false);
        return __exception;
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
internal static class AttackOnAttackTriggerPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(
        Attack __instance,
        out ShieldRuntimeSystem.ShieldPrimaryTriggerState __state)
    {
        return !ShieldRuntimeSystem.TryHandleCustomAttackTrigger(__instance, out __state);
    }

    private static void Postfix(ref ShieldRuntimeSystem.ShieldPrimaryTriggerState __state)
    {
        ShieldRuntimeSystem.EndShieldPrimaryVanillaTrigger(ref __state);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        ref ShieldRuntimeSystem.ShieldPrimaryTriggerState __state)
    {
        ShieldRuntimeSystem.EndShieldPrimaryVanillaTrigger(ref __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
internal static class AttackDoMeleeAttackSecondaryDurabilityFactorPatch
{
    private static void Prefix(Attack __instance, out SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        __state = SecondaryAttackManager.BeginSecondaryAttackDurabilityAdjustment(__instance);
    }

    private static void Postfix(ref SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(ref __state);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        ref SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(ref __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
internal static class AttackDoAreaAttackSecondaryDurabilityFactorPatch
{
    private static void Prefix(Attack __instance, out SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        __state = SecondaryAttackManager.BeginSecondaryAttackDurabilityAdjustment(__instance);
    }

    private static void Postfix(ref SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(ref __state);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        ref SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(ref __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Attack), "ProjectileAttackTriggered")]
internal static class AttackProjectileAttackTriggeredSecondaryDurabilityFactorPatch
{
    private static void Prefix(Attack __instance, out SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        __state = SecondaryAttackManager.BeginSecondaryAttackDurabilityAdjustment(__instance);
    }

    private static void Postfix(ref SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(ref __state);
    }

    private static Exception? Finalizer(
        Exception? __exception,
        ref SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(ref __state);
        return __exception;
    }
}
