namespace CaptainValheim;

internal static class SecondaryAttackRuntimeFacade
{
    internal static bool TryGetDefinition(ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        definition = null!;
        if (weapon?.m_dropPrefab == null)
        {
            return false;
        }

        return SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.TryGetValue(weapon.m_dropPrefab.name, out definition!);
    }

    internal static bool TryGetDefinition(string weaponPrefabName, out SecondaryAttackDefinition definition)
    {
        return SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.TryGetValue(weaponPrefabName, out definition!);
    }

    internal static bool TryGetCurrentWeaponDefinition(out SecondaryAttackDefinition definition, out bool secondaryAttack)
    {
        definition = null!;
        secondaryAttack = false;
        Player? localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return false;
        }

        Attack? currentAttack = ((Humanoid)localPlayer).m_currentAttack;
        if (currentAttack?.m_weapon?.m_dropPrefab == null)
        {
            return false;
        }

        secondaryAttack = ((Humanoid)localPlayer).m_currentAttackIsSecondary;
        return SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.TryGetValue(currentAttack.m_weapon.m_dropPrefab.name, out definition!);
    }

    internal static bool CanStartConfiguredSecondary(Humanoid humanoid, ItemDrop.ItemData weapon)
    {
        return true;
    }

    internal static bool BeginProjectileHitContext(Projectile projectile, UnityEngine.Collider collider, UnityEngine.Vector3 hitPoint, bool water, UnityEngine.Vector3 normal)
    {
        if (projectile == null || collider == null)
        {
            return false;
        }

        SecondaryAttackRuntimeContext.TryGetProjectileAttackAttribution(projectile, out ProjectileAttackAttribution? attribution);
        SecondaryAttackRuntimeContext.PushProjectileHitContext(new ProjectileHitContext(projectile, collider, hitPoint, water, normal, attribution));
        return true;
    }

    internal static void EndProjectileHitContext(bool active)
    {
        if (active)
        {
            SecondaryAttackRuntimeContext.PopProjectileHitContext();
        }
    }

    internal static void RegisterActiveAttack(Attack attack, ItemDrop.ItemData weapon, ShieldSpecialMode shieldMode = ShieldSpecialMode.Throw)
    {
        if (!TryGetDefinition(weapon, out SecondaryAttackDefinition definition) ||
            definition.BehaviorType != SecondaryAttackBehaviorType.ShieldSpecial)
        {
            return;
        }

        ActiveSecondaryAttack activeAttack = new(definition, shieldMode);
        SecondaryAttackRuntimeContext.SetActiveAttack(attack, activeAttack);
        SecondaryAttackAdrenalineSystem.Reset(attack);
        if (shieldMode == ShieldSpecialMode.Charge)
        {
            ShieldRuntimeSystem.TriggerShieldSpecialFromRuntimeFacade(attack, activeAttack);
        }
    }

    internal static bool TryHandleCustomAttackTrigger(Attack attack)
    {
        if (!SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
            activeAttack == null ||
            activeAttack.Definition.BehaviorType != SecondaryAttackBehaviorType.ShieldSpecial)
        {
            return false;
        }

        if (attack.m_character.IsStaggering())
        {
            return true;
        }

        if (activeAttack.ShieldMode == ShieldSpecialMode.PrimaryAttack)
        {
            ShieldRuntimeSystem.BeginShieldPrimaryVanillaTrigger(attack, activeAttack);
            return false;
        }

        if (!activeAttack.Triggered)
        {
            ShieldRuntimeSystem.TriggerShieldSpecialFromRuntimeFacade(attack, activeAttack);
        }

        return true;
    }

    internal static void TryUpdateSecondaryProjectileHoldRepeat(Player player, bool secondaryAttackHold)
    {
    }
}
