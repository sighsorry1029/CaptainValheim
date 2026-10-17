using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace CaptainValheim;

internal static class PathOfValheimanReflectionBridge
{
    internal const string HarmonyOwner = "treextr.pathofvalheiman";

    private static readonly MethodInfo? ProjectileSetup = typeof(Projectile).GetMethod(nameof(Projectile.Setup),
        BindingFlags.Public | BindingFlags.Instance, binder: null,
        new[] { typeof(Character), typeof(Vector3), typeof(float), typeof(HitData), typeof(ItemDrop.ItemData), typeof(ItemDrop.ItemData) }, modifiers: null);
    private static readonly MethodInfo ObservedSetup = typeof(PathOfValheimanReflectionBridge).GetMethod(nameof(SetupReflectedProjectile), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo LaunchObserver = typeof(PathOfValheimanReflectionBridge).GetMethod(nameof(ObserveLaunchSetup), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static Harmony? _harmony;
    private static MethodInfo? _launch;

    internal static bool Enabled => _launch != null;

    internal static void Initialize(Harmony harmony, Assembly assembly)
    {
        Dispose();
        MethodInfo? launch = null;
        try
        {
            Type? reflect = assembly.GetType("PassiveTree.ParryReflect", throwOnError: false);
            Type? pending = reflect?.GetNestedType("Pending", BindingFlags.NonPublic);
            launch = pending == null ? null : reflect!.GetMethod("Launch", BindingFlags.Static | BindingFlags.NonPublic,
                binder: null, new[] { typeof(Player), typeof(Character), pending }, modifiers: null);
            if (launch == null || launch.ReturnType != typeof(void) || ProjectileSetup == null)
            {
                CaptainValheimPlugin.ModLogger.LogWarning("Path of Valheiman projectile reflection compatibility is unavailable: the launch contract was not found.");
                return;
            }

            // Observe only PoV's reflection launch, never its other projectiles or
            // generic node effects. A failed/early-returning launch remains CV's job.
            harmony.Patch(launch, transpiler: new HarmonyMethod(LaunchObserver));
            _harmony = harmony;
            _launch = launch;
        }
        catch (Exception exception)
        {
            if (launch != null)
            {
                RemoveObserver(harmony, launch);
            }

            CaptainValheimPlugin.ModLogger.LogWarning($"Path of Valheiman projectile reflection compatibility was not installed: {exception.Message}");
        }
    }

    internal static void Dispose()
    {
        MethodInfo? launch = _launch;
        Harmony? harmony = _harmony;
        _launch = null;
        _harmony = null;
        if (launch != null && harmony != null)
        {
            RemoveObserver(harmony, launch);
        }
    }

    private static void RemoveObserver(Harmony harmony, MethodInfo launch)
    {
        try
        {
            harmony.Unpatch(launch, LaunchObserver);
        }
        catch (Exception exception)
        {
            CaptainValheimPlugin.ModLogger.LogWarning($"Path of Valheiman reflection observer cleanup failed: {exception.Message}");
        }
    }

    internal static IEnumerable<CodeInstruction> ObserveLaunchSetup(IEnumerable<CodeInstruction> instructions)
    {
        if (ProjectileSetup == null)
        {
            throw new MissingMethodException(typeof(Projectile).FullName, nameof(Projectile.Setup));
        }

        List<CodeInstruction> result = new();
        int matches = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (!instruction.Calls(ProjectileSetup))
            {
                result.Add(instruction);
                continue;
            }

            // The static wrapper receives the original call's receiver and all six
            // arguments unchanged. Preserve labels and exception-block boundaries.
            CodeInstruction observed = new(instruction)
            {
                opcode = OpCodes.Call,
                operand = ObservedSetup
            };
            result.Add(observed);
            matches++;
        }

        if (matches != 1)
        {
            throw new InvalidOperationException($"Expected one Projectile.Setup call in Path of Valheiman's reflection launch, found {matches}.");
        }

        return result;
    }

    private static void SetupReflectedProjectile(
        Projectile projectile,
        Character owner,
        Vector3 velocity,
        float hitNoise,
        HitData hit,
        ItemDrop.ItemData item,
        ItemDrop.ItemData ammo)
    {
        projectile.Setup(owner, velocity, hitNoise, hit, item, ammo);
        // Reaching here is the success signal: Launch already instantiated this
        // projectile and its Setup completed. Record before PoV's later trail/VFX
        // work can throw. No Unity lookup or mod reflection is needed in this path.
        SecondaryAttackManager.RecordPathOfValheimanReflection(owner);
    }
}
