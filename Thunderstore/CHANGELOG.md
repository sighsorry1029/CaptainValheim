# Changelog

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
