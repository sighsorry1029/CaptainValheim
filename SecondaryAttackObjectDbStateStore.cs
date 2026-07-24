using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CaptainValheim;

internal static class SecondaryAttackObjectDbStateStore
{
    private static readonly ConditionalWeakTable<
        ItemDrop.ItemData.SharedData,
        OriginalWeaponState> Snapshots = new();

    public static void Capture(ObjectDB objectDb)
    {
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

            ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
            if (sharedData == null)
            {
                continue;
            }

            if (sharedData.m_itemType != ItemDrop.ItemData.ItemType.Shield)
            {
                continue;
            }

            Snapshots.GetValue(sharedData, data => new OriginalWeaponState(data));
        }
    }

    public static void Restore(ObjectDB objectDb)
    {
        HashSet<ItemDrop.ItemData.SharedData> restoredSharedData = new();
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

            ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
            if (sharedData == null || !restoredSharedData.Add(sharedData))
            {
                continue;
            }

            if (Snapshots.TryGetValue(sharedData, out OriginalWeaponState snapshot))
            {
                snapshot.Restore(sharedData);
                snapshot.CaptureCurrent(sharedData);
            }
        }
    }

    internal static void SetSecondaryAttack(
        ItemDrop.ItemData.SharedData sharedData,
        Attack secondaryAttack)
    {
        if (ReferenceEquals(sharedData.m_secondaryAttack, secondaryAttack))
        {
            return;
        }

        OriginalWeaponState state = GetOrCaptureState(sharedData);
        sharedData.m_secondaryAttack = secondaryAttack;
        state.RecordSecondaryAttack(secondaryAttack);
    }

    internal static void SetBuildBlockCharges(
        ItemDrop.ItemData.SharedData sharedData,
        bool value)
    {
        if (sharedData.m_buildBlockCharges == value)
        {
            return;
        }

        OriginalWeaponState state = GetOrCaptureState(sharedData);
        sharedData.m_buildBlockCharges = value;
        state.RecordBuildBlockCharges(value);
    }

    internal static void SetMaxBlockCharges(
        ItemDrop.ItemData.SharedData sharedData,
        int value)
    {
        if (sharedData.m_maxBlockCharges == value)
        {
            return;
        }

        OriginalWeaponState state = GetOrCaptureState(sharedData);
        sharedData.m_maxBlockCharges = value;
        state.RecordMaxBlockCharges(value);
    }

    internal static void SetBlockChargeDecayTime(
        ItemDrop.ItemData.SharedData sharedData,
        float value)
    {
        if (sharedData.m_blockChargeDecayTime == value)
        {
            return;
        }

        OriginalWeaponState state = GetOrCaptureState(sharedData);
        sharedData.m_blockChargeDecayTime = value;
        state.RecordBlockChargeDecayTime(value);
    }

    internal static void SetBlockChargeBlockingDecayFactor(
        ItemDrop.ItemData.SharedData sharedData,
        float value)
    {
        if (sharedData.m_blockChargeBlockingDecayMult == value)
        {
            return;
        }

        OriginalWeaponState state = GetOrCaptureState(sharedData);
        sharedData.m_blockChargeBlockingDecayMult = value;
        state.RecordBlockChargeBlockingDecayFactor(value);
    }

    internal static void SetBlockChargeEffects(
        ItemDrop.ItemData.SharedData sharedData,
        EffectList effects)
    {
        if (ReferenceEquals(sharedData.m_blockChargeEffects, effects))
        {
            return;
        }

        OriginalWeaponState state = GetOrCaptureState(sharedData);
        sharedData.m_blockChargeEffects = effects;
        state.RecordBlockChargeEffects(effects);
    }

    internal static void SetGenericDamage(
        ItemDrop.ItemData.SharedData sharedData,
        float value)
    {
        if (sharedData.m_damages.m_damage == value)
        {
            return;
        }

        OriginalWeaponState state = GetOrCaptureState(sharedData);
        sharedData.m_damages.m_damage = value;
        state.RecordGenericDamage(value);
    }

    internal static EffectList CloneEffectList(EffectList? source, int maxVariant = int.MaxValue)
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

    private static OriginalWeaponState GetOrCaptureState(
        ItemDrop.ItemData.SharedData sharedData)
    {
        return Snapshots.GetValue(sharedData, data => new OriginalWeaponState(data));
    }

    private sealed class OriginalWeaponState
    {
        private Attack? _originalSecondaryAttack;
        private bool _originalBuildBlockCharges;
        private int _originalMaxBlockCharges;
        private float _originalBlockChargeDecayTime;
        private float _originalBlockChargeBlockingDecayFactor;
        private EffectList? _originalBlockChargeEffects;
        private float _originalGenericDamage;

        private bool _changedSecondaryAttack;
        private Attack? _appliedSecondaryAttack;
        private bool _changedBuildBlockCharges;
        private bool _appliedBuildBlockCharges;
        private bool _changedMaxBlockCharges;
        private int _appliedMaxBlockCharges;
        private bool _changedBlockChargeDecayTime;
        private float _appliedBlockChargeDecayTime;
        private bool _changedBlockChargeBlockingDecayFactor;
        private float _appliedBlockChargeBlockingDecayFactor;
        private bool _changedBlockChargeEffects;
        private EffectList? _appliedBlockChargeEffects;
        private bool _changedGenericDamage;
        private float _appliedGenericDamage;

        public OriginalWeaponState(ItemDrop.ItemData.SharedData sharedData)
        {
            CaptureCurrent(sharedData);
        }

        internal void CaptureCurrent(ItemDrop.ItemData.SharedData sharedData)
        {
            _originalSecondaryAttack = sharedData.m_secondaryAttack;
            _originalBuildBlockCharges = sharedData.m_buildBlockCharges;
            _originalMaxBlockCharges = sharedData.m_maxBlockCharges;
            _originalBlockChargeDecayTime = sharedData.m_blockChargeDecayTime;
            _originalBlockChargeBlockingDecayFactor = sharedData.m_blockChargeBlockingDecayMult;
            _originalBlockChargeEffects = sharedData.m_blockChargeEffects;
            _originalGenericDamage = sharedData.m_damages.m_damage;

            _changedSecondaryAttack = false;
            _appliedSecondaryAttack = null;
            _changedBuildBlockCharges = false;
            _changedMaxBlockCharges = false;
            _changedBlockChargeDecayTime = false;
            _changedBlockChargeBlockingDecayFactor = false;
            _changedBlockChargeEffects = false;
            _appliedBlockChargeEffects = null;
            _changedGenericDamage = false;
        }

        internal void RecordSecondaryAttack(Attack value)
        {
            _changedSecondaryAttack = true;
            _appliedSecondaryAttack = value;
        }

        internal void RecordBuildBlockCharges(bool value)
        {
            _changedBuildBlockCharges = true;
            _appliedBuildBlockCharges = value;
        }

        internal void RecordMaxBlockCharges(int value)
        {
            _changedMaxBlockCharges = true;
            _appliedMaxBlockCharges = value;
        }

        internal void RecordBlockChargeDecayTime(float value)
        {
            _changedBlockChargeDecayTime = true;
            _appliedBlockChargeDecayTime = value;
        }

        internal void RecordBlockChargeBlockingDecayFactor(float value)
        {
            _changedBlockChargeBlockingDecayFactor = true;
            _appliedBlockChargeBlockingDecayFactor = value;
        }

        internal void RecordBlockChargeEffects(EffectList value)
        {
            _changedBlockChargeEffects = true;
            _appliedBlockChargeEffects = value;
        }

        internal void RecordGenericDamage(float value)
        {
            _changedGenericDamage = true;
            _appliedGenericDamage = value;
        }

        internal void Restore(ItemDrop.ItemData.SharedData sharedData)
        {
            if (_changedSecondaryAttack &&
                ReferenceEquals(sharedData.m_secondaryAttack, _appliedSecondaryAttack))
            {
                sharedData.m_secondaryAttack = _originalSecondaryAttack!;
            }

            if (_changedBuildBlockCharges &&
                sharedData.m_buildBlockCharges == _appliedBuildBlockCharges)
            {
                sharedData.m_buildBlockCharges = _originalBuildBlockCharges;
            }

            if (_changedMaxBlockCharges &&
                sharedData.m_maxBlockCharges == _appliedMaxBlockCharges)
            {
                sharedData.m_maxBlockCharges = _originalMaxBlockCharges;
            }

            if (_changedBlockChargeDecayTime &&
                sharedData.m_blockChargeDecayTime == _appliedBlockChargeDecayTime)
            {
                sharedData.m_blockChargeDecayTime = _originalBlockChargeDecayTime;
            }

            if (_changedBlockChargeBlockingDecayFactor &&
                sharedData.m_blockChargeBlockingDecayMult == _appliedBlockChargeBlockingDecayFactor)
            {
                sharedData.m_blockChargeBlockingDecayMult = _originalBlockChargeBlockingDecayFactor;
            }

            if (_changedBlockChargeEffects &&
                ReferenceEquals(sharedData.m_blockChargeEffects, _appliedBlockChargeEffects))
            {
                sharedData.m_blockChargeEffects = _originalBlockChargeEffects!;
            }

            if (_changedGenericDamage &&
                sharedData.m_damages.m_damage == _appliedGenericDamage)
            {
                sharedData.m_damages.m_damage = _originalGenericDamage;
            }
        }
    }
}
