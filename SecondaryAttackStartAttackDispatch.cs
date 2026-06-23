namespace CaptainValheim;

internal static class SecondaryAttackStartAttackDispatch
{
    internal readonly struct StartAttackState
    {
        internal static readonly StartAttackState Empty = new();
    }

    internal static bool Prefix(
        Humanoid humanoid,
        bool secondaryAttack,
        ref bool result,
        ItemDrop.ItemData leftItem,
        ItemDrop.ItemData rightItem,
        out StartAttackState state)
    {
        state = StartAttackState.Empty;
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

    internal static void Postfix(
        Humanoid humanoid,
        bool secondaryAttack,
        bool result,
        StartAttackState state)
    {
        ShieldRuntimeSystem.EndShieldAttackStart(humanoid, result);
    }

    private static bool TryBlockActiveShieldCharge(Humanoid humanoid, ref bool result)
    {
        if (!ShieldRuntimeSystem.IsShieldChargeActiveForDebug(humanoid))
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
