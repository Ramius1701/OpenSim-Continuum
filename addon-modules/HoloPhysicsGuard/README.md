# HoloPhysicsGuard

**HoloPhysicsGuard** is an OpenSimulator region module that reduces idle physics load by putting selected physical objects to sleep when a region becomes empty, then waking them again when an avatar enters.

It is designed for objects such as bowling pins, bowling balls, vehicles, props, or other physical items that can keep a region’s physics engine busy even when nobody is present.

Physics should not get to run the nightclub after closing.

---

## What It Does

When a region becomes empty, HoloPhysicsGuard can:

- detect physical root prims/linksets
- optionally filter by object name
- record the object in a separate database table
- clear the object’s physical flag using OpenSim’s normal `UpdatePrimFlags()` path
- zero velocity and angular velocity
- reduce idle physics load

When an avatar enters the region, it can:

- look up previously slept objects
- restore their physical flag
- remove the sleep record
- clean up stale records for deleted objects

---

## Why This Exists

Some physical objects can heavily impact region performance, especially when they are stuck colliding, poorly shaped for physics, or scripted to remain active.

A real example:

```text
Before:
AtvPrm 45
SimFPS 0-4
PhyFPS 0.0-0.4
PhysFt ~242 ms

After sleeping physical objects:
AtvPrm 0
SimFPS ~54
PhyFPS ~54
PhysFt ~0.02 ms
```

This module is not meant to replace good object scripting. It is a safety guard for idle regions.

---

## Features

- OpenSimulator non-shared region module
- Sleeps physical objects when a region is empty
- Wakes slept objects when an avatar enters
- Optional wake-on-start behavior
- Separate database table for reversible sleep state
- Dry-run/report-only mode
- Name-based allow/block filters
- Persistent sleep tracking across restarts
- Uses OpenSim’s `UpdatePrimFlags()` path instead of directly rebuilding physics actors

---

## Recommended Use Cases

Good candidates:

```text
bowling_pin
bowling_ball
vehicles
physics toys
temporary game props
physical NPC props
```

Objects to be careful with:

```text
elevators
boats
scripted rides
region infrastructure
anything expected to remain physical all the time
```

---

## Installation

Build the module as an addon module and copy the DLL into the OpenSim `bin/` directory.

Typical output files:

```text
HoloPhysicsGuard.dll
HoloPhysicsGuard.pdb
```

The `.pdb` file is optional but useful for debugging stack traces.

Make sure production uses the same OpenSim build/runtime as the machine where the module was built.

---

## Addin Metadata

The module uses Mono.Addins attributes.

The working pattern for OpenSim 0.9.3.x is:

```csharp
using Mono.Addins;

[assembly: Addin("HoloPhysicsGuard", "0.4")]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]
```

And on the class:

```csharp
[Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "HoloPhysicsGuard")]
public class HoloPhysicsGuard : INonSharedRegionModule
{
}
```

After copying or rebuilding the module, it may be necessary to clear the addin cache:

```bash
cd /opt/opensim/bin
rm -rf addin-db-* addins-db-*
```

Then restart OpenSim.

---

## Database Table

HoloPhysicsGuard uses a separate table to remember objects that were physical before being put to sleep.

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

The module can create this table automatically if `AutoCreateTable = true`.

---

## Configuration

Add this section to your OpenSim configuration.

### Conservative production example

```ini
[HoloPhysicsGuard]
Enabled = true
Mode = PersistSleep
DryRun = false

ConnectionString = Data Source=127.0.0.1;Database=opensim;User ID=opensim;Password=YOUR_PASSWORD;Charset=utf8mb4;

AutoCreateTable = true

SleepWhenEmpty = true
WakeOnStart = false
WakeOnAvatarEnter = true

CheckIntervalSeconds = 30
EmptyDelaySeconds = 60

ZeroVelocities = true
Verbose = false

AlwaysSleepNameContains = bowling_pin,bowling_ball
NeverSleepNameContains = elevator,boat,vehicle
```

### Broad dev/test example

```ini
[HoloPhysicsGuard]
Enabled = true
Mode = PersistSleep
DryRun = false

ConnectionString = Data Source=127.0.0.1;Database=opensim;User ID=opensim;Password=YOUR_PASSWORD;Charset=utf8mb4;

AutoCreateTable = true

SleepWhenEmpty = true
WakeOnStart = false
WakeOnAvatarEnter = true

CheckIntervalSeconds = 5
EmptyDelaySeconds = 10

ZeroVelocities = true
Verbose = true

AlwaysSleepNameContains =
NeverSleepNameContains =
```

---

## Configuration Options

### `Enabled`

Turns the module on or off.

```ini
Enabled = true
```

### `Mode`

Supported values:

```text
ReportOnly
PersistSleep
```

`ReportOnly` logs what would be slept but does not change objects.

`PersistSleep` records objects in the guard table, clears their physical flag, and wakes them later.

```ini
Mode = PersistSleep
```

### `DryRun`

When true, forces report-only behavior.

```ini
DryRun = true
```

### `SleepWhenEmpty`

Controls whether the module sleeps objects when the region has no root agents.

```ini
SleepWhenEmpty = true
```

### `WakeOnStart`

If true, slept objects are woken when the region starts.

If false, slept objects remain asleep until an avatar enters.

```ini
WakeOnStart = false
```

### `WakeOnAvatarEnter`

If true, slept objects are restored when the first avatar enters the region.

```ini
WakeOnAvatarEnter = true
```

### `CheckIntervalSeconds`

How often the module checks region occupancy.

Recommended values:

```text
Dev/testing:               5
Known problem objects:     30
General production guard:  60
```

### `EmptyDelaySeconds`

How long a region must remain empty before objects are slept.

Recommended values:

```text
Dev/testing:               10
Known problem objects:     60
General production guard:  300
```

### `AlwaysSleepNameContains`

Comma-separated list of object name fragments that are eligible for sleep.

If empty, all physical root objects are eligible unless blocked by `NeverSleepNameContains`.

```ini
AlwaysSleepNameContains = bowling_pin,bowling_ball
```

### `NeverSleepNameContains`

Comma-separated list of object name fragments that should never be slept.

```ini
NeverSleepNameContains = elevator,boat,vehicle
```

### `ZeroVelocities`

Sets velocity and angular velocity to zero when sleeping an object.

```ini
ZeroVelocities = true
```

### `Verbose`

Logs more details about scans, sleeps, wakes, and skipped objects.

```ini
Verbose = false
```

---

## Useful SQL

### Show physical root prims in all regions

```sql
SELECT
    r.regionName,
    p.UUID,
    p.Name,
    p.SceneGroupID,
    p.PositionX,
    p.PositionY,
    p.PositionZ,
    p.ObjectFlags,
    (p.ObjectFlags & 1) AS IsPhysical
FROM prims p
JOIN regions r ON r.uuid = p.RegionUUID
WHERE p.UUID = p.SceneGroupID
  AND (p.ObjectFlags & 1) != 0
ORDER BY r.regionName, p.Name;
```

### Show slept objects

```sql
SELECT
    h.region_uuid,
    r.regionName,
    h.object_uuid,
    h.object_name,
    h.original_object_flags,
    FROM_UNIXTIME(h.slept_at) AS slept_at
FROM holo_physics_guard_sleep h
LEFT JOIN regions r ON r.uuid = h.region_uuid
ORDER BY h.slept_at DESC;
```

### Show slept objects and current physical state

```sql
SELECT
    h.region_uuid,
    r.regionName,
    h.object_uuid,
    h.object_name,
    p.ObjectFlags,
    (p.ObjectFlags & 1) AS IsPhysical,
    FROM_UNIXTIME(h.slept_at) AS slept_at
FROM holo_physics_guard_sleep h
LEFT JOIN prims p ON p.UUID = h.object_uuid
LEFT JOIN regions r ON r.uuid = h.region_uuid
ORDER BY h.slept_at DESC;
```

### Clear stale sleep records for deleted objects

```sql
DELETE h
FROM holo_physics_guard_sleep h
LEFT JOIN prims p ON p.UUID = h.object_uuid
WHERE p.UUID IS NULL;
```

---

## Checking Region Stats

In the OpenSim console:

```text
change region Region Name
show stats
```

Important fields:

```text
Dilatn   region time dilation
SimFPS   simulator frames per second
PhyFPS   physics frames per second
AtvPrm   active physical prims
PhysFt   physics frame time
```

Healthy idle region example:

```text
Dilatn 1.00
SimFPS 54
PhyFPS 54
AtvPrm 0
PhysFt 0.02
```

Troubled physics-heavy region example:

```text
Dilatn 0.00
SimFPS 0-4
PhyFPS 0.0-0.4
AtvPrm 45
PhysFt 240+
```

---

## Notes on Physics Updates

Earlier versions directly changed flags and called:

```csharp
sog.ApplyPhysics();
```

In some OpenSim builds this can duplicate active physics actors.

HoloPhysicsGuard should use the normal OpenSim flag update path:

```csharp
part.UpdatePrimFlags(usePhysics, setTemporary, setPhantom, setVolumeDetect, false);
```

This avoids duplicated physics actors and prevents objects from visibly twitching when woken.

---

## Deployment Checklist

1. Copy module files:

```bash
scp HoloPhysicsGuard.dll HoloPhysicsGuard.pdb root@server:/opt/opensim/bin/
```

2. Fix ownership:

```bash
chown opensim:opensim /opt/opensim/bin/HoloPhysicsGuard.dll /opt/opensim/bin/HoloPhysicsGuard.pdb
chmod 644 /opt/opensim/bin/HoloPhysicsGuard.dll /opt/opensim/bin/HoloPhysicsGuard.pdb
```

3. Confirm dependencies exist:

```bash
ls -lh /opt/opensim/bin/Mono.Addins.dll
ls -lh /opt/opensim/bin/MySql.Data.dll
```

4. Clear addin cache:

```bash
cd /opt/opensim/bin
rm -rf addin-db-* addins-db-*
```

5. Restart OpenSim.

6. Check logs:

```bash
grep -i "HoloPhysicsGuard\|HOLO PHYSICS GUARD" /opt/opensim/bin/OpenSim.log
```

Expected log lines include:

```text
[HOLO PHYSICS GUARD]: Initialise called
[HOLO PHYSICS GUARD]: Enabled
[HOLO PHYSICS GUARD]: Added region ...
```

---

## Safety Recommendations

Start narrow in production:

```ini
AlwaysSleepNameContains = bowling_pin,bowling_ball
```

Do not immediately sleep every physical object on a live grid. Some physical objects may be intentionally active, such as vehicles, elevators, boats, or scripted rides.

Use `ReportOnly` or `DryRun = true` first if you are unsure.

---

## License

See LICENSE.txt

