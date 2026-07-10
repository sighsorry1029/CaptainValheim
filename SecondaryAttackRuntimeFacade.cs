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

    internal static bool BeginProjectileHitContext(Projectile projectile, UnityEngine.Collider collider, UnityEngine.Vector3 hitPoint, bool water, UnityEngine.Vector3 normal)
    {
        if (projectile == null || collider == null)
        {
            return false;
        }

        SecondaryAttackRuntimeContext.PushProjectileHitContext(new ProjectileHitContext(projectile, collider, hitPoint, water, normal));
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
            definition.ShieldSpecial == null)
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

    internal static bool TryHandleCustomAttackTrigger(
        Attack attack,
        out ShieldRuntimeSystem.ShieldPrimaryTriggerState primaryTriggerState)
    {
        primaryTriggerState = default;
        if (!SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
            activeAttack == null ||
            activeAttack.Definition.ShieldSpecial == null)
        {
            return false;
        }

        if (attack.m_character.IsStaggering())
        {
            return true;
        }

        if (activeAttack.ShieldMode == ShieldSpecialMode.PrimaryAttack)
        {
            ShieldRuntimeSystem.BeginShieldPrimaryVanillaTrigger(attack, activeAttack, out primaryTriggerState);
            return false;
        }

        if (!activeAttack.Triggered)
        {
            ShieldRuntimeSystem.TriggerShieldSpecialFromRuntimeFacade(attack, activeAttack);
        }

        return true;
    }
}
