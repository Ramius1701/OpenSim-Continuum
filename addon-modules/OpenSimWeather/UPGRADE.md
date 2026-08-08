# Production replacement and rollback

The currently loaded module DLL cannot be safely replaced inside a running
OpenSim process. Use a controlled simulator restart.

## Before replacement

1. Save a copy of the currently deployed module source/DLL and its `[Weather]`
   configuration.
2. Export or record the region environment if the old module has changed it.
3. Use the old module's clear command in every affected region.
4. Confirm no weather effect is intentionally required during the restart.
5. Stop the affected region simulator process normally.
6. Back up the repository branch and create a Git checkpoint.

## Source installation

1. Remove the previous tracked `addon-modules/Weather` directory.
2. Remove stale `bin/OpenSim.Addons.Weather.dll` and matching PDB files.
3. Copy this package to `addon-modules/OpenSimWeather`.
4. Compare the old configuration with
   `config/OpenSimWeather.ini.example`. Do not carry forward `AllowDisabled`.
5. Start with `Enabled = false`, `CoverageMode = Region`, `EmitterGrid = 8`, environment/wind
   disabled, auto-cycle disabled, and surface effects disabled.
6. Paste valid particle asset UUIDs where available.
7. Run `runprebuild.bat` from the OpenSim source root.
8. Perform a clean Debug or Release build and resolve every compiler error
   before starting the simulator.

## Controlled activation

1. Start one test/duplicate region process with the module disabled.
2. Check the startup log for module load or assembly errors.
3. Set `Enabled = true` and restart that test process.
4. Run `/89 weather status`, then test rain, storm, snow, sunny, and clear.
5. Check building suppression and create explicit `Weather Exclusion` volumes
   where ray probing is not sufficient.
6. Restart once with precipitation active and verify weather objects do not
   return from region backup.
7. Test production region sizes and record emitter count/frame time.
8. Enable environment, wind, auto-cycle, and surface effects separately, with a
   restart/test between subsystems.

## Configuration migration notes

- Delete `AllowDisabled`; it no longer exists.
- Prefer `AdjustEnvironment`; `AdjustClouds` is accepted only as a legacy alias.
- Keep `EmitterGrid = 8` for the compatibility/default full-region layout.
- Set `EmitterGrid = 0` only when deliberately selecting fixed-metre
  `EmitterSpacingMeters` plus `MaxEmitters`.
- Particle and surface assets are OpenSim asset UUIDs.
- `SurfaceEffectsEnabled = true` still requires nonblank `PuddleTexture` and/or
  `SnowSurfaceTexture`.
- Keep `AllowPersistentEnvironmentChanges = false` until the environment
  integration has been deliberately approved.

## Rollback

If the repaired module fails the controlled test:

1. issue `weather clear` where possible;
2. stop the simulator normally;
3. restore the prior module source/DLL and prior configuration;
4. rerun prebuild and perform a clean build when restoring source;
5. restore the recorded/exported region environment if necessary;
6. restart the process and confirm no `OpenSimWeather:` temporary objects remain.

Do not operate both old and new weather module assemblies in the same `bin`
directory or source solution.
