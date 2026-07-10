| `Version` | `Update Notes`    |
|-----------|-------------------|
| 1.0.5     | - Reduced inherited SecondaryAttacks infrastructure to CaptainValheim's shield-only configuration, definition, and runtime pipeline, removing duplicate version RPC and unused generic paths. <br> - Hardened shield primary, throw, charge, and reflection context cleanup with scoped Harmony finalizers and simplified projectile visual and return handling. |
| 1.0.4     | - Fixed dedicated-server shield reflection for multiple players in the same loaded zone by correlating remote projectile and block contexts through player ZDOIDs. <br> - Added bounded ordered fallback matching and now creates reflected projectiles from their registered source prefabs. |
| 1.0.3     | - Improved dedicated-server shield reflect reliability by keeping successful blocks briefly pending so late projectile ownership/context RPCs can still trigger reflection. |
| 1.0.2     | - Improved multiplayer shield reflect reliability on dedicated servers by sending projectile fallback payloads for remote block contexts. <br> - Relaxed remote shield reflect context matching so valid blocks can reflect even when projectile hit context timing or hit points differ between clients. |
| 1.0.1     | - Added multiplayer shield reflect ownership handoff so remote projectile hits can be reflected by the blocking player on dedicated servers. <br> - Added optional shield reflect diagnostics. <br> - Fixed generated key hint separator font/style copying. |
| 1.0.0     | - Initial Release |
