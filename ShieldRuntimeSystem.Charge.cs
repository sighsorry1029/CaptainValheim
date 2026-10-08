using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CaptainValheim;

internal static partial class ShieldRuntimeSystem
{
    private const string ShieldChargeBullseyeEffectPrefabName = "vfx_archerytarget_bullseye";
    private const string ShieldChargeStartVfxPrefabName = "vfx_blocked";
    private const float ShieldChargeHitRadiusReferenceForce = 20f;
    private const float ShieldChargeHitPointForwardOffsetFactor = 0.5f;
    private const float ShieldChargeStartVfxForwardOffset = 0f;
    private const float ShieldChargeStartVfxYOffset = 0.5f;

    private static readonly ConditionalWeakTable<Character, ShieldChargeRuntimeState> ShieldChargeRuntimeStates = new();
    private static readonly Collider[] ShieldChargeImpactHits = new Collider[128];
    private static readonly Collider[] ShieldChargeScanHits = new Collider[128];
    private static readonly HashSet<IDestructible> ShieldChargeImpactedTargets = new();
    private static readonly HashSet<Character> ShieldChargeScanCandidates = new();
    private static readonly List<ShieldImpactTarget> ShieldChargeImpactTargets = [];
    private static readonly List<Collider> ShieldChargeTargetColliders = [];

    internal static bool CanStartShieldCharge(Humanoid humanoid)
    {
        if (humanoid == null || IsShieldChargeActive(humanoid) || humanoid.IsDead() ||
            humanoid.InAttack() || humanoid.InDodge() || !humanoid.CanMove() ||
            humanoid.IsKnockedBack() || humanoid.IsStaggering() || humanoid.InMinorAction())
        {
            return false;
        }

        if (!ShieldChargeRuntimeStates.TryGetValue(humanoid, out ShieldChargeRuntimeState? state))
        {
            return true;
        }

        return Time.time >= state.CooldownUntil;
    }

    internal static bool IsShieldChargeActive(Humanoid humanoid)
    {
        return humanoid != null &&
               ShieldChargeRuntimeStates.TryGetValue(humanoid, out ShieldChargeRuntimeState? state) &&
               state.Active;
    }

    internal static bool TryStartShieldChargeDirect(Humanoid humanoid, ItemDrop.ItemData shieldWeapon, SecondaryAttackDefinition definition)
    {
        if (humanoid == null ||
            shieldWeapon == null ||
            !CanStartShieldCharge(humanoid) ||
            (shieldWeapon.m_shared.m_useDurability && shieldWeapon.m_durability <= 0f) ||
            definition?.ShieldSpecial is not { } behavior ||
            !behavior.HasShieldCharge ||
            behavior.ShieldChargeDistance <= 0f)
        {
            return false;
        }

        if (!TryCreateDirectShieldChargeAttack(humanoid, shieldWeapon, out Attack? attack))
        {
            return false;
        }

        if (TryCalculateShieldSpecialRawStaminaCost(shieldWeapon, definition, ShieldSpecialMode.Charge, out float rawAttackStamina))
        {
            attack.m_attackStamina = rawAttackStamina;
        }

        float staminaCost = attack.GetAttackStamina();
        if (staminaCost > 0f && !humanoid.HaveStamina(staminaCost))
        {
            return false;
        }

        SecondaryAttackRuntimeContext.SetActiveAttack(attack, new ActiveSecondaryAttack(definition, ShieldSpecialMode.Charge));
        SecondaryAttackRuntimeContext.ResetAdrenaline(attack);
        SecondaryAttackManager.PlayTriggeredAttackEffects(attack, behavior.ShieldChargeDurabilityFactor);
        StartShieldCharge(attack, definition);
        return true;
    }

    private static bool TryCreateDirectShieldChargeAttack(Humanoid humanoid, ItemDrop.ItemData shieldWeapon, out Attack attack)
    {
        attack = null!;
        if (humanoid == null || shieldWeapon == null)
        {
            return false;
        }

        Attack sourceAttack = shieldWeapon.m_shared?.m_secondaryAttack ?? new Attack();
        ItemDrop? prefabItemDrop = shieldWeapon.m_dropPrefab != null ? shieldWeapon.m_dropPrefab.GetComponent<ItemDrop>() : null;
        if (ObjectDB.instance != null && prefabItemDrop != null)
        {
            sourceAttack = SecondaryAttackManager.ResolveSourceAttack(prefabItemDrop);
        }

        attack = SecondaryAttackManager.BuildSecondaryAttack(sourceAttack);
        GameAccess.SetChargeContext(attack, humanoid, shieldWeapon);

        return true;
    }

    private static void StartShieldCharge(Attack attack, SecondaryAttackDefinition definition)
    {
        ShieldSpecialSecondaryBehavior? behavior = definition.ShieldSpecial;
        if (behavior == null ||
            !behavior.HasShieldCharge ||
            behavior.ShieldChargeDistance <= 0f)
        {
            return;
        }

        float deflectionForce = attack.GetWeapon().GetDeflectionForce();
        float distance = Mathf.Max(0f, behavior.ShieldChargeDistance);
        float damage = Mathf.Max(0f, GetShieldBlockPower(attack) * behavior.ShieldChargeDamageFactor);
        float pushForce = CalculateShieldAttackPushForce(deflectionForce, behavior.ShieldChargePushFactor);
        float hitRadius = CalculateShieldChargeHitRadius(deflectionForce, behavior.ShieldChargeHitRadiusFactor);
        float cooldown = CalculateShieldChargeCooldown(attack.GetCharacter(), behavior);
        float staminaCost = attack.GetAttackStamina();
        if (staminaCost > 0f)
        {
            if (!attack.GetCharacter().HaveStamina(staminaCost))
            {
                attack.Stop();
                return;
            }

            attack.GetCharacter().UseStamina(staminaCost);
            attack.m_attackStamina = 0f;
        }

        PlayShieldThrowChargeStartSfx(attack);
        PlayShieldChargeStartVfx(attack);
        GameObject controllerObject = new("CaptainValheim_ShieldCharge");
        ShieldChargeController controller = controllerObject.AddComponent<ShieldChargeController>();
        controller.Initialize(
            attack,
            distance,
            damage,
            pushForce,
            hitRadius,
            behavior.ShieldChargeSpeed,
            cooldown,
            1f,
            0f);
    }

    private static float CalculateShieldChargeHitRadius(float deflectionForce, float hitRadiusFactor)
    {
        return Mathf.Sqrt(Mathf.Max(0f, deflectionForce) / ShieldChargeHitRadiusReferenceForce) * Mathf.Max(0f, hitRadiusFactor);
    }

    private static float CalculateShieldChargeCooldown(Character character, ShieldSpecialSecondaryBehavior behavior)
    {
        float baseCooldown = Mathf.Max(0f, behavior.ShieldChargeCooldown);
        if (baseCooldown <= 0f)
        {
            return 0f;
        }

        float blockingLevel = character != null ? Mathf.Clamp(character.GetSkillLevel(Skills.SkillType.Blocking), 0f, 100f) : 0f;
        float reduction = Mathf.Clamp01(blockingLevel / 100f) * Mathf.Clamp01(behavior.ShieldChargeCooldownReductionFactor);
        return Mathf.Max(0f, baseCooldown * (1f - reduction));
    }

    private static bool TryApplyShieldHit(
        Attack attack,
        Character target,
        Vector3 direction,
        Vector3 hitPoint,
        float damage,
        float pushForce,
        float hitRadius,
        HashSet<Character> hitTargets,
        ref bool skillRaised)
    {
        if (target == null || target.IsDead() || hitTargets.Contains(target) || !CanShieldAttackHitCharacter(attack, target))
        {
            return false;
        }

        hitTargets.Add(target);
        HitData hitData = CreateShieldHitData(attack, direction, hitPoint, damage, pushForce);
        hitData.m_hitCollider = FindBestHitCollider(target, hitPoint, hitRadius);
        using (ShieldWarfareHitContext.Begin(attack))
        {
            target.Damage(hitData);
        }
        if (BaseAI.IsEnemy(attack.GetCharacter(), target))
        {
            float adrenalineFactor = SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) && activeAttack != null
                ? SecondaryAttackRuntimeContext.ResolveAdrenalineFactor(activeAttack)
                : 1f;
            SecondaryAttackRuntimeContext.TryGrantAdrenalineOnce(
                attack,
                target,
                adrenalineFactor,
                "shield");
        }

        if (!skillRaised)
        {
            attack.GetCharacter().RaiseSkill(attack.GetWeapon().m_shared.m_skillType, attack.m_raiseSkillAmount);
            skillRaised = true;
        }

        return true;
    }

    private static bool TryApplyShieldChargeImpact(
        Attack attack,
        Vector3 impactPoint,
        Vector3 direction,
        float damage,
        float pushForce,
        float impactRadius,
        HashSet<Character> hitTargets,
        ref bool skillRaised,
        bool applyLowerDamagePerHit = false)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            impactPoint,
            impactRadius,
            ShieldChargeImpactHits,
            SecondaryAttackManager.GetShieldChargeImpactMask(),
            QueryTriggerInteraction.Ignore);

        try
        {
            for (int index = 0; index < hitCount; index++)
            {
                Collider collider = ShieldChargeImpactHits[index];
                ShieldChargeImpactHits[index] = null!;
                if (collider == null)
                {
                    continue;
                }

                IDestructible? destructible = ResolveShieldImpactTarget(collider);
                if (destructible == null || !ShieldChargeImpactedTargets.Add(destructible))
                {
                    continue;
                }

                if (destructible is not MonoBehaviour)
                {
                    continue;
                }

                ShieldChargeImpactTargets.Add(new ShieldImpactTarget(destructible, collider));
            }

            bool hitAny = false;
            int validTargetCount = ShieldChargeImpactTargets.Count;
            float damageScale = 1f;
            if (applyLowerDamagePerHit && validTargetCount > 1)
            {
                damageScale = 1f / (validTargetCount * 0.75f);
            }

            foreach (ShieldImpactTarget target in ShieldChargeImpactTargets)
            {
                float scaledDamage = damage * damageScale;
                float scaledPushForce = pushForce * damageScale;
                if (target.Destructible is Character candidate)
                {
                    if (TryApplyShieldHit(attack, candidate, direction, impactPoint, scaledDamage, scaledPushForce, impactRadius, hitTargets, ref skillRaised))
                    {
                        hitAny = true;
                    }

                    continue;
                }

                HitData hitData = CreateShieldHitData(attack, direction, impactPoint, scaledDamage, scaledPushForce);
                hitData.m_hitCollider = target.Collider;
                using (ShieldWarfareHitContext.Begin(attack))
                {
                    target.Destructible.Damage(hitData);
                }
                hitAny = true;
            }

            return hitAny;
        }
        finally
        {
            ShieldChargeImpactedTargets.Clear();
            ShieldChargeImpactTargets.Clear();
        }
    }

    private static Collider? FindBestHitCollider(Character target, Vector3 point, float radius)
    {
        ShieldChargeTargetColliders.Clear();
        target.GetComponentsInChildren(includeInactive: false, ShieldChargeTargetColliders);
        Collider? bestCollider = null;
        float bestDistance = float.MaxValue;
        float radiusSquared = radius * radius;
        foreach (Collider collider in ShieldChargeTargetColliders)
        {
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            Vector3 closestPoint = SecondaryAttackManager.ResolveSafeClosestPoint(collider, point);
            float distanceSquared = (closestPoint - point).sqrMagnitude;
            if (distanceSquared > radiusSquared || distanceSquared >= bestDistance)
            {
                continue;
            }

            bestDistance = distanceSquared;
            bestCollider = collider;
        }

        ShieldChargeTargetColliders.Clear();
        return bestCollider;
    }

    private static IDestructible? ResolveShieldImpactTarget(Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        GameObject hitObject = Projectile.FindHitObject(collider);
        return hitObject != null ? hitObject.GetComponent<IDestructible>() : null;
    }

    private static bool TryFindShieldChargeImpact(
        Attack attack,
        Vector3 start,
        Vector3 end,
        float hitRadius,
        HashSet<Character> hitTargets,
        out Character? impactTarget,
        out float impactProgress,
        out Vector3 impactPoint)
    {
        impactTarget = null;
        impactProgress = 0f;
        impactPoint = end;
        float closestProgress = float.MaxValue;
        float scanRadius = (end - start).magnitude * 0.5f + hitRadius;
        if (scanRadius <= 0f)
        {
            return false;
        }

        Vector3 scanCenter = (start + end) * 0.5f;
        int hitCount = Physics.OverlapSphereNonAlloc(
            scanCenter,
            scanRadius,
            ShieldChargeScanHits,
            SecondaryAttackManager.GetShieldChargeImpactMask(),
            QueryTriggerInteraction.Ignore);

        if (hitCount >= ShieldChargeScanHits.Length)
        {
            ClearShieldChargeScanHits(hitCount);
            return TryFindShieldChargeImpactByAllCharacters(
                attack,
                start,
                end,
                hitRadius,
                hitTargets,
                ref impactTarget,
                ref impactProgress,
                ref impactPoint,
                ref closestProgress);
        }

        try
        {
            for (int index = 0; index < hitCount; index++)
            {
                Collider collider = ShieldChargeScanHits[index];
                ShieldChargeScanHits[index] = null!;
                Character? candidate = collider != null ? ProjectileAccess.GetHitCharacter(collider) : null;
                if (candidate == null || !ShieldChargeScanCandidates.Add(candidate))
                {
                    continue;
                }

                TryConsiderShieldChargeImpactCandidate(
                    attack,
                    candidate,
                    start,
                    end,
                    hitRadius,
                    hitTargets,
                    ref impactTarget,
                    ref impactProgress,
                    ref impactPoint,
                    ref closestProgress);
            }

            return impactTarget != null;
        }
        finally
        {
            ShieldChargeScanCandidates.Clear();
        }
    }

    private static bool TryFindShieldChargeImpactByAllCharacters(
        Attack attack,
        Vector3 start,
        Vector3 end,
        float hitRadius,
        HashSet<Character> hitTargets,
        ref Character? impactTarget,
        ref float impactProgress,
        ref Vector3 impactPoint,
        ref float closestProgress)
    {
        foreach (Character candidate in Character.GetAllCharacters())
        {
            TryConsiderShieldChargeImpactCandidate(
                attack,
                candidate,
                start,
                end,
                hitRadius,
                hitTargets,
                ref impactTarget,
                ref impactProgress,
                ref impactPoint,
                ref closestProgress);
        }

        return impactTarget != null;
    }

    private static void TryConsiderShieldChargeImpactCandidate(
        Attack attack,
        Character? candidate,
        Vector3 start,
        Vector3 end,
        float hitRadius,
        HashSet<Character> hitTargets,
        ref Character? impactTarget,
        ref float impactProgress,
        ref Vector3 impactPoint,
        ref float closestProgress)
    {
        Character owner = attack.GetCharacter();
        if (candidate == null || candidate == owner || candidate.IsDead() || hitTargets.Contains(candidate))
        {
            return;
        }

        if (!CanShieldAttackHitCharacter(attack, candidate))
        {
            return;
        }

        Vector3 targetPoint = candidate.GetCenterPoint();
        float progress = SecondaryAttackManager.ClosestSegmentProgress(start, end, targetPoint);
        Vector3 closestPoint = Vector3.Lerp(start, end, progress);
        if ((targetPoint - closestPoint).sqrMagnitude > hitRadius * hitRadius || progress >= closestProgress)
        {
            return;
        }

        closestProgress = progress;
        impactTarget = candidate;
        impactProgress = progress;
        impactPoint = closestPoint;
    }

    private static void ClearShieldChargeScanHits(int hitCount)
    {
        int count = Mathf.Min(hitCount, ShieldChargeScanHits.Length);
        for (int index = 0; index < count; index++)
        {
            ShieldChargeScanHits[index] = null!;
        }
    }

    private static void PlayShieldChargeStartVfx(Attack attack)
    {
        if (attack?.GetCharacter() == null)
        {
            return;
        }

        GameObject? vfxPrefab = ZNetScene.instance?.GetPrefab(ShieldChargeStartVfxPrefabName);
        if (vfxPrefab == null)
        {
            if (SecondaryAttackManager.TryMarkCompatibilityWarningReported("shield_charge_start_vfx_missing"))
            {
                CaptainValheimPlugin.ModLogger.LogWarning($"Shield charge start VFX prefab '{ShieldChargeStartVfxPrefabName}' was not found.");
            }

            return;
        }

        Transform origin = attack.GetCharacter().transform;
        Vector3 position = origin.position + origin.forward * ShieldChargeStartVfxForwardOffset + Vector3.up * ShieldChargeStartVfxYOffset;
        GameObject vfxInstance = Object.Instantiate(vfxPrefab, position, origin.rotation);
        Object.Destroy(vfxInstance, 6f);
    }

    private static void PlayShieldChargeBullseyeEffect(Character attacker, Vector3 direction, float hitHeightOffset, float forwardOffset, float extraHeightOffset, float extraForwardOffset)
    {
        if (attacker == null)
        {
            return;
        }

        GameObject? effectPrefab = ZNetScene.instance?.GetPrefab(ShieldChargeBullseyeEffectPrefabName);
        if (effectPrefab == null)
        {
            return;
        }

        Vector3 normalizedDirection = direction.sqrMagnitude > 0.001f
            ? direction.normalized
            : attacker.transform.forward;
        Vector3 effectPosition = attacker.transform.position
                                 + Vector3.up * Mathf.Max(0f, hitHeightOffset + extraHeightOffset)
                                 + normalizedDirection * Mathf.Max(0.25f, forwardOffset + extraForwardOffset);
        GameObject effectInstance = Object.Instantiate(effectPrefab, effectPosition, Quaternion.LookRotation(normalizedDirection, Vector3.up));
        Object.Destroy(effectInstance, 6f);
    }

    private static void SetShieldChargeActive(Character character, bool active, float cooldown = 0f, ItemDrop.ItemData? shield = null)
    {
        if (character == null)
        {
            return;
        }

        ShieldChargeRuntimeState state = ShieldChargeRuntimeStates.GetValue(character, _ => new ShieldChargeRuntimeState());
        state.Active = active;
        if (!active && cooldown > 0f)
        {
            state.CooldownUntil = Mathf.Max(state.CooldownUntil, Time.time + cooldown);
            ShieldChargeCooldownStatusSystem.Apply(character, shield, cooldown);
        }
    }

    private sealed class ShieldChargeRuntimeState
    {
        public bool Active { get; set; }
        public float CooldownUntil { get; set; }
    }

    private readonly struct ShieldImpactTarget
    {
        public ShieldImpactTarget(IDestructible destructible, Collider collider)
        {
            Destructible = destructible;
            Collider = collider;
        }

        public IDestructible Destructible { get; }
        public Collider Collider { get; }
    }

    private sealed class ShieldChargeController : MonoBehaviour
    {
        private Attack _attack = null!;
        private Rigidbody _body = null!;
        private Vector3 _direction;
        private HashSet<Character> _hitTargets = null!;
        private float _remainingDistance;
        private float _speed;
        private float _damage;
        private float _pushForce;
        private float _hitRadius;
        private float _hitHeightOffset;
        private float _collisionRadius;
        private float _cooldown;
        private float _vfxForwardOffset;
        private float _vfxHeightOffset;
        private bool _skillRaised;
        private bool _stopped;

        public void Initialize(Attack attack, float travelDistance, float damage, float pushForce, float hitRadius, float configuredSpeed, float cooldown, float vfxForwardOffset, float vfxHeightOffset)
        {
            _attack = attack;
            _body = attack.GetCharacter().GetComponent<Rigidbody>();
            SetShieldChargeActive(attack.GetCharacter(), true);
            _hitTargets = new HashSet<Character>();
            _direction = SecondaryAttackManager.GetSentinelForward(attack.GetCharacter());
            _remainingDistance = travelDistance;
            _speed = configuredSpeed > 0f
                ? configuredSpeed
                : Mathf.Max(10f, travelDistance / 0.35f);
            _damage = damage;
            _pushForce = pushForce;
            _hitRadius = hitRadius;
            _hitHeightOffset = Mathf.Max(0.9f, attack.GetCharacter().GetCenterPoint().y - attack.GetCharacter().transform.position.y);
            _collisionRadius = Mathf.Max(0.2f, attack.GetCharacter().GetRadius() * 0.85f);
            _cooldown = Mathf.Max(0f, cooldown);
            _vfxForwardOffset = vfxForwardOffset;
            _vfxHeightOffset = vfxHeightOffset;
        }

        private void FixedUpdate()
        {
            if (_stopped)
            {
                return;
            }

            if (_attack == null || _attack.GetCharacter() == null || _attack.GetCharacter().IsDead() || _body == null)
            {
                StopChargeMotion();
                Destroy(gameObject);
                return;
            }

            if (!SecondaryAttackManager.HasCharacterAuthority(_attack.GetCharacter()))
            {
                StopChargeMotion();
                Destroy(gameObject);
                return;
            }

            if (_remainingDistance <= 0f)
            {
                StopChargeMotion();
                Destroy(gameObject);
                return;
            }

            float stepDistance = Mathf.Min(_remainingDistance, _speed * Time.fixedDeltaTime);
            Vector3 start = _body.position;
            bool blocked = TryResolveChargeEndPoint(start, stepDistance, out Vector3 end, out float traveledDistance, out Vector3 blockedImpactPoint);
            Vector3 hitPointOffset = _direction * (_hitRadius * ShieldChargeHitPointForwardOffsetFactor);
            Vector3 sweepStart = start + Vector3.up * _hitHeightOffset + hitPointOffset;
            Vector3 sweepEnd = end + Vector3.up * _hitHeightOffset + hitPointOffset;
            bool impactFound = TryFindShieldChargeImpact(_attack, sweepStart, sweepEnd, _hitRadius, _hitTargets, out Character? _, out float impactProgress, out Vector3 impactPoint);
            if (impactFound)
            {
                traveledDistance *= impactProgress;
                end = start + _direction * traveledDistance;
            }

            _attack.GetCharacter().transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
            Vector3 currentVelocity = _body.linearVelocity;
            _body.linearVelocity = new Vector3(0f, currentVelocity.y, 0f);
            _body.MovePosition(end);
            _remainingDistance -= traveledDistance;
            Vector3 resolvedImpactPoint = impactFound ? impactPoint : blockedImpactPoint;
            if (impactFound || blocked)
            {
                bool impactApplied = TryApplyShieldChargeImpact(
                    _attack,
                    resolvedImpactPoint,
                    _direction,
                    _damage,
                    _pushForce,
                    _hitRadius,
                    _hitTargets,
                    ref _skillRaised,
                    applyLowerDamagePerHit: true);
                if (impactApplied)
                {
                    PlayShieldChargeBullseyeEffect(_attack.GetCharacter(), _direction, _hitHeightOffset, _collisionRadius + 0.35f, _vfxHeightOffset, _vfxForwardOffset);
                    CreateShieldHitEffects(_attack, resolvedImpactPoint, Quaternion.identity);
                }
            }

            if (blocked || impactFound)
            {
                StopChargeMotion();
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_attack != null && _attack.GetCharacter() != null)
            {
                SetShieldChargeActive(_attack.GetCharacter(), false, _cooldown, _attack.GetWeapon());
            }

            StopChargeMotion();
        }

        private void StopChargeMotion()
        {
            if (_stopped || _body == null)
            {
                return;
            }

            Vector3 currentVelocity = _body.linearVelocity;
            _body.linearVelocity = new Vector3(0f, currentVelocity.y, 0f);
            _remainingDistance = 0f;
            enabled = false;
            _stopped = true;
        }

        private bool TryResolveChargeEndPoint(Vector3 start, float requestedDistance, out Vector3 end, out float traveledDistance, out Vector3 impactPoint)
        {
            Vector3 castOrigin = start + Vector3.up * _hitHeightOffset;
            float castDistance = Mathf.Max(0f, requestedDistance) + 0.05f;
            if (castDistance > 0f &&
                Physics.SphereCast(castOrigin, _collisionRadius, _direction, out RaycastHit hit, castDistance, SecondaryAttackManager.GetShieldChargeCollisionMask(), QueryTriggerInteraction.Ignore))
            {
                float safeDistance = Mathf.Max(0f, hit.distance - 0.05f);
                traveledDistance = Mathf.Min(requestedDistance, safeDistance);
                end = start + _direction * traveledDistance;
                impactPoint = hit.point;
                return true;
            }

            traveledDistance = requestedDistance;
            end = start + _direction * requestedDistance;
            impactPoint = end + Vector3.up * _hitHeightOffset;
            return false;
        }
    }
}
