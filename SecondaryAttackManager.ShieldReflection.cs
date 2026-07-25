using System;
using System.Collections.Generic;
using UnityEngine;

namespace CaptainValheim;

internal sealed class CaptainValheimCharacterRpc : MonoBehaviour
{
    private static readonly string ShieldReflectDamageRpcName =
        $"CaptainValheim_DeliverShieldReflectDamageV{SecondaryAttackManager.ShieldReflectProtocolVersion}";

    private Character _character = null!;
    private ZNetView? _nview;
    private bool _rpcRegistered;

    private void Awake()
    {
        _character = GetComponent<Character>();
        _nview = GetComponent<ZNetView>();
        TryRegisterAndAdvertiseProtocol();
    }

    private void Start()
    {
        TryRegisterAndAdvertiseProtocol();
    }

    private void TryRegisterAndAdvertiseProtocol()
    {
        if (!_rpcRegistered)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            _nview.Register<ZPackage>(ShieldReflectDamageRpcName, RPC_ShieldReflectDamage);
            _rpcRegistered = true;
        }

        if (_character is Player player)
        {
            SecondaryAttackManager.AdvertiseShieldReflectProtocol(player, _nview);
        }
    }

    internal static void SendShieldReflectDamage(ZNetView targetNView, ZPackage package)
    {
        targetNView.InvokeRPC(ShieldReflectDamageRpcName, package);
    }

    private void RPC_ShieldReflectDamage(long sender, ZPackage package)
    {
        if (_character is Player player)
        {
            SecondaryAttackManager.ReceiveRemoteShieldReflectDamage(player, _nview, sender, package);
        }
    }
}

internal static partial class SecondaryAttackManager
{
    internal const int ShieldReflectProtocolVersion = 2;
    private const string ShieldReflectProtocolZdoKey = "CaptainValheim_ShieldReflectProtocol";
    private const int ShieldReflectDeliveredEventLimit = 512;

    private static readonly HashSet<ShieldReflectEventKey> DeliveredShieldReflectEvents = new();
    private static readonly List<ShieldReflectEventKey> DeliveredShieldReflectEventOrder = new(ShieldReflectDeliveredEventLimit);
    private static readonly List<ShieldReflectDamageScope> ActiveShieldReflectDamageScopes = new(2);
    private static readonly List<BlockAttackContext> ActiveShieldReflectBlockAttackContexts = new(2);
    private static long NextShieldReflectEventId;

    internal sealed class BlockAttackContext
    {
        public Player? Player { get; set; }

        public ItemDrop.ItemData? Blocker { get; set; }

        public SecondaryAttackDefinition? Definition { get; set; }

        public ShieldReflectProjectileContext? ProjectileContext { get; set; }

        public float StaminaBefore { get; set; }

        public HitData? Hit { get; set; }

        public float BlockedDamage { get; set; }
    }

    internal struct ShieldReflectCharacterDamageState
    {
        internal ShieldReflectDamageScope? Scope { get; set; }

        internal ShieldReflectCharacterDamageState(ShieldReflectDamageScope scope)
        {
            Scope = scope;
        }
    }

    internal struct ShieldReflectRpcDamageState
    {
        internal ShieldReflectDamageScope? Scope { get; set; }

        internal HitData? Hit { get; set; }

        internal ShieldReflectRpcDamageState(ShieldReflectDamageScope scope, HitData hit)
        {
            Scope = scope;
            Hit = hit;
        }
    }

    internal static void AdvertiseShieldReflectProtocol(Player player, ZNetView? nview)
    {
        if (player == null || nview == null || !nview.IsValid() || !nview.IsOwner() || nview.GetZDO() == null)
        {
            return;
        }

        ZDO zdo = nview.GetZDO();
        if (zdo.GetInt(ShieldReflectProtocolZdoKey, 0) != ShieldReflectProtocolVersion)
        {
            zdo.Set(ShieldReflectProtocolZdoKey, ShieldReflectProtocolVersion);
        }
    }

    internal static bool BeginShieldReflectCharacterDamage(
        Character target,
        HitData hit,
        out ShieldReflectCharacterDamageState state)
    {
        state = default;
        try
        {
            return BeginShieldReflectCharacterDamageCore(target, hit, out state);
        }
        catch
        {
            EndShieldReflectCharacterDamage(ref state);
            return false;
        }
    }

    private static bool BeginShieldReflectCharacterDamageCore(
        Character target,
        HitData hit,
        out ShieldReflectCharacterDamageState state)
    {
        state = default;
        if (target is not Player targetPlayer || hit == null || !hit.m_blockable)
        {
            return false;
        }

        if (!SecondaryAttackRuntimeContext.TryPeekProjectileHitContext(out ProjectileHitContext? activeContext) ||
            !activeContext.HasValue)
        {
            return false;
        }

        ProjectileHitContext projectileHit = activeContext.Value;
        Projectile projectile = projectileHit.Projectile;
        if (projectile == null ||
            projectileHit.Water ||
            hit.m_hitCollider == null ||
            ProjectileAccess.GetHitCharacter(hit.m_hitCollider) != targetPlayer ||
            ShieldRuntimeSystem.IsReflectedProjectile(projectile))
        {
            return false;
        }

        if (!TryGetProjectileZdo(projectile, out ZNetView? projectileNView, out ZDO? projectileZdo) ||
            !projectileNView!.IsOwner())
        {
            return false;
        }

        long projectileOwnerPeerId = projectileZdo!.GetOwner();
        if (projectileOwnerPeerId == 0L || projectileOwnerPeerId != ZDOMan.GetSessionID())
        {
            return false;
        }

        if (!TryGetCharacterZdo(targetPlayer, out ZNetView? targetNView, out ZDO? targetZdo) ||
            targetZdo!.m_uid == ZDOID.None ||
            targetZdo.GetOwner() == 0L)
        {
            return false;
        }

        ZDOID projectileId = projectileZdo.m_uid;
        if (projectileId == ZDOID.None)
        {
            return false;
        }

        Vector3 normal = projectileHit.Normal;
        if (normal.sqrMagnitude <= 0.001f)
        {
            Vector3 velocity = ProjectileAccess.GetVelocity(projectile);
            normal = velocity.sqrMagnitude > 0.001f ? -velocity.normalized : -targetPlayer.GetLookDir();
        }

        hit.m_weakSpot = targetPlayer.FindWeakSpotIndex(hit.m_hitCollider);
        ShieldReflectProjectileContext snapshot = ShieldReflectProjectileContext.FromProjectile(
            projectile,
            hit.m_point,
            water: false,
            normal);
        if (!snapshot.IsValid)
        {
            return false;
        }

        if (targetNView!.IsOwner())
        {
            ShieldReflectDamageScope scope = new(
                targetPlayer,
                targetZdo.m_uid,
                projectileOwnerPeerId,
                snapshot,
                reflectionEnabled: true);
            ActiveShieldReflectDamageScopes.Add(scope);
            state = new ShieldReflectCharacterDamageState(scope);
            return false;
        }

        if (targetZdo.GetInt(ShieldReflectProtocolZdoKey, 0) != ShieldReflectProtocolVersion)
        {
            // A client that has not advertised this exact protocol continues through
            // vanilla Character.Damage, so compatibility failure cannot lose damage.
            return false;
        }

        ShieldReflectDamageEnvelope envelope = new(
            AllocateShieldReflectEventId(),
            projectileId,
            targetZdo.m_uid,
            projectileOwnerPeerId,
            hit.Clone(),
            snapshot);

        try
        {
            CaptainValheimCharacterRpc.SendShieldReflectDamage(targetNView, envelope.Serialize());
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void ReceiveRemoteShieldReflectDamage(Player player, ZNetView? nview, long sender, ZPackage package)
    {
        if (player == null ||
            nview == null ||
            !nview.IsValid() ||
            !nview.IsOwner() ||
            nview.GetZDO() == null ||
            package == null)
        {
            return;
        }

        if (!ShieldReflectDamageEnvelope.TryDeserialize(package, out ShieldReflectDamageEnvelope envelope))
        {
            return;
        }

        ZDO actualTargetZdo = nview.GetZDO();
        ZDOID actualTargetId = actualTargetZdo.m_uid;
        if (actualTargetId == ZDOID.None ||
            actualTargetZdo.GetOwner() != ZDOMan.GetSessionID() ||
            envelope.EventId == 0L ||
            envelope.TargetId != actualTargetId ||
            envelope.ProjectileId == ZDOID.None ||
            sender == 0L ||
            sender != envelope.ProjectileOwnerPeerId)
        {
            return;
        }

        ShieldReflectEventKey eventKey = new(
            envelope.ProjectileOwnerPeerId,
            envelope.EventId,
            envelope.ProjectileId,
            envelope.TargetId);
        if (!TryMarkShieldReflectEventDelivered(eventKey))
        {
            return;
        }

        bool senderOwnsLiveProjectile = true;
        ZDO? liveProjectileZdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(envelope.ProjectileId) : null;
        if (liveProjectileZdo != null)
        {
            long liveOwner = liveProjectileZdo.GetOwner();
            senderOwnsLiveProjectile = liveOwner == 0L || liveOwner == sender;
        }

        bool reflectionEnabled = senderOwnsLiveProjectile && envelope.ProjectileContext.IsValid;
        ShieldReflectDamageScope scope = new(
            player,
            envelope.TargetId,
            envelope.ProjectileOwnerPeerId,
            envelope.ProjectileContext,
            reflectionEnabled);
        ActiveShieldReflectDamageScopes.Add(scope);

        try
        {
            // The target owns this ZNetView. ZRoutedRpc handles the self-targeted
            // RPC_Damage synchronously. Calling it directly avoids re-running the
            // Character.Damage prefix chain that already processed this HitData on
            // the projectile owner.
            nview.InvokeRPC("RPC_Damage", envelope.Hit);
        }
        finally
        {
            RemoveShieldReflectDamageScope(scope);
        }
    }

    internal static BlockAttackContext CaptureBlockAttackContext(
        Humanoid humanoid,
        HitData hit,
        ItemDrop.ItemData blocker)
    {
        BlockAttackContext context = new();
        if (humanoid is not Player player || blocker == null || hit == null)
        {
            return context;
        }

        if (!ShieldRuntimeSystem.TryGetDefinition(blocker, out SecondaryAttackDefinition definition) ||
            !definition.ShieldProjectileReflect)
        {
            return context;
        }

        context.Player = player;
        context.Blocker = blocker;
        context.Definition = definition;
        context.StaminaBefore = player.GetStamina();
        context.Hit = hit;

        if (TryPeekShieldReflectContext(
                player,
                hit,
                out ShieldReflectProjectileContext projectileContext))
        {
            context.ProjectileContext = projectileContext;
        }

        ActiveShieldReflectBlockAttackContexts.Add(context);

        return context;
    }

    internal static void RecordShieldReflectBlockDamage(
        HitData hit,
        float blockableDamageBefore)
    {
        if (hit == null || ActiveShieldReflectBlockAttackContexts.Count == 0)
        {
            return;
        }

        BlockAttackContext context =
            ActiveShieldReflectBlockAttackContexts[ActiveShieldReflectBlockAttackContexts.Count - 1];
        if (!ReferenceEquals(context.Hit, hit))
        {
            return;
        }

        float blockedDamage = blockableDamageBefore - hit.GetTotalBlockableDamage();
        if (blockedDamage > 0.001f)
        {
            context.BlockedDamage += blockedDamage;
        }
    }

    internal static void EndShieldReflectBlockAttack(ref BlockAttackContext context)
    {
        if (context != null)
        {
            int lastIndex = ActiveShieldReflectBlockAttackContexts.Count - 1;
            if (lastIndex >= 0 && ReferenceEquals(ActiveShieldReflectBlockAttackContexts[lastIndex], context))
            {
                ActiveShieldReflectBlockAttackContexts.RemoveAt(lastIndex);
            }
            else
            {
                ActiveShieldReflectBlockAttackContexts.Remove(context);
            }
        }

        context = null!;
    }

    internal static void BeginShieldReflectRpcDamage(
        Character character,
        ref long sender,
        HitData hit,
        out ShieldReflectRpcDamageState state)
    {
        state = default;
        if (character is not Player player ||
            hit == null ||
            sender != ZDOMan.GetSessionID() ||
            ActiveShieldReflectDamageScopes.Count == 0)
        {
            return;
        }

        ShieldReflectDamageScope scope = ActiveShieldReflectDamageScopes[ActiveShieldReflectDamageScopes.Count - 1];
        if (scope.RpcBound || scope.BoundHit != null || !ShieldReflectDamageScopeMatches(scope, player))
        {
            return;
        }

        scope.RpcBound = true;
        scope.BoundHit = hit;
        state = new ShieldReflectRpcDamageState(scope, hit);
        sender = scope.OriginalSenderPeerId;
    }

    internal static void EndShieldReflectRpcDamage(ref ShieldReflectRpcDamageState state)
    {
        ShieldReflectDamageScope? scope = state.Scope;
        HitData? hit = state.Hit;
        state = default;
        if (scope != null && ReferenceEquals(scope.BoundHit, hit))
        {
            scope.BoundHit = null;
        }
    }

    internal static void EndShieldReflectCharacterDamage(ref ShieldReflectCharacterDamageState state)
    {
        ShieldReflectDamageScope? scope = state.Scope;
        state = default;
        RemoveShieldReflectDamageScope(scope);
    }

    internal static void FinalizeBlockAttack(bool result, HitData hit, BlockAttackContext context)
    {
        if (!result ||
            hit == null ||
            context == null ||
            context.Player == null ||
            context.Blocker == null ||
            context.Definition == null ||
            !context.ProjectileContext.HasValue)
        {
            return;
        }

        Player player = context.Player;
        if (context.BlockedDamage <= 0.001f)
        {
            return;
        }

        float actualBlockStaminaCost = Mathf.Max(0f, context.StaminaBefore - player.GetStamina());
        FinalizeShieldReflect(
            player,
            context.Blocker,
            context.Definition,
            actualBlockStaminaCost,
            context.ProjectileContext.Value);
    }

    private static void FinalizeShieldReflect(
        Player player,
        ItemDrop.ItemData blocker,
        SecondaryAttackDefinition definition,
        float actualBlockStaminaCost,
        ShieldReflectProjectileContext projectileContext)
    {
        if (!projectileContext.IsValid ||
            projectileContext.Water ||
            !projectileContext.Blockable ||
            projectileContext.Reflected)
        {
            return;
        }

        float staminaDelta = actualBlockStaminaCost *
                             (Mathf.Max(0f, definition.ShieldProjectileReflectStaminaFactor) - 1f);
        if (staminaDelta > 0f && !player.HaveStamina(staminaDelta))
        {
            return;
        }

        if (!TryReflectShieldProjectile(player, blocker, definition, projectileContext))
        {
            return;
        }

        if (staminaDelta > 0f)
        {
            player.UseStamina(staminaDelta);
        }
        else if (staminaDelta < 0f)
        {
            player.AddStamina(-staminaDelta);
        }
    }

    private static bool TryReflectShieldProjectile(
        Player player,
        ItemDrop.ItemData blocker,
        SecondaryAttackDefinition definition,
        ShieldReflectProjectileContext projectileContext)
    {
        GameObject? reflectedObject = null;
        try
        {
            GameObject? sourcePrefab = ZNetScene.instance?.GetPrefab(projectileContext.ProjectilePrefabName);
            if (sourcePrefab == null && projectileContext.Projectile != null)
            {
                sourcePrefab = projectileContext.Projectile.gameObject;
            }

            if (sourcePrefab == null)
            {
                return false;
            }

            Vector3 normal = projectileContext.Normal.sqrMagnitude > 0.001f
                ? projectileContext.Normal.normalized
                : -player.GetLookDir();
            Vector3 spawnPoint = projectileContext.HitPoint + normal * 0.15f;
            reflectedObject = UnityEngine.Object.Instantiate(
                sourcePrefab,
                spawnPoint,
                Quaternion.identity);
            Projectile? reflectedProjectile = reflectedObject.GetComponent<Projectile>();
            IProjectile? reflectedProjectileInterface = reflectedObject.GetComponent<IProjectile>();
            if (reflectedProjectile == null || reflectedProjectileInterface == null)
            {
                DestroyProjectileObject(reflectedObject);
                return false;
            }

            Vector3 incomingVelocity = projectileContext.Velocity;
            Vector3 fallbackDirection = incomingVelocity.sqrMagnitude > 0.001f
                ? Vector3.Reflect(incomingVelocity.normalized, normal)
                : player.GetLookDir();
            Vector3 aimDirection = ShieldRuntimeSystem.ResolvePlayerAimDirection(
                player,
                spawnPoint,
                fallbackDirection,
                maxTravelDistance: 60f);
            if (aimDirection.sqrMagnitude <= 0.001f)
            {
                aimDirection = fallbackDirection.sqrMagnitude > 0.001f
                    ? fallbackDirection.normalized
                    : player.GetLookDir();
            }

            float speed = Mathf.Max(incomingVelocity.magnitude, 10f);
            HitData reflectedHit = BuildReflectedProjectileHitData(player, blocker, definition, projectileContext);
            reflectedProjectileInterface.Setup(
                player,
                aimDirection.normalized * speed,
                projectileContext.HitNoise,
                reflectedHit,
                blocker,
                projectileContext.Ammo);

            ShieldRuntimeSystem.MarkReflectedProjectile(reflectedProjectile);
            return true;
        }
        catch
        {
            if (reflectedObject != null)
            {
                try
                {
                    DestroyProjectileObject(reflectedObject);
                }
                catch
                {
                    // Keep reflection cleanup failures from escaping into RPC_Damage.
                }
            }

            return false;
        }
    }

    private static HitData BuildReflectedProjectileHitData(
        Player player,
        ItemDrop.ItemData blocker,
        SecondaryAttackDefinition definition,
        ShieldReflectProjectileContext projectileContext)
    {
        HitData hitData = new();
        hitData.m_damage = projectileContext.Damage.Clone();
        float powerMultiplier = Mathf.Max(
            0f,
            blocker.GetDeflectionForce() * definition.ShieldProjectileReflectionFactor);
        hitData.ApplyModifier(powerMultiplier);
        hitData.m_pushForce = projectileContext.AttackForce * powerMultiplier;
        hitData.m_backstabBonus = projectileContext.BackstabBonus;
        hitData.m_statusEffectHash = projectileContext.StatusEffectHash;
        hitData.m_skill = Skills.SkillType.Blocking;
        hitData.m_skillRaiseAmount = 0f;
        hitData.m_blockable = projectileContext.Blockable;
        hitData.m_dodgeable = projectileContext.Dodgeable;
        hitData.SetAttacker(player);
        return hitData;
    }

    private static bool TryGetProjectileZdo(Projectile projectile, out ZNetView? nview, out ZDO? zdo)
    {
        nview = projectile != null ? projectile.GetComponent<ZNetView>() : null;
        zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
        return nview != null && nview.IsValid() && zdo != null;
    }

    private static bool TryPeekShieldReflectContext(
        Player player,
        HitData hit,
        out ShieldReflectProjectileContext context)
    {
        if (ActiveShieldReflectDamageScopes.Count == 0)
        {
            context = default;
            return false;
        }

        ShieldReflectDamageScope scope = ActiveShieldReflectDamageScopes[ActiveShieldReflectDamageScopes.Count - 1];
        if (!scope.ReflectionEnabled ||
            !ShieldReflectDamageScopeMatches(scope, player) ||
            !ReferenceEquals(scope.BoundHit, hit))
        {
            context = default;
            return false;
        }

        context = scope.ProjectileContext;
        return context.IsValid;
    }

    private static bool ShieldReflectDamageScopeMatches(ShieldReflectDamageScope scope, Player player)
    {
        return scope.Player == player &&
               TryGetCharacterZdo(player, out _, out ZDO? playerZdo) &&
               playerZdo!.m_uid == scope.TargetId;
    }

    private static void RemoveShieldReflectDamageScope(ShieldReflectDamageScope? scope)
    {
        if (scope == null)
        {
            return;
        }

        int lastIndex = ActiveShieldReflectDamageScopes.Count - 1;
        if (lastIndex >= 0 && ReferenceEquals(ActiveShieldReflectDamageScopes[lastIndex], scope))
        {
            ActiveShieldReflectDamageScopes.RemoveAt(lastIndex);
        }
        else
        {
            ActiveShieldReflectDamageScopes.Remove(scope);
        }
    }

    private static bool TryMarkShieldReflectEventDelivered(ShieldReflectEventKey eventKey)
    {
        if (!DeliveredShieldReflectEvents.Add(eventKey))
        {
            return false;
        }

        DeliveredShieldReflectEventOrder.Add(eventKey);
        while (DeliveredShieldReflectEventOrder.Count > ShieldReflectDeliveredEventLimit)
        {
            DeliveredShieldReflectEvents.Remove(DeliveredShieldReflectEventOrder[0]);
            DeliveredShieldReflectEventOrder.RemoveAt(0);
        }

        return true;
    }

    private static long AllocateShieldReflectEventId()
    {
        unchecked
        {
            ++NextShieldReflectEventId;
            if (NextShieldReflectEventId == 0L)
            {
                ++NextShieldReflectEventId;
            }

            return NextShieldReflectEventId;
        }
    }

    internal readonly struct ShieldReflectProjectileContext
    {
        private ShieldReflectProjectileContext(
            Projectile? projectile,
            string projectilePrefabName,
            Vector3 hitPoint,
            bool water,
            Vector3 normal,
            Vector3 velocity,
            float hitNoise,
            HitData.DamageTypes damage,
            float attackForce,
            float backstabBonus,
            bool blockable,
            bool dodgeable,
            int statusEffectHash,
            ItemDrop.ItemData? ammo,
            bool reflected)
        {
            Projectile = projectile;
            ProjectilePrefabName = projectilePrefabName;
            HitPoint = hitPoint;
            Water = water;
            Normal = normal;
            Velocity = velocity;
            HitNoise = hitNoise;
            Damage = damage;
            AttackForce = attackForce;
            BackstabBonus = backstabBonus;
            Blockable = blockable;
            Dodgeable = dodgeable;
            StatusEffectHash = statusEffectHash;
            Ammo = ammo;
            Reflected = reflected;
        }

        public Projectile? Projectile { get; }

        public string ProjectilePrefabName { get; }

        public Vector3 HitPoint { get; }

        public bool Water { get; }

        public Vector3 Normal { get; }

        public Vector3 Velocity { get; }

        public float HitNoise { get; }

        public HitData.DamageTypes Damage { get; }

        public float AttackForce { get; }

        public float BackstabBonus { get; }

        public bool Blockable { get; }

        public bool Dodgeable { get; }

        public int StatusEffectHash { get; }

        public ItemDrop.ItemData? Ammo { get; }

        public bool Reflected { get; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(ProjectilePrefabName) &&
            ProjectilePrefabName.Length <= 256 &&
            IsFinite(HitPoint) &&
            IsFinite(Normal) &&
            IsFinite(Velocity) &&
            IsFinite(HitNoise) &&
            IsFinite(Damage) &&
            IsFinite(AttackForce) &&
            IsFinite(BackstabBonus);

        public static ShieldReflectProjectileContext FromProjectile(
            Projectile projectile,
            Vector3 hitPoint,
            bool water,
            Vector3 normal)
        {
            return new ShieldReflectProjectileContext(
                projectile,
                ResolveProjectilePrefabName(projectile),
                hitPoint,
                water,
                normal,
                ProjectileAccess.GetVelocity(projectile),
                projectile.m_hitNoise,
                projectile.m_damage.Clone(),
                projectile.m_attackForce,
                projectile.m_backstabBonus,
                projectile.m_blockable,
                projectile.m_dodgeable,
                ProjectileAccess.GetStatusEffectHash(projectile),
                ProjectileAccess.GetAmmo(projectile),
                ShieldRuntimeSystem.IsReflectedProjectile(projectile));
        }

        internal void Serialize(ZPackage package)
        {
            package.Write(ProjectilePrefabName ?? string.Empty);
            package.Write(HitPoint);
            package.Write(Water);
            package.Write(Normal);
            package.Write(Velocity);
            package.Write(HitNoise);
            WriteDamage(package, Damage);
            package.Write(AttackForce);
            package.Write(BackstabBonus);
            package.Write(Blockable);
            package.Write(Dodgeable);
            package.Write(StatusEffectHash);
            package.Write(Reflected);
        }

        internal static ShieldReflectProjectileContext Deserialize(ZPackage package)
        {
            return new ShieldReflectProjectileContext(
                projectile: null,
                package.ReadString(),
                package.ReadVector3(),
                package.ReadBool(),
                package.ReadVector3(),
                package.ReadVector3(),
                package.ReadSingle(),
                ReadDamage(package),
                package.ReadSingle(),
                package.ReadSingle(),
                package.ReadBool(),
                package.ReadBool(),
                package.ReadInt(),
                ammo: null,
                package.ReadBool());
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(HitData.DamageTypes damage)
        {
            return IsFinite(damage.m_damage) &&
                   IsFinite(damage.m_blunt) &&
                   IsFinite(damage.m_slash) &&
                   IsFinite(damage.m_pierce) &&
                   IsFinite(damage.m_chop) &&
                   IsFinite(damage.m_pickaxe) &&
                   IsFinite(damage.m_fire) &&
                   IsFinite(damage.m_frost) &&
                   IsFinite(damage.m_lightning) &&
                   IsFinite(damage.m_poison) &&
                   IsFinite(damage.m_spirit);
        }
    }

    private readonly struct ShieldReflectDamageEnvelope
    {
        internal ShieldReflectDamageEnvelope(
            long eventId,
            ZDOID projectileId,
            ZDOID targetId,
            long projectileOwnerPeerId,
            HitData hit,
            ShieldReflectProjectileContext projectileContext)
        {
            EventId = eventId;
            ProjectileId = projectileId;
            TargetId = targetId;
            ProjectileOwnerPeerId = projectileOwnerPeerId;
            Hit = hit;
            ProjectileContext = projectileContext;
        }

        internal long EventId { get; }

        internal ZDOID ProjectileId { get; }

        internal ZDOID TargetId { get; }

        internal long ProjectileOwnerPeerId { get; }

        internal HitData Hit { get; }

        internal ShieldReflectProjectileContext ProjectileContext { get; }

        internal ZPackage Serialize()
        {
            ZPackage package = new();
            package.Write(ShieldReflectProtocolVersion);
            package.Write(EventId);
            package.Write(ProjectileId);
            package.Write(TargetId);
            package.Write(ProjectileOwnerPeerId);
            HitData hit = Hit.Clone();
            hit.Serialize(ref package);
            ProjectileContext.Serialize(package);
            package.SetPos(0);
            return package;
        }

        internal static bool TryDeserialize(ZPackage package, out ShieldReflectDamageEnvelope envelope)
        {
            envelope = default;
            try
            {
                package.SetPos(0);
                if (package.ReadInt() != ShieldReflectProtocolVersion)
                {
                    return false;
                }

                long eventId = package.ReadLong();
                ZDOID projectileId = package.ReadZDOID();
                ZDOID targetId = package.ReadZDOID();
                long projectileOwnerPeerId = package.ReadLong();
                HitData hit = new();
                hit.Deserialize(ref package);
                ShieldReflectProjectileContext projectileContext = ShieldReflectProjectileContext.Deserialize(package);
                envelope = new ShieldReflectDamageEnvelope(
                    eventId,
                    projectileId,
                    targetId,
                    projectileOwnerPeerId,
                    hit,
                    projectileContext);
                return true;
            }
            catch
            {
                envelope = default;
                return false;
            }
        }
    }

    private readonly struct ShieldReflectEventKey : IEquatable<ShieldReflectEventKey>
    {
        internal ShieldReflectEventKey(
            long senderPeerId,
            long eventId,
            ZDOID projectileId,
            ZDOID targetId)
        {
            SenderPeerId = senderPeerId;
            EventId = eventId;
            ProjectileId = projectileId;
            TargetId = targetId;
        }

        private long SenderPeerId { get; }

        private long EventId { get; }

        private ZDOID ProjectileId { get; }

        private ZDOID TargetId { get; }

        public bool Equals(ShieldReflectEventKey other)
        {
            return SenderPeerId == other.SenderPeerId &&
                   EventId == other.EventId &&
                   ProjectileId == other.ProjectileId &&
                   TargetId == other.TargetId;
        }

        public override bool Equals(object? obj)
        {
            return obj is ShieldReflectEventKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = SenderPeerId.GetHashCode();
                hashCode = (hashCode * 397) ^ EventId.GetHashCode();
                hashCode = (hashCode * 397) ^ ProjectileId.GetHashCode();
                return (hashCode * 397) ^ TargetId.GetHashCode();
            }
        }
    }

    internal sealed class ShieldReflectDamageScope
    {
        internal ShieldReflectDamageScope(
            Player player,
            ZDOID targetId,
            long originalSenderPeerId,
            ShieldReflectProjectileContext projectileContext,
            bool reflectionEnabled)
        {
            Player = player;
            TargetId = targetId;
            OriginalSenderPeerId = originalSenderPeerId;
            ProjectileContext = projectileContext;
            ReflectionEnabled = reflectionEnabled;
        }

        internal Player Player { get; }

        internal ZDOID TargetId { get; }

        internal long OriginalSenderPeerId { get; }

        internal ShieldReflectProjectileContext ProjectileContext { get; }

        internal bool ReflectionEnabled { get; }

        internal HitData? BoundHit { get; set; }

        internal bool RpcBound { get; set; }
    }

    private static string ResolveProjectilePrefabName(Projectile projectile)
    {
        return projectile != null ? Utils.GetPrefabName(projectile.gameObject) : string.Empty;
    }

    private static void WriteDamage(ZPackage package, HitData.DamageTypes damage)
    {
        package.Write(damage.m_damage);
        package.Write(damage.m_blunt);
        package.Write(damage.m_slash);
        package.Write(damage.m_pierce);
        package.Write(damage.m_chop);
        package.Write(damage.m_pickaxe);
        package.Write(damage.m_fire);
        package.Write(damage.m_frost);
        package.Write(damage.m_lightning);
        package.Write(damage.m_poison);
        package.Write(damage.m_spirit);
    }

    private static HitData.DamageTypes ReadDamage(ZPackage package)
    {
        return new HitData.DamageTypes
        {
            m_damage = package.ReadSingle(),
            m_blunt = package.ReadSingle(),
            m_slash = package.ReadSingle(),
            m_pierce = package.ReadSingle(),
            m_chop = package.ReadSingle(),
            m_pickaxe = package.ReadSingle(),
            m_fire = package.ReadSingle(),
            m_frost = package.ReadSingle(),
            m_lightning = package.ReadSingle(),
            m_poison = package.ReadSingle(),
            m_spirit = package.ReadSingle()
        };
    }
}
