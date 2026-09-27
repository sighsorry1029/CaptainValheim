using HarmonyLib;

namespace CaptainValheim;

internal static class ShieldTooltipSystem
{
    internal static void AppendShieldGuidance(ItemDrop.ItemData? item, ref string tooltip)
    {
        if (CaptainValheimPlugin.Settings.General.ShowShieldTooltip.Value != CaptainValheimPlugin.Toggle.On ||
            !HasEnabledShieldFeature(item))
        {
            return;
        }

        string headline = CaptainValheimLocalization.Text("captainvalheim_shield_tooltip");
        string details = CaptainValheimLocalization.Text("captainvalheim_shield_tooltip_note");
        string guidance = $"{headline}\n{details}";
        tooltip = string.IsNullOrWhiteSpace(tooltip)
            ? guidance
            : $"{tooltip}\n\n{guidance}";
    }

    private static bool HasEnabledShieldFeature(ItemDrop.ItemData? item)
    {
        if (item?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.Shield ||
            !ShieldRuntimeSystem.TryGetDefinition(item, out SecondaryAttackDefinition definition))
        {
            return false;
        }

        ShieldSpecialSecondaryBehavior? behavior = definition.ShieldSpecial;
        return definition.ShieldProjectileReflect || definition.ShieldBlockCharge ||
               behavior is
               {
                   HasShieldPrimaryAttack: true
               } ||
               behavior is
               {
                   HasShieldThrow: true
               } ||
               behavior is
               {
                   HasShieldCharge: true,
                   ShieldChargeDistance: > 0f
               };
    }
}

[HarmonyPatch(
    typeof(ItemDrop.ItemData),
    nameof(ItemDrop.ItemData.GetTooltip),
    typeof(ItemDrop.ItemData),
    typeof(int),
    typeof(bool),
    typeof(float),
    typeof(int),
    typeof(bool))]
internal static class ItemDataGetTooltipShieldGuidancePatch
{
    private static void Postfix(ItemDrop.ItemData item, ref string __result)
    {
        ShieldTooltipSystem.AppendShieldGuidance(item, ref __result);
    }
}
