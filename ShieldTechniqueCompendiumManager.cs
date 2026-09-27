using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CaptainValheim;

internal static class ShieldTechniqueCompendiumManager
{
    private const string PageTopic = "$captainvalheim_compendium_title";
    private const string BodyIconPrefix = "CaptainValheim_CompendiumTechniqueIcon_";
    private const char IconMarker = '*';
    private const float IconSize = 18f;
    private const float IconTextGap = 5f;
    private static readonly IReadOnlyList<ShieldTechniqueInfo> Entries =
    [
        new(
            ShieldTechniqueGroup.ActiveAttacks,
            "ShieldWood",
            "$captainvalheim_compendium_primary_attack_name",
            "$captainvalheim_compendium_primary_attack_description"),
        new(
            ShieldTechniqueGroup.ActiveAttacks,
            "ShieldBanded",
            "$captainvalheim_compendium_throw_name",
            "$captainvalheim_compendium_throw_description"),
        new(
            ShieldTechniqueGroup.ActiveAttacks,
            "ShieldIronTower",
            "$captainvalheim_compendium_charge_name",
            "$captainvalheim_compendium_charge_description"),
        new(
            ShieldTechniqueGroup.DefensiveTechniques,
            "ShieldSerpentscale",
            "$captainvalheim_compendium_reflect_name",
            "$captainvalheim_compendium_reflect_description"),
        new(
            ShieldTechniqueGroup.DefensiveTechniques,
            "ShieldCarapaceBuckler",
            "$captainvalheim_compendium_block_charge_name",
            "$captainvalheim_compendium_block_charge_description")
    ];
    private static readonly List<GameObject> BodyIcons = [];
    private static readonly List<TextsDialog> PageDialogs = [];

    internal static void AddTechniquePage(TextsDialog dialog)
    {
        if (dialog == null || GameAccess.Texts(dialog) == null)
        {
            return;
        }

        TrackDialog(dialog);
        GameAccess.Texts(dialog).RemoveAll(text => IsTechniquePage(text?.m_topic));
        GameAccess.Texts(dialog).Add(new TextsDialog.TextInfo(PageTopic, BuildPageText()));
    }

    internal static void RefreshPageContentIcons(TextsDialog dialog, TextsDialog.TextInfo info)
    {
        if (dialog?.m_textArea == null || info == null)
        {
            return;
        }

        ClearPageContentIcons(dialog);
        if (!IsTechniquePage(info.m_topic))
        {
            return;
        }

        TMP_Text textArea = dialog.m_textArea;
        RectTransform? content = textArea.transform.parent as RectTransform;
        if (content == null)
        {
            return;
        }

        textArea.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        textArea.ForceMeshUpdate();

        TMP_TextInfo textInfo = textArea.textInfo;
        int entryIndex = 0;
        for (int characterIndex = 0;
             characterIndex < textInfo.characterCount && entryIndex < Entries.Count;
             characterIndex++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[characterIndex];
            if (character.character != IconMarker)
            {
                continue;
            }

            ShieldTechniqueInfo entry = Entries[entryIndex];
            AttachBodyIcon(
                content,
                textArea.rectTransform,
                character,
                ResolveIcon(entry),
                entryIndex);
            entryIndex++;
        }
    }

    internal static void ClearPageContentIcons(TextsDialog dialog)
    {
        DestroyTrackedIcons();
        if (dialog?.m_textArea == null)
        {
            return;
        }

        ClearIconChildren(dialog.m_textArea.transform);
        if (dialog.m_textArea.transform.parent != null)
        {
            ClearIconChildren(dialog.m_textArea.transform.parent);
        }
    }

    internal static void Dispose()
    {
        DestroyTrackedIcons();
        foreach (TextsDialog dialog in PageDialogs)
        {
            if (dialog == null)
            {
                continue;
            }

            GameAccess.Texts(dialog)?.RemoveAll(text => IsTechniquePage(text?.m_topic));
            if (dialog.m_textArea == null)
            {
                continue;
            }

            ClearIconChildren(dialog.m_textArea.transform);
            if (dialog.m_textArea.transform.parent != null)
            {
                ClearIconChildren(dialog.m_textArea.transform.parent);
            }
        }

        PageDialogs.Clear();
    }

    private static string BuildPageText()
    {
        StringBuilder builder = new();
        builder
            .Append("<color=#D6D6D6>")
            .Append(CaptainValheimLocalization.Localize("$captainvalheim_compendium_intro"))
            .Append("</color>\n\n");

        ShieldTechniqueGroup? previousGroup = null;
        foreach (ShieldTechniqueInfo entry in Entries)
        {
            if (previousGroup != entry.Group)
            {
                if (previousGroup != null)
                {
                    builder.Append('\n');
                }

                builder
                    .Append("<color=#FFD27A><b>")
                    .Append(LocalizeGroupHeading(entry.Group))
                    .Append("</b></color>\n\n");
                previousGroup = entry.Group;
            }

            builder
                .Append("<color=#00000000>")
                .Append(IconMarker)
                .Append("</color>    ")
                .Append("<color=orange><b>")
                .Append(CaptainValheimLocalization.Localize(entry.NameToken))
                .Append("</b></color>\n     ")
                .Append(CaptainValheimLocalization.Localize(entry.DescriptionToken))
                .Append("\n\n");
        }

        return builder.ToString().TrimEnd();
    }

    private static string LocalizeGroupHeading(ShieldTechniqueGroup group)
    {
        return group switch
        {
            ShieldTechniqueGroup.ActiveAttacks =>
                CaptainValheimLocalization.Localize("$captainvalheim_compendium_group_active"),
            ShieldTechniqueGroup.DefensiveTechniques =>
                CaptainValheimLocalization.Localize("$captainvalheim_compendium_group_defensive"),
            _ => ""
        };
    }

    private static void AttachBodyIcon(
        RectTransform content,
        RectTransform textArea,
        TMP_CharacterInfo marker,
        Sprite? sprite,
        int index)
    {
        if (content == null || textArea == null || sprite == null)
        {
            return;
        }

        GameObject icon = new(
            $"{BodyIconPrefix}{index}",
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement))
        {
            layer = textArea.gameObject.layer
        };
        RectTransform rect = (RectTransform)icon.transform;
        rect.SetParent(content, false);
        rect.anchorMin = content.pivot;
        rect.anchorMax = content.pivot;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(IconSize, IconSize);

        Vector3 center = (marker.bottomLeft + marker.topLeft) * 0.5f;
        center.x += IconSize * 0.5f + IconTextGap;
        Vector3 worldCenter = textArea.TransformPoint(center);
        Vector3 contentCenter = content.InverseTransformPoint(worldCenter);
        rect.anchoredPosition = new Vector2(contentCenter.x, contentCenter.y);

        Image image = icon.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;

        LayoutElement layout = icon.GetComponent<LayoutElement>();
        layout.ignoreLayout = true;
        BodyIcons.Add(icon);
    }

    private static void DestroyTrackedIcons()
    {
        foreach (GameObject icon in BodyIcons)
        {
            if (icon == null)
            {
                continue;
            }

            icon.SetActive(false);
            icon.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(icon);
        }

        BodyIcons.Clear();
    }

    private static void ClearIconChildren(Transform parent)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Transform child = parent.GetChild(index);
            if (!child.name.StartsWith(BodyIconPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    private static void TrackDialog(TextsDialog dialog)
    {
        PageDialogs.RemoveAll(static trackedDialog => trackedDialog == null);
        if (!PageDialogs.Contains(dialog))
        {
            PageDialogs.Add(dialog);
        }
    }

    private static Sprite? ResolveIcon(ShieldTechniqueInfo entry)
    {
        if (ObjectDB.instance == null)
        {
            return null;
        }

        GameObject? iconPrefab = ObjectDB.instance.GetItemPrefab(entry.IconPrefabName) ??
                                 ObjectDB.instance.GetItemPrefab("ShieldWood");
        ItemDrop? itemDrop = iconPrefab?.GetComponent<ItemDrop>();
        return itemDrop?.m_itemData?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private static bool IsTechniquePage(string? topic)
    {
        return string.Equals(topic, PageTopic, StringComparison.Ordinal);
    }

    private enum ShieldTechniqueGroup
    {
        ActiveAttacks,
        DefensiveTechniques
    }

    private sealed class ShieldTechniqueInfo(
        ShieldTechniqueGroup group,
        string iconPrefabName,
        string nameToken,
        string descriptionToken)
    {
        internal ShieldTechniqueGroup Group { get; } = group;

        internal string IconPrefabName { get; } = iconPrefabName;

        internal string NameToken { get; } = nameToken;

        internal string DescriptionToken { get; } = descriptionToken;
    }
}

[HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
internal static class TextsDialogUpdateTextsListShieldTechniquePatch
{
    private static void Postfix(TextsDialog __instance)
    {
        ShieldTechniqueCompendiumManager.AddTechniquePage(__instance);
    }
}

[HarmonyPatch(typeof(TextsDialog), "ShowText", new[] { typeof(TextsDialog.TextInfo) })]
internal static class TextsDialogShowTextShieldTechniquePatch
{
    private static void Postfix(TextsDialog __instance, TextsDialog.TextInfo text)
    {
        ShieldTechniqueCompendiumManager.RefreshPageContentIcons(__instance, text);
    }
}

[HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.OnClose))]
internal static class TextsDialogOnCloseShieldTechniquePatch
{
    private static void Postfix(TextsDialog __instance)
    {
        ShieldTechniqueCompendiumManager.ClearPageContentIcons(__instance);
    }
}
