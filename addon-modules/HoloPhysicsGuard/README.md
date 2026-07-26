# HoloPhysicsGuard 0.5.0 — Continuum Reconciliation

HoloPhysicsGuard is an OpenSimulator non-shared region module originally created by Fiona Sweet / HoloNeon. It reduces idle physics load by making selected physical root objects nonphysical after a region remains empty, then restoring them when the region becomes occupied.

This reconciliation is based on `holoneon/HoloPhysicsGuard` commit `9f03ac2a9fbb33e0a2304405886d374f58ee2f20`.

## Safety model

The module is disabled by default. Its example configuration starts in `ReportOnly` with `DryRun = true`.

Object selection is conservative:

- `NeverSleepNameContains` always wins.
- A non-empty `AlwaysSleepNameContains` list acts as an allow list.
- An empty allow list sleeps nothing unless `AllowAllPhysicalObjects = true`.

Do not enable broad sleeping on elevators, boats, vehicles, rides, transport systems, or region infrastructure without controlled testing.

## Reconciliation changes

Version 0.5.0 preserves the upstream design while correcting integration risks:

- validates `Mode`; invalid values disable the module
- prevents overlapping timer callbacks
- queries wake state only when occupancy changes from empty to occupied
- retries wake operations when a database or row-deletion failure occurs
- spaces repeated empty-region scans by `EmptyDelaySeconds`
- inherits the MySQL connection from `[DatabaseService]` when the module-specific connection is blank
- uses only the module-owned `holo_physics_guard_sleep` table
- no longer writes directly to OpenSim's `prims` table
- changes physical state through `SceneObjectPart.UpdatePrimFlags()`
- removes deprecated `VALUES(column)` upsert expressions
- aligns assembly and prebuild versioning at `0.5.0`

## Installation in an OpenSim source tree

Place this directory at:

```text
addon-modules/HoloPhysicsGuard
```

Then regenerate and build:

```bat
call runprebuild.bat
dotnet build addon-modules\HoloPhysicsGuard\HoloPhysicsGuard.csproj -c Release --nologo -v:minimal
```

The build output is:

```text
bin\HoloPhysicsGuard.dll
```

Copy the `[HoloPhysicsGuard]` section from `ConfigExample.txt` into `OpenSim.ini` or an explicitly included configuration file.

OpenSim does not automatically load an arbitrary file merely because it is placed under `bin/addon-modules`.

## Standalone and grid use

The module runs in the region simulator process. It does not run inside Robust.

When `ConnectionString` is blank, the module reads the region process's `[DatabaseService] ConnectionString`. This supports normal MySQL-backed Standalone and Grid/Robust region configurations without hard-coded credentials.

Only the module-owned sleep table is accessed directly. OpenSim remains responsible for persisting object flag changes made through `UpdatePrimFlags()`.

## Database table

With `AutoCreateTable = true`, the module creates:

```sql
CREATE TABLE IF NOT EXISTS holo_physics_guard_sleep (
    region_uuid CHAR(36) NOT NULL,
    object_uuid CHAR(36) NOT NULL,
    scene_group_id CHAR(36) NOT NULL,
    object_name VARCHAR(255) NOT NULL DEFAULT '',
    original_object_flags BIGINT UNSIGNED NOT NULL DEFAULT 0,
    slept_at INT UNSIGNED NOT NULL,
    slept_by VARCHAR(64) NOT NULL DEFAULT 'HoloPhysicsGuard',
    PRIMARY KEY (region_uuid, object_uuid),
    KEY idx_region_uuid (region_uuid),
    KEY idx_scene_group_id (scene_group_id),
    KEY idx_slept_at (slept_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

## Controlled validation order

1. Start with `Enabled = true`, `Mode = ReportOnly`, and `DryRun = true`.
2. Use an explicit allow list for one disposable test object.
3. Confirm the report appears only after the region is empty for the configured delay.
4. Change to `Mode = PersistSleep` and `DryRun = false`.
5. Confirm the object becomes nonphysical and a sleep row is created.
6. Enter the region and confirm the object becomes physical and the row is removed.
7. Restart the region while the object is asleep and test both `WakeOnStart` settings.
8. Test database loss during sleep and wake; the simulator must remain running and the operation must retry safely.
9. Test a multi-region process and confirm one region's occupancy does not affect another.

A successful build proves compilation only. It does not certify runtime physics behavior.
