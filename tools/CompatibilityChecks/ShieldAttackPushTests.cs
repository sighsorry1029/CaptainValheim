using System.Reflection;
using Mono.Cecil;

// Invoke the shipped numeric helper against original managed game/Unity assemblies.
// These assertions cover attack tuning, not Character movement or displacement.
internal static class ShieldAttackPushTests
{
    internal static int Run(Assembly mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Shield attack push: " + message);
            assertions++;
        }
        Type runtime = mod.GetType("CaptainValheim.ShieldRuntimeSystem", true)!;
        MethodInfo calculate = runtime.GetMethod("CalculateShieldAttackPushForce", BindingFlags.Static | BindingFlags.NonPublic)!;
        float Push(float force, float factor = 1f) => (float)calculate.Invoke(null, new object[] { force, factor })!;
        bool Near(float actual, float expected) => Math.Abs(actual - expected) <= Math.Max(0.0001f, Math.Abs(expected) * 0.00001f);

        foreach (float force in new[] { 0f, 0.01f, 1f, 10f, 14.999f, 15f })
            Check(Near(Push(force), force), "low-force shields remain unchanged through the threshold: " + force);
        Check(Push(15.001f) > 15f && Push(15.001f) < 15.001f,
            "compression begins continuously above 15 without a jump or reversal");

        // Fixed expected outputs for representative normal and tower shield stats.
        foreach (var (force, expected) in new (float, float)[] {
            (20f, 17.320508f), (40f, 24.494898f), (60f, 30f), (70f, 32.403704f),
            (100f, 38.729834f), (150f, 47.434166f), (160f, 48.989796f) })
            Check(Near(Push(force), expected), "representative shield force " + force);
        Check(new[] { 100f, 150f, 160f }.All(f => Push(f) / f is >= 0.306f and <= 0.388f),
            "tower shields retain roughly one third of their previous attack push");
        Check(new[] { 20f, 40f, 60f, 70f }.All(f => Push(f) / f is >= 0.462f and <= 0.867f),
            "normal shields receive a smaller proportional reduction than towers");

        float previous = 0f;
        bool orderedAndNeverBuffed = true;
        foreach (float force in new[] { 0f, 1f, 5f, 15f, 16f, 20f, 30f, 40f, 50f, 60f, 70f, 100f, 110f, 150f, 160f, 200f, 1000f })
        {
            float push = Push(force);
            orderedAndNeverBuffed &= push >= previous && push <= force;
            previous = push;
        }
        Check(orderedAndNeverBuffed, "higher force never produces less push and compression never buffs a shield");
        foreach (float factor in new[] { 0.4f, 1f, 2f })
            Check(Near(Push(150f, factor), Push(150f) * factor),
                "primary/throw/charge multipliers remain linear after compression: " + factor);
        Check(Push(150f, 0f) == 0f, "zero pushFactor still disables attack push");
        Check(Push(150f, -1f) == 0f, "negative pushFactor cannot reverse the push");
        Check(Push(-100f, 2f) == 0f && Push(-100f, -1f) == 0f, "negative force cannot become positive push");
        Check(float.IsFinite(Push(float.MaxValue)) && Push(float.MaxValue) > 0f,
            "large finite custom shield stats do not overflow the compression step");
        return assertions;
    }

    internal static int CheckContract(AssemblyDefinition mod)
    {
        string[] expected = { "BeginShieldPrimaryVanillaTrigger", "StartShieldThrow", "StartShieldCharge" };
        const string owner = "CaptainValheim.ShieldRuntimeSystem";
        var calls = mod.MainModule.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions.Where(i => i.Operand is MethodReference r &&
                    r.DeclaringType.FullName == owner && r.Name == "CalculateShieldAttackPushForce")
                .Select(_ => m)).ToArray();
        foreach (string name in expected)
            if (calls.Count(m => m.DeclaringType.FullName == owner && m.Name == name) != 1)
                throw new Exception("Shield attack push contract: expected one helper call in " + name);
        if (calls.Length != expected.Length)
            throw new Exception("Shield attack push contract: helper must only affect primary, throw and charge attacks");
        return expected.Length + 1;
    }
}
