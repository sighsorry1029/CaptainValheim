using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CaptainValheim;

// Original Valheim access boundaries, checked through 1.0.16. Cache typed accessors so combat/UI updates
// do not search metadata or allocate reflection argument arrays every frame.
internal static class GameAccess
{
    private static readonly AccessTools.FieldRef<Attack, Humanoid> AttackCharacter =
        AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
    private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData> AttackWeapon =
        AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>("m_weapon");
    private static readonly AccessTools.FieldRef<Attack, BaseAI> AttackBaseAI =
        AccessTools.FieldRefAccess<Attack, BaseAI>("m_baseAI");
    internal static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack =
        AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
    internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenLeftItem =
        AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenLeftItem");
    internal static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenRightItem =
        AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem");
    internal static readonly AccessTools.FieldRef<Humanoid, int> BlockCharges =
        AccessTools.FieldRefAccess<Humanoid, int>("m_blockCharges");
    internal static readonly AccessTools.FieldRef<Humanoid, float> BlockChargeRemoveTimer =
        AccessTools.FieldRefAccess<Humanoid, float>("m_blockChargeRemoveTimer");
    internal static readonly AccessTools.FieldRef<Character, bool> SecondaryAttackPressed =
        AccessTools.FieldRefAccess<Character, bool>("m_secondaryAttack");
    internal static readonly AccessTools.FieldRef<Character, bool> SecondaryAttackHeld =
        AccessTools.FieldRefAccess<Character, bool>("m_secondaryAttackHold");
    internal static readonly AccessTools.FieldRef<Character, bool> BlockingInput =
        AccessTools.FieldRefAccess<Character, bool>("m_blocking");
    internal static readonly AccessTools.FieldRef<Player, float> QueuedSecondaryAttack =
        AccessTools.FieldRefAccess<Player, float>("m_queuedSecondAttackTimer");
    internal static readonly AccessTools.FieldRef<KeyHints, bool> KeyHintsEnabled =
        AccessTools.FieldRefAccess<KeyHints, bool>("m_keyHintsEnabled");
    internal static readonly AccessTools.FieldRef<TextsDialog, List<TextsDialog.TextInfo>> Texts =
        AccessTools.FieldRefAccess<TextsDialog, List<TextsDialog.TextInfo>>("m_texts");
    internal static readonly AccessTools.FieldRef<Projectile, bool> ChangedVisual =
        AccessTools.FieldRefAccess<Projectile, bool>("m_changedVisual");

    private delegate void ProjectileSpawnPoint(Attack attack, out Vector3 point, out Vector3 direction);
    private static readonly ProjectileSpawnPoint SpawnPoint =
        AccessTools.MethodDelegate<ProjectileSpawnPoint>(AccessTools.DeclaredMethod(typeof(Attack), "GetProjectileSpawnPoint"));
    private static readonly Func<Attack, float> AttackStamina =
        AccessTools.MethodDelegate<Func<Attack, float>>(AccessTools.DeclaredMethod(typeof(Attack), "GetAttackStamina"));
    internal static readonly Func<Character, Collider, short> FindWeakSpotIndex =
        AccessTools.MethodDelegate<Func<Character, Collider, short>>(AccessTools.DeclaredMethod(typeof(Character), "FindWeakSpotIndex"));
    internal static readonly Action<Localization, string, string> AddWord =
        AccessTools.MethodDelegate<Action<Localization, string, string>>(AccessTools.DeclaredMethod(typeof(Localization), "AddWord"));
    internal static readonly Action<Projectile> UpdateVisual =
        AccessTools.MethodDelegate<Action<Projectile>>(AccessTools.DeclaredMethod(typeof(Projectile), "UpdateVisual"));

    private static readonly Type EquipmentVisualType = typeof(ItemDrop).Assembly.GetType("IEquipmentVisual", throwOnError: true)!;
    private static readonly MethodInfo EquipmentVisualSetup = EquipmentVisualType.GetMethod("Setup")!;

    internal static Humanoid GetCharacter(this Attack attack) => AttackCharacter(attack);
    internal static BaseAI GetAttackBaseAI(this Attack attack) => AttackBaseAI(attack);
    internal static float GetAttackStamina(this Attack attack) => AttackStamina(attack);
    internal static void GetProjectileSpawnPoint(this Attack attack, out Vector3 point, out Vector3 direction) =>
        SpawnPoint(attack, out point, out direction);

    // Direct charge intentionally does not start a vanilla animation/attack lifecycle.
    internal static void SetChargeContext(Attack attack, Humanoid character, ItemDrop.ItemData weapon)
    {
        AttackCharacter(attack) = character;
        AttackWeapon(attack) = weapon;
    }

    internal static void SetupEquipmentVisual(GameObject visual, int variant)
    {
        Component component = visual.GetComponentInChildren(EquipmentVisualType);
        if (component != null)
        {
            // Called only when a replacement visual is created; preserve interface dispatch.
            EquipmentVisualSetup.Invoke(component, new object[] { variant });
        }
    }
}
