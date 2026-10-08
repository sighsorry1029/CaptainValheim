# Shield Specials

This document describes shield techniques and their equipment rules in `CaptainValheim`.

Relevant files:

- `ShieldRuntimeSystem.cs`
- `ShieldRuntimeSystem.Throw.cs`
- `ShieldRuntimeSystem.Charge.cs`
- `SecondaryAttackConfig.Raw.cs`
- `SecondaryAttackNormalizedShieldModels.cs`
- `SecondaryAttackConfigLoader.cs`

## Schema

Shield-prefab entries live in `CaptainValheim.yml` and use a flat schema.

Every unlisted shield prefab receives the reserved `Global` entry, or built-in shield defaults when `Global` is omitted.
Listed shield prefabs inherit `Global` and only override the fields written under that prefab.
The old shield `bash:` block is no longer supported; use `primaryAttack:` instead.

```yml
Global:
  primaryAttack:
    enabled: true
    damageFactor: 0.4
    pushFactor: 0.4
    durabilityFactor: 1.0
    staminaFactor: 0.8
  throw:
    enabled: true
    animation: battleaxe_attack1
    damageFactor: 0.8
    pushFactor: 1.0
    durabilityFactor: 1.0
    staminaFactor: 2.0
    ttlFactor: 1.0
    targets: 3
    damageDecay: 0.5
    radiusFactor: 6.0
  charge:
    enabled: true
    cooldown: 10.0
    cooldownReductionFactor: 0.5
    damageFactor: 1.2
    pushFactor: 2.0
    durabilityFactor: 2.0
    staminaFactor: 3.0
    distance: 4.0
    speed: 12.0
    hitRadiusFactor: 0.4
  reflect:
    enabled: true
    staminaFactor: 2.0
    reflectionFactor: 0.01
  blockCharge:
    enabled: true

ShieldWoodTower:
  primaryAttack:
    enabled: true
    damageFactor: 0.5
  # blockCharge:
  #   enabled: true
  #   chargeCount: 5
  #   decayTime: 4.0
  #   blockingDecayFactor: 0.5
```

## Activation Rules

Configured shield features apply when:

- the player is holding a shield in the left hand
- the shield prefab either has a shield entry in `CaptainValheim.yml` or is picking up `Global`/built-in shield defaults
- omitted `primaryAttack`, `throw`, or `charge` blocks keep the inherited `Global`/built-in values
- `enabled: false` on a shield feature block disables that feature after inheritance
- `primaryAttack` and `throw` require an empty right hand
- `charge` also requires a positive `distance`; zero or negative distance leaves the charge unavailable
- `charge`, `reflect`, and `blockCharge` allow an empty right hand, or a one-handed melee weapon when the corresponding ServerSync option permits it
- Shield throw has no cooldown, status effect, or cooldown HUD entry; it is limited by stamina, durability, projectile travel, and shield return/re-equip time.
- `throw.targets` is the maximum number of valid enemy targets hit, including the first enemy hit. Terrain and wall bounces do not spend targets.
- `throw.damageDecay` reduces damage after each enemy target hit as `nextDamage *= (1 - damageDecay)`.
- `blockCharge.enabled: true` enables Valheim's vanilla block-charge counterattack while keeping the prefab's own vanilla count and decay values; inherited `Global`/built-in shield defaults do the same

### Pickup Auto-Equip

The client-only `1 - General` option `Prevent Weapon Auto Equip While Shield Only` defaults to `On`. During automatic pickup, an equipped left-hand shield with all other active/sheathed hands empty is protected from newly collected weapons taking over the hands. An active thrown-shield return intent also protects empty hands. Pickup itself and inventory capacity rules remain unchanged; manual pickups, manual equipment choices and ordinary weapon loadouts keep their existing behavior.

Recoverable projectile drops carry a thrower marker so your own thrown spear or weapon can still use the game's normal auto-equip rules. A new spear or another player's marked weapon does not qualify. Existing FearNoSpear and SecondaryAttacks dropped-item thrower markers are also recognized. Markers belong to the world drop and are not added to inventory item custom data. Unmarked drops from earlier throws or custom throw systems are treated as ordinary loot while protection is active. Disabling the option restores normal pickup auto-equipping; it does not disable shield returns.

### One-Handed Weapon Options

The following settings are in the BepInEx config section `2 - Shield Actions With One-Handed Weapon`, are synchronized by ServerSync, and follow the server's configuration lock:

| Setting | Default |
| --- | --- |
| `Allow Shield Charge With One-Handed Weapon` | `Off` |
| `Allow Block Charge With One-Handed Weapon` | `On` |
| `Allow Projectile Reflection With One-Handed Weapon` | `On` |

Each option grants permission to use its feature with a one-handed melee weapon in the right hand and an actual equipped shield in the left hand. It does not override YAML `enabled: false`. `Off` still allows that feature with an empty right hand. Other right-hand items, including bows, two-handed weapons, torches, and tools, do not qualify. The weapon's damage is not added to shield techniques.

### Primary Attack

- Input: primary attack
- Requires an empty right hand
- Uses the native unarmed combo from the cloned unarmed primary attack template
- Uses a compact impact sphere for hit detection, while damage and push use the shield `primaryAttack` formula
- Uses `Blocking` skill for skill gain

### Throw

- Input: secondary attack
- Used when the player is not currently blocking
- Requires an empty right hand
- Unequips and removes the shield from the inventory, throws it, chains to nearby targets, then returns it to the inventory
- Automatic re-equipping waits for temporary attack, dodge, or swimming restrictions to end. Direct returns and marked ground pickups use the same deferred equip policy.
- A hand equipment choice during flight or while waiting cancels automatic re-equipping. Both active and sheathed hands must remain empty; another mod's equipment restrictions are respected.
- Re-equipping never adds another item or drops a second copy. A shield whose equip attempt is rejected stays in the inventory for manual use.
- Captures the equipped left-hand visual prefab and style before throwing and carries that snapshot through ricochets and return flight. Cosmetic appearance is synchronized separately from the real item; unavailable cosmetic prefabs fall back to the original shield. No Armoire dependency is required.
- Drops the shield back into the world when it cannot be returned to the inventory, including when the inventory is full

### Charge

- Input: secondary attack
- Used when the player is currently blocking
- With a one-handed melee weapon, requires `Allow Shield Charge With One-Handed Weapon = On`
- When enabled for the equipped shield and weapon, Block + Secondary Attack belongs to charge; a failed charge attempt does not fall through to a weapon attack
- Without Block, the right-hand weapon retains its normal secondary attack
- Stops on the first valid character or environment impact
- Deals one impact pulse at the stop point

### Reflect

- While guarding, blockable projectiles can be reflected toward the current aim point
- Reflection can exist with or without `primaryAttack`, `throw`, or `charge`
- With a one-handed melee weapon, requires `Allow Projectile Reflection With One-Handed Weapon = On`

### Block Charge

- Enables Valheim's vanilla block-charge counterattack by setting the shield prefab's `m_buildBlockCharges`
- Optional `chargeCount`, `decayTime`, and `blockingDecayFactor` override the prefab's vanilla values
- Omitting those fields keeps the original prefab values, such as ordinary shields/bucklers `3 / 1.0 / 0.25` and tower shields `5 / 4.0 / 0.5`
- With a one-handed melee weapon, both charge accumulation and release require `Allow Block Charge With One-Handed Weapon = On`
- Stored charges are cleared when the equipped items no longer permit this feature or the feature is disabled, so charges cannot be carried into a disallowed equipment state

## Formulas

### Shared Inputs

- `blockPower = weapon.GetBlockPower(blockingSkillFactor)`
- `baseBlockPower = weapon.GetBaseBlockPower()`
- `deflectionForce = weapon.GetDeflectionForce()`

Only primary attacks, throws and Shield Charge adjust deflection for their push calculation:

```text
F = max(0, deflectionForce)
attackPushBase = F <= 15 ? F : sqrt(15 * F)
```

Existing YAML `pushFactor` numbers are preserved and now multiply `attackPushBase`. For example, deflection 20 gives about 17.32 before the multiplier, while 100 gives 38.73 and 150 gives 47.43. This is an attack-force adjustment, not a guaranteed knockback-distance ratio: the target's mass, equipment, movement and collisions also affect displacement. Throw search radius and TTL, charge hit radius, reflection, normal blocking and vanilla Block Charge continue using their existing inputs and formulas.

### Primary Attack

Damage:

```text
damage = blockPower * primaryAttack.damageFactor
```

Push:

```text
push = max(0, attackPushBase * primaryAttack.pushFactor)
```

Hit shape:

```text
Uses the cloned vanilla unarmed primary attack shape.
```

Raw stamina before Valheim skill reduction:

```text
sqrt(baseBlockPower) * primaryAttack.staminaFactor
```

### Throw

Damage:

```text
damage = blockPower * throw.damageFactor
```

Push:

```text
push = max(0, attackPushBase * throw.pushFactor)
```

Ricochet search radius:

```text
bounceSearchRadius = throw.radiusFactor / cbrt(deflectionForce / 20)
```

TTL:

```text
ttl = max(0.3, throw.ttlFactor / cbrt(deflectionForce / 20))
```

Effective flight distance is projectile speed multiplied by this TTL.

Ricochet damage decay:

```text
currentDamage *= (1 - throw.damageDecay)
```

Raw stamina before Valheim skill reduction:

```text
sqrt(baseBlockPower) * throw.staminaFactor
```

### Charge

Damage:

```text
damage = blockPower * charge.damageFactor
```

Push:

```text
push = max(0, attackPushBase * charge.pushFactor)
```

Travel distance:

```text
distance = charge.distance
```

Impact radius:

```text
hitRadius = sqrt(deflectionForce / 20) * charge.hitRadiusFactor
```

Raw stamina before Valheim skill reduction:

```text
sqrt(baseBlockPower) * charge.staminaFactor
```

Cooldown:

```text
charge.cooldown * (1 - BlockingSkill / 100 * charge.cooldownReductionFactor)
```

Durability cost:

```text
shield useDurabilityDrain * durabilityFactor
```

### Reflect

Total reflect stamina is based on real vanilla block stamina cost:

```text
totalReflectStamina = vanillaBlockStaminaCost * reflect.staminaFactor
```

Reflected projectile power multiplier:

```text
reflectPowerScale = deflectionForce * reflect.reflectionFactor
```

That multiplier is applied to reflected projectile damage and push force.

## Multi-Hit Behavior

### Primary Attack

If one primary attack hits multiple targets, damage and push use Valheim-style multi-hit falloff.

### Charge

Charge impact uses the same multi-hit falloff style for damage and push when one impact pulse hits multiple targets.

### Throw

Throw ricochet damage decay is per bounce, not per simultaneous target.

## Notes

- `primaryAttack`, `throw`, `charge`, and `reflect` live at the shield entry root.
- `throw.animation` overrides the attack animation used when the throw starts; omitted values inherit `Global` or the built-in `battleaxe_attack1`.
- `primaryAttack` and `charge` do not expose animation config fields.
- Reflected projectile power can scale with `deflection force`, which gives high-force tower shields a natural reflect advantage.
