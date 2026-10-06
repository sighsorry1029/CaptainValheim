using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Exercise the shipped snapshot/refresh policy with ORIGINAL game managed types.
// This does not construct Unity objects, install patches, or simulate multiplayer.
internal static class ProjectileAppearanceTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static int Run(Assembly game, Assembly mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Projectile appearance: " + message);
            assertions++;
        }

        Type snapshot = mod.GetType("CaptainValheim.ShieldProjectileAppearance", true)!;
        MethodInfo pack = snapshot.GetMethod("Pack", All)!;
        MethodInfo unpack = snapshot.GetMethod("TryUnpack", All)!;
        object Create(int hash, int variant) => Activator.CreateInstance(snapshot, All, null, new object[] { hash, variant }, null)!;
        long Pack(int hash, int variant) => (long)pack.Invoke(Create(hash, variant), null)!;
        (bool Valid, object Snapshot) Unpack(long value)
        {
            var args = new object?[] { value, null };
            bool valid = (bool)unpack.Invoke(null, args)!;
            return (valid, args[1]!);
        }

        Type packageType = game.GetType("ZPackage") ?? Assembly.Load("assembly_utils").GetType("ZPackage", true)!;
        MethodInfo write = packageType.GetMethod("Write", new[] { typeof(long) })!;
        MethodInfo setPos = packageType.GetMethod("SetPos", All)!;
        MethodInfo read = packageType.GetMethod("ReadLong", All)!;
        foreach (int hash in new[] { 1, -1, int.MinValue, int.MaxValue, -1731486127 })
        foreach (int variant in new[] { 0, 1, 7, int.MaxValue })
        {
            long payload = Pack(hash, variant);
            Check(payload != 0, "valid signed hash must not become absent metadata");
            object package = Activator.CreateInstance(packageType)!;
            write.Invoke(package, new object[] { payload });
            write.Invoke(package, new object[] { 0x123456789ABCDEFL });
            setPos.Invoke(package, new object[] { 0 });
            long received = (long)read.Invoke(package, null)!;
            Check(received == payload, "game ZPackage preserves all 64 appearance bits");
            var decoded = Unpack(received);
            Check(decoded.Valid && (int)snapshot.GetProperty("PrefabHash", All)!.GetValue(decoded.Snapshot)! == hash &&
                  (int)snapshot.GetProperty("Variant", All)!.GetValue(decoded.Snapshot)! == variant,
                "negative prefab hash and variant round-trip independently");
            Check((long)read.Invoke(package, null)! == 0x123456789ABCDEFL, "appearance payload does not consume adjacent data");
        }

        Check(Pack(0, 0) == 0 && Pack(0, 7) == 0, "absent prefab cannot create a cosmetic snapshot");
        Check(Pack(1, -1) == 0 && Pack(-1, int.MinValue) == 0, "negative variants cannot be published");
        foreach (long malformed in new[] { 0L, 1L, (long)int.MaxValue, unchecked((long)0x00000001FFFFFFFFUL), unchecked((long)0xFFFFFFFF80000000UL) })
            Check(!Unpack(malformed).Valid, "absent hash or negative variant is rejected before prefab lookup");

        Type runtime = mod.GetType("CaptainValheim.ShieldRuntimeSystem", true)!;
        Type stateType = runtime.GetNestedType("ShieldProjectileVisualState", All)!;
        object state = Activator.CreateInstance(stateType, nonPublic: true)!;
        bool ShouldApply(long payload, bool intact = true) => (bool)stateType.GetMethod("ShouldApply", All)!.Invoke(state, new object[] { payload, intact })!;
        void Applied(long payload) => stateType.GetMethod("MarkApplied", All)!.Invoke(state, new object[] { payload });

        Check(ShouldApply(0), "old/no-metadata projectile gets one default application");
        Applied(0);
        Check(!ShouldApply(0), "default fallback remains stable while cosmetic metadata is absent");
        long first = Pack(-1731486127, 3);
        Check(ShouldApply(first), "late appearance metadata can replace an already-created fallback");
        Applied(first);
        Check(!ShouldApply(first), "unchanged appearance must not request per-frame mesh work");
        Check(ShouldApply(first, intact: false), "destroyed/replaced visual can be repaired once");
        Check(ShouldApply(Pack(-1731486127, 4)), "variant-only metadata changes are observable");
        Check(ShouldApply(Pack(1, 3)), "prefab-only metadata changes are observable");
        stateType.GetMethod("MarkFailed", All)!.Invoke(state, new object[] { first });
        Check(!ShouldApply(first, intact: false), "broken asset does not trigger an exception/creation loop");
        Check(ShouldApply(Pack(1, 3), intact: false), "new metadata permits recovery after a failed asset");
        Applied(first);
        Check(ShouldApply(first, intact: false), "successful application clears the previous failure suppression");
        return assertions;
    }

    internal static int CheckContract(AssemblyDefinition mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Projectile appearance contract: " + message);
            assertions++;
        }
        TypeDefinition runtime = mod.MainModule.GetType("CaptainValheim.ShieldRuntimeSystem");
        MethodDefinition Method(string name) => runtime.Methods.Single(m => m.Name == name);
        MethodReference[] Calls(MethodDefinition method) => method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        MethodDefinition consume = Method("TryConsumeShieldForThrow");
        MethodReference[] consumeCalls = Calls(consume);
        int capture = Array.FindIndex(consumeCalls, m => m.Name == "CaptureShieldProjectileAppearance");
        int unequip = Array.FindIndex(consumeCalls, m => m.Name == "UnequipItem");
        Check(capture >= 0 && unequip > capture, "appearance captured before equipment is removed and its presentation cleared");
        Check(consume.Parameters.Any(p => p.ParameterType.FullName == "CaptainValheim.ShieldProjectileAppearance&"),
            "throw consumption returns cosmetic snapshot independently of physical ItemData");

        MethodReference[] captureCalls = Calls(Method("CaptureShieldProjectileAppearance"));
        Check((captureCalls.Any(m => m.Name == "ReferenceEquals") ||
               Method("CaptureShieldProjectileAppearance").Body.Instructions.Any(i => i.OpCode == OpCodes.Ceq ||
                   i.OpCode == OpCodes.Beq || i.OpCode == OpCodes.Beq_S ||
                   i.OpCode == OpCodes.Bne_Un || i.OpCode == OpCodes.Bne_Un_S)) &&
              captureCalls.Any(m => m.Name == "get_LeftItem"),
            "snapshot checks exact active-hand item identity");
        Check(captureCalls.Any(m => m.Name == "IsOwner"), "snapshot is taken only by the equipment owner");
        Check(Method("CaptureShieldProjectileAppearance").Body.ExceptionHandlers.Count > 0,
            "optional presentation lookup failure has a fallback boundary");

        MethodDefinition publish = Method("ApplyShieldProjectileVisual");
        MethodReference[] publishCalls = Calls(publish);
        int authority = Array.FindIndex(publishCalls, m => m.Name == "IsOwner");
        int firstWrite = Array.FindIndex(publishCalls, m => m.DeclaringType.Name == "ZDO" && m.Name == "Set");
        Check(authority >= 0 && firstWrite > authority, "publication checks network ownership before ZDO writes");
        Check(publishCalls.Any(m => m.DeclaringType.Name == "ZDO" && m.Name == "Set" && m.Parameters.Last().ParameterType.FullName == "System.Int64"),
            "appearance pair is published as a single atomic long");
        Check(publish.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "s_visual") &&
              publish.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_dropPrefab"),
            "original visual identity is still published from the physical item's prefab");

        TypeDefinition controller = runtime.NestedTypes.Single(t => t.Name == "ShieldProjectileController");
        FieldDefinition storedAppearance = controller.Fields.Single(f => f.Name == "_appearance");
        Check(storedAppearance.FieldType.FullName == "CaptainValheim.ShieldProjectileAppearance",
            "flight controller owns an immutable cosmetic snapshot alongside physical cargo");
        var respawns = controller.Methods.Where(m => m.HasBody && Calls(m).Any(c => c.Name == "TrySpawnShieldProjectile")).ToArray();
        Check(respawns.Length == 2 && respawns.All(m => m.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == "_appearance")),
            "both bounce and return spawns use the stored flight snapshot");

        Check(Calls(Method("TryUpdateShieldThrowProjectileVisual")).First().Name == "IsMarkedShieldProjectile",
            "visual override first rejects unrelated/reflected projectiles");
        foreach (string name in new[] { "CaptureShieldProjectileAppearance", "ApplyShieldProjectileVisual", "ApplyShieldProjectileAppearance", "CreateShieldProjectileVisual", "TryUpdateShieldThrowProjectileVisual" })
        {
            MethodDefinition method = Method(name);
            Check(!Calls(method).Any(m => m.Name is "AddItem" or "RemoveItem" or "DropItem" or "EquipItem" or "UnequipItem") &&
                  !method.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f &&
                      (f.DeclaringType.Name == "ItemData" || f.DeclaringType.Name == "HitData")),
                name + " cannot transfer items or rewrite physical item/hit fields");
        }
        Check(publish.Body.ExceptionHandlers.Count > 0, "visual failures cannot enter projectile cargo recovery through publication");
        Check(!mod.MainModule.AssemblyReferences.Any(a => a.Name.Contains("Armoire", StringComparison.OrdinalIgnoreCase)),
            "no hard dependency on an Armoire assembly or release version");
        return assertions;
    }
}
