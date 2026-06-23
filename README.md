# CaptainValheim

CaptainValheim makes shields feel like active weapons. It adds configurable shield attacks, throws, charges, projectile reflection, and block-charge support while scaling the results from each shield's own block power, deflection force, durability cost, and stamina profile.

## Highlights

- Gives every shield a configurable active combat kit through one synced YAML file.
- Scales damage, push, stamina, and durability from the shield's actual stats instead of using one flat value for every shield.
- Provides a global fallback so unlisted shields work immediately, while individual prefab overrides can tune special cases.
- Syncs config through ServerSync for dedicated servers.
- Adds a WarfareTweaks bridge so Warfare effects can recognize CaptainValheim shield hit contexts.
- Keeps the mod focused on shields only, with no ranged, melee, or Blood Magic preset schema mixed in.

## Shield Features

- `primaryAttack`: turns shield use into a direct shield strike with configurable damage, push, stamina, and durability factors.
- `throw`: throws the shield, supports multiple targets, damage decay, search radius, lifetime scaling, and return behavior.
- `charge`: sends the player forward with shield-based damage, push, hit radius, distance, speed, cooldown, and stamina tuning.
- `reflect`: lets guarded projectile reflection scale from shield deflection.
- `blockCharge`: enables and tunes Valheim's vanilla block-charge counterattack behavior, including support for shields missing charge visuals.

## Configuration

CaptainValheim creates:

- `BepInEx/config/CaptainValheim.yml`

The root `Global` block defines fallback behavior for all shields. Any shield prefab listed below `Global` inherits those defaults and only overrides the fields you write.

Example:

```yml
Global:
  throw:
    enabled: true
    damageFactor: 0.80
    targets: 3
  charge:
    enabled: true
    cooldown: 10.0
    distance: 4.0

ShieldCarapaceBuckler:
  throw:
    damageFactor: 1.00
    targets: 2
  charge:
    cooldown: 8.0
```

Set a mode block's `enabled: false` to disable that feature after inheritance.

## Compatibility

- Works on dedicated servers through synced config.
- Can pass shield hit context to WarfareTweaks so configured Warfare effects apply cleanly to shield attacks and shield charges.
- Designed to coexist with SecondaryAttacks, which handles non-shield ranged, melee, bomb, staff, and Blood Magic behaviors.

## Installation

Install BepInExPack Valheim, then place `CaptainValheim.dll` in a BepInEx plugin folder. Launch once to generate `CaptainValheim.yml`, edit the values you want, and restart or reload config as needed.
