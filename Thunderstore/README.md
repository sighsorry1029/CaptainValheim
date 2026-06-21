# CaptainValheim

Configurable custom secondary behaviors for Valheim weapons.

## Features

- Uses `BepInEx/config/CaptainValheim/CaptainValheim.Ranged.yml`
- Uses `BepInEx/config/CaptainValheim/CaptainValheim.Melee.yml`
- Uses `BepInEx/config/CaptainValheim/CaptainValheim.BloodMagic.yml`
- Uses `BepInEx/config/CaptainValheim/CaptainValheim.Shields.yml`
- Uses `BepInEx/config/CaptainValheim/CaptainValheim.TerrainTools.yml`
- Supports projectile presets such as `spreadShot`, `volley`, `piercingPrimary`, `scatterRicochet`, and `sunSentinel`
- Supports `stickyDetonator` and `overchargedBomb` bomb secondaries for sticky charges or stronger multi-bomb throws
- Supports copied secondary attacks from another weapon prefab
- Supports shield-only primaryAttack/throw/charge/reflect behaviors and vanilla shield block charges
- Supports summon-staff empower secondaries for `StaffSkeleton` and `StaffRedTroll`
- Supports optional MonsterAI prefab cloning for summon items/projectiles, including MagicPlugin summon projectiles
- Supports optional summon quality presets: item quality can raise active summon count or summon level per configured summon item/projectile
- Supports `StaffShield` shield-to-heal conversion
- Supports smoke effects on knife secondary hits by default, and on listed melee prefabs through `preset: sneakAmbush`
- Supports `cleavingThrust` expanded greatsword-secondary fan hits on two-handed sword secondary attacks by default
- Supports `launchSlam` on one-handed Clubs weapons by default, replacing secondary push with vertical launch plus landing AOE damage
- Supports `knockbackChain` on fist/unarmed weapons by default, multiplying secondary push and causing knocked enemies to crash into others
- Supports `aftershock` on two-handed Clubs Area attacks, such as sledges, by default, replacing secondary with repeated weakened shockwaves
- Supports `impactBurst` on battleaxe-like two-handed axe throws by default, copying a throw attack and bursting on projectile impact
- Supports `riftTrail` on one-handed sword secondary attacks by default, leaving a temporary sword-trail rift that deals periodic damage
- Supports `terrainDig` on pickaxe primary terrain digging by default, letting the player adjust radius/depth together with a hotkey + mouse wheel
- Supports `harvestSweep` on scythe/Farming weapons by default, adding an Atgeir-style spinning secondary that harvests harvestable and foraging Pickables while the player steers movement
- Can expose scythe/Farming harvest weapons as `ItemType.Tool` for tool-slot compatibility while still letting them attack
- Supports configurable Hoe/Cultivator terrain tool costs and hotkey + mouse-wheel range, with material/stamina costs scaling by selected range
- Warfare-native weapon effect tuning has moved to the standalone `WarfareTweaks` mod.
- Syncs config through `ServerSync`

## Schema

Non-shield weapon entries are split by behavior family.

- `CaptainValheim.Ranged.yml` uses flat projectile preset fields
- `CaptainValheim.Melee.yml` uses flat copied-secondary fields
- `CaptainValheim.BloodMagic.yml` uses flat blood-magic fields with `type`, plus an optional `magicSummons:` block for summon `SpawnAbility` prefab redirects
- Do not use `secondary:`, `projectile:`, `copy:`, `summonEmpower:`, or `shieldConvert:` wrapper blocks

`CaptainValheim.Shields.yml` is a separate strict flat schema for shield prefabs.

- Use flat root fields such as `primaryAttack`, `throw`, `charge`, `reflect`, and `blockCharge`
- Use the reserved `Global` root key to configure shield fallbacks for unlisted shield prefabs
- Do not use a `shield:` wrapper
- Every unlisted shield prefab receives the `Global` shield values, or built-in shield defaults if `Global` is omitted
- If a shield prefab is listed in `CaptainValheim.Shields.yml`, that YAML entry inherits `Global` and only overrides the fields written under that prefab

The old nested `secondary:` and `shield:` wrapper blocks are no longer supported.
The old combined `CaptainValheim.Attacks.yml` file is no longer read; use `Ranged`, `Melee`, and `BloodMagic` files instead.
The old shield `bash:` block is no longer supported; use `primaryAttack:` instead.

The optional `magicSummons:` block in `CaptainValheim.BloodMagic.yml` redirects summon `SpawnAbility` prefabs. When `targetProjectile` is omitted, the entry key is treated as `targetItem`; when `targetProjectile` is set, the entry patches that projectile or its `Projectile.m_spawnOnHit` payload directly. If `qualityPreset` is set, the entry key is also treated as `targetItem` unless `targetItem` is explicitly set. Each configured source prefab is cloned, made into a friendly BloodMagic-owned summon, registered into `ZNetScene`, and assigned back to the target `SpawnAbility`.

`qualityPreset` entries under `magicSummons:` are always active. `countByQuality` makes item quality raise the active summon count while keeping summon level fixed. `levelByQuality` makes item quality raise summon level while keeping one active summon per configured item/projectile group. The summoned creature's BloodMagic copied attack scaling is left unchanged.

For `targetProjectile` quality entries, use the real item prefab as the root key or set `targetItem` when `maxQuality` should be raised on the item.

`CaptainValheim.TerrainTools.yml` is optional. `Global` and weapon-prefab roots can configure tool-domain weapon blocks such as `fractureLine`, `terrainDig`, and `harvestSweep`. Hoe/Cultivator roots still use child keys that match exact piece prefab names from that tool's `PieceTable`; `cost` replaces the base material cost, and `range.enabled` lets the configured range modifier hotkey + mouse wheel adjust the selected `TerrainOp` radius. `materialCostFactor`, `staminaCostFactor`, and `durabilityFactor` control how strongly costs scale with the selected 2D area: `1 + max(0, (selectedRange / vanillaRange)^2 - 1) * factor`.

## Example

```yml
# CaptainValheim.Ranged.yml
BowHuntsman:
  preset: spreadShot
  resourceMultiplier: 1.5
  damageFactor: 1.0
  count: 5
  spreadAngle: 28
  ammoConsumption: 3

# Keeps the primary projectile pattern, but changes projectile damage/speed.
StaffLightning:
  preset: piercingPrimary
  resourceMultiplier: 1.5
  damageFactor: 2.0
  projectileSpeedFactor: 0.35

StaffClusterbomb:
  preset: scatterRicochet
  resourceMultiplier: 1.5
  damageFactor: 1.0
  count: 6
  splitAngle: 45
  ricochetBounces: 1
  ricochetPower: 0.85
  ricochetRoughness: 0.1

# CaptainValheim.Melee.yml
Global:
  sneakAmbush:
    enabled: true
    cooldown: 30.0
    cooldownReductionFactor: 0.5
    durabilityFactor: 1.0
    resourceMultiplier: 1.0
    chargeMaxSeconds: 8.0
    chargeSkillFactor: 2.0
    aggroResetRangePerChargeSecond: 1.0
    senseBlockDurationPerChargeSecond: 0.25
    backstabResetSecondsPerChargeSecond: 35.0
  cleavingThrust:
    enabled: true
    cooldown: 6.0
    cooldownReductionFactor: 0.5
    damageFactor: 1.0
    pushFactor: 1.0
    durabilityFactor: 1.0
    resourceMultiplier: 1.0
    rangeFactor: 2.5
  launchSlam:
    enabled: true
    cooldown: 5.0
    cooldownReductionFactor: 0.5
    damageFactor: 1.0
    durabilityFactor: 1.0
    resourceMultiplier: 1.0
    launchHeight: 4.0
    landingAreaRadiusFactor: 1.0
    landingAreaRadiusMax: 4.0
    vfx: vfx_HitSparks
    vfxRotationOffset: 0, 0, 0
    sfx: sfx_club_hit
  knockbackChain:
    enabled: true
    cooldown: 5.0
    cooldownReductionFactor: 0.5
    pushFactor: 8.0
    durabilityFactor: 1.0
    resourceMultiplier: 1.0
    chainDecay: 0.75
  aftershock:
    enabled: true
    cooldown: 8.0
    cooldownReductionFactor: 0.5
    durabilityFactor: 1.0
    resourceMultiplier: 1.0
    waves: 3
    interval: 0.5
    scaleFactor: 0.75
    scaleDecay: 0.15
    forwardStep: 3.0
  impactBurst:
    enabled: true
    cooldown: 6.0
    cooldownReductionFactor: 0.5
    damageFactor: 0.75
    pushFactor: 2.0
    durabilityFactor: 1.0
    resourceMultiplier: 1.5
    animation: spear_throw
    projectileSpinAxis: horizontal
    projectileVisualRotationOffset: 0, 0, 0
    vfx: ""
    radius: 4.0
  boomerang:
    enabled: true
    cooldown: 6.0
    cooldownReductionFactor: 0.5
    damageFactor: 1.0
    pushFactor: 1.0
    durabilityFactor: 1.0
    resourceMultiplier: 1.5
    animation: spear_throw
    projectileSpinAxis: horizontal
    projectileVisualRotationOffset: 0, 0, 0
    maxDistance: 20.0
    curveFactor: 0.5
    hitDamageDecay: 0.20
    includeDestructibles: false
  spinningSweep:
    enabled: true
    cooldown: 8.0
    cooldownReductionFactor: 0.5
    durabilityFactor: 1.0
    resourceMultiplier: 1.0
    animation: atgeir_secondary
    loopStart: 0.4
    loopEnd: 0.6
    animationSpeed: 1.0
    moveSpeedFactor: 0.75
  riftTrail:
    enabled: true
    cooldown: 5.0
    cooldownReductionFactor: 0.5
    damageFactor: 0.25
    pushFactor: 0.0
    durabilityFactor: 1.0
    resourceMultiplier: 1.0
    duration: 2.0
    tickInterval: 0.5
    visualScaleFactor: 1.0
    visualForwardOffset: 0.0
    visualTint: "#6F1AB6"
    visualAlphaFactor: 1.0
    hitEffectLimit: 3
  spearRain:
    enabled: true
    cooldown: 20
    cooldownReductionFactor: 0.5
    damageFactor: 0.25
    durabilityFactor: 1.0
    resourceMultiplier: 1.0
    count: 6
    spawnHeight: 10.0
    spawnRadius: 10.0
    flightTime: 1

# Two-handed sword-skill weapons already inherit Global cleavingThrust. List one only to override values or disable it.
# THSwordKrom:
#   preset: cleavingThrust
#   cleavingThrust:
#     rangeFactor: 3.0

# Spear-skill weapons already inherit Global spearRain. List one only to override values or disable it.
# SpearBronze:
#   preset: spearRain
#   spearRain:
#     count: 10

MaceIron:
  preset: launchSlam
  launchSlam:
    launchHeight: 5.0
    damageFactor: 1.2

BattleaxeWood:
  preset: impactBurst
  impactBurst:
    radius: 4.5
    damageFactor: 0.85
    vfx: vfx_HitSparks

# One-handed axe prefabs already inherit Global boomerang. List one only to override values or disable it.
# AxeBronze:
#   preset: boomerang
#   boomerang:
#     maxDistance: 14.0
#     curveFactor: 0.5
#     hitDamageDecay: 0.15

# Fist-skill weapons with `unarmed_kick` secondary animation already inherit Global knockbackChain. List one only to override values or disable it.
# FistFenrirClaw:
#   preset: knockbackChain
#   knockbackChain:
#     pushFactor: 10.0

# Two-handed Clubs Area-attack weapons, such as sledges, already inherit Global aftershock.
# SledgeDemolisher:
#   preset: aftershock
#   aftershock:
#     waves: 4
#     forwardStep: 1.25

# One-handed sword secondary attacks already inherit Global riftTrail. List one only to override values or disable it.
# SwordSilver:
#   preset: riftTrail
#   riftTrail:
#     duration: 2.5
#     tickInterval: 0.5
#     damageFactor: 0.25
#     visualForwardOffset: 0.5
#     visualTint: "#2A0038"
#     hitEffectLimit: 2

# Polearm/atgeir weapons already inherit Global spinningSweep. List one only to override values or disable it.
# AtgeirIron:
#   preset: spinningSweep
#   spinningSweep:
#     loopStart: 0.4
#     loopEnd: 0.6
#     moveSpeedFactor: 0.85

# CaptainValheim.TerrainTools.yml
Global:
  fractureLine:
    enabled: true
    cooldown: 6.0
    cooldownReductionFactor: 0.5
    damageFactor: 0.35
    durabilityFactor: 1.0
    resourceMultiplier: 1.0
    range: 5.0
    hitSpacing: 0.75
    duration: 1.2
    tickInterval: 0.3
  terrainDig:
    enabled: true
    cooldown: 4.0
    cooldownReductionFactor: 0.5
    durabilityFactor: 1.0
    staminaFactor: 1.0
    radiusScaleMax: 1.5
    depthScaleMax: 1.5
  harvestSweep:
    enabled: true
    cooldown: 16.0
    cooldownReductionFactor: 0.5
    durabilityFactor: 1.5
    resourceMultiplier: 1.5
    animation: atgeir_secondary
    loopStart: 0.4
    loopEnd: 0.6
    animationSpeed: 1.0
    moveSpeedFactor: 0.75

# Pickaxe weapons already inherit Global fractureLine and terrainDig. List one only to override values or disable either part.
# PickaxeIron:
#   preset: fractureLine
#   fractureLine:
#     range: 6.0
#   terrainDig:
#     radiusScaleMax: 1.75
#     depthScaleMax: 1.75
#     staminaFactor: 1.0

# Scythe/Farming weapons already inherit Global harvestSweep. They copy AtgeirIron secondary and keep a steerable spinning harvest loop.
# Scythe_0:
#   harvestSweep:
#     animation: atgeir_secondary
#     loopStart: 0.4
#     loopEnd: 0.6
#     animationSpeed: 1.0
#     moveSpeedFactor: 0.75

Hoe:
  raise_v2:
    cost:
      Stone: 2
    range:
      enabled: true
      min: 1
      max: 5
      step: 0.5
      materialCostFactor: 1
      staminaCostFactor: 1
      durabilityFactor: 1

# CaptainValheim.BloodMagic.yml
StaffSkeleton:
  type: summonEmpower
  animation: staff_summon
  radius: 15
  duration: 12
  moveSpeedPerBloodMagic: 0.002
  attackCooldownReductionPerBloodMagic: 0.001

StaffShield:
  type: shieldConvert
  animation: staff_shield
  radius: 8
  healFactor: 0.5
  consumeShield: true

magicSummons:
# Root key as item target:
#  StaffSkeletonDraugr:
#    targetItem: StaffSkeleton
#    sourcePrefab: Draugr
#    clonePrefab: SA_StaffSkeleton_Draugr
#    displayName: "Draugr (Summon)"
#    health: 250
#    scale: 1.0
#
# Direct projectile target:
#  MagicPluginNeckTotemTroll:
#    targetProjectile: BMP_NeckTotem_Neck_Spawn
#    sourcePrefab: Troll
#    clonePrefab: SA_BMP_NeckTotem_Troll
#    displayName: "Troll (Summon)"
#    health: 600
#
# Quality raises summon count, level stays fixed:
#  BMP_NeckTotem:
#    targetProjectile: BMP_NeckTotem_Neck_Spawn
#    qualityPreset: countByQuality
#    maxQuality: 4
#    fixedSummonLevel: 1
#
# Quality raises summon level, active count stays one:
#  BMP_DrakeTotem:
#    targetProjectile: BMP_DrakeTotem_Spawn
#    qualityPreset: levelByQuality
#    maxQuality: 4
```

```yml
# CaptainValheim.Shields.yml
Global:
  primaryAttack:
    enabled: true
    damageFactor: 0.5
    pushFactor: 0.5
    durabilityFactor: 1.0
    staminaFactor: 0.35
  throw:
    enabled: true
    damageFactor: 0.8
    pushFactor: 3.0
    durabilityFactor: 2.0
    staminaFactor: 2.0
    animation: battleaxe_attack1
    ttlFactor: 1.0
    targets: 3
    damageDecay: 0.5
    radiusFactor: 6.0
  charge:
    enabled: true
    cooldown: 10.0
    cooldownReductionFactor: 0.5
    damageFactor: 1.0
    pushFactor: 6.0
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

## Secondary Types

### `CaptainValheim.Ranged.yml`

Uses a projectile preset. Configure preset parameters directly under the prefab entry.

Important fields:

- `preset`
- `animation`
- `damageFactor`
- `projectileSpeedFactor`
- `count`
- `spreadAngle`
- `ammoConsumption`
- `volley...`
- `burstInterval`
- `holdRepeatInterval`
- `barrageSpacing`
- `meteor...`
- `pierceDamageDecay`
- `split...`
- `ricochet...`
- `spiral...`
- `detectionRange`
- `hoverDistance`
- `hoverHeight`
- `orbitRadius`
- `orbitSpeed`
- `attackDelay`
- `lifetime`
- `maxCharges`
- `detonateAnimation`
- `aoeRadiusFactor`

`piercingPrimary` ignores `count` and keeps the weapon primary attack's projectile count pattern. Each spawned projectile pierces character targets, with subsequent character-hit damage per projectile reduced by `pierceDamageDecay`. `projectileSpeedFactor` preserves travel distance by increasing `ttl` and reducing gravity/drag when projectile speed is lowered.
`crossbowBurst` uses `count` as the number of repeated firing animations. Each firing animation spawns the weapon primary attack's projectile count pattern, so a primary that normally fires multiple projectiles will fire that same cluster each time; default `ammoConsumption` is still `count`.
`animation` overrides the secondary attack trigger. If it is `staff_rapidfire`, holding secondary repeats the configured secondary at `holdRepeatInterval` seconds; `burstInterval` remains the interval for preset-internal staggered shots.
`stickyDetonator` and `overchargedBomb` do not use `cooldown` or `cooldownReductionFactor`; they are limited by bomb items, sticky charge count, and configured bomb consumption instead.
`stickyDetonator` makes the secondary projectile stick on impact instead of exploding immediately. While holding that configured bomb, right-click detonates all of the player's active sticky charges; if there are no charges, right-click is consumed and does nothing. `maxCharges` limits active charges per player, `lifetime` removes old charges, `detonateAnimation` optionally plays an animation trigger when charges are detonated, `damageFactor` scales the stored explosion hit data, and `aoeRadiusFactor` scales direct projectile AOE or spawned `Aoe.m_radius`.
`overchargedBomb` consumes `ammoConsumption` bombs total, then throws one stronger projectile. `damageFactor` scales the hit data used by the projectile or spawned AOE, and `aoeRadiusFactor` temporarily scales direct `Projectile.m_aoe`, spawned `Aoe.m_radius`, impact visual prefab scale, and generated particle modules only while that projectile resolves its hit.
### `CaptainValheim.Melee.yml`

Copies another weapon prefab's secondary attack.

Important fields:

- `preset`
- `copyFrom`
- `animation`
- `resourceMultiplier`
- `outputMultiplier`
- `sneakAmbush`
- `cleavingThrust`
- `spearRain`
- `impactBurst`
- `boomerang`
- `spinningSweep`
- `launchSlam`
- `knockbackChain`
- `aftershock`
- `riftTrail`

If `copyFrom` is empty, the weapon copies its own existing secondary.
Each listed melee prefab can apply only one special preset: `none`, `sneakAmbush`, `cleavingThrust`, `spearRain`, `impactBurst`, `boomerang`, `spinningSweep`, `launchSlam`, `knockbackChain`, `aftershock`, or `riftTrail`. Set `preset` to choose explicitly. If `preset` is omitted, one configured special block is inferred; if several are present, the mod uses one deterministic preset and logs a warning.
`equip` is a utility block, so it does not count as a melee special preset.
Most melee special presets support `cooldown`, `cooldownReductionFactor`, and `durabilityFactor`. The cooldown final value is `cooldown * (1 - skillLevel / 100 * cooldownReductionFactor)`. Regular melee presets use the equipped weapon skill for cooldown reduction. Set `cooldownReductionFactor: 0` to disable skill-based cooldown reduction. `durabilityFactor` scales the weapon/tool's vanilla `useDurabilityDrain`; `0` means no secondary durability cost, `1` means vanilla cost, and `2` means double cost. `boomerang` and `impactBurst` consume cooldown when their copied projectile burst fires. `spearRain` starts cooldown only when its pending copied projectile hits a valid character and spawns the follow-up projectiles. While these copied projectile presets are on cooldown, or while `spearRain` has a pending carrier in flight, the original weapon secondary attack runs instead of the copied projectile carrier. Other melee presets keep running the copied or vanilla secondary attack while only the preset effect is skipped.
Root `resourceMultiplier` scales only ordinary copied secondary attacks. Melee special presets use their own block-level `resourceMultiplier`.
Cooldown status effects use these item icons: `spearRain`=`SpearWood`, `impactBurst`=`Battleaxe`, `boomerang`=`AxeBronze`, `spinningSweep`=`AtgeirWood`, `knockbackChain`=`FistBjornClaw`, `riftTrail`=`SwordWood`, `aftershock`=`SledgeWood`, `launchSlam`=`MaceWood`, `cleavingThrust`=`THSwordWood`, and `sneakAmbush`=`KnifeWood`.
`Global.sneakAmbush` applies to knife weapons by default. The effect triggers when a configured weapon's secondary attack hits an enemy, but it only consumes cooldown and spawns VFX when the transferred charge is at least 0.25 seconds and the calculated `aggroResetRange` is above 0. Crouching/sneaking with a sneakAmbush weapon builds `chargeSeconds` up to `chargeMaxSeconds`; charge speed lerps from 1.0x at Sneak 0 to `chargeSkillFactor` at Sneak 100, and `Sneak Ambush Charge` shows the prepared amount as 0-100%. Charge decays at a fixed 1.0 charge-second per second when not sneaking, and does not build while sneakAmbush cooldown is active. Starting a secondary attack transfers the current charge to that attack and clears the displayed charge, so a miss spends the preparation. Awareness reset, sense blocking, and backstab timer reset scale only from that transferred amount and are applied after the triggering hit resolves, so that reset does not grant backstab damage to the same hit. The backstab timer reset subtracts from the target's remaining vanilla backstab wait, so targets that have never taken backstab damage remain ready. Other melee weapons can opt in with `preset: sneakAmbush`, with omitted `sneakAmbush` fields inherited from `Global`.
`Global.cleavingThrust` applies to two-handed sword-skill weapons by default. The effect replaces a `greatsword_secondary` melee hit with the whole expanded vanilla fan shape up to `actualSecondaryRange * rangeFactor`, using the attack's actual `attackAngle` and `attackRayWidth` instead of a custom cone. The visual trail scales automatically from `rangeFactor` with an internal correction factor: `1 + max(0, rangeFactor - 1) * 3`. The trail stretch is applied when a cooldown-ready cleavingThrust secondary starts, and does not change hit range. CleavingThrust does not hit through walls. Other melee weapons can opt in with `preset: cleavingThrust`, but the active copied attack must still be the two-handed `greatsword_secondary` style.
`Global.launchSlam` applies to one-handed Clubs weapons by default. It cancels secondary hit push, launches the target upward by `launchHeight`, and deals landing AOE damage equal to `originalSecondaryDamage * damageFactor`. During ascent, the launched target temporarily ignores collision with other character colliders so crowding and large monsters are less likely to cut the launch short; ignored collisions are restored when ascent ends, on landing, or on timeout. The AOE always includes the launched target and uses `min(landingAreaRadiusMax, launchedTargetFootprintRadiusXZ * landingAreaRadiusFactor)` for nearby enemies. `vfx` and `sfx` play once at the landing AOE origin; set either to an empty string to disable it. `vfxRotationOffset` rotates only the landing VFX as `x, y, z` Euler degrees. Landing damage waits at least 0.2 seconds and expires after 3 seconds if the target never lands. Other melee weapons can opt in with `preset: launchSlam`.
`Global.knockbackChain` applies to fist-skill weapons whose secondary attack animation is `unarmed_kick` by default. It multiplies the original secondary hit push by `pushFactor`; while the knocked target is moving faster than the fixed 0.5 m/s threshold, nearby enemy collisions within the fixed 2m radius deal `originalSecondaryDamage * chainPower`, push by `boostedPush * chainPower`, and continue with `chainPower * chainDecay` until 5 chain targets or 1.5 seconds is reached. The same target has a fixed 0.2 second chain hit cooldown. The first weapon hit uses `vfx_archerytarget_bullseye`; chain collisions use `vfx_HitSparks` plus `sfx_club_hit`. Other melee weapons can opt in with `preset: knockbackChain`.
`Global.aftershock` applies to two-handed Clubs weapons with an Area primary or secondary, such as sledges, by default. It resolves an Area primary first, then an Area secondary, so Warfare-style sledges that move the original sledge attack to secondary remain compatible when their new primary is not Area. Each wave uses `scaleFactor * (1 - scaleDecay)^waveIndex` for damage, push, radius, visual scale, and SFX volume; `forwardStep` moves each wave origin along the attacker's initial forward direction.
`Global.impactBurst` applies to battleaxe-like two-handed axe prefabs by default. DualAxe-style prefabs are excluded. It copies the fixed `SpearFlint` throw carrier with `impactBurst.animation`, then applies vanilla Area-style damage and push around the projectile impact point. Damage is `originalProjectileDamage * damageFactor`; push is `originalProjectilePush * pushFactor`. The direct projectile target is excluded from the burst, destructible colliders such as rocks, trees, and pieces are included, and non-character impacts can trigger the burst. `vfx` plays at the burst origin; an empty value disables it. Other melee weapons can opt in with `preset: impactBurst`.
`Global.boomerang` applies to one-handed axe prefabs by default. It copies the fixed `SpearFlint` throw carrier with `boomerang.animation`, flies around a 3D ellipse sized from the aimed hit distance at fixed projectile speed 20, and can damage several valid targets before returning. Valid damaged targets do not stop it; invalid targets, terrain, and walls stop it through the copied projectile's normal hit. Damage decays per valid hit without a floor: `originalProjectileDamage * damageFactor * (1 - hitDamageDecay)^hitIndex`. Return/catch behavior is fixed to right side, 0.8m despawn distance, 1.2m catch radius, 0.25s catch delay, 0.25s same-target hit cooldown, and auto-equip enabled. Hit count is not capped beyond the flight path, flight time, and fixed same-target hit cooldown. Other melee weapons can opt in with `preset: boomerang`.
`Global.spinningSweep` applies to polearm weapons whose original secondary animation is `atgeir_secondary` by default. It copies an atgeir-style secondary attack and loops it until resources run out, the weapon changes, or secondary attack is pressed again; set `preset: none` on a listed polearm to keep its original secondary. The active attack animation is rewound from `loopEnd` to `loopStart` so the spin section can visually chain without replaying the full wind-up every time; `animationSpeed` scales the animator while the sweep is active. Each loop pays the configured secondary raw resource cost scaled by `spinningSweep.resourceMultiplier`; `moveSpeedFactor` controls how much the player can move during each spin, while turn speed uses the default attack value.
`Global.riftTrail` applies to one-handed sword secondary attacks by default. The runtime samples the actual `MeleeWeaponTrail` base/tip transforms during the swing, reuses the trail's material/color/size settings to leave a vanilla-style world-space ribbon, and tints it with `visualTint`. `duration` controls how long the damage ticks continue; the visual stays fully visible for that duration, then fades out over a fixed 1 second. `visualForwardOffset` moves only that visual ribbon along the attacker's forward direction; the damage area stays on the actual secondary attack shape. It ticks every `tickInterval`, dealing `originalSecondaryDamage * damageFactor` inside the actual one-handed sword secondary attack shape: `attackRange`, `attackAngle`, and `attackRayWidth`. RiftTrail does not hit through walls and does not include destructibles. Each tick applies vanilla-style multi-target reduction: damage and push are multiplied by `1 / (hitTargets * 0.75)` when more than one target is hit. Each tick reuses the secondary hit SFX/VFX up to `hitEffectLimit` targets. Other melee weapons can opt in with `preset: riftTrail`, but the active copied attack must still be the one-handed `sword_secondary` style.
`Treat Scythes As Tools` changes scythe/Farming harvest item prefabs to `ItemType.Tool` after ObjectDB restore/apply, then patches `ItemData.IsWeapon()` so those tools can still start primary and secondary attacks. This lets tool-slot systems such as Jewelcrafting classify them as tools; weapon-only gem effects may no longer apply unless the gem also supports `Tool` or `All`.
`Global.spearRain` applies to spear-skill weapons by default. Those weapons copy their own projectile secondary throw and attach `spearRain`; set `preset: none` on a listed spear prefab to opt out. `Global.spearRain` also supplies inherited field values for listed non-spear prefabs that set `preset: spearRain`.

`preset: spearRain` adds extra no-pickup weapon-visual projectiles after a copied projectile secondary hits a valid character. The follow-up projectiles use the copied throw projectile prefab and current weapon mesh/hit effects, but item respawn/drop fields are suppressed so they cannot duplicate the real weapon. The initial rain point uses a small clamped velocity lead, then the follow-up projectiles guide toward a short-lived internal marker attached to the hit target.

### `CaptainValheim.TerrainTools.yml`

This file accepts two flat root-entry shapes. `Global` or weapon prefab roots with `fractureLine`, `terrainDig`, or `harvestSweep` are merged into the melee weapon config path. Hoe/Cultivator roots whose children are piece prefab names keep the existing piece cost/range behavior.

`Global.fractureLine` applies to pickaxe weapons by default and can also be opted into on listed tool weapon prefabs. It copies a valid melee secondary, or the primary attack if no secondary exists, then leaves a ground crack. `duration` controls how long the crack damage ticks continue; the visual stays fully visible for that duration, then fades out over a fixed 1 second. Raw attack resource cost is `copiedAttackRawCost * fractureLine.resourceMultiplier`. Every `tickInterval`, the grounded main crack line is sampled every `hitSpacing`; each sample deals `copiedAttackDamage * damageFactor` within radius `hitSpacing` and a fixed 1.2m surface height to enemy characters and valid destructibles. Each tick applies the same `1 / (hitTargets * 0.75)` multi-target reduction to damage and push when more than one target is hit. Push uses a fixed `0.3` factor. The fixed ground crack visual is always enabled; branch cracks are visual only. Cooldown reduction always uses Pickaxes skill.

`Global.terrainDig` applies to pickaxe primary terrain digging by default. It is a utility block, so it can coexist with one selected melee preset such as `fractureLine` or `riftTrail`, but it does not replace the pickaxe secondary attack. The player adjusts one selected dig scale with the configured `Secondary Attack Wheel Modifier Hotkey` + mouse wheel; that scale is applied to both radius and depth. The maximum selectable scale is `min(radiusScaleMax, depthScaleMax)` and is not gated by Pickaxes skill. Extra stamina uses `1 + (selectedScale^3 - 1) * staminaFactor`, and extra durability uses `1 + (selectedScale^3 - 1) * durabilityFactor`. Cooldown reduction always uses Pickaxes skill.

`Global.harvestSweep` applies to scythe/Farming weapons by default. The default fallback copies `AtgeirIron` secondary with `harvestSweep.animation`, keeps the spinning secondary loop active until resources run out, the weapon changes, or secondary attack is pressed again, and harvests harvestable `Pickable` targets plus foraging-style edible respawning `Pickable` targets while the player steers movement with normal WASD input. The active attack animation is rewound from `loopEnd` to `loopStart`, `animationSpeed` scales the animator while active, and `moveSpeedFactor` controls movement speed during the spin without forcing movement. Each copied sweep harvests once when it reaches `loopEnd`, using the equipped scythe primary attack's harvest center and Farming-scaled harvest radius; vanilla Scythe values are 1.5m at Farming 0 and 2.5m at Farming 100. Cooldown reduction is always based on Farming skill. The weapon `MeleeWeaponTrail` stretches automatically in proportion to the actual Farming-scaled harvest radius.

Respawning `Pickable` and healthy `Plant` hover text shows only the remaining time in seconds, such as `17280s`; ready targets add no extra line.

Cooldown status effects for tool-domain presets use these item icons: `fractureLine`=`PickaxeAntler`, `terrainDig`=`PickaxeStone`, and `harvestSweep`=`Scythe`.

### `CaptainValheim.BloodMagic.yml`

Uses `type` because these entries select different behavior families.

### `type: summonEmpower`

Buffs nearby matching summons created by the same summon-staff family.

Important fields:

- `animation`
- `radius`
- `duration`
- `moveSpeedPerBloodMagic`
- `attackCooldownReductionPerBloodMagic`

The remaining buff time is shown as synced overhead text.

### `type: shieldConvert`

Finds nearby targets with an active `StaffShield` shield, heals them for `remainingShield * healFactor`, and optionally removes the shield.

Important fields:

- `animation`
- `radius`
- `healFactor`
- `consumeShield`

The current shield remainder is shown as synced overhead text, including in multiplayer.

### `CaptainValheim.BloodMagic.yml` `magicSummons:`

Overrides the summon prefab list behind a summon item/projectile. Use `targetItem` for vanilla-style staff items such as `StaffSkeleton` or `StaffRedTroll`, or `targetProjectile` for modded summon projectile prefabs. A single `sourcePrefab` entry clones one MonsterAI prefab; a `summons:` list clones several and assigns them all to the same `SpawnAbility`.

Important fields:

- `targetItem`
- `targetProjectile`
- `sourcePrefab`
- `clonePrefab`
- `displayName`
- `health`
- `scale`
- `levelUpFactor`
- `summons`
- `qualityPreset`
- `maxQuality`
- `fixedSummonLevel`

## Shield Block

Use `CaptainValheim.Shields.yml` only on shield prefabs.

Every shield prefab not listed in `CaptainValheim.Shields.yml` gets the `Global` shield values automatically. If `Global` is omitted, built-in shield defaults are used.

### `primaryAttack`, `throw`, `charge`

Enable shield-only primaryAttack/throw/charge behavior.

- Primary while shield-only: primaryAttack
- Secondary normally: throw
- Secondary while blocking: charge
- Omit a block or field to keep the inherited `Global`/built-in value
- Set `enabled: false` on a shield feature block to disable that feature after inheritance
- `charge.hitRadiusFactor` scales `sqrt(deflectionForce / 20)`; the default `0.4` makes deflectionForce `20` use radius `0.4`
- `primaryAttack.durabilityFactor`, `throw.durabilityFactor`, and `charge.durabilityFactor` scale shield durability cost from the shield's vanilla `useDurabilityDrain`
- `charge.cooldown` is reduced by Blocking skill with `cooldown * (1 - BlockingSkill / 100 * cooldownReductionFactor)`
- Charge cooldown appears as a status effect using the active shield's icon
- Shield throw has no cooldown, status effect, or cooldown HUD entry; it is limited by stamina, durability, projectile travel, and shield return/re-equip time.
- `throw.targets` is the maximum number of valid enemy targets hit, including the first enemy hit. Terrain and wall bounces do not spend targets.
- `throw.damageDecay` reduces damage after each enemy target hit as `nextDamage *= (1 - damageDecay)`.
- `throw.radiusFactor` sets `bounceSearchRadius = radiusFactor / cbrt(deflectionForce / 20)`
- `throw.ttlFactor` sets `ttl = max(0.3, ttlFactor / cbrt(deflectionForce / 20))`; effective flight distance is projectile speed multiplied by this TTL

### `reflect`

Enables guard-based projectile reflection.

Important fields:

- `staminaFactor`
- `reflectionFactor`

### `blockCharge`

Enables Valheim's vanilla block-charge counterattack on matching shield prefabs. `blockCharge.enabled: true` keeps the shield prefab's own vanilla charge count and decay values unless optional count/decay fields are set; inherited `Global`/built-in shield defaults also keep those prefab values. If a shield has no charge VFX, CaptainValheim fills vanilla charge effects up to the final `chargeCount`; if the shield has zero attack damage, it gets 5 generic block-charge damage. Set `blockCharge.enabled: false` to keep a shield's original block-charge values.

Optional fields:

- `chargeCount`
- `decayTime`
- `blockingDecayFactor`

## Warfare Effects

Warfare-native weapon effect tuning has moved out of CaptainValheim into the standalone `WarfareTweaks` mod. Use `BepInEx/config/WarfareTweaks/WarfareTweaks.Warfare.yml` there for Warfare and WarfareFireAndIce effect assignment/tuning.

## Notes

- `animation` uses the exact attack trigger/key.
- Root `resourceMultiplier` scales ordinary copied raw attack costs for non-shield secondary definitions. Melee special presets use their own block-level `resourceMultiplier`. For reload weapons, secondary fire charges or refunds the difference so the net reload resource cost is `vanilla reload cost * resourceMultiplier`.
- `RequiresReload` weapons keep loaded state on the item until fired, including after swapping, unequipping, dropping, or relogging.
- Ranged projectile secondary attacks using the `staff_rapidfire` looping animation keep firing while secondary attack is held.
- `Backstab Sneak Skill Raise Amount` awards Sneak skill when any attack successfully triggers backstab damage; set it to `0` to disable this reward.
- `Blood Magic Health Cost Skill Raise Factor` replaces vanilla Blood Magic skill gain with actual-health-cost-based gain when above `0`; set it to `0` to keep vanilla Blood Magic skill gain.
- `Blood Magic Health Cost Uses Max Health` makes Blood Magic percentage health costs use max health at cast time instead of current health; flat health cost and skill cost reduction are unchanged.
- `Sneak Movement Speed Skill Factor` scales crouched movement speed by Sneak skill. `1.0` keeps vanilla; `2.0` means Sneak 100 moves twice as fast while crouching.
- `Enable Backstab Hover Debug` adds the target's vanilla backstab timer readiness and alert state to enemy hover text for local debugging.
- `cleavingThrust` stretches the visual melee trail when a cooldown-ready secondary starts. Hit range is controlled by `cleavingThrust.rangeFactor`; visual length follows it automatically with a fixed internal correction.
- `riftTrail` captures the real weapon `MeleeWeaponTrail` to build its lingering visual ribbon, and falls back to a simple arc only when no trail samples are available.
- `Treat Scythes As Tools` is a synced Farming override that exposes scythe/Farming harvest weapons as tools for compatibility with tool-slot effects, while preserving attack support through an `IsWeapon()` compatibility patch.
- Copied throw visuals with `projectileSpinAxis: horizontal` or `vertical` spin at a fixed 720 degrees per second. `projectileSpinAxis` on `impactBurst` and `boomerang` accepts `none`, `horizontal`, or `vertical`. If omitted, `impactBurst` and `boomerang` default to `horizontal`. `projectileVisualRotationOffset` rotates the copied weapon visual before spin as `x, y, z` Euler degrees, for example `projectileVisualRotationOffset: 0, 90, 0`. `spearRain` keeps no added spin or visual rotation offset.
- `Ranged.yml` uses `damageFactor` for projectile damage per hit; it does not scale push or stagger.
- Ranged projectile skill/adrenaline rewards are automatic: `piercingPrimary` uses 1x, `stickyDetonator` and `overchargedBomb` use 0x, and other count-based presets use 1/count per projectile hit.
- `Ranged.yml` uses `projectileSpeedFactor` for `piercingPrimary` projectile speed scaling.
- `outputMultiplier` scales copied attack `damage_multiplier`, `force_multiplier`, and `stagger_multiplier`.
- Shield entries do not use `resourceMultiplier` or `outputMultiplier`; tune shield damage, push, stamina, reflect power, and vanilla block charges with `primaryAttack`, `throw`, `charge`, `reflect`, and `blockCharge` fields directly.
- The generated default `Ranged`, `Melee`, `BloodMagic`, `Shields`, and `TerrainTools` YAML files include working examples or commented templates.

## Related

- Shield-specific formulas and behavior details: [SHIELD_SPECIALS.md](./SHIELD_SPECIALS.md)


