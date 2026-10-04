# Turrets Shoot Everything

Map turrets no longer only hunt players: they now detect and shoot **monsters** too.

## Features
- Turrets target the nearest monster in line of sight and range.
- Vanilla player targeting is untouched, so turrets shoot players *and* monsters.
- Host applies the damage, so it works with vanilla and modded enemies (uses the game's `HitEnemy`).
- Fully configurable.

## Compatibility
- **ToilHead**: turrets attached to enemies are never modified, and monsters carrying their own turret are ignored by default.
- **FairAI**: no patches on enemy AI, so it runs side by side.
- Uses no Harmony patches, so conflicts with other mods are unlikely.
- Only the host needs it for damage; install it for everyone so aiming/effects look right.

## Config (`BepInEx/config/com.example.turretsshooteverything.cfg`)
| Setting | Default | Description |
|---|---|---|
| Range | 30 | Max engagement distance (m) |
| ViewAngle | 180 | Detection cone in degrees (360 = all around) |
| FireInterval | 0.21 | Seconds between shots |
| Damage | 1 | Damage per shot to monsters |
| EnemyBlacklist | (empty) | Comma-separated enemy names to never shoot |
| TargetEnemiesWithTurrets | false | Also shoot monsters that carry a turret |

## Installation
Install with a mod manager (r2modman / Thunderstore Mod Manager) or drop the DLL into `BepInEx/plugins`.
