# Vendored ServerSync: Valheim 1.0.7 compatibility

This project vendors the reviewed `valheim-1.0.7-r1` baseline from:

`C:/Users/blizz/.codex/references/valheim/integrations/serversync/versions/valheim-1.0.7-r1`

- DLL SHA-256: `B4DD786997F4E90D770F09EF3E9D64154754FE7E8EDFB4841795751895B35846`
- Assembly identity: `ServerSync, Version=1.0.0.0`
- File version: `1.0.0.1`
- Upstream commit: `c57c2aa54e07cdcc7630d6068699ea781622323e`
- License: MIT-0

The baseline is rebuilt against original Valheim 1.0.7 assemblies. It compiles `ZRoutedRpc.Everybody` as the new constant, uses public `ZNet.IsAdmin`, and buffers the new connection-time player/history/admin/time messages in vanilla order while configuration synchronization is in progress. Existing configuration identifiers, locking, version checks, and wire format are preserved.

`Libs/ServerSync.dll` remains the pinned compile and ILRepack input. Do not reapply the old binary patch to this completed DLL or install it as a standalone BepInEx plugin. The baseline's source, build inputs, hashes, static and isolated tests, and remaining runtime test scope are documented in its `README.md` and `manifest.json`.
