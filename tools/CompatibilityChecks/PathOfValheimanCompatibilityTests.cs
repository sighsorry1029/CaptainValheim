using System.Reflection;
using System.Runtime.Loader;
using System.Linq.Expressions;
using System.Collections;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Optional-mod contracts are read from the supplied ORIGINAL binary, never from a
// publicized assembly. Managed tests may load the optional original assembly and
// bind its delegates, without invoking PoV gameplay methods, initializing Unity,
// constructing real Unity objects, or installing Harmony patches.
internal static class PathOfValheimanCompatibilityTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static int Run(Assembly game, Assembly mod, string? originalPath)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("PathOfValheiman managed policy: " + message);
            assertions++;
        }
        Type bridge = mod.GetType("CaptainValheim.PathOfValheimanBridge", true)!;
        object? Call(string name, params object?[] values) => bridge.GetMethod(name, All)!.Invoke(null, values);
        var callbackFields = bridge.GetFields(All).Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType)).ToArray();
        var savedCallbacks = callbackFields.Select(f => f.GetValue(null)).ToArray();
        FieldInfo failedCharge = bridge.GetField("_chargeQueryFailed", All)!;
        object? savedFailure = failedCharge.GetValue(null);
        try
        {
            // Player.m_localPlayer initializes the original game's Unity-dependent
            // static fields. Do not fake or rewrite that native boundary in this
            // .NET host; live input/learning/refund behavior remains a game test.
            bridge.GetField("_hasCharge", All)!.SetValue(null, (Func<bool>)(() => true));
            bridge.GetField("_charging", All)!.SetValue(null, (Func<bool>)(() => true));
            failedCharge.SetValue(null, true);
            Call("Dispose");
            Check(callbackFields.All(f => f.GetValue(null) == null), "plugin teardown releases every cached callback");
            Check(!(bool)failedCharge.GetValue(null)!, "plugin teardown clears the fail-closed charge latch");
            assertions += RunReflectionRewrite(game, mod, originalPath);
            if (originalPath != null)
            {
                // Binding is executed against the original private members, but
                // the returned delegates are never called: no PoV type startup.
                Assembly original = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(originalPath));
                MethodInfo bind = bridge.GetMethod("Bind", All)!;
                foreach (var (field, typeName, name) in new[] {
                    ("_canThrow", "PassiveTree.ShieldThrow", "CanThrow"),
                    ("_throwTemplate", "PassiveTree.ShieldThrow", "Spear"),
                    ("_bashShield", "PassiveTree.ShieldOps", "BashShield"),
                    ("_bashTemplate", "PassiveTree.ShieldOps", "BashTemplate"),
                    ("_charging", "PassiveTree.ShieldOps", "get_Charging") })
                {
                    Type delegateType = bridge.GetField(field, All)!.FieldType;
                    object? callback = bind.MakeGenericMethod(delegateType).Invoke(null, new object[] { original.GetType(typeName, true)!, name });
                    Check(callback is Delegate, "actual cached binder accepts original member " + typeName + "." + name);
                }
                Type stat = original.GetType("PassiveTree.Stat", true)!;
                MethodInfo originalHas = (MethodInfo)Call("RequireMethod", original.GetType("PassiveTree.ShieldOps", true)!, "Has", typeof(bool), new[] { stat })!;
                var nodeQuery = Expression.Lambda<Func<bool>>(Expression.Call(originalHas,
                    Expression.Constant(Enum.Parse(stat, "MasteryBlock1"), stat))).Compile();
                Check(nodeQuery != null, "private named-node query compiles against original metadata without running it");
            }
        }
        finally
        {
            for (int index = 0; index < callbackFields.Length; index++) callbackFields[index].SetValue(null, savedCallbacks[index]);
            failedCharge.SetValue(null, savedFailure);
        }
        return assertions;
    }

    private static int RunReflectionRewrite(Assembly game, Assembly mod, string? originalPath)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("PathOfValheiman reflection rewrite: " + message);
            assertions++;
        }
        Type bridge = mod.GetType("CaptainValheim.PathOfValheimanReflectionBridge", true)!;
        MethodInfo rewrite = bridge.GetMethod("ObserveLaunchSetup", All)!;
        Type instructionType = rewrite.GetParameters()[0].ParameterType.GetGenericArguments().Single();
        Type listType = typeof(List<>).MakeGenericType(instructionType);
        FieldInfo opcode = instructionType.GetField("opcode")!, operand = instructionType.GetField("operand")!;
        FieldInfo labels = instructionType.GetField("labels")!, blocks = instructionType.GetField("blocks")!;
        MethodInfo setup = game.GetType("Projectile", true)!.GetMethods(All).Single(m => m.Name == "Setup");
        MethodInfo wrapper = bridge.GetMethod("SetupReflectedProjectile", All)!;
        Check(wrapper.ReturnType == setup.ReturnType && wrapper.IsStatic &&
            wrapper.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { setup.DeclaringType! }.Concat(setup.GetParameters().Select(p => p.ParameterType))),
            "wrapper preserves the receiver and every original argument type on the IL stack");
        var opcodes = typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
        IList NewList() => (IList)Activator.CreateInstance(listType)!;
        object Instruction(System.Reflection.Emit.OpCode op, object? value) => Activator.CreateInstance(instructionType, op, value)!;
        IList instructions = NewList();
        if (originalPath != null)
        {
            using var original = AssemblyDefinition.ReadAssembly(originalPath);
            foreach (Instruction source in original.MainModule.GetType("PassiveTree.ParryReflect").Methods.Single(m => m.Name == "Launch").Body.Instructions)
            {
                object? value = source.Operand;
                if (value is MethodReference method && method.DeclaringType.FullName == "Projectile" && method.Name == "Setup") value = setup;
                instructions.Add(Instruction(opcodes[source.OpCode.Value], value));
            }
        }
        else
        {
            instructions.Add(Instruction(System.Reflection.Emit.OpCodes.Nop, null));
            instructions.Add(Instruction(System.Reflection.Emit.OpCodes.Callvirt, setup));
            instructions.Add(Instruction(System.Reflection.Emit.OpCodes.Ret, null));
        }
        object[] before = instructions.Cast<object>().ToArray();
        int site = Array.FindIndex(before, i => Equals(operand.GetValue(i), setup));
        Check(site >= 0, "input includes the reflection Setup site");
        var label = new System.Reflection.Emit.DynamicMethod("PoVLabelOnly", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
        ((IList)labels.GetValue(before[site])!).Add(label);
        Type blockType = blocks.FieldType.GetGenericArguments().Single();
        Type blockKind = blockType.GetField("blockType")!.FieldType;
        object block = Activator.CreateInstance(blockType, Enum.Parse(blockKind, "BeginExceptionBlock"), null)!;
        ((IList)blocks.GetValue(before[site])!).Add(block);
        object[] after = ((IEnumerable)rewrite.Invoke(null, [instructions])!).Cast<object>().ToArray();
        Check(after.Length == before.Length, "rewriting inserts no extra IL or duplicate setup call");
        Check(after.Where((value, index) => index != site).SequenceEqual(before.Where((value, index) => index != site)),
            "every unrelated original instruction keeps its identity and order");
        Check(Equals(opcode.GetValue(after[site]), System.Reflection.Emit.OpCodes.Call) && Equals(operand.GetValue(after[site]), wrapper),
            "only the original Setup call is redirected to the observer");
        Check(((IList)labels.GetValue(after[site])!).Contains(label) && ((IList)blocks.GetValue(after[site])!).Contains(block),
            "the observed call preserves branch labels and exception-block boundaries");
        foreach (int count in new[] { 0, 2 })
        {
            IList invalid = NewList();
            for (int index = 0; index < count; index++) invalid.Add(Instruction(System.Reflection.Emit.OpCodes.Callvirt, setup));
            bool rejected = false;
            try { rewrite.Invoke(null, [invalid]); }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected, "a changed launch with " + count + " setup sites is rejected without a partial result");
        }
        return assertions;
    }

    internal static int CheckContract(AssemblyDefinition mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("PathOfValheiman integration contract: " + message);
            assertions++;
        }
        TypeDefinition Type(string name) => mod.MainModule.GetType("CaptainValheim." + name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        MethodReference[] Calls(MethodDefinition method) => method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        bool CallsBridge(MethodDefinition method, string name) => Calls(method).Any(m => m.DeclaringType.Name == "PathOfValheimanBridge" && m.Name == name);
        bool BeforePoV(MethodDefinition method) => method.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyBefore" &&
            a.ConstructorArguments.SelectMany(v => v.Value is CustomAttributeArgument[] list ? list : new[] { v }).Any(v => Equals(v.Value, "treextr.pathofvalheiman")));
        Check(!mod.MainModule.AssemblyReferences.Any(a => a.Name == "PathOfValheiman"), "optional integration introduces no hard assembly reference");
        foreach (string name in new[] { "OwnsPrimary", "OwnsSecondary", "BlocksCaptainCharge" })
            Check(!Calls(Method("PathOfValheimanBridge", name)).Any(m => m.Name is "GetMethod" or "GetType" or "DynamicInvoke" || m.DeclaringType.FullName == "System.Reflection.MethodBase"),
                "per-input query uses cached delegates without reflection search: " + name);
        foreach (string name in new[] { "BlocksCaptainCharge", "OwnsSecondary" })
            Check(CallsBridge(Method("ShieldRuntimeSystem", "CanSelectWeaponCharge"), name), "mixed-equipment charge input respects " + name);
        Check(CallsBridge(Method("ShieldRuntimeSystem", "CanStartShieldCharge"), "BlocksCaptainCharge"), "all direct charge starts enforce external movement ownership");
        foreach (string name in new[] { "OwnsPrimary", "OwnsSecondary", "BlocksCaptainCharge" })
            Check(CallsBridge(Method("ShieldOnlyKeyHintSystem", "TryBuildHintState"), name), "visible hint eligibility uses runtime ownership policy: " + name);
        var guard = Method("HumanoidStartAttackCaptainChargeGuard", "Prefix");
        Check(BeforePoV(guard) && Calls(guard).Any(m => m.Name == "IsShieldChargeActive"), "already-moving CV charge locks attack before PoV replacing prefixes");
        Check(Calls(Method("PathOfValheimanBridge", "PreventOverlappingCharge")).Any(m => m.Name == "IsShieldChargeActive"),
            "PoV Wants hook checks active CV movement before granting a new charge");

        MethodDefinition observed = Method("PathOfValheimanReflectionBridge", "SetupReflectedProjectile");
        MethodReference[] setupCalls = Calls(observed);
        int setup = Array.FindIndex(setupCalls, m => m.DeclaringType.FullName == "Projectile" && m.Name == "Setup");
        int record = Array.FindIndex(setupCalls, m => m.Name == "RecordPathOfValheimanReflection");
        Check(setup >= 0 && record > setup && observed.Body.ExceptionHandlers.Count == 0,
            "successful Setup is recorded only after the original returns; failed Setup cannot suppress fallback");
        MethodDefinition postfix = Method("HumanoidBlockAttackPatch", "Postfix");
        Check(BeforePoV(postfix) && Calls(postfix).Any(m => m.Name == "RecordCompletedBlockAttack"),
            "block result/stamina snapshot is ordered before PoV block callbacks");
        MethodDefinition finalizer = Method("HumanoidBlockAttackPatch", "Finalizer");
        Check(Calls(finalizer).Any(m => m.Name == "FinalizeBlockAttack") &&
            finalizer.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally &&
                finalizer.Body.Instructions.SkipWhile(i => i != h.HandlerStart).TakeWhile(i => i != h.HandlerEnd)
                    .Any(i => i.Operand is MethodReference m && m.Name == "EndShieldReflectBlockAttack")),
            "reflection is resolved after postfix callbacks and block scope cleanup is in finally");
        Check(Calls(Method("SecondaryAttackManager", "FinalizeBlockAttack")).Any(m => m.Name == "get_PathOfValheimanReflected"),
            "CV duplicate suppression consumes actual launch success, not merely node possession");
        Check(Calls(Method("SecondaryAttackManager", "CaptureBlockAttackContext")).Any(m => m.DeclaringType.Name == "PathOfValheimanReflectionBridge" && m.Name == "get_Enabled"),
            "nested non-reflecting block sentinels are limited to an installed observer");
        MethodDefinition recordSuccess = Method("SecondaryAttackManager", "RecordPathOfValheimanReflection");
        MethodReference[] success = Calls(recordSuccess);
        Instruction[] recordIl = recordSuccess.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
        int ownerRead = Array.FindIndex(recordIl, i => i.Operand is MethodReference m && m.Name == "get_Player");
        int successWrite = Array.FindIndex(recordIl, i => i.Operand is MethodReference m && m.Name == "set_PathOfValheimanReflected");
        bool sameOwnerOnly = false;
        if (ownerRead >= 0 && ownerRead + 2 < recordIl.Length && successWrite > ownerRead && recordIl[ownerRead + 1].OpCode == OpCodes.Ldarg_0)
        {
            Instruction comparison = recordIl[ownerRead + 2];
            // Release folds ReferenceEquals + a false branch into bne.un. Debug
            // retains ceq and a later brfalse. Both compare these exact references
            // and must skip the success write when they are not the same object.
            sameOwnerOnly = comparison.OpCode.Code is Code.Bne_Un or Code.Bne_Un_S
                ? comparison.Operand is Instruction target && target.Offset > recordIl[successWrite].Offset
                : (comparison.OpCode == OpCodes.Ceq || comparison.Operand is MethodReference m && m.Name == "ReferenceEquals") &&
                    recordIl.Skip(ownerRead + 3).Take(successWrite - ownerRead - 3).Any(i =>
                        i.OpCode.Code is Code.Brfalse or Code.Brfalse_S && i.Operand is Instruction target && target.Offset > recordIl[successWrite].Offset);
        }
        Check(new[] { "get_Count", "get_Item", "get_BlockCompleted", "get_BlockSucceeded", "set_PathOfValheimanReflected" }
                .All(name => success.Any(m => m.Name == name)) && sameOwnerOnly,
            "successful-launch observer checks the top scope, completed success and exact owner identity");
        Check(Calls(Method("SecondaryAttackManager", "EndShieldReflectBlockAttack")).Any(m => m.Name == "RemoveAt") &&
            Calls(Method("SecondaryAttackManager", "EndShieldReflectBlockAttack")).Any(m => m.Name == "Remove"),
            "scope cleanup supports both nested top removal and exceptional out-of-order removal");
        return assertions;
    }

    internal static (int Assertions, string[] Contracts) CheckExternal(string path)
    {
        using var original = AssemblyDefinition.ReadAssembly(path);
        int assertions = 0;
        var contracts = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("PathOfValheiman original contract: " + message);
            assertions++;
        }
        MethodDefinition Method(string type, string name, string result, bool publiclyVisible, params string[] arguments)
        {
            MethodDefinition? found = original.MainModule.GetType(type)?.Methods.SingleOrDefault(m =>
                m.Name == name && m.IsStatic && m.ReturnType.FullName == result &&
                m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(arguments));
            Check(found != null && found.IsPublic == publiclyVisible, type + "." + name + " signature/access");
            contracts.Add(found!.FullName + " [" + found.Attributes + "]");
            return found;
        }
        var plugin = original.MainModule.GetTypes().SelectMany(t => t.CustomAttributes)
            .SingleOrDefault(a => a.AttributeType.FullName == "BepInEx.BepInPlugin" &&
                a.ConstructorArguments.Count == 3 && Equals(a.ConstructorArguments[0].Value, "treextr.pathofvalheiman"));
        Check(plugin != null, "the supplied DLL is the expected BepInEx plugin");
        contracts.Add("BepInEx GUID treextr.pathofvalheiman; plugin version " + plugin!.ConstructorArguments[2].Value);

        var has = Method("PassiveTree.ShieldOps", "Has", "System.Boolean", false, "PassiveTree.Stat");
        Method("PassiveTree.ShieldOps", "get_Charging", "System.Boolean", true);
        Method("PassiveTree.ShieldOps", "Wants", "System.Boolean", false, "Player", "PassiveTree.StatSet");
        Method("PassiveTree.ShieldOps", "BashShield", "ItemDrop/ItemData", true, "Humanoid");
        Method("PassiveTree.ShieldOps", "BashTemplate", "Attack", false, "Humanoid");
        Method("PassiveTree.ShieldThrow", "CanThrow", "System.Boolean", false, "Player", "ItemDrop/ItemData&");
        Method("PassiveTree.ShieldThrow", "Spear", "Attack", false);
        Method("PassiveTree.ShieldThrow/Throw", "Prefix", "System.Boolean", false, "Humanoid", "System.Boolean", "System.Boolean&");
        Method("PassiveTree.ShieldOps/Bash", "Prefix", "System.Boolean", false, "Humanoid", "System.Boolean", "System.Boolean&");
        Method("PassiveTree.ShieldOps/Charge", "Postfix", "System.Void", false, "Player");
        TypeDefinition? stats = original.MainModule.GetType("PassiveTree.Stat");
        foreach (string name in new[] { "MasteryBlock1", "MasteryBlock2", "ShieldThrow", "ParryReflectProjectile" })
        {
            FieldDefinition? stat = stats?.Fields.SingleOrDefault(f => f.Name == name && f.IsLiteral);
            Check(stat != null, "named acquired-node identifier exists: " + name);
            contracts.Add("PassiveTree.Stat." + name + " = " + stat!.Constant);
        }
        Check(has.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "PassiveTree.ShieldOps" && m.Name == "get_S"),
            "node lookup reads current progress rather than a one-time installation flag");

        var launch = Method("PassiveTree.ParryReflect", "Launch", "System.Void", false,
            "Player", "Character", "PassiveTree.ParryReflect/Pending");
        Check(launch.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "Projectile" && m.Name == "Setup") == 1,
            "the observed reflection launch sets up exactly one Projectile");
        Check(launch.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && Equals(i.Operand, "pt.reflect")),
            "the original reflection launch retains its own network marker");
        return (assertions, contracts.ToArray());
    }
}
