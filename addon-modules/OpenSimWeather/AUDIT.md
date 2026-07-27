# OpenSimWeather production audit — 0.3.3

## 0.3.3 reconciliation result

The combined Copilot/ChatGPT/Claude module remains the authoritative source.
The audit found that the expanded configuration settings are implemented, but
the 0.3.2 emitter defaults changed established behavior: ActiveArea became the
default and the original dimension-scaled grid was demoted. Version 0.3.3
restores Region plus an 8x8 scaled grid as the normal path, retains the newer
advanced modes, and separates the common example from the complete reference.

## Result

The supplied module contained production-breaking configuration behavior,
non-portable build paths, incomplete rollback, fixed-density assumptions, and
visual/environment behavior that was either hard-coded or misleadingly
documented. Version 0.3.3 keeps the useful multi-emitter design but rebuilds the
configuration and runtime controls around production-safe defaults.

## Critical faults corrected from the supplied production copy

1. **`Enabled = false` was not authoritative.** The old code could ignore the
   master switch through a second undocumented condition, and a missing section
   could activate demonstration defaults. A missing section or false master
   switch now means disabled.

2. **The project was tied to one computer.** References to a developer-specific
   `S:\...` source/build tree were replaced with repository-relative paths.

3. **Weather transitions could race.** Chat commands, automatic cycling,
   surface updates, active-area refreshes, clear operations, and shutdown now
   serialize state-changing work.

4. **Rollback was incomplete.** The first repair rolled back the old
   full-region emitter list but could miss emitters created directly by the new
   active-area path. A failed start now drains and deletes the complete tracked
   emitter set before environment/wind restoration.

5. **Environment persistence was understated.** Applying LightShare/EEP through
   the region environment interface stores region settings. The feature now
   defaults off and requires `AllowPersistentEnvironmentChanges = true`.

6. **The 0.3.2 default changed established coverage.** The fixed-axis grid
   already used the actual region dimensions for spacing, position, and particle
   radius. Making `ActiveArea` the default removed full-region weather. Version
   0.3.3 restores `Region` with an 8x8 scaled grid and retains spacing mode as an
   explicit advanced alternative.

## 0.3.2 realism and configurability work

### Weather cells

- Added `CoverageMode = ActiveArea|Region`.
- Added var-region-aware `EmitterSpacingMeters`.
- Retained `EmitterGrid` as the compatibility/default full-region layout.
- Added `MaxEmitters`, active-area radius/refresh, radius scale, and emitter
  height controls.
- Active-area emitters are created and removed as root agents move.

### Assets

- Added configurable `RainTexture`, `StormTexture`, `SnowTexture`, and
  `LightningTexture` asset UUIDs.
- Blank particle texture warnings explain the square/rectangle billboard
  result.
- Added configurable puddle and snow overlay textures.
- Surface effects disable themselves when required alpha textures are blank.

### Environment profiles

- Replaced one buried fixed cloud treatment with separate Sunny, Rain, Storm,
  and Snow profile sections.
- Exposed every sky/cloud field the module changes.
- Preserved unmodified current environment fields, including water values.
- Retained in-code defaults only as fallbacks for omitted sections.
- Restores an originally inherited environment via the reset path rather than
  storing a copied custom default.

### Indoor suppression

- Replaced one centre-point cover test with a configurable multi-point probe
  grid over each weather cell.
- Added minimum open-sky fraction and proportional radius reduction.
- Added suppression around covered avatars in ActiveArea mode.
- Added optional phantom-prims-in-cover-ray behavior.
- Added named `Weather Exclusion` X/Y volumes for complex interiors.
- Lightning candidate selection now avoids covered/excluded locations where
  possible.

### Surface effects

- Added optional normalized wetness and snow state.
- Added temperature/freezing behavior, configurable accumulation, drying, and
  melting rates.
- Added temporary puddle overlays on low, shallow, outdoor terrain.
- Added temporary snow overlays on outdoor terrain below a slope threshold.
- Restricted patches to areas near active avatars and capped their count.
- Reconciles desired and existing patches instead of deleting/recreating every
  patch on each timer tick.
- Does not alter terrain textures or heightmaps.

### Generated-object safety

All generated emitters, flashes, puddles, and snow overlays:

- are added with backup/persistence disabled;
- carry `TemporaryOnRez`;
- are marked as temporary module instances;
- have a module marker name/description;
- are removed during clear, change, region removal, and shutdown.

Startup cleanup removes matching non-persistent leftovers only. The original
use of non-backup insertion already prevented ordinary region backup; the
additional flags and marker cleanup provide defense in depth and clearer
recovery after an interrupted module instance.

## Other corrected behavior

- Rejects `CommandChannel = 0` and uses 89 instead.
- Exact command tokens replace substring matching.
- Lightning geometry starts near terrain.
- Thunder originates at the strike position.
- Unsupported wind plugins are left unchanged.
- Invalid thunder UUIDs do not disable visual lightning.
- Full status now includes emitter, exclusion-volume, and surface-patch counts.
- Private Visual Studio and generated build artifacts are excluded.
- Configuration comments and examples match the keys read by the source.

## Remaining technical limits

1. **Particles do not collide.** Cell suppression reduces indoor weather but
   cannot stop each viewer particle at a roof, wall, or terrain surface.

2. **Exclusion volumes are X/Y columns.** They are intentionally simple and
   reliable for building footprints, but do not model stacked indoor/outdoor
   floors at the same coordinates.

3. **Environment restoration is not crash-proof.** A hard process failure can
   occur after a stored environment change and before restoration.

4. **Surface overlays are approximate.** They apply to terrain only, use flat
   phantom overlays, and do not conform to prim/mesh floors or roofs.

5. **Accumulation is session-local.** Wetness and snow levels reset when the
   module process restarts; no database schema or terrain mutation is added.

6. **Temperature is configured, not externally sourced.** The module does not
   currently read live weather or a region climate service.

7. **One simulator configuration is shared.** Each region has a separate module
   instance, but regions hosted by the same simulator process read the same INI
   settings unless the process configuration is separated.

## Validation boundary

The source was statically checked against the current official OpenSim API
surface available during this audit. This workspace does not contain a .NET SDK
or a complete buildable OpenSim checkout, so it cannot replace a clean build in
the user's actual OpenSim repository. See `VALIDATION.txt` and `UPGRADE.md`.

## 0.3.2 packaging correction

The 0.3.0 archive had the correct directory and project paths, but its add-on
`prebuild.xml` retained an XML declaration. OpenSim add-on prebuild files are
inserted into the main prebuild document and must be fragments rooted directly
at `<Project>`. Version 0.3.2 removes the declaration. Runtime behavior is
unchanged.
