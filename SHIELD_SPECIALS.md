# Shield Specials

This document describes the current shield-only behavior in `CaptainValheim`.

Relevant files:

- `ShieldRuntimeSystem.cs`
- `SecondaryAttackConfig.Raw.cs`
- `SecondaryAttackConfig.Normalized.cs`

## Schema

Shield-prefab entries now live in `CaptainValheim.Shields.yml` and use a flat schema.

Every unlisted shield prefab receives the reserved `Global` entry, or built-in shield defaults when `Global` is omitted.
Listed shield prefabs inherit `Global` and only override the fields written under that prefab.
The old shield `bash:` block is no longer supported; use `primaryAttack:` instead.

```yml
Global:
  primaryAttack:
    enabled: true
    damageFactor: 0.5
    pushFactor: 0.5
    durabilityFactor: 1.0
    staminaFactor: 0.35
  throw:
    enabled: true
    animation: battleaxe_attack1
    damageFactor: 0.8
    pushFactor: 1.0
    durabilityFactor: 2.0
    staminaFactor: 2.0
    ttlFactor: 1.0
    targets: 3
    damageDecay: 0.5
    radiusFactor: 6.0
  charge:
    enabled: true
    cooldown: 10.0
    cooldownReductionFactor: 0.5
    damageFactor: 1.0
    pushFactor: 2.0
    durabilityFactor: 1.0
    staminaFactor: 2.0
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

Shield specials only apply when:

- the player is holding a shield in the left hand
- the right hand weapon is empty
- the shield prefab either has a shield entry in `CaptainValheim.Shields.yml` or is picking up `Global`/built-in shield defaults
- omitted `primaryAttack`, `throw`, or `charge` blocks keep the inherited `Global`/built-in values
- `enabled: false` on a shield feature block disables that feature after inheritance
- Shield throw has no cooldown, status effect, or cooldown HUD entry; it is limited by stamina, durability, projectile travel, and shield return/re-equip time.
- `throw.targets` is the maximum number of valid enemy targets hit, including the first enemy hit. Terrain and wall bounces do not spend targets.
- `throw.damageDecay` reduces damage after each enemy target hit as `nextDamage *= (1 - damageDecay)`.
- `blockCharge.enabled: true` enables Valheim's vanilla block-charge counterattack while keeping the prefab's own vanilla count and decay values; inherited `Global`/built-in shield defaults do the same

### Primary Attack

- Input: primary attack
- Uses the native unarmed combo from the cloned unarmed primary attack template
- Uses a compact impact sphere for hit detection, while damage and push use the shield `primaryAttack` formula
- Uses `Blocking` skill for skill gain

### Throw

- Input: secondary attack
- Used when the player is not currently blocking
- Unequips and removes the shield from the inventory, throws it, chains to nearby targets when ricochet cooldown is ready, then drops it back into the world

### Charge

- Input: secondary attack
- Used when the player is currently blocking
- Stops on the first valid character or environment impact
- Deals one impact pulse at the stop point

### Reflect

- While guarding, blockable projectiles can be reflected toward the current aim point
- Reflection can exist with or without `primaryAttack`, `throw`, or `charge`

### Block Charge

- Enables Valheim's vanilla block-charge counterattack by setting the shield prefab's `m_buildBlockCharges`
- `Enable Shield Block Charge = Off` ignores `blockCharge` entries and keeps the prefab's original block-charge values
- Optional `chargeCount`, `decayTime`, and `blockingDecayFactor` override the prefab's vanilla values
- Omitting those fields keeps the original prefab values, such as ordinary shields/bucklers `3 / 1.0 / 0.25` and tower shields `5 / 4.0 / 0.5`

## Formulas

### Shared Inputs

- `blockPower = weapon.GetBlockPower(blockingSkillFactor)`
- `baseBlockPower = weapon.GetBaseBlockPower()`
- `deflectionForce = weapon.GetDeflectionForce()`

### Primary Attack

Damage:

```text
damage = blockPower * primaryAttack.damageFactor
```

Push:

```text
push = deflectionForce * primaryAttack.pushFactor
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
push = deflectionForce * throw.pushFactor
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
push = deflectionForce * charge.pushFactor
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

