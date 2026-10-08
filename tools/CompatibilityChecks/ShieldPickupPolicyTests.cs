using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Actual mod helpers and original managed ItemData/ZDO storage; no Unity scene,
// Harmony installation, ownership transfer, or end-to-end pickup simulation.
internal static class ShieldPickupPolicyTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static int Run(Assembly game, Assembly mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Shield pickup policy: " + message);
            assertions++;
        }
        Type policy = mod.GetType("CaptainValheim.ShieldPickupPolicy", true)!;
        object? Call(string name, params object?[] values) => policy.GetMethod(name, All)!.Invoke(null, values);
        Type itemType = game.GetType("ItemDrop+ItemData", true)!;
        Type sharedType = itemType.GetNestedType("SharedData", All)!;
        Type category = itemType.GetNestedType("ItemType", All)!;
        object Item(string kind)
        {
            object item = RuntimeHelpers.GetUninitializedObject(itemType);
            object shared = RuntimeHelpers.GetUninitializedObject(sharedType);
            sharedType.GetField("m_itemType")!.SetValue(shared, Enum.Parse(category, kind));
            itemType.GetField("m_shared")!.SetValue(item, shared);
            return item;
        }
        object shield = Item("Shield"), weapon = Item("OneHandedWeapon");
        bool Hands(object? left, object? right = null, object? hiddenLeft = null, object? hiddenRight = null) =>
            (bool)Call("HasShieldOnlyHands", left, right, hiddenLeft, hiddenRight)!;
        Check(Hands(shield), "equipped shield with otherwise empty hands is protected");
        Check(!Hands(null) && !Hands(weapon), "empty or non-shield left hand is not shield-only");
        Check(!Hands(shield, weapon), "an active right-hand choice is respected");
        Check(!Hands(shield, hiddenLeft: shield) && !Hands(shield, hiddenRight: weapon), "sheathed hand choices are respected");
        Check(!Hands(RuntimeHelpers.GetUninitializedObject(itemType)), "missing shared item data is not a shield");
        bool Allow(bool requested, bool protect, long player, long thrower) =>
            (bool)Call("ShouldAllowAutoEquip", requested, protect, player, thrower)!;
        foreach (var (requested, protect, player, thrower, expected) in new (bool, bool, long, long, bool)[] {
            (true, true, 11, 0, false), (true, true, 11, 11, true), (true, true, 11, 22, false),
            (true, true, 0, 0, false), (true, true, 0, 11, false), (true, false, 11, 0, true),
            (false, true, 11, 11, false), (false, false, 11, 11, false) })
            Check(Allow(requested, protect, player, thrower) == expected,
                $"requested={requested}, protected={protect}, player={player}, thrower={thrower}");
        Check(!Allow(true, true, 11, 0), "a newly found spear has no recovery exception without its own thrower marker");

        object player1 = RuntimeHelpers.GetUninitializedObject(game.GetType("Player", true)!);
        object player2 = RuntimeHelpers.GetUninitializedObject(game.GetType("Player", true)!);
        bool Automatic(object player) => (bool)Call("IsAutomaticPickup", player)!;
        void End(string method, object?[] state) => policy.GetMethod(method, All)!.Invoke(null, state);
        Check(!Automatic(player1), "manual/outside pickup does not enter the automatic scope");
        var outer = new[] { Call("BeginAutoPickup", player1) };
        var inner = new[] { Call("BeginAutoPickup", player2) };
        try
        {
            Check(Automatic(player2) && !Automatic(player1), "nested pickup belongs only to its active player");
            End("EndAutoPickup", inner);
            Check(Automatic(player1) && !Automatic(player2), "inner cleanup restores the outer player");
            End("EndAutoPickup", inner);
            Check(Automatic(player1), "repeated/default cleanup cannot erase the outer scope");
        }
        finally { End("EndAutoPickup", inner); End("EndAutoPickup", outer); }
        Check(!Automatic(player1) && !Automatic(player2), "outer cleanup leaves manual pickup unaffected");
        foreach (var (patchName, scopeName) in new[] {
            ("PlayerAutoPickupShieldScopePatch", "AutoPickupScope"), ("ProjectileSpawnRecoveredWeaponScopePatch", "RecoverableDropScope") })
        {
            var failure = new InvalidOperationException("original pickup/drop failure");
            object?[] args = { failure, Activator.CreateInstance(policy.GetNestedType(scopeName, All)!) };
            object? result = mod.GetType("CaptainValheim." + patchName, true)!.GetMethod("Finalizer", All)!.Invoke(null, args);
            Check(ReferenceEquals(result, failure), "finalizer preserves the original exception when its prefix did not enter: " + patchName);
        }

        Type dropScope = policy.GetNestedType("RecoverableDropScope", All)!;
        FieldInfo currentDrop = policy.GetField("RecoveredDrop", All)!;
        object originalDrop = currentDrop.GetValue(null)!;
        object Scope(object? item, long owner) => Activator.CreateInstance(dropScope, All, null, new object?[] { item, owner }, null)!;
        long Current(object? item) => (long)Call("GetCurrentRecoveredThrower", item)!;
        try
        {
            currentDrop.SetValue(null, Scope(weapon, 11));
            Check(Current(weapon) == 11 && Current(Item("OneHandedWeapon")) == 0 && Current(null) == 0,
                "only the exact source item receives a recovery marker, not another identical weapon");
            var previous = new object?[] { Scope(weapon, 11) };
            currentDrop.SetValue(null, Scope(shield, 22));
            Check(Current(shield) == 22 && Current(weapon) == 0, "nested drop cannot inherit another item's thrower");
            End("EndRecoveredDrop", previous);
            Check(Current(weapon) == 11 && Current(shield) == 0, "drop cleanup restores the previous source and thrower");
            End("EndRecoveredDrop", previous);
            Check(Current(weapon) == 11, "default drop cleanup is harmless after a skipped or already-cleaned scope");
        }
        finally { currentDrop.SetValue(null, originalDrop); }

        Type zdoType = game.GetType("ZDO", true)!, idType = game.GetType("ZDOID", true)!;
        Type storage = game.GetType("ZDOExtraData", true)!;
        object zdo = RuntimeHelpers.GetUninitializedObject(zdoType);
        object id = Activator.CreateInstance(idType, new object[] { 99881234L, 5678u })!;
        zdoType.GetField("m_uid")!.SetValue(zdo, id);
        MethodInfo hash = Assembly.Load("assembly_utils").GetType("StringExtensionMethods", true)!.GetMethod("GetStableHashCode", new[] { typeof(string) })!;
        string[] keys = { "CaptainValheim.RecoveredWeaponThrowerID", "FearNoSpear.ThrowerPlayerID", "SecondaryAttacks.ThrowerPlayerID" };
        int[] hashes = keys.Select(k => (int)hash.Invoke(null, new object[] { k })!).ToArray();
        MethodInfo set = storage.GetMethod("Set", new[] { idType, typeof(int), typeof(long) })!;
        long Read() => (long)Call("ReadRecoveredThrower", zdo)!;
        void Set(int index, long value) => set.Invoke(null, new object[] { id, hashes[index], value });
        try
        {
            Check((long)Call("ReadRecoveredThrower", new object?[] { null })! == 0 && Read() == 0, "missing ZDO/metadata has no recovery exception");
            Set(2, 33); Check(Read() == 33, "SecondaryAttacks dropped-item marker is understood");
            Set(1, 22); Check(Read() == 22, "FearNoSpear marker precedes SecondaryAttacks");
            Set(0, 11); Check(Read() == 11, "CaptainValheim's own marker has first priority");
            Set(0, 0); Check(Read() == 22, "zero own marker permits optional-mod fallback");
            Set(1, 0); Check(Read() == 33, "zero FearNoSpear marker permits SecondaryAttacks fallback");
        }
        finally { foreach (int h in hashes) storage.GetMethod("RemoveLong", new[] { idType, typeof(int) })!.Invoke(null, new object[] { id, h }); }
        return assertions;
    }

    internal static int CheckContract(AssemblyDefinition mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Shield pickup contract: " + message);
            assertions++;
        }
        TypeDefinition policy = mod.MainModule.GetType("CaptainValheim.ShieldPickupPolicy");
        MethodReference[] Calls(MethodDefinition m) => m.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        foreach (string field in new[] { "AutoPickupPlayer", "RecoveredDrop" })
            Check(policy.Fields.Single(f => f.Name == field).CustomAttributes.Any(a => a.AttributeType.FullName == "System.ThreadStaticAttribute"), field + " is isolated to its synchronous thread");
        foreach (var (begin, end) in new[] { ("BeginAutoPickup", "EndAutoPickup"), ("BeginRecoveredDrop", "EndRecoveredDrop") })
        {
            TypeDefinition patch = mod.MainModule.GetTypes().Single(t => t.Methods.Any(m => m.HasBody && Calls(m).Any(c => c.Name == begin && c.DeclaringType.FullName == policy.FullName)));
            Check(patch.Methods.Any(m => m.Name == "Finalizer" && Calls(m).Any(c => c.Name == end)), begin + " cleans up on both success and exceptions");
            Check(!patch.Methods.Any(m => m.Name == "Transpiler"), begin + " composes without replacing the game's drop/pickup calls");
        }
        MethodDefinition mark = policy.Methods.Single(m => m.Name == "MarkRecoveredDrop");
        Check(Calls(mark).Any(m => m.Name == "IsOwner") && Calls(mark).Any(m => m.Name == "Set" && m.DeclaringType.Name == "ZDO") && mark.Body.ExceptionHandlers.Count > 0,
            "drop annotation requires ownership and isolates metadata failures");
        Check(policy.Methods.Where(m => m.HasBody).All(m => !Calls(m).Any(c => c.Name is "AddItem" or "RemoveItem" or "DropItem" or "EquipItem" or "UnequipItem") &&
            !m.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_customData")), "policy cannot transfer/equip items or persist markers in inventory customData");
        MethodDefinition filter = policy.Methods.Single(m => m.Name == "FilterAutoEquip");
        Check(Calls(filter).Any(m => m.Name == "IsAutomaticPickup") && !filter.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name is "m_skillType" or "m_pickedUp"),
            "filter is scoped to auto pickup and has no spear-category or previously-picked-up shortcut");
        Check(Calls(filter).Any(m => m.Name == "HasPendingShieldReturn"), "pending shield return protects otherwise empty hands");
        MethodDefinition pickup = mod.MainModule.GetType("CaptainValheim.HumanoidPickupThrownShieldPatch").Methods.Single(m => m.Name == "Prefix");
        Check(pickup.ReturnType.FullName == "System.Void" && pickup.Parameters.Any(p => p.Name == "autoequip" && p.ParameterType.FullName == "System.Boolean&") &&
            !pickup.Parameters.Any(p => p.Name == "__result") && Calls(pickup).Count(m => m.Name == "FilterAutoEquip") == 1,
            "pickup prefix changes only the autoequip request and lets original inventory/result logic run");
        TypeDefinition tagPatch = mod.MainModule.GetType("CaptainValheim.ItemDropRecoveredWeaponTagPatch");
        Check(tagPatch.Methods.Single(m => m.Name == "Postfix").Parameters.Any(p => p.Name == "__result" && p.ParameterType.FullName == "ItemDrop") &&
            Calls(tagPatch.Methods.Single(m => m.Name == "Postfix")).Any(m => m.Name == "MarkRecoveredDrop"),
            "drop annotation observes the actual returned ItemDrop after creation");
        MethodDefinition bind = mod.MainModule.GetType("CaptainValheim.CaptainValheimPlugin/GeneralSettings").Methods.Single(m => m.Name == "Bind");
        Instruction[] il = bind.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
        int store = Array.FindIndex(il, i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "PreventWeaponAutoEquip");
        Check(store >= 6 && Equals(il[store - 5].Operand, "Prevent Weapon Auto Equip While Shield Only") && il[store - 4].OpCode == OpCodes.Ldc_I4_1 && il[store - 2].OpCode == OpCodes.Ldc_I4_0,
            "protection defaults to On and is client-only, not synchronized");
        return assertions;
    }
}
