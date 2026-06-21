using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CaptainValheim;

internal static class ShieldOnlyKeyHintSystem
{
    private static KeyHints? _activeKeyHints;
    private static readonly List<KeyHintCell> HintCells = [];
    private static bool _showingHints;

    internal static void InitializeKeyHints(KeyHints hints)
    {
        _activeKeyHints = hints;
        DestroyHints();
        _showingHints = false;
        UpdateKeyHint(hints);
    }

    internal static void RefreshKeyHintUi()
    {
        if (_activeKeyHints != null)
        {
            UpdateKeyHint(_activeKeyHints);
        }
    }

    internal static void UpdateKeyHint(KeyHints hints)
    {
        if (hints == null)
        {
            return;
        }

        _activeKeyHints = hints;
        if (!ShouldAllowCustomCombatHints(hints))
        {
            HideHints();
            if (hints.m_combatHints != null)
            {
                hints.m_combatHints.SetActive(false);
            }

            return;
        }

        if (!TryBuildHintRows(out List<ShieldHintRow> rows) || rows.Count == 0)
        {
            HideHints();
            return;
        }

        EnsureHints(hints, rows.Count);
        if (HintCells.Count == 0)
        {
            return;
        }

        PrepareCombatHintGroup(hints);
        for (int index = 0; index < HintCells.Count; index++)
        {
            KeyHintCell cell = HintCells[index];
            if (index >= rows.Count)
            {
                cell.SetActive(false);
                continue;
            }

            ShieldHintRow row = rows[index];
            cell.Set(row.Label, row.Keys, hideExtraTexts: row.Keys.Count <= 1);
            cell.RebuildParentLayout();
        }

        _showingHints = true;
    }

    private static bool TryBuildHintRows(out List<ShieldHintRow> rows)
    {
        rows = [];
        Player? player = Player.m_localPlayer;
        if (!ShouldShowCombatHints(player))
        {
            return false;
        }

        ItemDrop.ItemData? leftItem = player.GetLeftItem();
        ItemDrop.ItemData? rightItem = player.GetRightItem();
        if (rightItem != null ||
            leftItem?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.Shield ||
            !SecondaryAttackRuntimeFacade.TryGetDefinition(leftItem, out SecondaryAttackDefinition definition) ||
            definition.Behavior is not ShieldSpecialSecondaryBehavior shieldBehavior)
        {
            return false;
        }

        if (shieldBehavior.HasShieldCharge && shieldBehavior.ShieldChargeDistance > 0f)
        {
            rows.Add(new ShieldHintRow("Charge", [ResolveButtonLabel("Block"), ResolveButtonLabel("SecondaryAttack")]));
        }

        if (shieldBehavior.HasShieldPrimaryAttack)
        {
            rows.Add(new ShieldHintRow("Attack", [ResolveButtonLabel("Attack")]));
        }

        if (shieldBehavior.HasShieldThrow)
        {
            rows.Add(new ShieldHintRow("Throw", [ResolveButtonLabel("SecondaryAttack")]));
        }

        return true;
    }

    private static bool ShouldAllowCustomCombatHints(KeyHints hints)
    {
        return hints.m_keyHintsEnabled &&
               !InventoryGui.IsVisible() &&
               !Menu.IsVisible() &&
               !Console.IsVisible() &&
               !Game.IsPaused() &&
               (Chat.instance == null || !Chat.instance.HasFocus()) &&
               (InventoryGui.instance == null ||
                (!InventoryGui.instance.IsSkillsPanelOpen &&
                 !InventoryGui.instance.IsTrophisPanelOpen &&
                 !InventoryGui.instance.IsTextPanelOpen));
    }

    private static bool ShouldShowCombatHints(Player? player)
    {
        return player != null &&
               !player.IsDead() &&
               !Hud.IsPieceSelectionVisible() &&
               !Hud.InRadial() &&
               !InventoryGui.IsVisible() &&
               !Menu.IsVisible() &&
               !Console.IsVisible() &&
               !Game.IsPaused() &&
               (Chat.instance == null || !Chat.instance.HasFocus()) &&
               (InventoryGui.instance == null ||
                (!InventoryGui.instance.IsSkillsPanelOpen &&
                 !InventoryGui.instance.IsTrophisPanelOpen &&
                 !InventoryGui.instance.IsTextPanelOpen)) &&
               !PlayerCustomizaton.IsBarberGuiVisible() &&
               player.GetDoodadController() == null;
    }

    private static void PrepareCombatHintGroup(KeyHints hints)
    {
        if (hints.m_combatHints != null)
        {
            hints.m_combatHints.SetActive(true);
        }

        SetVanillaCombatHintActive(hints.m_bowDrawGP, false);
        SetVanillaCombatHintActive(hints.m_bowDrawKB, false);
        SetVanillaCombatHintActive(hints.m_primaryAttackGP, false);
        SetVanillaCombatHintActive(hints.m_primaryAttackKB, false);
        SetVanillaCombatHintActive(hints.m_secondaryAttackGP, false);
        SetVanillaCombatHintActive(hints.m_secondaryAttackKB, false);
    }

    private static void SetVanillaCombatHintActive(GameObject? hint, bool active)
    {
        if (hint != null)
        {
            hint.SetActive(active);
        }
    }

    private static void EnsureHints(KeyHints hints, int count)
    {
        GameObject? template = ResolveCombatHintTemplate(hints);
        if (template == null || template.transform.parent == null)
        {
            return;
        }

        if (HintCells.Count > 0 && HintCells[0].Root.transform.parent != template.transform.parent)
        {
            DestroyHints();
        }

        while (HintCells.Count < count)
        {
            KeyHintCell? cell = KeyHintCell.CloneFrom(
                template,
                $"CaptainValheim_ShieldOnlyHint_{HintCells.Count}",
                hideOnRestore: true);
            if (cell == null)
            {
                break;
            }

            cell.MoveBefore(template);
            HintCells.Add(cell);
        }

        foreach (KeyHintCell cell in HintCells)
        {
            cell.MoveBefore(template);
        }
    }

    private static GameObject? ResolveCombatHintTemplate(KeyHints hints)
    {
        GameObject? preferredPrimary = ZInput.IsGamepadActive() ? hints.m_primaryAttackGP : hints.m_primaryAttackKB;
        if (KeyHintCell.IsUsableTemplate(preferredPrimary))
        {
            return preferredPrimary;
        }

        GameObject? alternatePrimary = ZInput.IsGamepadActive() ? hints.m_primaryAttackKB : hints.m_primaryAttackGP;
        if (KeyHintCell.IsUsableTemplate(alternatePrimary))
        {
            return alternatePrimary;
        }

        GameObject? preferredSecondary = ZInput.IsGamepadActive() ? hints.m_secondaryAttackGP : hints.m_secondaryAttackKB;
        if (KeyHintCell.IsUsableTemplate(preferredSecondary))
        {
            return preferredSecondary;
        }

        GameObject? alternateSecondary = ZInput.IsGamepadActive() ? hints.m_secondaryAttackKB : hints.m_secondaryAttackGP;
        if (KeyHintCell.IsUsableTemplate(alternateSecondary))
        {
            return alternateSecondary;
        }

        if (hints.m_combatHints == null)
        {
            return null;
        }

        Transform? parent = KeyHintCell.FindParentWithTemplates(hints.m_combatHints, ZInput.IsGamepadActive() ? "Gamepad" : "Keyboard")
                            ?? KeyHintCell.FindParentWithTemplates(hints.m_combatHints, "Keyboard")
                            ?? KeyHintCell.FindParentWithTemplates(hints.m_combatHints, "Gamepad")
                            ?? hints.m_combatHints.transform;
        foreach (Transform child in parent)
        {
            if (KeyHintCell.IsUsableTemplate(child.gameObject))
            {
                return child.gameObject;
            }
        }

        return null;
    }

    private static string ResolveButtonLabel(string button)
    {
        bool gamepad = ZInput.IsGamepadActive();
        string buttonName = button;
        if (gamepad && button.Equals("Attack", System.StringComparison.OrdinalIgnoreCase))
        {
            buttonName = "JoyAttack";
        }
        else if (gamepad && button.Equals("Block", System.StringComparison.OrdinalIgnoreCase))
        {
            buttonName = "JoyBlock";
        }
        else if (gamepad && button.Equals("SecondaryAttack", System.StringComparison.OrdinalIgnoreCase))
        {
            buttonName = "JoySecondaryAttack";
        }

        string boundKey = ZInput.instance?.GetBoundKeyString(buttonName, emptyStringOnMissing: true) ?? "";
        if (!string.IsNullOrWhiteSpace(boundKey))
        {
            return Localization.instance != null ? Localization.instance.Localize(boundKey) : boundKey;
        }

        return button switch
        {
            "Attack" => gamepad ? "RT" : "LMB",
            "Block" => gamepad ? "LB" : "RMB",
            _ => gamepad ? "RB" : "MMB"
        };
    }

    private static void HideHints()
    {
        if (!_showingHints)
        {
            return;
        }

        foreach (KeyHintCell cell in HintCells)
        {
            cell.SetActive(false);
            cell.RebuildParentLayout();
        }

        _showingHints = false;
    }

    private static void DestroyHints()
    {
        foreach (KeyHintCell cell in HintCells)
        {
            if (cell.Root != null)
            {
                Object.Destroy(cell.Root);
            }
        }

        HintCells.Clear();
    }

    private readonly struct ShieldHintRow(string label, IReadOnlyList<string> keys)
    {
        internal readonly string Label = label;
        internal readonly IReadOnlyList<string> Keys = keys;
    }
}

[HarmonyPatch(typeof(KeyHints), "Awake")]
internal static class KeyHintsAwakeShieldOnlyPatch
{
    private static void Postfix(KeyHints __instance)
    {
        ShieldOnlyKeyHintSystem.InitializeKeyHints(__instance);
    }
}

[HarmonyPatch(typeof(KeyHints), "UpdateHints")]
internal static class KeyHintsUpdateShieldOnlyPatch
{
    private static void Postfix(KeyHints __instance)
    {
        ShieldOnlyKeyHintSystem.UpdateKeyHint(__instance);
    }
}
