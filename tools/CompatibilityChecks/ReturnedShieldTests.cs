using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;

// Exercise the actual pending-intent state with original managed ItemData objects;
// these checks do not simulate Unity, install Harmony, or bypass other mods' policies.
internal static class ReturnedShieldTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static int Run(Assembly game, Assembly mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Returned shield: " + message);
            assertions++;
        }

        Type runtime = mod.GetType("CaptainValheim.ShieldRuntimeSystem", true)!;
        Type stateType = runtime.GetNestedType("ReturnedShieldEquipState", All)!;
        Type itemType = game.GetType("ItemDrop+ItemData", true)!;
        object shield = RuntimeHelpers.GetUninitializedObject(itemType);
        object chosenItem = RuntimeHelpers.GetUninitializedObject(itemType);
        object State(bool returned) => Activator.CreateInstance(stateType, All, null, new object[] { shield, returned, 10 }, null)!;
        string Decide(object state, bool owner = true, bool present = true, object? left = null,
            object? right = null, object? hiddenLeft = null, object? hiddenRight = null, bool blocked = false, int frame = 11) =>
            stateType.GetMethod("Decide", All)!.Invoke(state,
                new object?[] { owner, present, left, right, hiddenLeft, hiddenRight, blocked, frame })!.ToString()!;

        object flight = State(false);
        Check(Decide(flight, present: false, frame: 1000) == "Wait", "in-flight item need not be in inventory");
        Check(Decide(flight, right: chosenItem) == "Cancel", "hand choice during flight cancels return intent");
        stateType.GetMethod("MarkReturned", All)!.Invoke(flight, new object[] { 1000 });
        Check(Decide(flight, frame: 1000) == "Wait", "return schedules after current frame");
        Check(Decide(flight, frame: 1001) == "Attempt", "direct catch becomes eligible next frame");

        object pickup = State(true);
        Check(Decide(pickup, frame: 10) == "Wait", "pickup also defers until next frame");
        Check(Decide(pickup) == "Attempt", "eligible pickup uses same intent decision");
        foreach (int frame in new[] { 11, 120, 3600, 36000 })
            Check(Decide(pickup, blocked: true, frame: frame) == "Wait", "transient restriction has no wall-time expiry at frame " + frame);
        Check(Decide(pickup, frame: 36001) == "Attempt", "one normal attempt after long attack/dodge/swim restriction ends");
        Check(Decide(pickup, owner: false, blocked: true) == "Cancel", "death/disconnect/nonlocal authority cancels even while blocked");
        Check(Decide(pickup, present: false, blocked: true) == "Cancel", "removed or transferred inventory reference cancels");
        Check(Decide(pickup, left: chosenItem) == "Cancel", "new left hand is preserved");
        Check(Decide(pickup, right: chosenItem) == "Cancel", "new right hand is preserved");
        Check(Decide(pickup, hiddenLeft: chosenItem) == "Cancel", "sheathed left hand is preserved");
        Check(Decide(pickup, hiddenRight: chosenItem) == "Cancel", "sheathed right hand is preserved");
        Check(Decide(pickup, left: shield) == "Equipped", "actual left-hand reference is success even without equipped flag");
        itemType.GetField("m_equipped")!.SetValue(shield, true);
        Check(Decide(pickup) == "Cancel", "custom-slot equipped flag is respected but not reported as hand success");
        Check(Decide(pickup, left: shield) == "Equipped", "actual equipped shield is success");
        return assertions;
    }

    internal static int CheckContract(AssemblyDefinition mod)
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Returned shield contract: " + message);
            assertions++;
        }
        TypeDefinition runtime = mod.MainModule.GetType("CaptainValheim.ShieldRuntimeSystem");
        MethodDefinition update = runtime.Methods.Single(m => m.Name == "UpdateReturnedShieldAutoEquip");
        var calls = update.Body.Instructions.Where(i => i.Operand is MethodReference).ToArray();
        int remove = Array.FindIndex(calls, i => ((MethodReference)i.Operand).Name == "Remove");
        int equip = Array.FindIndex(calls, i => ((MethodReference)i.Operand).Name == "EquipItem");
        Check(remove >= 0 && equip > remove, "intent removed before patched EquipItem, so policy veto is final");
        Check(calls.Count(i => ((MethodReference)i.Operand).Name == "EquipItem") == 1, "only one normal equip call");
        Check(!calls.Any(i => ((MethodReference)i.Operand).Name is "AddItem" or "DropItem"), "equip processing cannot add/drop another item");

        TypeDefinition pickup = mod.MainModule.GetType("CaptainValheim.HumanoidPickupThrownShieldPatch");
        var pickupCalls = pickup.Methods.Single(m => m.Name == "Postfix").Body.Instructions
            .Select(i => i.Operand).OfType<MethodReference>().ToArray();
        Check(pickupCalls.Any(m => m.Name == "QueuePickedUpShieldAutoEquip") && !pickupCalls.Any(m => m.Name == "EquipItem"),
            "marked pickup queues same delayed policy, not a one-shot inline equip");

        foreach (string patch in new[] { "HumanoidUseItemCancelReturnedShieldPatch", "HumanoidEquipItemCancelReturnedShieldPatch" })
        {
            TypeDefinition type = mod.MainModule.GetType("CaptainValheim." + patch);
            Check(type.Methods.Single(m => m.Name == "Prefix").Body.Instructions.Select(i => i.Operand)
                .OfType<MethodReference>().Any(m => m.Name == "CancelReturnedShieldOnHandChoice"),
                patch + " cancels explicit hand intent");
        }
        return assertions;
    }
}
