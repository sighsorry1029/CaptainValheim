using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
            SecondaryAttackManager.RefreshShieldBlockChargePolicy(__instance);
            ShieldRuntimeSystem.UpdateReturnedShieldAutoEquip(__instance);
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
internal static class PlayerSetControlsShieldChargePatch
{
    private static void Postfix(Player __instance)
    {
        ShieldRuntimeSystem.ObserveWeaponChargeInput(__instance);
    }
}

[HarmonyPatch(typeof(Humanoid), "SetupEquipment")]
internal static class HumanoidSetupEquipmentShieldChargePolicyPatch
{
    private static void Postfix(Humanoid __instance)
    {
        SecondaryAttackManager.RefreshShieldBlockChargePolicy(__instance);
    }
}

[HarmonyPatch(typeof(ObjectDB), "Awake")]
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
    private static void Prefix(Humanoid __instance, GameObject go, ref ItemDrop.ItemData __state)
    {
        __state = null!;
        if (ShieldRuntimeSystem.CanQueueReturnedShieldAutoEquip(__instance))
        {
            ShieldRuntimeSystem.TryGetAutoEquipThrownShieldState(go, out __state);
        }
    }

    private static void Postfix(Humanoid __instance, bool __result, ItemDrop.ItemData __state)
    {
        if (!__result || __state == null || __instance is not Player player)
        {
            return;
        }

        ShieldRuntimeSystem.QueuePickedUpShieldAutoEquip(player, __state);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
internal static class HumanoidUseItemCancelReturnedShieldPatch
{
    private static void Prefix(Humanoid __instance, ItemDrop.ItemData item) =>
        ShieldRuntimeSystem.CancelReturnedShieldOnHandChoice(__instance, item);
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
internal static class HumanoidEquipItemCancelReturnedShieldPatch
{
    private static void Prefix(Humanoid __instance, ItemDrop.ItemData item) =>
        ShieldRuntimeSystem.CancelReturnedShieldOnHandChoice(__instance, item);
}

[HarmonyPatch(typeof(Humanoid), "BlockAttack")]
internal static class HumanoidBlockAttackPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(Humanoid __instance, HitData hit, ItemDrop.ItemData ___m_leftItem, out SecondaryAttackManager.BlockAttackContext? __state)
    {
        SecondaryAttackManager.RefreshShieldBlockChargePolicy(__instance);
        __state = SecondaryAttackManager.CaptureBlockAttackContext(__instance, hit, ___m_leftItem);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // Vanilla increments charges, emits an effect and fires the counterattack in one
        // branch. Gate its condition without replacing the block or mutating SharedData.
        FieldInfo enabledField = AccessTools.Field(typeof(ItemDrop.ItemData.SharedData), "m_buildBlockCharges");
        MethodInfo policy = AccessTools.Method(typeof(SecondaryAttackManager), nameof(SecondaryAttackManager.ShouldBuildShieldBlockCharges));
        List<CodeInstruction> result = new();
        int matches = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            result.Add(instruction);
            if (!instruction.LoadsField(enabledField))
            {
                continue;
            }

            matches++;
            result.Add(new CodeInstruction(OpCodes.Ldarg_0));
            result.Add(new CodeInstruction(OpCodes.Call, policy));
        }

        if (matches != 1)
        {
            throw new InvalidOperationException($"Expected one block-charge condition in Humanoid.BlockAttack, found {matches}.");
        }

        return result;
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

[HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
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

[HarmonyPatch(typeof(Attack), "DoAreaAttack")]
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
