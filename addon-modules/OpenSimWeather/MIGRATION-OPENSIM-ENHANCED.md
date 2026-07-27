# Migration from the initial OpenSim Enhanced Weather module

The current enhanced repository contains:

```text
addon-modules/Weather
bin/OpenSim.Addons.Weather.dll
```

OpenSimWeather 0.3.3 uses:

```text
addon-modules/OpenSimWeather
bin/OpenSimWeather.Module.dll
```

The two implementations must not be installed together. They expose the same
region-module identity and configuration section. Remove the old source project
and stale DLL before starting a simulator with the replacement.

The upgrade installer supplied with this package performs that replacement,
runs prebuild, builds the complete solution, and creates a checkpoint only
after a clean successful build.
