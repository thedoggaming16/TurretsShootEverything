# Turrets Shoot Everything

Turrets no longer ignore monsters. They now fire at **players and every enemy** in line of sight.

## Features

* Turrets target any killable enemy in range with line of sight (vanilla logic still handles players, and players keep priority).
* Fully configurable: range, damage per bullet, fire rate, charge time, per-enemy ignore list.
* Host-authoritative damage: only the host needs to run the logic, but everyone should have it installed for matching configs.
* **ToilHead**: turret-head enemies are treated like any other enemy; a turret never shoots the creature it is attached to. Optional `SpareEnemiesWithOwnTurret` setting.
* **FairAI**: this mod patches no AI methods (only a harmless postfix on `Turret.Update`), so there is no overlap.

## Config (`BepInEx/config/thedoggaming16.TurretsShootEverything.cfg`)

|Setting|Default|Description|
|-|-|-|
|Enabled|true|Master switch|
|Range|30|Max target distance|
|DamagePerShot|1|Damage per bullet to enemies|
|FireInterval|0.21|Seconds between bullets|
|ChargeTime|1.0|Tracking time before firing|
|SpareEnemiesWithOwnTurret|false|Don't shoot enemies carrying a turret|
|IgnoredEnemies|(empty)|Comma-separated enemy names to never shoot|

## Known limitations

* Muzzle flash/sound for enemy shots are played on the host only.
* Please report incompatibilities with other mods on the GitHub issues page.

