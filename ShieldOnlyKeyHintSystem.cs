using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CaptainValheim;

internal static class ShieldOnlyKeyHintSystem
{
    private static readonly List<KeyHintCell> HintCells = [];
    private static readonly List<ShieldHintRow> ReusableRows = [];
    private static ShieldHintState _lastHintState = ShieldHintState.Hidden;
    private static bool _hasLastHintState;
    private static bool _showingHints;

    internal static void InitializeKeyHints(KeyHints hints)
    {
        DestroyHints();
        _hasLastHintState = false;
        _lastHintState = ShieldHintState.Hidden;
        _showingHints = false;
        UpdateKeyHint(hints);
    }

    internal static void UpdateKeyHint(KeyHints hints)
    {
        if (hints == null)
        {
            return;
        }

        Player? player = Player.m_localPlayer;
        if (!ShouldShowCustomCombatHints(hints, player))
        {
            HideHints();
            if (hints.m_combatHints != null)
            {
                hints.m_combatHints.SetActive(false);
            }

            return;
        }

        if (!TryBuildHintState(player!, out ShieldHintState state) || !state.HasRows)
        {
            HideHints();
            RememberHintState(ShieldHintState.Hidden);
            return;
        }

        if (_showingHints && _hasLastHintState && _lastHintState.Equals(state))
        {
            PrepareCombatHintGroup(hints, state.PreserveWeaponHints);
            return;
        }

        BuildHintRows(state, ReusableRows);
        EnsureHints(hints, ReusableRows.Count);
        if (HintCells.Count == 0)
        {
            return;
        }

        PrepareCombatHintGroup(hints, state.PreserveWeaponHints);
        for (int index = 0; index < HintCells.Count; index++)
        {
            KeyHintCell cell = HintCells[index];
            if (index >= ReusableRows.Count)
            {
                cell.SetActive(false);
                continue;
            }

            ShieldHintRow row = ReusableRows[index];
            cell.Set(row.Label, row.Keys, hideExtraTexts: row.Keys.Count <= 1);
        }

        HintCells[0].RebuildParentLayout();
        RememberHintState(state);
        _showingHints = true;
    }

    private static bool TryBuildHintState(Player player, out ShieldHintState state)
    {
        state = ShieldHintState.Hidden;
        ItemDrop.ItemData? leftItem = player.LeftItem;
        ItemDrop.ItemData? rightItem = player.RightItem;
        if (leftItem?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.Shield ||
            !ShieldRuntimeSystem.TryGetDefinition(leftItem, out SecondaryAttackDefinition definition) ||
            definition.ShieldSpecial is not { } shieldBehavior)
        {
            return false;
        }

        bool shieldOnly = rightItem == null;
        bool hasCharge = shieldBehavior.HasShieldCharge && shieldBehavior.ShieldChargeDistance > 0f &&
                         ShieldRuntimeSystem.IsShieldFeatureAllowed(
                             player, leftItem, CaptainValheimPlugin.Settings.WeaponCompatibility.AllowCharge.Value);
        state = new ShieldHintState(
            Localization.instance?.GetSelectedLanguage() ?? "",
            ZInput.IsGamepadActive(),
            preserveWeaponHints: !shieldOnly,
            hasCharge,
            shieldOnly && shieldBehavior.HasShieldPrimaryAttack,
            shieldOnly && shieldBehavior.HasShieldThrow,
            ResolveButtonLabel("Attack"),
            ResolveButtonLabel("Block"),
            ResolveButtonLabel("SecondaryAttack"));
        return state.HasRows;
    }

    private static void BuildHintRows(ShieldHintState state, List<ShieldHintRow> rows)
    {
        rows.Clear();
        if (state.HasCharge)
        {
            rows.Add(new ShieldHintRow(
                CaptainValheimLocalization.Text("captainvalheim_hint_charge"),
                [state.BlockKey, state.SecondaryAttackKey]));
        }

        if (state.HasPrimaryAttack)
        {
            rows.Add(new ShieldHintRow(
                CaptainValheimLocalization.Text("captainvalheim_hint_attack"),
                [state.AttackKey]));
        }

        if (state.HasThrow)
        {
            rows.Add(new ShieldHintRow(
                CaptainValheimLocalization.Text("captainvalheim_hint_throw"),
                [state.SecondaryAttackKey]));
        }
    }

    private static bool ShouldShowCustomCombatHints(KeyHints hints, Player? player)
    {
        return player != null &&
               !player.IsDead() &&
               GameAccess.KeyHintsEnabled(hints) &&
               !player.InPlaceMode() &&
               !Hud.IsPieceSelectionVisible() &&
               !Hud.InRadial() &&
               !InventoryGui.IsVisible() &&
               !Menu.IsVisible() &&
               !Console.IsVisible() &&
               !Game.IsPaused() &&
               (Chat.instance == null || !Chat.instance.IsChatDialogWindowVisible()) &&
               (InventoryGui.instance == null ||
                 (!InventoryGui.instance.IsSkillsPanelOpen &&
                  !InventoryGui.instance.IsTrophisPanelOpen &&
                  !InventoryGui.instance.IsTextPanelOpen)) &&
               !PlayerCustomizaton.IsBarberGuiVisible() &&
               player.GetDoodadController() == null;
    }

    private static void PrepareCombatHintGroup(KeyHints hints, bool preserveWeaponHints)
    {
        if (hints.m_combatHints != null)
        {
            hints.m_combatHints.SetActive(true);
        }

        // UpdateHints has already selected the weapon's primary/secondary hints.
        // Mixed equipment adds the charge chord without replacing those weapon controls.
        if (preserveWeaponHints)
        {
            return;
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
                $"CaptainValheim_ShieldOnlyHint_{HintCells.Count}");
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
        }

        if (HintCells.Count > 0)
        {
            HintCells[0].RebuildParentLayout();
        }

        _showingHints = false;
    }

    internal static void Dispose()
    {
        DestroyHints();
        ReusableRows.Clear();
        _lastHintState = ShieldHintState.Hidden;
        _hasLastHintState = false;
        _showingHints = false;
    }

    private static void DestroyHints()
    {
        foreach (KeyHintCell cell in HintCells)
        {
            if (cell.Root != null)
            {
                cell.Root.SetActive(false);
                cell.Root.transform.SetParent(null, false);
                Object.Destroy(cell.Root);
            }
        }

        HintCells.Clear();
    }

    private static void RememberHintState(ShieldHintState state)
    {
        _lastHintState = state;
        _hasLastHintState = true;
    }

    private readonly struct ShieldHintState(
        string language,
        bool gamepad,
        bool preserveWeaponHints,
        bool hasCharge,
        bool hasPrimaryAttack,
        bool hasThrow,
        string attackKey,
        string blockKey,
        string secondaryAttackKey)
    {
        internal static readonly ShieldHintState Hidden =
            new("", false, false, false, false, false, "", "", "");

        private readonly string _language = language;
        private readonly bool _gamepad = gamepad;
        internal readonly bool PreserveWeaponHints = preserveWeaponHints;
        internal readonly bool HasCharge = hasCharge;
        internal readonly bool HasPrimaryAttack = hasPrimaryAttack;
        internal readonly bool HasThrow = hasThrow;
        internal readonly string AttackKey = attackKey;
        internal readonly string BlockKey = blockKey;
        internal readonly string SecondaryAttackKey = secondaryAttackKey;

        internal bool HasRows => HasCharge || HasPrimaryAttack || HasThrow;

        internal bool Equals(ShieldHintState other)
        {
            return _gamepad == other._gamepad &&
                   PreserveWeaponHints == other.PreserveWeaponHints &&
                   HasCharge == other.HasCharge &&
                   HasPrimaryAttack == other.HasPrimaryAttack &&
                   HasThrow == other.HasThrow &&
                   string.Equals(AttackKey, other.AttackKey, System.StringComparison.Ordinal) &&
                   string.Equals(BlockKey, other.BlockKey, System.StringComparison.Ordinal) &&
                   string.Equals(SecondaryAttackKey, other.SecondaryAttackKey, System.StringComparison.Ordinal) &&
                   string.Equals(_language, other._language, System.StringComparison.Ordinal);
        }
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
