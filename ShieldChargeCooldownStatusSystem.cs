using UnityEngine;
using Object = UnityEngine.Object;

namespace CaptainValheim;

internal static class ShieldChargeCooldownStatusSystem
{
    private const string StatusEffectName = "CaptainValheim_Cooldown_shieldCharge";
    private const string DisplayName = "Shield Charge Cooldown";
    private const string Tooltip = "Shield Charge is recharging.";
    private const string FallbackIconPrefabName = "ShieldWood";

    internal static void RegisterStatusEffect(ObjectDB objectDb)
    {
        if (objectDb == null ||
            objectDb.m_StatusEffects.Exists(statusEffect => statusEffect != null && ((Object)statusEffect).name == StatusEffectName))
        {
            return;
        }

        ShieldChargeCooldownStatusEffect statusEffect = ScriptableObject.CreateInstance<ShieldChargeCooldownStatusEffect>();
        statusEffect.Initialize(StatusEffectName, DisplayName, Tooltip, ResolveIcon(objectDb, FallbackIconPrefabName));
        objectDb.m_StatusEffects.Add(statusEffect);
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

internal sealed class ShieldChargeCooldownStatusEffect : StatusEffect
{
    internal void Initialize(string prefabName, string displayName, string tooltip, Sprite? icon)
    {
        name = prefabName;
        m_name = displayName;
        m_tooltip = tooltip;
        m_icon = icon;
    }
}
