# OpenSimWeather 0.3.4

OpenSimWeather is an experimental but production-conscious OpenSimulator region
module that provides Clear, Sunny, Rain, Storm, and Snow conditions. It uses
viewer-visible particle systems and can optionally coordinate wind,
LightShare/EEP-style region environment profiles, lightning, thunder, forecast
messages, and temporary terrain surface effects.

This release replaces the initial OpenSim Enhanced Weather implementation.
It retains the useful multiple-emitter architecture while correcting unsafe,
non-portable, incomplete, and hard-coded behavior in the earlier module.

## What "real-world style" means here

The module models weather as related systems instead of one visual effect:

- precipitation cells with wind drift;
- different sky/cloud profiles for sunny, rain, storm, and snow;
- lightning and spatial thunder during storms;
- roof/interior suppression at the weather-cell level;
- temperature-aware wetness, drying, snowfall, and melting;
- optional puddle and snow overlays on suitable outdoor terrain.

It does **not** retrieve live meteorological data. Weather is selected manually
or by the configured automatic cycle. No external web service is required.

## Safe defaults

The complete reference is intentionally conservative:

- a missing `[Weather]` section leaves the module disabled;
- `Enabled = false` always disables it;
- the private command channel defaults to 89 and channel 0 is rejected;
- `CoverageMode = Region` with `EmitterGrid = 8` restores full-region coverage across standard and variable-sized regions;
- automatic cycling, entry IMs, thunder, wind changes, environment changes,
  puddles, and snow accumulation are opt-in;
- environment changes require a second explicit persistence acknowledgement;
- surface effects require configured alpha texture UUIDs and otherwise disable
  themselves;
- generated emitters, flashes, puddles, and snow patches are inserted as
  non-backup objects and marked temporary;
- non-persistent weather objects left by an interrupted module instance can be
  removed automatically during region load.

## Installation

Copy the package directory to:

```text
addon-modules/OpenSimWeather
```

From the OpenSim source root:

```text
runprebuild.bat
compile.bat
```

A Visual Studio build can be used after `runprebuild.bat`. The included project
and prebuild files use repository-relative paths and contain no local drive
references.

Place `[Weather]` and any `[Weather.Environment.*]` sections in `OpenSim.ini` or
an INI file included by `OpenSim.ini`. Do not place module configuration in
`Regions.ini`.

For a production replacement, follow `UPGRADE.md` rather than copying a new DLL
into a running simulator process.

## Configuration examples

- `config/OpenSimWeather.ini.example` — common disabled installation baseline.
- `config/OpenSimWeather.reference.ini.example` — complete disabled reference.
- `config/OpenSimWeather.manual.ini.example` — controlled first test.
- `config/OpenSimWeather.autocycle.ini.example` — automatic particle weather.
- `config/OpenSimWeather.environment.ini.example` — persistent environment and
  wind integration example.
- `config/OpenSimWeather.surface.ini.example` — experimental puddle and snow
  accumulation example.
- `assets/README.md` — texture and sound asset requirements.

## Commands

With `CommandChannel = 89`:

```text
/89 weather status
/89 weather sunny
/89 weather rain
/89 weather storm
/89 weather snow
/89 weather clear
```

`meteo` is retained as an alias. The command and weather state are parsed as
exact tokens, so unrelated text containing words such as `rain` is ignored.
Replies are sent by the object-style chat source named `Weather`, so reply text
does not repeat a second `Weather:` prefix.
When `EstateManagerOnly = true`, only estate managers/owners can change the
weather.

`weather status` reports the current weather, emitter count, coverage mode,
known exclusion volumes, surface patch count, wetness, snow level, configured
temperature, and auto-cycle state.

`weather clear` clears the active condition but does not disable an enabled
auto-cycle timer.

## Region and varregion coverage

The compatibility/default layout restores the behavior that worked before the
active-area redesign:

```ini
CoverageMode = Region
EmitterGrid = 8
EmitterRadiusScale = 0.62
```

`EmitterGrid` is a count per axis, not a metre measurement. The module uses the
actual `RegionSizeX` and `RegionSizeY` to recalculate each cell's spacing,
position, and particle burst radius. An 8x8 layout therefore remains 64
temporary emitter objects on a standard region or a varregion while scaling the
coverage to the full dimensions.

Advanced operators can instead select fixed-metre spacing:

```ini
CoverageMode = Region
EmitterGrid = 0
EmitterSpacingMeters = 24.0
MaxEmitters = 384
```

In spacing mode, independent X and Y counts are calculated from the region
dimensions. When the requested count exceeds `MaxEmitters`, the layout is
reduced proportionally and the reduction is logged.

`ActiveArea` remains available as an explicit optimization that creates cells
near root agents and removes cells no longer needed. It is no longer the
implicit or documented production default because it changes full-region
behavior and can leave unoccupied portions of a region without weather.

## Particle textures

Every visual asset is configurable:

```ini
RainTexture = <asset UUID>
StormTexture = <asset UUID>
SnowTexture = <asset UUID>
LightningTexture = <asset UUID>
```

These values are OpenSim asset UUIDs, not filenames or inventory item names.
Blank rain or snow textures fall back to OpenSim's blank texture and therefore
can look like rectangular or square billboards. Upload an alpha rain streak and
alpha snowflake texture, then paste the resulting asset UUIDs into the
configuration. No made-up universal texture UUID is bundled.

## Buildings and indoor weather

OpenSim particles do not physically collide with roofs, walls, or terrain. The
module can suppress weather **cells**, but it cannot make each particle detect a
building.

The repaired system uses:

1. a configurable odd grid of sky probes across every proposed cell;
2. `MinimumOpenSkyFraction` to scale or reject partially covered cells;
3. optional suppression of the active weather bubble around covered avatars;
4. explicit named exclusion volumes for complex interiors.

A practical production baseline is:

```ini
AvoidCoveredAreas = true
CoverProbeGrid = 3
MinimumOpenSkyFraction = 0.67
SuppressAroundCoveredAvatars = true
CoverIncludesPhantomPrims = false
UseExclusionVolumes = true
ExclusionObjectName = Weather Exclusion
```

For a building, create a transparent phantom box or linkset covering its indoor
X/Y footprint and name the root object `Weather Exclusion` or begin its name
with `Weather Exclusion `. The module treats the object's axis-aligned X/Y
bounds as a no-weather column. This is more reliable for caves, hangars,
underground rooms, irregular mesh structures, and phantom roofs than physics
ray tests alone.

The exclusion object itself is ordinary region content and should be made
invisible/phantom by the builder. The module does not create or delete it.

Even with these controls, particles near a cell boundary can occasionally be
visible through an adjacent wall or overhang. That is a viewer particle-system
limitation, not real collision simulation.

## Configurable environment profiles

`AdjustEnvironment` applies configurable weather-specific sky/cloud values from:

- `[Weather.Environment.Sunny]`
- `[Weather.Environment.Rain]`
- `[Weather.Environment.Storm]`
- `[Weather.Environment.Snow]`

All fields changed by the module are exposed in those sections. The module
starts with the current region LightShare data, applies the complete configured
weather sky/cloud profile including `SunMoonPosition`, `EastAngle`, and
`MaxAltitude`, and preserves water fields and other unmanaged environment
values.

Each missing profile or missing individual key uses the built-in profile value.
The built-in sun-position values come from OpenSim's `RegionLightShareData`
defaults, preventing a dark pre-weather sun position from leaking into Sunny.
The shipped complete example exposes every applied field.

### Persistence warning

OpenSim stores region environment changes. They are not merely a temporary
viewer overlay. Therefore both settings are required:

```ini
AdjustEnvironment = true
AllowPersistentEnvironmentChanges = true
```

The module records the pre-weather environment and restores it on clear and
normal shutdown when `RestoreCloudsOnClear = true`. If the region originally
inherited its environment, restoration uses the environment reset path rather
than storing a copied custom default. A process crash or power loss can still
occur before restoration. Export or record a custom environment before enabling
this feature.

## Wind

Wind adjustment supports OpenSim's `SimpleRandomWind` and `ConfigurableWind`
plugins. Unsupported active plugins are detected and left unchanged. Saved
parameters are restored on clear and normal shutdown when
`RestoreWindOnClear = true`.

Particle drift is also derived from the configured direction and strength, so
visible precipitation remains coordinated even when region wind integration is
disabled.

## Lightning and thunder

Storm strikes retry several candidate locations and prefer an outdoor,
non-excluded point. The lightning object begins near terrain rather than at the
precipitation emitter altitude. Thunder is sent from the strike position.

Thunder requires a valid uploaded sound UUID:

```ini
ThunderEnabled = true
ThunderSound = <asset UUID>
```

Visual lightning can remain enabled without a sound asset.

## Puddles and snow accumulation

Surface effects are experimental and disabled by default. They are temporary
terrain overlays near active avatars; they do not rewrite terrain textures,
modify the terrain heightmap, or persist as saved region objects.

The model tracks normalized wetness and snow levels:

- rain and storms increase wetness;
- snow accumulates when `TemperatureC` is near or below `FreezingPointC`;
- warmer snowfall contributes wetness instead;
- clear and sunny states dry puddles and melt snow at configurable rates;
- rain accelerates snow melt;
- more qualifying patches appear as the level rises.

Puddles are restricted to shallow outdoor terrain, below the configured maximum
slope, above water, and near local low points. Snow uses similar outdoor and
slope checks. Both effects honor exclusion volumes and cover tests.

Required assets:

```ini
SurfaceEffectsEnabled = true
PuddleTexture = <alpha puddle asset UUID>
SnowSurfaceTexture = <feathered snow asset UUID>
```

A blank required surface texture disables that effect instead of creating a
solid default disk. Accumulation currently applies only to terrain—not roofs,
mesh floors, prim roads, or vegetation—and resets when the module or simulator
restarts. This is intentional for production safety.

## Generated objects and cleanup

Weather-created objects use names beginning with `OpenSimWeather:` and a marker
description. They are:

- added with scene backup disabled;
- marked `TemporaryOnRez`;
- marked as temporary module instances;
- deleted when the weather changes, clears, the region unloads, or the module
  shuts down.

`CleanupGeneratedObjectsOnStartup = true` removes matching **non-persistent**
objects left by an interrupted earlier module instance. It deliberately does
not delete saved region objects merely because their name happens to resemble a
weather object.

## First production test

Use a duplicate/non-public region process first:

1. Install the source and run a clean prebuild/build.
2. Start with `OpenSimWeather.manual.ini.example` and `Enabled = false`.
3. Add valid particle texture UUIDs, then enable the module.
4. Verify `weather status`, rain, storm, snow, sunny, and clear.
5. Confirm the default 8x8 Region layout produces 64 or fewer emitters after cover suppression on each region size.
6. Test a building with sky probes, then add `Weather Exclusion` volumes where
   needed.
7. Restart once while particle weather is active and verify no generated objects
   are restored from backup.
8. Enable environment, wind, auto-cycle, and surface effects one subsystem at a
   time.
9. Watch simulator frame time, physics time, object updates, and viewer behavior
   before deploying to all production regions.

See `AUDIT.md`, `CHANGELOG.md`, and `VALIDATION.txt` for the repair record and
remaining limitations.
