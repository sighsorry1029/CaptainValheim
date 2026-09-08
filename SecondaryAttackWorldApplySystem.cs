using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CaptainValheim;

internal static class SecondaryAttackWorldApplySystem
{
    public static SecondaryAttackAppliedWorldSnapshot Apply(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        if (objectDb == null)
        {
            return SecondaryAttackAppliedWorldSnapshot.Empty;
        }

        SecondaryAttackObjectDbStateStore.Restore(objectDb);
        ShieldRuntimeSystem.ResetTransientState();
        Dictionary<string, SecondaryAttackDefinition> appliedDefinitions = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenConfiguredPrefabs = new(StringComparer.OrdinalIgnoreCase);
        List<(
            ItemDrop.ItemData.SharedData SharedData,
            SecondaryAttackDefinition Definition,
            Attack? SecondaryAttack,
            EffectList? BlockChargeEffects)> mutationPlan = new();
        int appliedCount = 0;
        int appliedGlobalShieldFallbackCount = 0;

        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null)
            {
                continue;
            }

            ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null)
            {
                continue;
            }

            bool usesGlobalShieldFallback = false;
            if (!compiledSnapshot.Shields.TryGetValue(
                    itemPrefab.name,
                    out NormalizedShieldModeConfig? shieldConfig))
            {
                if (!ShouldApplyGlobalShieldFallback(itemDrop) ||
                    compiledSnapshot.GlobalShieldFallback == null)
                {
                    continue;
                }

                shieldConfig = compiledSnapshot.GlobalShieldFallback;
                usesGlobalShieldFallback = true;
            }
            else
            {
                seenConfiguredPrefabs.Add(itemPrefab.name);
            }

            if (!SecondaryAttackDefinitionCompiler.TryCreateDefinition(
                    itemPrefab.name,
                    itemDrop,
                    shieldConfig,
                    emitMissingWarnings,
                    out SecondaryAttackDefinition? definition))
            {
                continue;
            }

            SecondaryAttackDefinition resolvedDefinition = definition!;
            appliedDefinitions[itemPrefab.name] = resolvedDefinition;
            Attack? configuredSecondaryAttack = null;
            if (resolvedDefinition.AppliesSecondaryOverride)
            {
                Attack sourceAttack = SecondaryAttackManager.ResolveSourceAttack(itemDrop);
                configuredSecondaryAttack = SecondaryAttackManager.BuildSecondaryAttack(sourceAttack);
            }

            EffectList? blockChargeEffects = null;
            ItemDrop.ItemData.SharedData sharedData = itemDrop.m_itemData.m_shared;
            if (resolvedDefinition.ShieldBlockCharge && !HasEffect(sharedData.m_blockChargeEffects))
            {
                int maxBlockCharges = resolvedDefinition.ShieldBlockChargeCount.HasValue
                    ? Mathf.Max(1, resolvedDefinition.ShieldBlockChargeCount.Value)
                    : sharedData.m_maxBlockCharges;
                if (TryBuildBlockChargeEffects(objectDb, maxBlockCharges, out EffectList plannedEffects))
                {
                    blockChargeEffects = plannedEffects;
                }
            }

            mutationPlan.Add((
                sharedData,
                resolvedDefinition,
                configuredSecondaryAttack,
                blockChargeEffects));
            appliedCount++;
            if (usesGlobalShieldFallback)
            {
                appliedGlobalShieldFallbackCount++;
            }
        }

        SecondaryAttackAppliedWorldSnapshot appliedWorldSnapshot = new(appliedDefinitions);

        foreach (string configuredPrefabName in compiledSnapshot.Shields.Keys.Where(key => !seenConfiguredPrefabs.Contains(key)))
        {
            if (!emitMissingWarnings)
            {
                continue;
            }

            string warningKey = $"missing_objectdb_prefab:{configuredPrefabName}";
            if (SecondaryAttackManager.TryMarkCompatibilityWarningReported(warningKey))
            {
                CaptainValheimPlugin.ModLogger.LogWarning($"Configured prefab '{configuredPrefabName}' was not found in ObjectDB.");
            }
        }

        try
        {
            foreach ((
                         ItemDrop.ItemData.SharedData sharedData,
                         SecondaryAttackDefinition definition,
                         Attack? secondaryAttack,
                         EffectList? blockChargeEffects) in mutationPlan)
            {
                ApplyShieldBlockCharge(
                    sharedData,
                    definition,
                    blockChargeEffects);
                if (secondaryAttack != null)
                {
                    SecondaryAttackObjectDbStateStore.SetSecondaryAttack(
                        sharedData,
                        secondaryAttack);
                }
            }

            if (mutationPlan.Any(plan =>
                    plan.Definition.ShieldSpecial is
                    {
                        HasShieldCharge: true,
                        ShieldChargeDistance: > 0f
                    }))
            {
                ShieldChargeCooldownStatusSystem.RegisterStatusEffect(objectDb);
            }
            else
            {
                ShieldChargeCooldownStatusSystem.UnregisterStatusEffect(objectDb);
            }
        }
        catch
        {
            SecondaryAttackObjectDbStateStore.Restore(objectDb);
            ShieldRuntimeSystem.ResetTransientState();
            throw;
        }

        CaptainValheimPlugin.ModLogger.LogInfo($"Applied {appliedCount} shield definition(s), including {appliedGlobalShieldFallbackCount} global shield fallback definition(s).");
        return appliedWorldSnapshot;
    }

    private static bool ShouldApplyGlobalShieldFallback(ItemDrop itemDrop)
    {
        return itemDrop.m_itemData?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield;
    }

    private static void ApplyShieldBlockCharge(
        ItemDrop.ItemData.SharedData sharedData,
        SecondaryAttackDefinition definition,
        EffectList? plannedBlockChargeEffects)
    {
        if (!definition.ShieldBlockCharge)
        {
            return;
        }

        if (sharedData.m_itemType != ItemDrop.ItemData.ItemType.Shield)
        {
            return;
        }

        SecondaryAttackObjectDbStateStore.SetBuildBlockCharges(sharedData, true);
        if (definition.ShieldBlockChargeCount.HasValue)
        {
            SecondaryAttackObjectDbStateStore.SetMaxBlockCharges(
                sharedData,
                Mathf.Max(1, definition.ShieldBlockChargeCount.Value));
        }

        if (definition.ShieldBlockChargeDecayTime.HasValue)
        {
            SecondaryAttackObjectDbStateStore.SetBlockChargeDecayTime(
                sharedData,
                Mathf.Max(0f, definition.ShieldBlockChargeDecayTime.Value));
        }

        if (definition.ShieldBlockChargeBlockingDecayFactor.HasValue)
        {
            SecondaryAttackObjectDbStateStore.SetBlockChargeBlockingDecayFactor(
                sharedData,
                Mathf.Max(0f, definition.ShieldBlockChargeBlockingDecayFactor.Value));
        }

        if (!HasEffect(sharedData.m_blockChargeEffects) &&
            plannedBlockChargeEffects != null)
        {
            SecondaryAttackObjectDbStateStore.SetBlockChargeEffects(
                sharedData,
                plannedBlockChargeEffects);
        }

        if (sharedData.m_damages.GetTotalDamage() <= 0f)
        {
            SecondaryAttackObjectDbStateStore.SetGenericDamage(sharedData, 5f);
        }
    }

    private static bool TryBuildBlockChargeEffects(ObjectDB objectDb, int maxBlockCharges, out EffectList blockChargeEffects)
    {
        blockChargeEffects = new EffectList();
        int maxVariant = Mathf.Clamp(maxBlockCharges, 1, 5);

        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            ItemDrop? itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            EffectList? source = itemDrop?.m_itemData?.m_shared?.m_blockChargeEffects;
            if (!HasEffect(source))
            {
                continue;
            }

            EffectList cloned = CloneEffectList(source, maxVariant);
            if (HasEffect(cloned))
            {
                blockChargeEffects = cloned;
                return true;
            }
        }

        ZNetScene? scene = ZNetScene.instance;
        if (scene == null)
        {
            return false;
        }

        List<EffectList.EffectData> effects = new();
        for (int i = 1; i <= maxVariant; i++)
        {
            GameObject? effectPrefab = scene.GetPrefab($"fx_ShieldCharge_{i}");
            if (effectPrefab == null)
            {
                continue;
            }

            effects.Add(new EffectList.EffectData
            {
                m_prefab = effectPrefab,
                m_enabled = true,
                m_variant = i
            });
        }

        if (effects.Count == 0)
        {
            return false;
        }

        blockChargeEffects = new EffectList { m_effectPrefabs = effects.ToArray() };
        return true;
    }

    private static EffectList CloneEffectList(EffectList? source, int maxVariant)
    {
        EffectList.EffectData[] sourceEffects = source?.m_effectPrefabs ?? [];
        List<EffectList.EffectData> clonedEffects = new(sourceEffects.Length);
        foreach (EffectList.EffectData sourceEffect in sourceEffects)
        {
            if (sourceEffect.m_variant > maxVariant)
            {
                continue;
            }

            clonedEffects.Add(new EffectList.EffectData
            {
                m_prefab = sourceEffect.m_prefab,
                m_enabled = sourceEffect.m_enabled,
                m_variant = sourceEffect.m_variant,
                m_attach = sourceEffect.m_attach,
                m_follow = sourceEffect.m_follow,
                m_inheritParentRotation = sourceEffect.m_inheritParentRotation,
                m_inheritParentScale = sourceEffect.m_inheritParentScale,
                m_multiplyParentVisualScale = sourceEffect.m_multiplyParentVisualScale,
                m_randomRotation = sourceEffect.m_randomRotation,
                m_scale = sourceEffect.m_scale,
                m_childTransform = sourceEffect.m_childTransform
            });
        }

        return new EffectList { m_effectPrefabs = clonedEffects.ToArray() };
    }

    private static bool HasEffect(EffectList? effectList)
    {
        return effectList != null && effectList.HasEffects();
    }

}
