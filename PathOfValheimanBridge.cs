using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;

namespace CaptainValheim;

// Query the installed mod's own eligibility rules. No PoV item, return or stat
// state is copied into CaptainValheim, and no reflection runs in input updates.
internal static class PathOfValheimanBridge
{
    internal const string PluginGuid = "treextr.pathofvalheiman";
    private const BindingFlags StaticMethods =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private delegate bool CanThrow(Player player, out ItemDrop.ItemData shield);

    private static CanThrow? _canThrow;
    private static Func<Attack>? _throwTemplate;
    private static Func<Humanoid, ItemDrop.ItemData>? _bashShield;
    private static Func<Humanoid, Attack>? _bashTemplate;
    private static Func<bool>? _hasCharge;
    private static Func<bool>? _charging;
    private static bool _chargeQueryFailed;

    internal static void Initialize(Harmony harmony, Assembly assembly)
    {
        Dispose();
        try
        {
            Type? shieldThrow = assembly.GetType("PassiveTree.ShieldThrow");
            CanThrow canThrow = Bind<CanThrow>(shieldThrow, "CanThrow");
            Func<Attack> template = Bind<Func<Attack>>(shieldThrow, "Spear");
            MethodInfo prefix = RequireMethod(shieldThrow?.GetNestedType("Throw", BindingFlags.NonPublic),
                "Prefix", typeof(bool), [typeof(Humanoid), typeof(bool), typeof(bool).MakeByRefType()]);
            RequireInstalledPatch(AccessTools.Method(typeof(Humanoid), nameof(Humanoid.StartAttack)), prefix);
            _canThrow = canThrow;
            _throwTemplate = template;
        }
        catch (Exception exception)
        {
            ReportUnavailable("shield throw", exception);
        }

        try
        {
            Type? shieldOps = assembly.GetType("PassiveTree.ShieldOps");
            Func<Humanoid, ItemDrop.ItemData> bash =
                Bind<Func<Humanoid, ItemDrop.ItemData>>(shieldOps, "BashShield");
            Func<Humanoid, Attack> template = Bind<Func<Humanoid, Attack>>(shieldOps, "BashTemplate");
            MethodInfo prefix = RequireMethod(shieldOps?.GetNestedType("Bash", BindingFlags.NonPublic),
                "Prefix", typeof(bool), [typeof(Humanoid), typeof(bool), typeof(bool).MakeByRefType()]);
            RequireInstalledPatch(AccessTools.Method(typeof(Humanoid), nameof(Humanoid.StartAttack)), prefix);
            _bashShield = bash;
            _bashTemplate = template;
        }
        catch (Exception exception)
        {
            ReportUnavailable("shield bash", exception);
        }

        try
        {
            Type? shieldOps = assembly.GetType("PassiveTree.ShieldOps");
            Type stat = assembly.GetType("PassiveTree.Stat", throwOnError: true)!;
            Type stats = assembly.GetType("PassiveTree.StatSet", throwOnError: true)!;
            MethodInfo has = RequireMethod(shieldOps, "Has", typeof(bool), [stat]);
            object chargeNode = Enum.Parse(stat, "MasteryBlock1");
            Func<bool> hasCharge = Expression.Lambda<Func<bool>>(
                Expression.Call(has, Expression.Constant(chargeNode, stat))).Compile();
            Func<bool> charging = Bind<Func<bool>>(shieldOps, "get_Charging");
            MethodInfo wants = RequireMethod(shieldOps, "Wants", typeof(bool), [typeof(Player), stats]);
            MethodInfo charge = RequireMethod(shieldOps?.GetNestedType("Charge", BindingFlags.NonPublic),
                "Postfix", typeof(void), [typeof(Player)]);
            RequireInstalledPatch(AccessTools.Method(typeof(Player), "FixedUpdate"), charge, postfix: true);
            harmony.Patch(wants, postfix: new HarmonyMethod(
                typeof(PathOfValheimanBridge), nameof(PreventOverlappingCharge)));
            _hasCharge = hasCharge;
            _charging = charging;
        }
        catch (Exception exception)
        {
            ReportUnavailable("shield charge", exception);
        }
    }

    internal static bool OwnsPrimary(Humanoid humanoid)
    {
        if (!IsLocal(humanoid) || _canThrow == null || _throwTemplate == null)
        {
            return false;
        }
        try
        {
            return _canThrow((Player)humanoid, out _) && _throwTemplate()?.m_attackProjectile != null;
        }
        catch (Exception exception)
        {
            _canThrow = null;
            _throwTemplate = null;
            ReportUnavailable("shield throw", exception);
            return false;
        }
    }

    internal static bool OwnsSecondary(Humanoid humanoid)
    {
        if (!IsLocal(humanoid) || _bashShield == null || _bashTemplate == null)
        {
            return false;
        }
        try
        {
            return _bashShield(humanoid) != null &&
                   !string.IsNullOrEmpty(_bashTemplate(humanoid)?.m_attackAnimation);
        }
        catch (Exception exception)
        {
            _bashShield = null;
            _bashTemplate = null;
            ReportUnavailable("shield bash", exception);
            return false;
        }
    }

    internal static bool BlocksCaptainCharge(Humanoid humanoid)
    {
        if (!IsLocal(humanoid))
        {
            return false;
        }

        if (_chargeQueryFailed)
        {
            return true;
        }

        if (_hasCharge == null || _charging == null)
        {
            return false;
        }
        try
        {
            return _hasCharge() || _charging();
        }
        catch (Exception exception)
        {
            _hasCharge = null;
            _charging = null;
            // After a live query fails, the other movement controller may still
            // be active. Stop new CV charges until reinitialization instead of
            // guessing that it is safe to start a second one.
            _chargeQueryFailed = true;
            ReportUnavailable("shield charge", exception);
            return true;
        }
    }

    private static void PreventOverlappingCharge(Player __0, ref bool __result)
    {
        // Learning the node while a CV charge is already moving must not start a
        // second movement controller. The running charge finishes normally.
        if (__result && ShieldRuntimeSystem.IsShieldChargeActive(__0))
        {
            __result = false;
        }
    }

    private static bool IsLocal(Humanoid humanoid) =>
        humanoid != null && humanoid is Player && humanoid == Player.m_localPlayer;

    private static T Bind<T>(Type? type, string name) where T : Delegate
    {
        MethodInfo signature = typeof(T).GetMethod("Invoke")!;
        ParameterInfo[] parameters = signature.GetParameters();
        Type[] types = new Type[parameters.Length];
        for (int index = 0; index < types.Length; index++)
        {
            types[index] = parameters[index].ParameterType;
        }

        return (T)Delegate.CreateDelegate(typeof(T), RequireMethod(type, name, signature.ReturnType, types));
    }

    private static MethodInfo RequireMethod(Type? type, string name, Type returnType, Type[] parameters)
    {
        MethodInfo? method = type?.GetMethod(name, StaticMethods, null, parameters, null);
        if (method == null || method.ReturnType != returnType || method.ContainsGenericParameters)
        {
            throw new MissingMethodException(type?.FullName ?? "Path of Valheiman", name);
        }

        return method;
    }

    private static void RequireInstalledPatch(MethodBase? target, MethodInfo expected, bool postfix = false)
    {
        // PoV catches individual PatchAll failures and can remain loaded without
        // one of these actions. A method's presence alone must not disable CV.
        Patches? patches = target == null ? null : Harmony.GetPatchInfo(target);
        if (patches != null)
        {
            foreach (Patch patch in postfix ? patches.Postfixes : patches.Prefixes)
            {
                if (patch.owner == PluginGuid && patch.PatchMethod == expected)
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException(
            $"The action patch {expected.DeclaringType?.FullName}.{expected.Name} is not installed.");
    }

    private static void ReportUnavailable(string feature, Exception exception) =>
        CaptainValheimPlugin.ModLogger.LogWarning(
            $"Path of Valheiman {feature} compatibility is unavailable: {exception.Message}");

    internal static void Dispose()
    {
        _canThrow = null;
        _throwTemplate = null;
        _bashShield = null;
        _bashTemplate = null;
        _hasCharge = null;
        _charging = null;
        _chargeQueryFailed = false;
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
internal static class HumanoidStartAttackCaptainChargeGuard
{
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore(PathOfValheimanBridge.PluginGuid)]
    private static bool Prefix(Humanoid __instance, ref bool __result)
    {
        // CV charges deliberately do not create a vanilla attack animation. Keep
        // their existing attack lock ahead of PoV's replacing attack prefixes.
        if (!ShieldRuntimeSystem.IsShieldChargeActive(__instance))
        {
            return true;
        }

        __result = false;
        return false;
    }
}
