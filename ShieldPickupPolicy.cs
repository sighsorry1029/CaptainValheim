using System;
using HarmonyLib;
using UnityEngine;

namespace CaptainValheim;

// Keep pickup intent at the call boundary. Recovery metadata belongs to the ground
// drop, never the inventory item, so an ordinary later drop is ordinary loot again.
internal static class ShieldPickupPolicy
{
    internal const string RecoveredWeaponThrowerIdKey = "CaptainValheim.RecoveredWeaponThrowerID";
    internal const string FearNoSpearThrowerIdKey = "FearNoSpear.ThrowerPlayerID";
    internal const string SecondaryAttacksThrowerIdKey = "SecondaryAttacks.ThrowerPlayerID";

    [ThreadStatic] private static Player? AutoPickupPlayer;
    [ThreadStatic] private static RecoverableDropScope RecoveredDrop;
    private static bool _reportedMetadataFailure;

    internal readonly struct AutoPickupScope
    {
        internal readonly Player? PreviousPlayer;
        internal readonly bool Entered;
        internal AutoPickupScope(Player? previous) { PreviousPlayer = previous; Entered = true; }
    }

    internal readonly struct RecoverableDropScope
    {
        internal readonly ItemDrop.ItemData? Item;
        internal readonly long ThrowerId;
        internal readonly bool Entered;
        internal RecoverableDropScope(ItemDrop.ItemData? item, long throwerId)
        {
            Item = item;
            ThrowerId = throwerId;
            Entered = true;
        }
    }

    internal static AutoPickupScope BeginAutoPickup(Player player)
    {
        AutoPickupScope previous = new(AutoPickupPlayer);
        AutoPickupPlayer = player;
        return previous;
    }

    internal static void EndAutoPickup(ref AutoPickupScope previous)
    {
        if (!previous.Entered) return;
        AutoPickupPlayer = previous.PreviousPlayer;
        previous = default;
    }

    internal static bool IsAutomaticPickup(Humanoid humanoid) => humanoid is not null && ReferenceEquals(AutoPickupPlayer, humanoid);

    internal static bool HasShieldOnlyHands(ItemDrop.ItemData? left, ItemDrop.ItemData? right,
        ItemDrop.ItemData? hiddenLeft, ItemDrop.ItemData? hiddenRight) =>
        left?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield &&
        right == null && hiddenLeft == null && hiddenRight == null;

    internal static bool ShouldAllowAutoEquip(bool requested, bool protectShieldOnly, long playerId, long throwerId) =>
        requested && (!protectShieldOnly || (playerId != 0L && throwerId == playerId));

    internal static bool FilterAutoEquip(Humanoid humanoid, GameObject go, bool autoequip)
    {
        if (!autoequip || !IsAutomaticPickup(humanoid) ||
            CaptainValheimPlugin.Settings.General.PreventWeaponAutoEquip.Value != CaptainValheimPlugin.Toggle.On ||
            humanoid is not Player player || player != Player.m_localPlayer || !player.IsOwner() || player.IsDead())
            return autoequip;

        bool shieldOnly = HasShieldOnlyHands(player.LeftItem, player.RightItem,
            GameAccess.HiddenLeftItem(player), GameAccess.HiddenRightItem(player)) ||
            ShieldRuntimeSystem.HasPendingShieldReturn(player);
        if (!shieldOnly) return autoequip;

        ZNetView? view = go != null ? go.GetComponent<ZNetView>() : null;
        long throwerId = view != null && view.IsValid() ? ReadRecoveredThrower(view.GetZDO()) : 0L;
        // The original Pickup still performs all pickup, inventory and equip checks.
        return ShouldAllowAutoEquip(autoequip, shieldOnly, player.GetPlayerID(), throwerId);
    }

    internal static long ReadRecoveredThrower(ZDO? zdo)
    {
        if (zdo == null || !zdo.IsValid()) return 0L;
        long id = zdo.GetLong(RecoveredWeaponThrowerIdKey, 0L);
        if (id == 0L) id = zdo.GetLong(FearNoSpearThrowerIdKey, 0L);
        if (id == 0L) id = zdo.GetLong(SecondaryAttacksThrowerIdKey, 0L);
        return id;
    }

    internal static RecoverableDropScope BeginRecoveredDrop(Projectile projectile, Character? owner)
    {
        RecoverableDropScope previous = new(RecoveredDrop.Item, RecoveredDrop.ThrowerId);
        RecoveredDrop = default;
        try
        {
            if (!projectile.m_respawnItemOnHit || projectile.m_spawnItem?.m_shared == null ||
                !projectile.m_spawnItem.IsWeapon() || owner is not Player player)
                return previous;

            ZNetView? view = projectile.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner())
                RecoveredDrop = new RecoverableDropScope(projectile.m_spawnItem, player.GetPlayerID());
        }
        catch (Exception exception) { ReportMetadataFailure(exception); }
        return previous;
    }

    internal static void EndRecoveredDrop(ref RecoverableDropScope previous)
    {
        if (!previous.Entered) return;
        RecoveredDrop = previous;
        previous = default;
    }

    internal static long GetCurrentRecoveredThrower(ItemDrop.ItemData? item) =>
        item != null && ReferenceEquals(item, RecoveredDrop.Item) ? RecoveredDrop.ThrowerId : 0L;

    internal static void MarkRecoveredDrop(ItemDrop.ItemData item, ItemDrop? drop)
    {
        long throwerId = GetCurrentRecoveredThrower(item);
        if (throwerId == 0L || drop == null) return;
        try
        {
            ZNetView? view = drop.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner())
                view.GetZDO().Set(RecoveredWeaponThrowerIdKey, throwerId);
        }
        catch (Exception exception)
        {
            // The item already exists. Never retry the drop or fail the pickup lifecycle.
            ReportMetadataFailure(exception);
        }
    }

    private static void ReportMetadataFailure(Exception exception)
    {
        if (_reportedMetadataFailure) return;
        _reportedMetadataFailure = true;
        CaptainValheimPlugin.ModLogger.LogWarning($"Could not identify a recovered weapon for automatic equipment: {exception.Message}");
    }

    internal static void Reset()
    {
        AutoPickupPlayer = null;
        RecoveredDrop = default;
    }
}

[HarmonyPatch(typeof(Player), "AutoPickup")]
internal static class PlayerAutoPickupShieldScopePatch
{
    private static void Prefix(Player __instance, out ShieldPickupPolicy.AutoPickupScope __state) =>
        __state = ShieldPickupPolicy.BeginAutoPickup(__instance);

    private static Exception? Finalizer(Exception? __exception, ref ShieldPickupPolicy.AutoPickupScope __state)
    {
        ShieldPickupPolicy.EndAutoPickup(ref __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Projectile), "SpawnOnHit", typeof(GameObject), typeof(Collider), typeof(Vector3))]
internal static class ProjectileSpawnRecoveredWeaponScopePatch
{
    private static void Prefix(Projectile __instance, Character ___m_owner, out ShieldPickupPolicy.RecoverableDropScope __state) =>
        __state = ShieldPickupPolicy.BeginRecoveredDrop(__instance, ___m_owner);

    private static Exception? Finalizer(Exception? __exception, ref ShieldPickupPolicy.RecoverableDropScope __state)
    {
        ShieldPickupPolicy.EndRecoveredDrop(ref __state);
        return __exception;
    }
}

// Observe the real DropItem result, including calls wrapped by other mods. Keeping
// the SpawnOnHit instruction intact composes with FearNoSpear's recovery wrapper.
[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem), typeof(ItemDrop.ItemData), typeof(int), typeof(Vector3), typeof(Quaternion))]
internal static class ItemDropRecoveredWeaponTagPatch
{
    private static void Postfix(ItemDrop.ItemData item, ItemDrop __result) =>
        ShieldPickupPolicy.MarkRecoveredDrop(item, __result);
}
