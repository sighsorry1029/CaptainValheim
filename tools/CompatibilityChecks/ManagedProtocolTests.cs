using System.Reflection;
using System.Runtime.Loader;

internal static class ManagedProtocolTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    // Runs actual mod/game managed codec methods, without substituting/publicizing the
    // game DLL. This .NET host does not validate Unity native code, Harmony or networking.
    internal static (int Protocol, int EquipmentPolicy, int ReturnedShield, int KeyHintPolicy, int ProjectileAppearance) Run(string modPath, string managed, string core)
    {
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            foreach (string directory in new[] { managed, core, Path.GetDirectoryName(modPath)! })
            {
                string path = Path.GetFullPath(Path.Combine(directory, name.Name + ".dll"));
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            assertions++;
        }
        object? Call(Type type, string name, object? instance, params object?[] values) =>
            type.GetMethod(name, All)!.Invoke(instance, values);
        try
        {
            var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(managed, "assembly_valheim.dll")));
            var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(modPath));
            var manager = mod.GetType("CaptainValheim.SecondaryAttackManager", true)!;
            var contextType = manager.GetNestedType("ShieldReflectProjectileContext", All)!;
            var envelopeType = manager.GetNestedType("ShieldReflectDamageEnvelope", All)!;
            var hitType = game.GetType("HitData", true)!;
            var damageType = hitType.GetNestedType("DamageTypes", All)!;
            var packageType = game.GetType("ZPackage") ?? Assembly.Load("assembly_utils").GetType("ZPackage", true)!;
            var vectorType = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector3", true)!;
            var idType = game.GetType("ZDOID", true)!;
            object damage = Activator.CreateInstance(damageType)!;
            var channels = damageType.GetFields(All).Where(f => !f.IsStatic && f.FieldType == typeof(float)).ToArray();
            Check(channels.Length == 12, "Expected all 12 Valheim damage channels");
            for (int i = 0; i < channels.Length; i++) channels[i].SetValue(damage, i + 0.25f);
            object package = Activator.CreateInstance(packageType)!;
            Call(manager, "WriteDamage", null, package, damage);
            packageType.GetMethod("Write", new[] { typeof(int) })!.Invoke(package, new object[] { 0x12345678 });
            Call(packageType, "SetPos", package, 0);
            object copy = Call(manager, "ReadDamage", null, package)!;
            foreach (var channel in channels) Check(Equals(channel.GetValue(damage), channel.GetValue(copy)), "Damage round-trip: " + channel.Name);
            Check((int)Call(packageType, "ReadInt", package)! == 0x12345678, "Damage packet boundary");

            object Context(object values) => contextType.GetConstructors(All).Single().Invoke(new object?[] {
                null, "test_projectile", Activator.CreateInstance(vectorType), false, Activator.CreateInstance(vectorType),
                Activator.CreateInstance(vectorType), 1f, values, 2f, 3f, true, true, 0, (short)7, null, false });
            object context = Context(damage);
            Check((bool)contextType.GetProperty("IsValid")!.GetValue(context)!, "Finite context accepted");
            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                object invalidDamage = Activator.CreateInstance(damageType)!;
                damageType.GetField("m_nonPlayer")!.SetValue(invalidDamage, invalid);
                Check(!(bool)contextType.GetProperty("IsValid")!.GetValue(Context(invalidDamage))!, "Invalid nonPlayer rejected");
            }
            object hit = Activator.CreateInstance(hitType)!;
            hitType.GetField("m_damage")!.SetValue(hit, damage);
            hitType.GetField("m_eitrAdd")!.SetValue(hit, 2.75f);
            hitType.GetField("m_variant")!.SetValue(hit, (short)4);
            object envelope = envelopeType.GetConstructors(All).Single().Invoke(new[] {
                (object)1L, Activator.CreateInstance(idType)!, Activator.CreateInstance(idType)!, 2L, hit, context });
            object wire = Call(envelopeType, "Serialize", envelope)!;
            var readArgs = new object?[] { wire, null };
            Check((bool)envelopeType.GetMethod("TryDeserialize", All)!.Invoke(null, readArgs)!, "v3 envelope decoded");
            object receivedHit = envelopeType.GetProperty("Hit", All)!.GetValue(readArgs[1])!;
            Check(Equals(hitType.GetField("m_eitrAdd")!.GetValue(receivedHit), 2.75f), "Game HitData eitr preserved");
            Check(Equals(hitType.GetField("m_variant")!.GetValue(receivedHit), (short)4), "Game HitData variant preserved");
            object receivedContext = envelopeType.GetProperty("ProjectileContext", All)!.GetValue(readArgs[1])!;
            Check(Equals(contextType.GetProperty("HitVariant")!.GetValue(receivedContext), (short)7), "Reflected status-effect variant preserved");
            object receivedDamage = contextType.GetProperty("Damage")!.GetValue(receivedContext)!;
            foreach (var channel in channels) Check(Equals(channel.GetValue(damage), channel.GetValue(receivedDamage)), "Envelope context: " + channel.Name);
            foreach (int version in new[] { 2, 3 })
            {
                object invalidPacket = Activator.CreateInstance(packageType)!;
                packageType.GetMethod("Write", new[] { typeof(int) })!.Invoke(invalidPacket, new object[] { version });
                Check(!(bool)envelopeType.GetMethod("TryDeserialize", All)!.Invoke(null, new object?[] { invalidPacket, null })!, "Old/truncated envelope rejected: " + version);
            }
            int equipmentAssertions = EquipmentPolicyTests.Run(game, mod);
            int returnedShieldAssertions = ReturnedShieldTests.Run(game, mod);
            int keyHintAssertions = KeyHintPolicyTests.Run(game, mod);
            int appearanceAssertions = ProjectileAppearanceTests.Run(game, mod);
            return (assertions, equipmentAssertions, returnedShieldAssertions, keyHintAssertions, appearanceAssertions);
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }
}
