namespace CaptainValheim;

internal static class SecondaryAttackStartAttackDispatch
{
    internal static bool Prefix(
        Humanoid humanoid,
        bool secondaryAttack,
        ref bool result,
        ItemDrop.ItemData leftItem,
        ItemDrop.ItemData rightItem)
    {
        if (TryBlockActiveShieldCharge(humanoid, ref result))
        {
            return false;
        }

        if (!secondaryAttack)
        {
            BeginShieldPrimaryStartIfNeeded(humanoid, leftItem, rightItem);
            return true;
        }

        if (TryHandleShieldSecondaryStart(humanoid, leftItem, rightItem, ref result, out bool runOriginal))
        {
            return runOriginal;
        }

        return true;
    }

    internal static void Postfix(Humanoid humanoid, bool result)
    {
        ShieldRuntimeSystem.EndShieldAttackStart(humanoid, result);
    }

    internal static void Finalize(Humanoid humanoid)
    {
        ShieldRuntimeSystem.EndShieldAttackStart(humanoid, startedAttack: false);
    }

    private static bool TryBlockActiveShieldCharge(Humanoid humanoid, ref bool result)
    {
        if (!ShieldRuntimeSystem.IsShieldChargeActive(humanoid))
        {
            return false;
        }

        result = false;
        return true;
    }

    private static void BeginShieldPrimaryStartIfNeeded(
        Humanoid humanoid,
        ItemDrop.ItemData leftItem,
        ItemDrop.ItemData rightItem)
    {
        if (ShieldRuntimeSystem.TryGetShieldOnlyPrimary(humanoid, leftItem, rightItem, out ItemDrop.ItemData primaryShieldWeapon, out _))
        {
            ShieldRuntimeSystem.BeginShieldPrimaryStart(humanoid, primaryShieldWeapon);
        }
    }

    private static bool TryHandleShieldSecondaryStart(
        Humanoid humanoid,
        ItemDrop.ItemData leftItem,
        ItemDrop.ItemData rightItem,
        ref bool result,
        out bool runOriginal)
    {
        runOriginal = true;
        if (!ShieldRuntimeSystem.TryGetShieldOnlySecondary(humanoid, leftItem, rightItem, out ItemDrop.ItemData shieldWeapon, out SecondaryAttackDefinition definition))
        {
            return false;
        }

        ShieldSpecialMode mode = humanoid is Player player
            ? ShieldRuntimeSystem.ResolveShieldSpecialMode(player, definition)
            : ShieldSpecialMode.Throw;

        if (mode == ShieldSpecialMode.Charge && !ShieldRuntimeSystem.CanStartShieldCharge(humanoid, definition))
        {
            result = false;
            runOriginal = false;
            return true;
        }

        if (mode == ShieldSpecialMode.Charge)
        {
            result = ShieldRuntimeSystem.TryStartShieldChargeDirect(humanoid, shieldWeapon, definition);
            runOriginal = false;
            return true;
        }

        ShieldRuntimeSystem.BeginShieldSecondaryStart(humanoid, shieldWeapon, mode);
        return true;
    }
}
