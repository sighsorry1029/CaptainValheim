using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;

// Runs actual mod helpers against original managed game types. These synthetic
// managed objects never represent Unity scene objects or prove gameplay execution.
internal static class EquipmentPolicyTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static int Run(Assembly game, Assembly mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Equipment/input: " + message);
            assertions++;
        }
        Type runtime = mod.GetType("CaptainValheim.ShieldRuntimeSystem", true)!;
        Type itemType = game.GetType("ItemDrop+ItemData", true)!;
        Type sharedType = itemType.GetNestedType("SharedData", All)!;
        Type categoryType = itemType.GetNestedType("ItemType", All)!;
        Type attackType = game.GetType("Attack", true)!;
        Type attackKindType = attackType.GetNestedType("AttackType", All)!;
        Type toggle = mod.GetType("CaptainValheim.CaptainValheimPlugin+Toggle", true)!;
        object on = Enum.Parse(toggle, "On"), off = Enum.Parse(toggle, "Off");

        object Attack(string kind, bool bowDraw = false)
        {
            object attack = RuntimeHelpers.GetUninitializedObject(attackType);
            attackType.GetField("m_attackType")!.SetValue(attack, Enum.Parse(attackKindType, kind));
            attackType.GetField("m_bowDraw")!.SetValue(attack, bowDraw);
            return attack;
        }
        object Item(string category, string primary = "Horizontal", bool bowDraw = false, string secondary = "None")
        {
            object item = RuntimeHelpers.GetUninitializedObject(itemType);
            object shared = RuntimeHelpers.GetUninitializedObject(sharedType);
            sharedType.GetField("m_itemType")!.SetValue(shared, Enum.Parse(categoryType, category));
            sharedType.GetField("m_attack")!.SetValue(shared, Attack(primary, bowDraw));
            sharedType.GetField("m_secondaryAttack")!.SetValue(shared, Attack(secondary));
            itemType.GetField("m_shared")!.SetValue(item, shared);
            return item;
        }
        bool Melee(object? item) => (bool)runtime.GetMethod("IsOneHandedMeleeWeapon", All)!.Invoke(null, new[] { item })!;
        bool Allowed(object? left, object? right, object policy) =>
            (bool)runtime.GetMethod("IsShieldEquipmentAllowed", All)!.Invoke(null, new[] { left, right, policy })!;

        object shield = Item("Shield"), sword = Item("OneHandedWeapon");
        Check(!Melee(null), "empty hand is not a melee weapon");
        Check(!Melee(RuntimeHelpers.GetUninitializedObject(itemType)), "missing shared data rejected");
        foreach (object policy in new[] { off, on })
        {
            Check(Allowed(shield, null, policy), "shield-only remains available under both policies");
            Check(!Allowed(null, sword, policy), "a right-hand weapon cannot supply a missing shield");
            Check(!Allowed(sword, null, policy), "non-shield left item rejected");
            Check(!Allowed(null, null, policy), "empty equipment rejected");
        }
        foreach (string kind in new[] { "Horizontal", "Vertical", "Area" })
        {
            object weapon = Item("OneHandedWeapon", kind);
            Check(Melee(weapon), kind + " one-handed melee accepted");
            Check(Allowed(shield, weapon, on), kind + " allowed when enabled");
            Check(!Allowed(shield, weapon, off), kind + " blocked when disabled");
        }
        // A spear's secondary throws a projectile; its PRIMARY still makes it melee.
        object spear = Item("OneHandedWeapon", "Horizontal", secondary: "Projectile");
        Check(Melee(spear) && Allowed(shield, spear, on), "spear with projectile secondary is supported");
        foreach (string category in new[] { "Torch", "Bow", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Tool", "Shield", "None" })
        {
            object item = Item(category);
            Check(!Melee(item), category + " cannot qualify from a melee-looking attack alone");
            Check(!Allowed(shield, item, on), category + " denied even when policy is enabled");
        }
        foreach (string kind in new[] { "Projectile", "TriggerProjectile", "None" })
        {
            object item = Item("OneHandedWeapon", kind);
            Check(!Melee(item) && !Allowed(shield, item, on), "one-handed " + kind + " primary rejected");
        }
        Check(!Melee(Item("OneHandedWeapon", bowDraw: true)), "bow draw cannot qualify from a melee attack tag");
        object missingAttack = Item("OneHandedWeapon");
        sharedType.GetField("m_attack")!.SetValue(itemType.GetField("m_shared")!.GetValue(missingAttack), null);
        Check(!Melee(missingAttack), "missing primary attack rejected");
        Check(!Allowed(shield, sword, Enum.ToObject(toggle, 2)), "unknown policy fails closed");

        Type inputType = runtime.GetNestedType("WeaponChargeInputState", All)!;
        object input = Activator.CreateInstance(inputType, true)!;
        void Observe(bool held, bool wantsCharge) => inputType.GetMethod("Observe", All)!.Invoke(input, new object[] { held, wantsCharge });
        bool State(string name) => (bool)inputType.GetProperty(name, All)!.GetValue(input)!;
        bool Attempt() => (bool)inputType.GetMethod("TryBeginAttempt", All)!.Invoke(input, null)!;
        Check(!State("Held") && !State("Claimed") && !Attempt(), "idle cannot start an attempt");
        Observe(true, false);
        Check(State("Held") && !State("Claimed") && !Attempt(), "ordinary secondary remains weapon intent");
        Observe(true, true);
        Check(!State("Claimed") && !Attempt(), "block/equipment/policy changes cannot steal a held weapon input");
        Observe(false, true);
        Check(!State("Held") && !State("Claimed") && !Attempt(), "release clears intent regardless of selection");
        Observe(true, true);
        Check(State("Held") && State("Claimed"), "fresh blocked secondary selects charge");
        Observe(true, false);
        Check(State("Claimed"), "removing block/equipment/policy cannot fall back to weapon while held");
        Check(Attempt(), "first charge attempt allowed");
        Check(!Attempt(), "failed or successful attempt cannot repeat during same press");
        Observe(true, true);
        Check(State("Claimed") && !Attempt(), "re-enabling selection does not reset attempted state");
        Observe(false, false);
        Check(!State("Held") && !State("Claimed") && !Attempt(), "release clears completed attempt");
        Observe(true, true);
        Check(Attempt() && !Attempt(), "re-press enables exactly one new attempt");
        Observe(false, false);
        Observe(true, false);
        Check(!State("Claimed") && !Attempt(), "new ordinary input remains ordinary after a charge");
        return assertions;
    }

    internal static int CheckConfiguration(AssemblyDefinition mod)
    {
        var settings = mod.MainModule.GetType("CaptainValheim.CaptainValheimPlugin/WeaponCompatibilitySettings");
        var bind = settings.Methods.Single(m => m.Name == "Bind");
        var il = bind.Body.Instructions.Where(i => i.OpCode.Code != Mono.Cecil.Cil.Code.Nop).ToArray();
        var expected = new[] {
            (Field: "AllowCharge", Key: "Allow Shield Charge With One-Handed Weapon", Default: 0),
            (Field: "AllowBlockCharge", Key: "Allow Block Charge With One-Handed Weapon", Default: 1),
            (Field: "AllowReflection", Key: "Allow Projectile Reflection With One-Handed Weapon", Default: 1)
        };
        int assertions = 0;
        foreach (var contract in expected)
        {
            int store = Array.FindIndex(il, i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld &&
                i.Operand is FieldReference field && field.Name == contract.Field);
            if (store < 6 || il[store - 1].Operand is not MethodReference config || config.Name != "config" ||
                !Equals(il[store - 6].Operand, "2 - Shield Actions With One-Handed Weapon") ||
                !Equals(il[store - 5].Operand, contract.Key) || Integer(il[store - 4]) != contract.Default ||
                Integer(il[store - 2]) != 1)
                throw new Exception("Synchronized configuration binding/default changed: " + contract.Field);
            assertions++;
        }
        return assertions;

        static int? Integer(Mono.Cecil.Cil.Instruction i) => i.OpCode.Code switch {
            Mono.Cecil.Cil.Code.Ldc_I4_0 => 0,
            Mono.Cecil.Cil.Code.Ldc_I4_1 => 1,
            Mono.Cecil.Cil.Code.Ldc_I4 => (int)i.Operand,
            Mono.Cecil.Cil.Code.Ldc_I4_S => (sbyte)i.Operand,
            _ => null
        };
    }

    internal static int CheckTranspilerContract(ModuleDefinition game, AssemblyDefinition mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("BlockAttack transpiler: " + message);
            assertions++;
        }
        var gate = mod.MainModule.GetType("CaptainValheim.SecondaryAttackManager").Methods.Single(m => m.Name == "ShouldBuildShieldBlockCharges");
        var rewrite = mod.MainModule.GetType("CaptainValheim.HumanoidBlockAttackPatch").Methods.Single(m => m.Name == "Transpiler");
        var field = game.GetType("ItemDrop/ItemData/SharedData").Fields.Single(f => f.Name == "m_buildBlockCharges");
        var body = game.GetType("Humanoid").Methods.Single(m => m.Name == "BlockAttack").Body;
        var reads = body.Instructions.Where(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldfld &&
            i.Operand is FieldReference f && f.FullName == field.FullName).ToArray();
        Check(reads.Length == 1, "original DLL has exactly one block-charge condition");
        Check(field.FieldType.FullName == "System.Boolean" && !field.IsStatic, "condition is an instance boolean");
        Check(reads[0].Next.OpCode.FlowControl == Mono.Cecil.Cil.FlowControl.Cond_Branch,
            "original field value controls a branch");
        Check(gate.IsStatic && gate.ReturnType.FullName == "System.Boolean" &&
            gate.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "System.Boolean", "Humanoid" }),
            "gate preserves stack contract (enabled, humanoid) -> bool");
        var il = rewrite.Body.Instructions;
        Check(il.Any(i => Equals(i.Operand, "m_buildBlockCharges")) && il.Any(i => Equals(i.Operand, gate.Name)),
            "transpiler resolves expected field and policy method");
        Check(il.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "HarmonyLib.CodeInstructionExtensions" && m.Name == "LoadsField"),
            "transpiler matches a field load");
        Check(il.Count(i => i.Operand is FieldReference f && f.DeclaringType.FullName == "System.Reflection.Emit.OpCodes" && f.Name == "Ldarg_0") == 1 &&
            il.Count(i => i.Operand is FieldReference f && f.DeclaringType.FullName == "System.Reflection.Emit.OpCodes" && f.Name == "Call") == 1,
            "transpiler constructs one owner-load and one policy-call instruction");
        Check(il.Count(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Newobj && i.Operand is MethodReference m && m.DeclaringType.FullName == "HarmonyLib.CodeInstruction") == 2,
            "transpiler creates exactly two instruction templates");
        Check(il.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Newobj && i.Operand is MethodReference m && m.DeclaringType.FullName == "System.InvalidOperationException"),
            "unexpected match-count has an explicit failure path");
        // Installed BepInEx Harmony AccessTools cannot initialize under this .NET 10
        // test host (MethodInvoker.GetHandler throws). Do not present static contracts
        // as executing the transpiler, installing patches, or proving Mono/Unity IL.
        return assertions;
    }
}
