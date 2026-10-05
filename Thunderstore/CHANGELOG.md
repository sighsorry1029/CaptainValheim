# Changelog

## 1.0.12

- Reduced shield key-hint UI work by preventing the game's combat hints and CaptainValheim's hints from repeatedly toggling each other every frame. Preserved normal weapon hints when using a shield with a melee weapon and cleaned up owned hints when the HUD is destroyed.
- Improved automatic re-equipping after direct shield returns and marked ground pickups. Re-equipping now waits for temporary attack, dodge, or swimming restrictions to end instead of expiring after one second.
- A new hand equipment choice cancels pending re-equipping. Other mods' equipment restrictions remain authoritative; a rejected equip leaves the shield in the inventory without repeated attempts or extra item drops.
- Added thrower identity metadata to dropped thrown shields for optional FearNoSpear integration. Compatible FearNoSpear versions can restrict automatic pickup to the thrower while retaining manual pickup; metadata failures after a successful drop do not trigger a duplicate drop.

## 1.0.11

- Added server-synchronized options for using shield charge, block-charge counterattacks, and projectile reflection with a one-handed melee weapon. Charge defaults to Off; block charge and reflection default to On.
- When enabled, Block + Secondary Attack uses shield charge while ordinary weapon attacks remain available. A failed or held charge input cannot fall back to a weapon attack.
- Block-charge accumulation and counterattacks now respect the equipment policy and YAML enable setting without changing other players' shields; entering disallowed equipment clears stored charges.
- Prevented direct shield charge from starting during attacks, dodges, knockback, stagger, or other restricted actions.
- Updated shield hints, tooltips, and the Compendium to explain weapon compatibility while retaining normal weapon hints.
- Updated the required BepInExPack version to 5.4.2351.

## 1.0.10

- Updated CaptainValheim for Valheim 1.0.7, including the new shield tooltip signature and original game assembly access boundaries.
- Updated the bundled ServerSync compatibility build for Valheim 1.0.7, fixing configuration initialization and preserving connection-time player, history, administrator, and network-time messages.
- Preserved Valheim 1.0 damage channels, attack resource effects, and status-effect variants through shield hits and the versioned projectile reflection protocol.
- Prevented new mid-flight projectile template behavior from bypassing shield throw hit ownership and duplicate-hit handling.
- Updated the required BepInExPack version to 5.4.2350.

## 1.0.9

- Reorganized shield throw and charge state alongside their runtime code and removed redundant attack setup forwarding without changing combat rules.
- Removed an unnecessary ObjectDB snapshot pass while retaining capture-before-write and conditional restoration of modified shield data.
- Avoided allocating reflection block contexts for characters and shields that do not use projectile reflection.
- Added `DeployToGame` support for automatic local DLL updates after Debug builds, plus options to skip game copies or release packaging during validation.

## 1.0.8

- Added the client-only `Show Shield Tooltip` setting, enabled by default, so each player can hide CaptainValheim guidance from shield item tooltips without changing server behavior.
- Removed the shield reflection debug logging option and its diagnostic-only logging and data plumbing.

## 1.0.7

- Added localized shield key hints, contextual shield tooltip guidance, and a new Compendium page explaining Shield Strike, Throw, Charge, Projectile Reflection, and Block Charge.
- Added built-in English and Korean text plus client-side `CaptainValheim.<Language>.yml` overrides; Thunderstore packages now include `CaptainValheim.English.yml` as a translation template.
- Simplified the shield-only configuration and runtime structure by removing unused inherited SecondaryAttacks paths and consolidating code that changes together.
- Hardened live configuration reload and synchronization, ObjectDB restoration and reapplication, runtime cleanup, and shield state handling while preserving existing combat behavior.

## 1.0.6

- Rebuilt dedicated-server projectile reflection around owner-authoritative `HitData` and projectile snapshots with exact, one-shot target correlation, allowing multiple shield players in the same loaded zone to reflect independently.
- Removed pending queues, TTL pruning, fuzzy matching, and string payload workarounds while preserving the original sender, validating ownership, and marking reflected projectiles across ownership transfers.

## 1.0.5

- Reduced inherited SecondaryAttacks infrastructure to CaptainValheim's shield-only configuration, definition, and runtime pipeline, removing duplicate version RPC and unused generic paths.
- Hardened shield primary, throw, charge, and reflection context cleanup with scoped Harmony finalizers and simplified projectile visual and return handling.

## 1.0.4

- Fixed dedicated-server shield reflection for multiple players in the same loaded zone by correlating remote projectile and block contexts through player ZDOIDs.
- Added bounded ordered fallback matching and now creates reflected projectiles from their registered source prefabs.

## 1.0.3

- Improved dedicated-server shield reflect reliability by keeping successful blocks briefly pending so late projectile ownership/context RPCs can still trigger reflection.

## 1.0.2

- Improved multiplayer shield reflect reliability on dedicated servers by sending projectile fallback payloads for remote block contexts.
- Relaxed remote shield reflect context matching so valid blocks can reflect even when projectile hit context timing or hit points differ between clients.

## 1.0.1

- Added multiplayer shield reflect ownership handoff so remote projectile hits can be reflected by the blocking player on dedicated servers.
- Added optional shield reflect diagnostics.
- Fixed generated key hint separator font/style copying.

## 1.0.0

- Initial Release.
