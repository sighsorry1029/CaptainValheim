# CaptainValheim 1.1.1 / Path of Valheiman compatibility

2026-10-18. This patch coordinates overlapping actions; it does not combine the two mods' combat formulas or item-return systems.

## Evidence and scope

- CaptainValheim base: `cc64aac` (1.1.0).
- Supplied Path of Valheiman plugin: 5.0.4, GUID `treextr.pathofvalheiman`, SHA-256 `36E45B65B44667AD596446CAC2AE989041669948C11710B05185E4FE16697DB4`. Its assembly version is 1.0.0.0; the plugin version comes from `BepInPlugin`.
- The supplied original DLL was decompiled without publicizing it. Only the shield input, charge, reflection, relevant node queries and return boundaries were inspected. Embedded assets and unrelated nodes were not audited.
- The build uses original installed Valheim 1.0.20 client assemblies. `assembly_valheim.dll` matches the preserved client build 25909357 snapshot: SHA-256 `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`.
- No PoV DLL is compiled into, modified by, or shipped with CaptainValheim. A BepInEx soft dependency establishes initialization order when PoV is present. There is no exact-version restriction. Startup checks validate individual integration contracts and the registration of PoV's corresponding attack/charge patches; this is not a claim that untested future versions work.

## Action ownership

| Situation | Owner |
| --- | --- |
| PoV Shield Throw qualifies under its own node, equipment, slot-lock and template checks | PoV owns primary attack; CV hides its primary-strike hint |
| PoV Shield Strike qualifies under its own node, equipment and template checks | PoV owns secondary attack; CV hides throw/charge hints |
| PoV Shield Charge node acquired, or its charge still active after refund | PoV owns charge movement; CV does not start a new charge |
| CV charge already active when a node is acquired | CV finishes its charge; PoV `Wants` cannot start overlapping movement, and attack-start protection precedes PoV replacement prefixes |
| PoV parry reflection completes `Projectile.Setup` | PoV owns that reflection; CV does not create a second projectile or charge its reflection stamina adjustment |
| Eligible ordinary block, missing PoV reflection node, or PoV launch returns without completing Setup | Existing CV reflection remains available after a normally completed block |

A shield-only Block + Secondary gesture is consumed while PoV owns charge instead of becoming an accidental CV throw. PoV Shield Strike still owns that secondary input when eligible. With a melee weapon, PoV charge ownership releases CV's charge reservation so the weapon's normal secondary input remains available unless PoV Shield Strike owns it.

The reflection observer replaces exactly one Setup call in PoV's private `ParryReflect.Launch` with a forwarding wrapper. All original arguments and IL labels/exception boundaries are preserved. The block result and actual stamina expenditure are captured before PoV postfixes, then the normal finalizer decides whether CV must reflect. Each nested local block has its own scope, including blocks ineligible for CV reflection; finalizers always remove the scope. Existing RPC sender/owner validation, event correlation, protocol and deduplication are unchanged.

Methods and node values are bound once. Current progress is queried through cached delegates, so acquiring/refunding a node or changing characters does not require reconnecting. There is no per-frame reflection search or `MethodInfo.Invoke`. PoV's own `Spear()` helper still performs a bounded ObjectDB prefab/component lookup when its throw is eligible; no claim of zero lookup cost is made. HUD rows retain the existing state-change cache and are not recreated for unchanged state. Performance has not been measured in game.

## Deliberately preserved policies

- CV damage, push, stamina formulas, saved config/YAML keys and public integration APIs are unchanged.
- PoV block-power bonuses are not forcibly added to CV attacks; existing `SEMan.ModifyAttack` processing remains.
- PoV synthetic ram block events and CV block-charge accumulation are not rebalanced in this patch.
- PoV's inventory-slot locking/Weapon Return and CV's remove-and-return model remain separate. Projectile chain/bounce and two-shield support are not newly enabled.
- Original game/PoV DLL access restrictions are preserved. New private PoV calls use cached delegates or an explicitly installed Harmony observer.

## Validation boundaries

The repository's `tools/CompatibilityChecks` accepts an optional fifth argument containing the original PoV DLL. It checks that binary's method signatures, access levels and relevant IL, in addition to existing CV/game contracts. Managed checks are separate from Unity gameplay and actual Harmony installation.

Debug build and final merged-DLL deployment passed with zero warnings/errors; the output and installed plugin SHA-256 matched. Checks passed against the original client and preserved dedicated-server build 25909431 assemblies. The existing four-argument command also passed without supplying PoV.

- Game member references: 356; Harmony target contracts: 44; existing reflection contracts: 34; errors: 0.
- Existing managed assertions: protocol 36, equipment/input policy 52, return intent 20, key hints 55, projectile appearance 97, push 24, pickup policy 31. Existing static contracts also passed.
- PoV original contracts: 19; integration IL contracts: 19; isolated managed assertions: 16 with the optional DLL, 10 without it. These execute cache teardown and the actual reflection transpiler (including missing/duplicate call rejection and label/exception-block preservation); the optional six assertions bind original private methods/compile the named-node query without calling PoV gameplay.

Attempts to execute player-dependent checks hit `Player`'s Unity native animation initialization; accessing live block scopes also reaches the installed Harmony runtime's initialization, unsupported by this .NET host. Those tests were removed from the managed execution claims and replaced with static checks where possible. Neither original game DLLs nor their access restrictions were changed to bypass this boundary. Node acquisition/refund, nested block execution and patch installation remain actual-game verification items.

Release build also passed with zero warnings/errors. The final merged 1.1.1.0 DLL passed the same checks against the original client (with PoV) and dedicated-server assemblies (without the optional DLL argument). Debug and Release's equivalent reference-comparison IL forms are both checked. Release DLL, installed plugin, and the DLL entries in both packages match SHA-256 `81D4571D548785A75BD3C8BE2A44E76A47DE9AA1015D38B45601297E0B752863`.

- `bin/Release/CaptainValheim.dll`: assembly/file version 1.1.1.0.
- `Thunderstore/CaptainValheim_v1.1.1.zip`: six expected entries; README, changelog, English template, manifest and icon match their sources. Manifest version is 1.1.1 and BepInExPack dependency is 5.4.2351.
- `Nexus/CaptainValheim_v1.1.1.zip`: the same final DLL as its only entry.

```powershell
dotnet build CaptainValheim.csproj -c Debug -p:DeployToGame=true
dotnet build tools/CompatibilityChecks/CompatibilityChecks.csproj -c Debug
dotnet tools/CompatibilityChecks/bin/Debug/net10.0/CompatibilityChecks.dll bin/Debug/CaptainValheim.dll "<original Managed>" "<BepInEx/core>" "<report.json>" "<original PathOfValheiman.dll>"
dotnet build CaptainValheim.csproj -c Release
```

Actual client, host, dedicated-server and crossplay gameplay has not been run for this patch. Before treating gameplay compatibility as verified, test:

1. PoV absent, installed without nodes, each overlapping node separately, node refunds and a character switch. Check primary/secondary inputs and keyboard/gamepad hints with shield only, one-handed weapon plus shield, and two shields.
2. Simultaneous Block + Run + Secondary, cooldown/stamina failure, and node changes during a charge. Confirm one movement controller and no accidental CV throw.
3. Ordinary block and parry, with/without the reflection node, missing projectile template, remote-owned incoming projectile, nested on-block damage and successive hits. Confirm at most one reflected projectile, correct owner and unchanged chosen mod's damage/stamina rules.
4. Each mod's throw separately: normal return, full inventory, death, disconnect, manual equipment change and another player picking up a drop. Count the original shield and returned/dropped copies to verify no loss or duplication.
5. Repeat relevant cases on a host and a dedicated-server client. All participants must update CaptainValheim together because the existing minimum-version check follows 1.1.1.
