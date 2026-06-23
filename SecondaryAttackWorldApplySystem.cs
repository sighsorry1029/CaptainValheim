using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CaptainValheim;

internal static class SecondaryAttackWorldApplySystem
{
    private static int _nextApplyRevision = 1;

    public static SecondaryAttackAppliedWorldSnapshot Apply(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        if (objectDb == null)
        {
            return SecondaryAttackAppliedWorldSnapshot.Empty;
        }

        SecondaryAttackWorldApplyContributors.BeforeDefinitions(objectDb, compiledSnapshot, emitMissingWarnings);
        SecondaryAttackManager.ResetWorldApplyTransientState();
        SecondaryAttackDefinitionBuildContext buildContext = new(objectDb, emitMissingWarnings);

        Dictionary<string, SecondaryAttackDefinition> appliedDefinitions = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenConfiguredPrefabs = new(StringComparer.OrdinalIgnoreCase);
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
            if (!compiledSnapshot.Weapons.TryGetValue(itemPrefab.name, out NormalizedWeaponConfig? weaponConfig))
            {
                if (!ShouldApplyGlobalShieldFallback(itemDrop))
                {
                    continue;
                }

                weaponConfig = compiledSnapshot.GlobalShieldFallback ??
                               SecondaryAttackWeaponConfigNormalizer.CreateGlobalDefaultShieldFallback();
                usesGlobalShieldFallback = true;
            }
            else
            {
                seenConfiguredPrefabs.Add(itemPrefab.name);
                if (weaponConfig.Shield == null)
                {
                    continue;
                }
            }

            if (!SecondaryAttackDefinitionCompiler.TryCreateDefinition(
                    buildContext,
                    itemPrefab.name,
                    itemDrop,
                    weaponConfig,
                    out SecondaryAttackDefinition? definition))
            {
                continue;
            }

            SecondaryAttackDefinition resolvedDefinition = definition!;
            appliedDefinitions[itemPrefab.name] = resolvedDefinition;
            ApplyShieldBlockCharge(objectDb, itemDrop, resolvedDefinition);

            if (resolvedDefinition.AppliesSecondaryOverride)
            {
                Attack sourceAttack = SecondaryAttackManager.ResolveSourceAttack(objectDb, itemDrop, resolvedDefinition);
                Attack configuredSecondaryAttack = SecondaryAttackManager.BuildSecondaryAttack(sourceAttack, resolvedDefinition);
                resolvedDefinition.ConfiguredSecondaryAttack = SecondaryAttackManager.CloneAttack(configuredSecondaryAttack);
                itemDrop.m_itemData.m_shared.m_secondaryAttack = configuredSecondaryAttack;
            }

            appliedCount++;
            if (usesGlobalShieldFallback)
            {
                appliedGlobalShieldFallbackCount++;
            }
        }

        SecondaryAttackAppliedWorldSnapshot appliedWorldSnapshot = new(compiledSnapshot, appliedDefinitions, _nextApplyRevision++);

        foreach (string configuredPrefabName in compiledSnapshot.Weapons.Keys.Where(key => !seenConfiguredPrefabs.Contains(key)))
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

        SecondaryAttackWorldApplyContributors.AfterDefinitions(objectDb, appliedWorldSnapshot, emitMissingWarnings);
        CaptainValheimPlugin.ModLogger.LogInfo($"Applied {appliedCount} shield definition(s), including {appliedGlobalShieldFallbackCount} global shield fallback definition(s).");
        return appliedWorldSnapshot;
    }

    private static bool ShouldApplyGlobalShieldFallback(ItemDrop itemDrop)
    {
        return itemDrop.m_itemData?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield;
    }

    private static void ApplyShieldBlockCharge(ObjectDB objectDb, ItemDrop itemDrop, SecondaryAttackDefinition definition)
    {
        if (!definition.ShieldBlockCharge)
        {
            return;
        }

        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        if (sharedData == null || sharedData.m_itemType != ItemDrop.ItemData.ItemType.Shield)
        {
            return;
        }

        sharedData.m_buildBlockCharges = true;
        if (definition.ShieldBlockChargeCount.HasValue)
        {
            sharedData.m_maxBlockCharges = Mathf.Max(1, definition.ShieldBlockChargeCount.Value);
        }

        if (definition.ShieldBlockChargeDecayTime.HasValue)
        {
            sharedData.m_blockChargeDecayTime = Mathf.Max(0f, definition.ShieldBlockChargeDecayTime.Value);
        }

        if (definition.ShieldBlockChargeBlockingDecayFactor.HasValue)
        {
            sharedData.m_blockChargeBlockingDecayMult = Mathf.Max(0f, definition.ShieldBlockChargeBlockingDecayFactor.Value);
        }

        if (!HasEffect(sharedData.m_blockChargeEffects) &&
            TryBuildBlockChargeEffects(objectDb, sharedData.m_maxBlockCharges, out EffectList blockChargeEffects))
        {
            sharedData.m_blockChargeEffects = blockChargeEffects;
        }

        if (sharedData.m_damages.GetTotalDamage() <= 0f)
        {
            sharedData.m_damages.m_damage = 5f;
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

            EffectList cloned = CloneEffectList(source!, maxVariant);
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

    private static bool HasEffect(EffectList? effectList)
    {
        return effectList != null && effectList.HasEffects();
    }

    private static EffectList CloneEffectList(EffectList source, int maxVariant)
    {
        EffectList.EffectData[] sourceEffects = source.m_effectPrefabs ?? [];
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
}
