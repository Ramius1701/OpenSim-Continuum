# Changelog

## 0.3.4 - chat response and complete environment profiles

- Removed the duplicate `Weather:` prefix from object-style chat replies.
- Kept `weather` as the canonical private-channel command namespace and retained
  `meteo` as an alias.
- Added configurable `SunMoonPosition`, `EastAngle`, and `MaxAltitude` to Sunny,
  Rain, Storm, and Snow environment profiles.
- Missing environment sections and missing individual keys use built-in profile
  values derived from OpenSim LightShare defaults.
- Sunny no longer inherits a dark pre-weather sun position from the region.

## 0.3.3 — coverage compatibility and configuration reconciliation

- Restored `CoverageMode = Region` as the safe default.
- Restored the dimension-scaled 8x8 emitter layout that covers standard regions
  and varregions with a stable 64-object upper baseline before cover suppression.
- Restored the original `EmitterRadiusScale = 0.62` coverage behavior.
- Kept `ActiveArea`, fixed-metre spacing, and `MaxEmitters` as explicit advanced
  options rather than silently changing normal region coverage.
- Invalid `CoverageMode` values now warn and fall back to `Region`.
- Simplified `OpenSimWeather.ini.example` to common operational settings.
- Added `OpenSimWeather.reference.ini.example` for the complete advanced
  configuration and all environment profiles.
- Updated focused examples to use full-region coverage unless the operator
  deliberately selects ActiveArea.

## 0.3.2 — OpenSim Enhanced replacement packaging

- Replaces the initial `addon-modules/Weather` implementation rather than
  installing beside it.
- Documents removal of the stale `OpenSim.Addons.Weather.dll` output.
- Uses capability-focused OpenSim Enhanced wording in public documentation.
- Removes an unused `OpenMetaverse.StructuredData` project reference.
- Omits the generated `.csproj`; `runprebuild.bat` creates it for the target
  checkout.
- Weather runtime behavior is unchanged from 0.3.1.

## 0.3.2 — add-on packaging correction

- Removed the XML declaration from the root `prebuild.xml`. OpenSim inserts add-on
  `prebuild.xml` files into its main prebuild document, so an add-on fragment must
  contain only the `<Project>` tree.
- Revalidated the archive layout for direct extraction under `addon-modules`.
- No weather runtime behavior changed from 0.3.0.

## 0.3.2 — realistic/configurable production candidate

### Coverage and var-regions

- Added `CoverageMode` with production-oriented `ActiveArea` and full `Region`
  modes.
- Added var-region-aware `EmitterSpacingMeters` and global `MaxEmitters`.
- Retained `EmitterGrid` as a warned legacy setting only.
- Proportionally reduces over-limit full-region layouts instead of imposing a
  fixed per-axis cap.
- Added active-area radius and refresh controls.

### Particle and storm assets

- Added configurable rain, storm, snow, and lightning texture UUIDs.
- Added clear warnings when blank particle textures will appear square.
- Lightning now retries outdoor/non-excluded candidate positions.
- Thunder remains spatial at the strike location.

### Environment

- Added configurable Sunny, Rain, Storm, and Snow environment sections.
- Exposed every sky/cloud field changed by the module.
- Preserves unmanaged current LightShare fields.
- Renamed the canonical switch to `AdjustEnvironment`; `AdjustClouds` remains a
  compatibility alias.
- Retained explicit acknowledgement for persistent environment changes.

### Indoor suppression

- Added multi-point cover probing over each weather cell.
- Added minimum open-sky fraction and partially open cell scaling.
- Added covered-avatar suppression for active-area weather.
- Added optional phantom cover-ray inclusion and bounded hit count.
- Added named `Weather Exclusion` footprint volumes with periodic refresh.

### Puddles and snow accumulation

- Added optional session-local wetness and snow levels.
- Added configurable accumulation, drying, melting, and temperature behavior.
- Added capped, non-persistent terrain puddle and snow overlays near avatars.
- Added water, slope, low-point, cover, and exclusion checks.
- Reconciles patches incrementally instead of rebuilding every object each tick.
- Automatically disables an effect when its required overlay texture is blank.

### Runtime safety

- Fixed active-area rollback so **all** tracked emitters are removed when any
  later startup step fails.
- Added marker names/descriptions to all generated objects.
- Added `TemporaryOnRez`, temporary-instance marking, and non-backup insertion.
- Added optional startup cleanup for matching non-persistent leftovers.
- Extended status output with exclusion and surface counts.

### Packaging

- Added complete v0.3 configuration reference and focused manual, auto-cycle,
  environment, and surface examples.
- Added asset guidance and a production replacement/rollback guide.
- Updated audit and validation documentation.

## 0.2.0 — initial production audit repair

- Made `Enabled = false` authoritative and disabled the module when its section
  is absent.
- Removed the undocumented disable override behavior.
- Replaced developer-specific build paths with repository-relative references.
- Serialized weather transitions.
- Added initial rollback, shutdown restoration, command parsing, lightning, and
  thunder fixes.
- Added explicit persistent-environment acknowledgement.
- Removed generated/private IDE artifacts.

## 0.1 — supplied baseline

- Original supplied module package.
