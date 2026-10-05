# CaptainValheim

Turns shields into active weapons. All you need is one shield. Shield strikes, throws, charges, projectile reflection, and block charges all follow real shield stats and blocking skill.

![](https://i.ibb.co/zhPT15g1/shieldprimary.gif) <br>

**Shield Primary Attack**  
CaptainValheim lets a shield act as a real close-range weapon instead of only a defensive item. A primary shield strike uses the equipped shield's own block power, deflection force, stamina profile, and durability cost as the base, then applies the values from `CaptainValheim.yml`. That means a buckler, tower shield, and round shield can all feel different without needing separate hardcoded behavior.

![](https://i.ibb.co/bRWn3Vjt/richochetshield.gif) <br>

**Shield Throw**  
Throw your shield forward as a returning weapon. The throw can hit multiple targets, lose power through damage decay, search for nearby targets, and return after its lifetime expires. Damage, push force, target count, damage decay, ricochet search radius, flight lifetime, stamina cost, and durability cost can all be tuned, while the final result still scales from the shield you actually equipped.

With FearNoSpear, dropped thrown shields can only be auto-picked up by the thrower. Other players can still pick them up manually. Use the updated versions on participating clients. FearNoSpear is optional; without it, pickup behavior is unchanged. Shield returns and automatic re-equipping still work as before.

![](https://i.ibb.co/ZpmyTscz/shieldbash.gif) <br>

**Shield Charge**  
Shield Charge turns your block into a forward burst that drives into enemies with shield-based damage and push force. It is useful for closing distance, interrupting enemies, or opening space in a crowd. Charge distance, speed, hit radius, cooldown, stamina cost, durability cost, damage factor, and push factor are configurable, and WarfareTweaks can read the shield-hit context when both mods are installed.

![](https://i.ibb.co/35gghwtd/blockchargemain.gif) <br>

**Block Charge**  
Valheim already contains a block-charge style counterattack, but it is effectively locked away for normal shield gameplay. CaptainValheim exposes and tunes that vanilla behavior so shields can use it intentionally. The mod can also provide fallback charge visuals for shields that do not have their own charge setup, making the feature usable across a wider range of shield prefabs.

![](https://i.ibb.co/67qYbBwY/projectilereflect.gif) <br>

**Projectile Reflection**  
Projectile Reflection rewards accurate blocking by letting guarded projectile hits bounce back instead of simply being absorbed. Reflection behavior scales with shield deflection and can be tuned separately from attacks, throws, and charges, so servers can decide how strong defensive projectile play should be without changing the rest of the shield kit.

## Highlights

- Gives every shield a configurable active combat kit through one synced YAML file.
- Scales damage, push, stamina, and durability from the shield's actual stats instead of using one flat value for every shield.
- Provides a global fallback so unlisted shields work immediately, while individual prefab overrides can tune special cases.
- Syncs config through ServerSync for dedicated servers.
- Lets servers independently allow Shield Charge, Block Charge, and Projectile Reflection with a one-handed melee weapon and a shield.
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

- `BepInEx/config/sighsorry.CaptainValheim.cfg`
- `BepInEx/config/CaptainValheim.yml`

`Show Shield Tooltip` in the BepInEx config controls the extra CaptainValheim guidance appended to shield item tooltips. It defaults to `On` and is a client-only display setting that is not synchronized with the server.

The `2 - Shield Actions With One-Handed Weapon` section contains three ServerSync settings. These follow the server's `Lock Configuration` setting:

| Setting | Default | When enabled |
| --- | --- | --- |
| `Allow Shield Charge With One-Handed Weapon` | `Off` | Allows Block + Secondary Attack to charge while holding a one-handed melee weapon and a shield. |
| `Allow Block Charge With One-Handed Weapon` | `On` | Allows blocking to build charges and release the counterattack with that equipment. |
| `Allow Projectile Reflection With One-Handed Weapon` | `On` | Allows guarded projectile reflection with that equipment. |

`Off` restricts that feature to an empty right hand. The shield must be equipped in the left hand, and the corresponding feature must also be enabled in `CaptainValheim.yml`. Bows, two-handed weapons, torches, and tools are not included. Shields still use their own damage and stamina settings; the right-hand weapon does not add its damage to shield techniques.

When charge is allowed for the equipped shield and weapon, Block + Secondary Attack is reserved for shield charge. If a cooldown, stamina shortage, or another start condition prevents charging, the input does not fall through to the weapon's secondary attack. Without Block, the weapon's secondary attack remains available. Shield strikes and throws still require an empty right hand. Equipping an item that disallows block charge, or disabling block charge, clears any stored charges.

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

## Localization

The package includes `CaptainValheim.English.yml` as a translation template. Copy it beside `CaptainValheim.dll`, translate only the values while keeping every key unchanged, and rename it to `CaptainValheim.<Valheim language>.yml`. For example, a Turkish translation must be named `CaptainValheim.Turkish.yml`.

Save translation files as UTF-8, then restart Valheim or reselect the language after changing a file. Localization is client-local, so each player can use a different language file; missing files or values fall back to the built-in English text.

## Compatibility

- Works on dedicated servers through synced config.
- Can pass shield hit context to WarfareTweaks so configured Warfare effects apply cleanly to shield attacks and shield charges.
- Designed to coexist with SecondaryAttacks, which handles non-shield ranged, melee, bomb, staff, and Blood Magic behaviors.

## Building

Build against a local Valheim installation with BepInEx and the original game assemblies configured in `environment.props`. Private game members used by the mod are accessed explicitly through cached Harmony accessors.

For normal development, build Debug and automatically copy the final merged plugin DLL into the configured Valheim plugins folder:

```text
dotnet build CaptainValheim.csproj -c Debug -p:DeployToGame=true
```

Use `-p:DeployToGame=false` to skip the game DLL copies. The legacy `SkipDeployment=true` option is still supported when `DeployToGame` is not specified. Debug builds do not update the release manifest or generate packages. Use Release only for an explicitly requested release; `SkipPackaging=true` skips its manifest update and Thunderstore/Nexus packages.

## Github
https://github.com/sighsorry1029/CaptainValheim
