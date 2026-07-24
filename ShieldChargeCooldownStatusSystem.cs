using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CaptainValheim;

internal static class ShieldChargeCooldownStatusSystem
{
    private const string StatusEffectName = "CaptainValheim_Cooldown_shieldCharge";
    private const string DisplayName = "$captainvalheim_shield_charge_cooldown_name";
    private const string Tooltip = "$captainvalheim_shield_charge_cooldown_tooltip";
    private const string FallbackIconPrefabName = "ShieldWood";
    private static readonly ConditionalWeakTable<StatusEffect, object> OwnedStatusEffects = new();

    internal static void RegisterStatusEffect(ObjectDB objectDb)
    {
        if (objectDb == null ||
            objectDb.m_StatusEffects.Exists(statusEffect => statusEffect != null && ((Object)statusEffect).name == StatusEffectName))
        {
            return;
        }

        StatusEffect statusEffect = ScriptableObject.CreateInstance<StatusEffect>();
        statusEffect.name = StatusEffectName;
        statusEffect.m_name = DisplayName;
        statusEffect.m_tooltip = Tooltip;
        statusEffect.m_icon = ResolveIcon(objectDb, FallbackIconPrefabName);
        objectDb.m_StatusEffects.Add(statusEffect);
        OwnedStatusEffects.Add(statusEffect, new object());
    }

    internal static void UnregisterStatusEffect(ObjectDB objectDb)
    {
        if (objectDb == null)
        {
            return;
        }

        for (int index = objectDb.m_StatusEffects.Count - 1; index >= 0; index--)
        {
            StatusEffect statusEffect = objectDb.m_StatusEffects[index];
            if (ReferenceEquals(statusEffect, null) ||
                !OwnedStatusEffects.TryGetValue(statusEffect, out _))
            {
                continue;
            }

            objectDb.m_StatusEffects.RemoveAt(index);
            OwnedStatusEffects.Remove(statusEffect);
            Object.Destroy(statusEffect);
        }
    }

    internal static void Apply(Character character, ItemDrop.ItemData? shield, float cooldown)
    {
        if (character == null || cooldown <= 0f)
        {
            return;
        }

        SEMan? seMan = character.GetSEMan();
        if (seMan == null)
        {
            return;
        }

        int statusHash = StatusEffectName.GetStableHashCode();
        seMan.AddStatusEffect(statusHash, resetTime: true, itemLevel: 0, skillLevel: 0f);
        if (seMan.GetStatusEffect(statusHash) is StatusEffect statusEffect)
        {
            statusEffect.m_ttl = cooldown;
            statusEffect.m_icon = ResolveShieldIcon(shield) ?? statusEffect.m_icon;
        }
    }

    private static Sprite? ResolveShieldIcon(ItemDrop.ItemData? shield)
    {
        return shield?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private static Sprite? ResolveIcon(ObjectDB objectDb, string itemPrefabName)
    {
        ItemDrop? itemDrop = objectDb.GetItemPrefab(itemPrefabName)?.GetComponent<ItemDrop>();
        return itemDrop?.m_itemData?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }
}
