using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections;
using Mono.Cecil;
using Mono.Cecil.Cil;

// The visibility helper runs against original managed types without constructing
// Unity objects. Instruction rewriting below runs the real transpiler on original
// IL projected into Harmony instructions, without executing the resulting IL or
// installing any Unity/Mono patch.
internal static class KeyHintPolicyTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly string[] VisibilityFields =
    [
        "m_combatHints", "m_bowDrawGP", "m_bowDrawKB", "m_primaryAttackGP",
        "m_primaryAttackKB", "m_secondaryAttackGP", "m_secondaryAttackKB"
    ];

    internal static int Run(Assembly game, Assembly mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Key hint policy: " + message);
            assertions++;
        }

        Type runtime = mod.GetType("CaptainValheim.ShieldOnlyKeyHintSystem", true)!;
        Type hintsType = game.GetType("KeyHints", true)!;
        Type stateType = runtime.GetNestedType("ShieldHintState", All)!;
        object owner = RuntimeHelpers.GetUninitializedObject(hintsType);
        object otherOwner = RuntimeHelpers.GetUninitializedObject(hintsType);
        FieldInfo preparedOwner = runtime.GetField("_preparedOwner", All)!;
        FieldInfo preparedState = runtime.GetField("_preparedState", All)!;
        FieldInfo preparedVisible = runtime.GetField("_preparedVisible", All)!;
        MethodInfo filter = runtime.GetMethod("FilterCombatHintVisibility", All)!;
        object? savedOwner = preparedOwner.GetValue(null);
        object? savedState = preparedState.GetValue(null);
        object? savedVisible = preparedVisible.GetValue(null);

        object State(bool mixed) => stateType.GetConstructors(All).Single(c => !c.IsStatic && c.GetParameters().Length == 9).Invoke(
            new object[] { "English", false, mixed, true, !mixed, !mixed, "LMB", "RMB", "MMB" });
        bool Filter(bool vanilla, object hints, bool group) =>
            (bool)filter.Invoke(null, new object[] { vanilla, hints, group })!;

        try
        {
            preparedOwner.SetValue(null, owner);
            preparedState.SetValue(null, State(false));
            preparedVisible.SetValue(null, false);
            foreach (bool group in new[] { false, true })
            foreach (bool vanilla in new[] { false, true })
                Check(Filter(vanilla, owner, group) == vanilla,
                    $"hidden/unprepared policy preserves vanilla={vanilla}, group={group}");

            preparedVisible.SetValue(null, true);
            foreach (bool group in new[] { false, true })
            foreach (bool vanilla in new[] { false, true })
                Check(Filter(vanilla, otherOwner, group) == vanilla,
                    $"another KeyHints owner preserves vanilla={vanilla}, group={group}");

            foreach (bool vanilla in new[] { false, true })
            {
                Check(Filter(vanilla, owner, true), "shield-only keeps combat parent active without toggling");
                Check(!Filter(vanilla, owner, false), "shield-only suppresses native weapon child before SetActive");
            }

            preparedState.SetValue(null, State(true));
            foreach (bool vanilla in new[] { false, true })
            {
                Check(Filter(vanilla, owner, true), "mixed equipment keeps parent visible for the extra chord");
                Check(Filter(vanilla, owner, false) == vanilla, "mixed equipment preserves each vanilla weapon child decision");
            }

            preparedVisible.SetValue(null, false);
            foreach (bool group in new[] { false, true })
            foreach (bool vanilla in new[] { false, true })
                Check(Filter(vanilla, owner, group) == vanilla,
                    $"hiding inventory/menu/dead policy stops the previous override: vanilla={vanilla}, group={group}");
        }
        finally
        {
            preparedOwner.SetValue(null, savedOwner);
            preparedState.SetValue(null, savedState);
            preparedVisible.SetValue(null, savedVisible);
        }

        assertions += CheckRewrite(game, mod);
        return assertions;
    }

    private static int CheckRewrite(Assembly game, Assembly mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Key hint rewrite: " + message);
            assertions++;
        }

        MethodInfo transpiler = mod.GetType("CaptainValheim.KeyHintsUpdateShieldOnlyPatch", true)!
            .GetMethod("Transpiler", All)!;
        Type instructionType = transpiler.GetParameters()[0].ParameterType.GetGenericArguments().Single();
        Type listType = typeof(List<>).MakeGenericType(instructionType);
        FieldInfo opcodeField = instructionType.GetField("opcode")!;
        FieldInfo operandField = instructionType.GetField("operand")!;
        FieldInfo labelsField = instructionType.GetField("labels")!;
        Type hintsType = game.GetType("KeyHints", true)!;
        MethodInfo setActive = hintsType.GetField("m_combatHints")!.FieldType.GetMethod("SetActive", [typeof(bool)])!;
        var opcodes = typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
        using var gameDefinition = AssemblyDefinition.ReadAssembly(game.Location);
        var source = gameDefinition.MainModule.GetType("KeyHints").Methods.Single(m => m.Name == "UpdateHints").Body.Instructions;

        IList Project(bool duplicateParent = false)
        {
            IList instructions = (IList)Activator.CreateInstance(listType)!;
            foreach (Instruction original in source)
            {
                object? operand = original.Operand;
                if (operand is FieldReference field && field.DeclaringType.FullName == "KeyHints")
                    operand = hintsType.GetField(field.Name, All)!;
                else if (IsSetActive(original))
                    operand = setActive;
                instructions.Add(Activator.CreateInstance(instructionType, opcodes[original.OpCode.Value], operand)!);
            }
            if (duplicateParent)
            {
                instructions.Add(Activator.CreateInstance(instructionType, System.Reflection.Emit.OpCodes.Ldarg_0, null)!);
                instructions.Add(Activator.CreateInstance(instructionType, System.Reflection.Emit.OpCodes.Ldfld,
                    hintsType.GetField("m_combatHints"))!);
                instructions.Add(Activator.CreateInstance(instructionType, System.Reflection.Emit.OpCodes.Ldc_I4_0, null)!);
                instructions.Add(Activator.CreateInstance(instructionType, System.Reflection.Emit.OpCodes.Callvirt, setActive)!);
            }
            return instructions;
        }
        object[] Rewrite(IList instructions) =>
            ((IEnumerable)transpiler.Invoke(null, [instructions])!).Cast<object>().ToArray();
        System.Reflection.Emit.OpCode Opcode(object instruction) => (System.Reflection.Emit.OpCode)opcodeField.GetValue(instruction)!;
        IList Labels(object instruction) => (IList)labelsField.GetValue(instruction)!;

        IList projected = Project();
        object[] originalInstructions = projected.Cast<object>().ToArray();
        var sites = new Dictionary<int, (bool Group, System.Reflection.Emit.Label Label)>();
        var generator = new System.Reflection.Emit.DynamicMethod("KeyHintLabelsOnly", typeof(void), Type.EmptyTypes).GetILGenerator();
        for (int index = 3; index < originalInstructions.Length; index++)
        {
            if (Opcode(originalInstructions[index]) != System.Reflection.Emit.OpCodes.Callvirt ||
                !Equals(operandField.GetValue(originalInstructions[index]), setActive) ||
                operandField.GetValue(originalInstructions[index - 2]) is not FieldInfo field ||
                !VisibilityFields.Contains(field.Name)) continue;
            var label = generator.DefineLabel();
            Labels(originalInstructions[index]).Add(label);
            sites.Add(index, (field.Name == "m_combatHints", label));
        }
        Check(sites.Count == 15, "projected original has 15 receiver-specific writes");
        object[] rewritten = Rewrite(projected);
        Check(rewritten.Length == originalInstructions.Length + 45, "exactly three instructions added at each of 15 sites");
        int output = 0;
        for (int index = 0; index < originalInstructions.Length; index++)
        {
            if (sites.TryGetValue(index, out var site))
            {
                Check(Opcode(rewritten[output]) == System.Reflection.Emit.OpCodes.Ldarg_0 &&
                      Opcode(rewritten[output + 1]) == (site.Group ? System.Reflection.Emit.OpCodes.Ldc_I4_1 : System.Reflection.Emit.OpCodes.Ldc_I4_0) &&
                      Opcode(rewritten[output + 2]) == System.Reflection.Emit.OpCodes.Call &&
                      operandField.GetValue(rewritten[output + 2]) is MethodInfo method && method.Name == "FilterCombatHintVisibility",
                    "owner/group/filter stack insertion at original offset " + source[index].Offset);
                Check(Labels(rewritten[output]).Contains(site.Label) && !Labels(originalInstructions[index]).Contains(site.Label),
                    "branch label moved before filter at original offset " + source[index].Offset);
                output += 3;
            }
            if (!ReferenceEquals(rewritten[output++], originalInstructions[index]))
                throw new Exception("Key hint rewrite changed original instruction identity/order at " + index);
        }
        Check(output == rewritten.Length, "all original instructions retained in their original order");
        foreach (var invalid in new[] { (Input: (IList)Activator.CreateInstance(listType)!, Name: "missing sites"), (Input: Project(true), Name: "extra parent write") })
        {
            bool rejected = false;
            try { Rewrite(invalid.Input); }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected, invalid.Name + " is rejected without a partial returned rewrite");
        }
        return assertions;
    }

    internal static int CheckContract(AssemblyDefinition mod, ModuleDefinition game)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Key hint contract: " + message);
            assertions++;
        }

        TypeDefinition hints = game.GetType("KeyHints");
        MethodDefinition original = hints.Methods.Single(m => m.Name == "UpdateHints");
        var originalIl = original.Body.Instructions.Where(i => i.OpCode.Code != Code.Nop).ToArray();
        var counts = VisibilityFields.ToDictionary(name => name, _ => 0);
        for (int index = 0; index < originalIl.Length; index++)
        {
            if (originalIl[index].OpCode.Code != Code.Ldfld ||
                originalIl[index].Operand is not FieldReference field ||
                field.DeclaringType.FullName != "KeyHints" || !counts.ContainsKey(field.Name))
                continue;

            counts[field.Name]++;
            Check(index > 0 && index + 2 < originalIl.Length && originalIl[index - 1].OpCode.Code == Code.Ldarg_0 &&
                  IsBoolLoad(originalIl[index + 1], original) && IsSetActive(originalIl[index + 2]),
                "original field receiver/single bool load/SetActive shape: " + field.Name);
        }
        foreach (var count in counts)
            Check(count.Value == (count.Key == "m_combatHints" ? 9 : 1), "original per-field coverage: " + count.Key);
        Check(counts.Values.Sum() == 15, "all 15 original visibility writes are covered");
        Check(original.Body.ExceptionHandlers.Count == 0, "original target has no exception boundary moved by insertion");

        TypeDefinition runtime = mod.MainModule.GetType("CaptainValheim.ShieldOnlyKeyHintSystem");
        TypeDefinition patch = mod.MainModule.GetType("CaptainValheim.KeyHintsUpdateShieldOnlyPatch");
        MethodDefinition filter = runtime.Methods.Single(m => m.Name == "FilterCombatHintVisibility");
        Check(filter.IsStatic && filter.ReturnType.FullName == "System.Boolean" &&
              filter.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(
                  new[] { "System.Boolean", "KeyHints", "System.Boolean" }),
            "filter consumes bool/owner/group and returns bool, leaving SetActive receiver untouched");
        Check(!Calls(filter).Any(m => m.DeclaringType.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)),
            "prepared visibility filter never invokes native Unity methods");

        MethodDefinition prefix = patch.Methods.Single(m => m.Name == "Prefix");
        Check(prefix.ReturnType.FullName == "System.Void" && !prefix.Parameters.Any(p => p.ParameterType.IsByReference) &&
              Calls(prefix).Any(m => m.Name == "PrepareKeyHintUpdate"),
            "non-skipping prefix prepares this invocation's policy");
        MethodDefinition postfix = patch.Methods.Single(m => m.Name == "Postfix");
        Check(postfix.Parameters.Any(p => p.Name == "__runOriginal" && p.ParameterType.FullName == "System.Boolean") &&
              Calls(postfix).Any(m => m.Name == "UpdateKeyHint" && m.Parameters.Count == 2),
            "postfix receives whether another mod skipped original hints");
        MethodDefinition visible = runtime.Methods.Single(m => m.Name == "ShouldShowCustomCombatHints");
        Check(Calls(visible).Any(m => m.Name == "get_IsAchievementsPanelOpen"),
            "achievement panel visibility is excluded like original UI");
        foreach (string method in new[] { "PrepareKeyHintUpdate", "UpdateKeyHint", "HideVanillaWeaponHints" })
            Check(!runtime.Methods.Single(m => m.Name == method).Body.Instructions.Any(i =>
                    i.Operand is FieldReference field && field.Name == "m_combatHints"),
                method + " does not toggle vanilla combat parent after its decision");

        MethodDefinition transpiler = patch.Methods.Single(m => m.Name == "Transpiler");
        var strings = transpiler.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldstr).Select(i => (string)i.Operand).ToArray();
        Check(VisibilityFields.All(strings.Contains), "transpiler identifies each exact KeyHints field");
        Check(strings.Contains("FilterCombatHintVisibility"), "transpiler resolves the checked filter signature");
        Check(Calls(transpiler).Any(m => m.Name == "MoveLabelsFrom"),
            "existing call labels move to the inserted owner instruction");
        Check(Calls(transpiler).Any(m => m.Name == "IsVisibilityLoad") &&
              Calls(transpiler).Any(m => m.DeclaringType.FullName == "System.InvalidOperationException" && m.Name == ".ctor"),
            "transpiler validates bool-load shape and rejects unexpected coverage");
        foreach (string opcode in new[] { "Ldarg_0", "Ldc_I4_0", "Ldc_I4_1", "Call" })
            Check(transpiler.Body.Instructions.Any(i => i.Operand is FieldReference field &&
                      field.DeclaringType.FullName == "System.Reflection.Emit.OpCodes" && field.Name == opcode),
                "insertion has the owner/group/filter opcode: " + opcode);

        return assertions;
    }

    private static IEnumerable<MethodReference> Calls(MethodDefinition method) =>
        method.Body.Instructions.Where(i => i.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
            .Select(i => i.Operand).OfType<MethodReference>();

    private static bool IsSetActive(Instruction instruction) =>
        instruction.OpCode.Code == Code.Callvirt && instruction.Operand is MethodReference method &&
        method.DeclaringType.FullName == "UnityEngine.GameObject" && method.Name == "SetActive" &&
        method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "System.Boolean";

    private static bool IsBoolLoad(Instruction instruction, MethodDefinition method)
    {
        if (instruction.OpCode.Code is Code.Ldc_I4_0 or Code.Ldc_I4_1) return true;
        int index = instruction.OpCode.Code switch
        {
            Code.Ldloc_0 => 0, Code.Ldloc_1 => 1, Code.Ldloc_2 => 2, Code.Ldloc_3 => 3,
            Code.Ldloc or Code.Ldloc_S when instruction.Operand is VariableDefinition variable => variable.Index,
            _ => -1
        };
        return index >= 0 && index < method.Body.Variables.Count &&
               method.Body.Variables[index].VariableType.FullName == "System.Boolean";
    }
}
