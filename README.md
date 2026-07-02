# CaptainValheim

Turns shields into active weapons. All you need is one shield. Shield strikes, throws, charges, projectile reflection, and block charges all follow real shield stats and blocking skill.

![](https://i.ibb.co/zhPT15g1/shieldprimary.gif) <br>

**Shield Primary Attack**  
CaptainValheim lets a shield act as a real close-range weapon instead of only a defensive item. A primary shield strike uses the equipped shield's own block power, deflection force, stamina profile, and durability cost as the base, then applies the values from `CaptainValheim.yml`. That means a buckler, tower shield, and round shield can all feel different without needing separate hardcoded behavior.

![](https://i.ibb.co/bRWn3Vjt/richochetshield.gif) <br>

**Shield Throw**  
Throw your shield forward as a returning weapon. The throw can hit multiple targets, lose power through damage decay, search for nearby targets, and return after its lifetime expires. Damage, push force, target count, range, return speed, stamina cost, durability cost, and cooldown can all be tuned, while the final result still scales from the shield you actually equipped.

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

## Github
https://github.com/sighsorry1029/CaptainValheim