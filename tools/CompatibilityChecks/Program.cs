using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

// Static contract check against ORIGINAL target DLLs. Does not load Unity or install patches.
if (args.Length != 4)
    throw new ArgumentException("Usage: <merged mod.dll> <original Managed directory> <BepInEx/core directory> <report.json>");
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetFullPath(args[1]));
resolver.AddSearchDirectory(Path.GetFullPath(args[2]));
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
using var mod = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });
var errors = new List<string>();
var members = new SortedSet<string>();
var patches = new List<string>();
var reflected = new List<string>();
var gameNames = new HashSet<string> { "assembly_valheim", "assembly_utils", "assembly_guiutils" };
bool IsGame(TypeReference t) => t.GetElementType().Scope is AssemblyNameReference a && gameNames.Contains(a.Name);
foreach (var reference in mod.MainModule.GetMemberReferences().Where(r => IsGame(r.DeclaringType)))
{
    try
    {
        IMemberDefinition? resolved = reference switch
        {
            MethodReference m => m.Resolve(),
            FieldReference f => f.Resolve(),
            _ => null
        };
        if (resolved == null) errors.Add("Missing member: " + reference.FullName);
        else members.Add(reference.FullName);
    }
    catch (Exception e) { errors.Add($"Cannot resolve {reference.FullName}: {e.Message}"); }
}
foreach (var method in mod.MainModule.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody))
foreach (var il in method.Body.Instructions)
{
    if (il.Operand is FieldReference f && IsGame(f.DeclaringType) && f.Resolve() is { IsLiteral: true })
        errors.Add($"Literal field instruction in {method.FullName}: {il}");
}

void CheckPatch(CustomAttribute attr, IEnumerable<MethodDefinition> hooks, string owner)
{
    if (attr.ConstructorArguments.Count == 0) return; // Marker class; method attributes handled below.
    var type = attr.ConstructorArguments.Select(a => a.Value).OfType<TypeReference>().SingleOrDefault();
    var name = attr.ConstructorArguments.Select(a => a.Value).OfType<string>().SingleOrDefault();
    if (type == null || name == null) { errors.Add("Unsupported patch specification: " + owner); return; }
    var explicitArgs = attr.ConstructorArguments.Where(a => a.Type.FullName == "System.Type[]")
        .Select(a => ((CustomAttributeArgument[])a.Value).Select(v => ((TypeReference)v.Value).FullName).ToArray()).SingleOrDefault();
    var targets = type.Resolve().Methods.Where(m => m.Name == name &&
        (explicitArgs == null || m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(explicitArgs))).ToArray();
    if (targets.Length != 1) { errors.Add($"Patch {owner}: expected one {type.FullName}.{name}, found {targets.Length}"); return; }
    var target = targets[0];
    patches.Add(owner + " -> " + target.FullName);
    // A transpiler receives Harmony's instruction/generator/method inputs, not
    // arguments from the patched gameplay method. Keep normal hook validation.
    foreach (var hook in hooks.Where(h => h.Name != "Transpiler" &&
        !h.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyTranspiler")))
    foreach (var p in hook.Parameters)
    {
        string actual = p.ParameterType.GetElementType().FullName;
        if (p.Name.StartsWith("___"))
        {
            var field = target.DeclaringType.Fields.SingleOrDefault(f => f.Name == p.Name[3..]);
            if (field == null || actual != field.FieldType.FullName) errors.Add($"Invalid field injection: {hook.FullName} {p.Name}");
        }
        else if (p.Name == "__instance")
        {
            if (target.IsStatic || actual != target.DeclaringType.FullName) errors.Add($"Invalid instance: {hook.FullName}");
        }
        else if (p.Name == "__result")
        {
            if (actual != target.ReturnType.FullName) errors.Add($"Invalid result: {hook.FullName}");
        }
        else if (!p.Name.StartsWith("__"))
        {
            var original = target.Parameters.SingleOrDefault(a => a.Name == p.Name);
            if (original == null || actual != original.ParameterType.GetElementType().FullName)
                errors.Add($"Invalid argument: {hook.FullName} {p.Name}");
        }
    }
}
foreach (var type in mod.MainModule.GetTypes())
{
    bool IsHook(MethodDefinition m) => new[] { "Prefix", "Postfix", "Finalizer", "Transpiler" }.Contains(m.Name) ||
        m.CustomAttributes.Any(a => new[] { "HarmonyPrefix", "HarmonyPostfix", "HarmonyFinalizer", "HarmonyTranspiler" }.Contains(a.AttributeType.Name));
    foreach (var attr in type.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
        CheckPatch(attr, type.Methods.Where(IsHook), type.FullName);
    foreach (var method in type.Methods)
    foreach (var attr in method.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
        CheckPatch(attr, new[] { method }, method.FullName);
}

// Explicit reflection contracts, including the vendored ServerSync handshake/admin paths.
var game = resolver.Resolve(new AssemblyNameReference("assembly_valheim", new Version(0, 0, 0, 0))).MainModule;
var gui = resolver.Resolve(new AssemblyNameReference("assembly_guiutils", new Version(0, 0, 0, 0))).MainModule;
foreach (var (typeName, name, fieldType) in new (string, string, string)[] {
    ("Attack", "m_character", "Humanoid"), ("Attack", "m_weapon", "ItemDrop/ItemData"), ("Attack", "m_baseAI", "BaseAI"),
    ("Humanoid", "m_currentAttack", "Attack"), ("KeyHints", "m_keyHintsEnabled", "System.Boolean"),
    ("Humanoid", "m_blockCharges", "System.Int32"), ("Humanoid", "m_blockChargeRemoveTimer", "System.Single"),
    ("Character", "m_secondaryAttack", "System.Boolean"), ("Character", "m_secondaryAttackHold", "System.Boolean"),
    ("Character", "m_blocking", "System.Boolean"), ("Player", "m_queuedSecondAttackTimer", "System.Single"),
    ("TextsDialog", "m_texts", "System.Collections.Generic.List`1<TextsDialog/TextInfo>"),
    ("Projectile", "m_changedVisual", "System.Boolean"), ("Projectile", "m_ammo", "ItemDrop/ItemData"),
    ("Projectile", "m_originalHitData", "HitData"), ("Projectile", "m_statusEffectHash", "System.Int32"),
    ("Projectile", "m_vel", "UnityEngine.Vector3"), ("ZNet", "m_adminList", "SyncedList"),
    ("ZNet", "m_connectionStatus", "ZNet/ConnectionStatus"), ("ZRpc", "m_socket", "ISocket"),
    ("ZNetPeer", "m_socket", "ISocket"), ("ZRoutedRpc", "m_peers", "System.Collections.Generic.List`1<ZNetPeer>"),
    ("ZRpc", "m_functions", "System.Collections.Generic.Dictionary`2<System.Int32,ZRpc/RpcMethodBase>"),
    ("ZRpc/RpcMethod`1", "m_action", "System.Action`2<ZRpc,T>") })
{
    var field = game.GetType(typeName)?.Fields.SingleOrDefault(f => f.Name == name);
    if (field == null || field.FieldType.FullName != fieldType) errors.Add($"Reflection field mismatch: {typeName}.{name}");
    else reflected.Add(field.FullName + " [" + field.Attributes + "]");
}
foreach (var (module, typeName, name, signature) in new (ModuleDefinition, string, string, string)[] {
    (game, "Attack", "GetAttackStamina", "System.Single()"),
    (game, "Attack", "GetProjectileSpawnPoint", "System.Void(UnityEngine.Vector3&,UnityEngine.Vector3&)"),
    (game, "Character", "FindWeakSpotIndex", "System.Int16(UnityEngine.Collider)"),
    (game, "Projectile", "UpdateVisual", "System.Void()"),
    (game, "IEquipmentVisual", "Setup", "System.Void(System.Int32)"),
    (gui, "Localization", "AddWord", "System.Void(System.String,System.String)"),
    (game, "ZNet", "ListContainsId", "System.Boolean(SyncedList,System.String)"),
    (game, "ZNet", "GetPeer", "ZNetPeer(ZRpc)") })
{
    var method = module.GetType(typeName)?.Methods.SingleOrDefault(m => m.Name == name &&
        m.ReturnType.FullName + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")" == signature);
    if (method == null) errors.Add($"Reflection method mismatch: {typeName}.{name} {signature}");
    else reflected.Add(method.FullName + " [" + method.Attributes + "]");
}
int protocolAssertions = 0;
int equipmentPolicyAssertions = 0;
int transpilerContractAssertions = 0;
int configurationAssertions = 0;
if (errors.Count == 0)
{
    try
    {
        configurationAssertions = EquipmentPolicyTests.CheckConfiguration(mod);
        transpilerContractAssertions = EquipmentPolicyTests.CheckTranspilerContract(game, mod);
        (protocolAssertions, equipmentPolicyAssertions) = ManagedProtocolTests.Run(args[0], args[1], args[2]);
    }
    catch (Exception e) { errors.Add("Isolated managed/contract test failed: " + e); }
}
File.WriteAllText(args[3], JsonSerializer.Serialize(new { mod = Path.GetFullPath(args[0]), managed = Path.GetFullPath(args[1]), members, patches, reflected, protocolAssertions, equipmentPolicyAssertions, transpilerContractAssertions, configurationAssertions, errors }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Game references: {members.Count}; Harmony targets: {patches.Count}; reflection contracts: {reflected.Count}; errors: {errors.Count}");
Console.WriteLine($"Managed protocol assertions: {protocolAssertions} (no Unity/Harmony/network execution)");
Console.WriteLine($"Equipment/input assertions: {equipmentPolicyAssertions}; static transpiler contracts: {transpilerContractAssertions}; synchronized config contracts: {configurationAssertions} (no transpiler/patch installation or Unity gameplay execution)");
foreach (var error in errors) Console.WriteLine(error);
return errors.Count == 0 ? 0 : 1;
