using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CaptainValheim;

internal static class SecondaryAttackObjectDbStateStore
{
    private static readonly ConditionalWeakTable<ObjectDB, Dictionary<string, OriginalWeaponState>> Snapshots = new();

    public static void Capture(ObjectDB objectDb)
    {
        Dictionary<string, OriginalWeaponState> snapshots = Snapshots.GetValue(
            objectDb,
            _ => new Dictionary<string, OriginalWeaponState>(StringComparer.OrdinalIgnoreCase));

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

            ItemDrop.ItemData.SharedData sharedData = itemDrop.m_itemData.m_shared;
            if (!snapshots.ContainsKey(itemPrefab.name))
            {
                snapshots[itemPrefab.name] = new OriginalWeaponState(
                    SecondaryAttackManager.CloneAttack(sharedData.m_secondaryAttack),
                    sharedData.m_equipStatusEffect,
                    sharedData.m_buildBlockCharges,
                    sharedData.m_maxBlockCharges,
                    sharedData.m_blockChargeDecayTime,
                    sharedData.m_blockChargeBlockingDecayMult,
                    sharedData.m_blockChargeEffects,
                    sharedData.m_damages,
                    sharedData.m_damagesPerLevel);
            }
        }
    }

    public static void Restore(ObjectDB objectDb)
    {
        if (!Snapshots.TryGetValue(objectDb, out Dictionary<string, OriginalWeaponState>? snapshots))
        {
            return;
        }

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

            if (snapshots.TryGetValue(itemPrefab.name, out OriginalWeaponState snapshot))
            {
                ItemDrop.ItemData.SharedData sharedData = itemDrop.m_itemData.m_shared;
                sharedData.m_secondaryAttack = SecondaryAttackManager.CloneAttack(snapshot.OriginalSecondaryAttack);
                sharedData.m_equipStatusEffect = snapshot.OriginalEquipStatusEffect;
                sharedData.m_buildBlockCharges = snapshot.OriginalBuildBlockCharges;
                sharedData.m_maxBlockCharges = snapshot.OriginalMaxBlockCharges;
                sharedData.m_blockChargeDecayTime = snapshot.OriginalBlockChargeDecayTime;
                sharedData.m_blockChargeBlockingDecayMult = snapshot.OriginalBlockChargeBlockingDecayFactor;
                sharedData.m_blockChargeEffects = CloneEffectList(snapshot.OriginalBlockChargeEffects);
                sharedData.m_damages = snapshot.OriginalDamages;
                sharedData.m_damagesPerLevel = snapshot.OriginalDamagesPerLevel;
            }
        }
    }

    public static bool TryGetOriginalSecondaryAttack(ObjectDB objectDb, string prefabName, out Attack? attack)
    {
        attack = null;
        if (objectDb == null ||
            string.IsNullOrWhiteSpace(prefabName) ||
            !Snapshots.TryGetValue(objectDb, out Dictionary<string, OriginalWeaponState>? snapshots) ||
            !snapshots.TryGetValue(prefabName.Trim(), out OriginalWeaponState snapshot))
        {
            return false;
        }

        attack = SecondaryAttackManager.CloneAttack(snapshot.OriginalSecondaryAttack);
        return true;
    }

    private sealed class OriginalWeaponState
    {
        public OriginalWeaponState(
            Attack originalSecondaryAttack,
            StatusEffect? originalEquipStatusEffect,
            bool originalBuildBlockCharges,
            int originalMaxBlockCharges,
            float originalBlockChargeDecayTime,
            float originalBlockChargeBlockingDecayFactor,
            EffectList originalBlockChargeEffects,
            HitData.DamageTypes originalDamages,
            HitData.DamageTypes originalDamagesPerLevel)
        {
            OriginalSecondaryAttack = originalSecondaryAttack;
            OriginalEquipStatusEffect = originalEquipStatusEffect;
            OriginalBuildBlockCharges = originalBuildBlockCharges;
            OriginalMaxBlockCharges = originalMaxBlockCharges;
            OriginalBlockChargeDecayTime = originalBlockChargeDecayTime;
            OriginalBlockChargeBlockingDecayFactor = originalBlockChargeBlockingDecayFactor;
            OriginalBlockChargeEffects = CloneEffectList(originalBlockChargeEffects);
            OriginalDamages = originalDamages;
            OriginalDamagesPerLevel = originalDamagesPerLevel;
        }

        public Attack OriginalSecondaryAttack { get; }

        public StatusEffect? OriginalEquipStatusEffect { get; }

        public bool OriginalBuildBlockCharges { get; }

        public int OriginalMaxBlockCharges { get; }

        public float OriginalBlockChargeDecayTime { get; }

        public float OriginalBlockChargeBlockingDecayFactor { get; }

        public EffectList OriginalBlockChargeEffects { get; }

        public HitData.DamageTypes OriginalDamages { get; }

        public HitData.DamageTypes OriginalDamagesPerLevel { get; }
    }

    private static EffectList CloneEffectList(EffectList? source)
    {
        EffectList.EffectData[] sourceEffects = source?.m_effectPrefabs ?? [];
        EffectList.EffectData[] clonedEffects = new EffectList.EffectData[sourceEffects.Length];
        for (int i = 0; i < sourceEffects.Length; i++)
        {
            EffectList.EffectData sourceEffect = sourceEffects[i];
            clonedEffects[i] = new EffectList.EffectData
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
            };
        }

        return new EffectList { m_effectPrefabs = clonedEffects };
    }
}
