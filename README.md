# OpenSim Continuum

OpenSim Continuum is an OpenSimulator distribution that reconciles selected fixes, services, scripting capabilities, and optional modules from the wider OpenSim ecosystem onto a clean OpenSim Dev base.

The current integration base is OpenSim Dev commit `247b9182c1ca0f11743de06a2808f003bc8e2a90` from 2026-08-01. This branch is an **alpha integration build**, not a production-test candidate. The complete solution builds, but cross-simulator Display Names, viewer currency purchasing, Experiences, Abuse Reports, search propagation, Hypergrid behavior, and other distributed paths still require donor-regression and multi-service runtime verification. Do not deploy it as a production replacement. The required tests are described in [the donor feature testing handoff](doc/donor-feature-test-handoff.md) and [the ContinuumEconomy runbook](doc/continuum-economy-production-test.md).

## Project status — alpha

- Target runtime: .NET 8
- Required deployment targets: Windows standalone with SQLite; Windows grid mode
  with Robust using MySQL/MariaDB or PostgreSQL
- Baseline build: successful with four known CS9193 compiler warnings
- Continuum completion build: successful
- Later LSL/GLTF compatibility reconciliation build: successful with zero warnings and zero errors
- Grid-wide Display Names, Experiences, and Abuse Reports are enabled in the Continuum grid/Robust example profiles
- Public web, economy, automatic permission grants, and experimental modules remain explicit opt-ins
- OpenSim-Grid-Interface and WhiteCore WebUI work is intentionally deferred until the simulator, Robust services, and addons are complete

Do not replace a running production installation in place. Build a separate test deployment, back up all databases and configuration, and rehearse every migration against a copy of production data.

## Integrated functionality

### Identity, accounts, and grid services

- Mutable Display Names with persistence, CAPS, search, caching, and Hypergrid handling
- User alias service and compatible OAR identity resolution
- Hypergrid stale service-URL, circuit, and cached-identity repair
- RSA key authentication and Terms of Service acceptance support
- `InternalPort = MATCHING` region configuration support
- Viewer Abuse Report submission, screenshot handling, Robust service, and MySQL storage
- Mobius parcel auto-return details and in-world terrain console commands

### Experiences and scripting

- Tranquillity/Mobius Experience service, connectors, CAPS, estate/parcel allowed, blocked and trusted controls, MySQL storage, and restart handling
- Gunthar Experience scripting additions reconciled with the authoritative Tranquillity service
- Experience permission, Combat2 damage, and path-update script events
- Experience KVP, permission, sit, environment, and information APIs
- EEP region, parcel, and agent environment functions
- Pathfinding character APIs and compatibility behavior
- Combat2 damage functions
- RSA signing and verification functions
- GLTF and render-material overrides
- Scripted sit, inventory transfer, ownership transfer, estate return, terrain, parcel-sale, attachment-filter, notecard-search, sculpt-animation, sound, and region-time helpers
- `osTriggerSoundAtPos`
- YEngine orphaned resumed-event recovery
- Region crossing, attachment recovery, movement-animation resend, and background map-generation fixes

The extended scripting surface is powerful and has estate/security implications. Test permission checks and failure paths before enabling scripts from untrusted owners.

### Optional region modules and services

- Gunthar RegionWeb per-region website and protected estate tools
- GroupAutoInvite
- RegionCurrency
- OpenSimMarketplace direct-delivery addon
- Gloebit money module
- ContinuumEconomy: a unified MoneyServer successor using the current DTL/NSL-compatible service as its OpenSim base, WhiteCore's integrated currency behavior as its feature reference, and Gunthar's module/portal work as integration guidance; Gloebit is excluded
- HoloPhysicsGuard
- OpenSimSearch
- OpenSimTide
- OpenSimWeather
- Viewer Abuse Reports

These are independent optional components. They are not all required for a grid, and enabling multiple economy modules simultaneously is not supported unless their interaction has been explicitly designed and tested.

### Rendering, physics, and tooling

- Warp3D alpha texture-card sprite rendering, disabled by default
- Dedicated experimental ubODE tuning branch for social collision, contact, bounce, buoyancy, and water behavior
- Windows first-run setup tooling, retained as experimental until its remaining donor assumptions and operational edge cases are certified

## Safe defaults

The following components require deliberate opt-in:

- `[RegionWeb] Enabled = false`
- `[ScriptExperiences] Enabled = false`
- `[OpenSimMarketplace] Enabled = false`
- Recovered economy, search, environment, and protection modules through their own configuration sections
- Warp3D flat-card sprite rendering through its renderer flags

`[ScriptExperiences]` controls the separate Experience-Lite automatic permission/KVP layer; it is not the grid-wide Experience service itself. The grid-wide service and its Robust endpoint are enabled in the Continuum grid examples. Keep automatic grants restricted to trusted estate managers, owners, or objects during testing.

Never commit production passwords, API keys, database connection strings, marketplace credentials, or external economy credentials. Experience-Lite automatic grants must be restricted to explicit trusted owner or object UUIDs and must not grant debit permission.

## Database support

OpenSim core retains its normal datastore support. Continuum requires SQLite for
standalone deployments and both MySQL/MariaDB and PostgreSQL for grid/Robust
deployments. Imported services must not be described as generally ready while
they provide only a MySQL implementation.

The full Experience service, Abuse Reports, aliases, MoneyServer, and
ContinuumEconomy currently contain MySQL-only storage paths. Their SQLite and/or
PostgreSQL providers and migrations are incomplete release blockers. Do not
assume schema compatibility merely because the solution compiles. For every
service migration, test both a clean database and an upgrade from the exact
schema used by the target deployment.

## Building

See [BUILDING.md](BUILDING.md) for the upstream build prerequisites and platform details.

On Windows, generate the Visual Studio 2022 solution for .NET 8 and build Release:

```powershell
Copy-Item bin\System.Drawing.Common.dll.win bin\System.Drawing.Common.dll -Force
dotnet bin\prebuild.dll /target vs2022 /targetframework net8_0 /excludedir '=' 'obj | bin' /file prebuild.xml
dotnet restore OpenSim.sln
dotnet build --configuration Release OpenSim.sln --no-restore
```

Generated `bin/*.runtimeconfig.json` files are build artifacts and must not be committed unless the build system intentionally begins managing them.

## Running a test deployment

1. Create a separate copy of the built `bin` directory and configuration.
2. Start with all Continuum optional features disabled.
3. Confirm login, inventory, assets, scripts, teleport, region crossing, shutdown, and restart against the upstream baseline behavior.
4. Enable one service or module at a time.
5. Run the acceptance tests in [doc/donor-feature-test-handoff.md](doc/donor-feature-test-handoff.md).
6. Inspect logs for repeated exceptions, authentication failures, sensitive data, and migration errors.
7. Test Robust outage/recovery and Hypergrid boundaries before enabling identity or Experience features on a public grid.

The Windows setup wizard is not a substitute for reviewing production configuration.

## Branches used for testing

- `codex/complete-opensim-feature-set`: complete OpenSim-side feature candidate
- `codex/production-feature-integration`: earlier conservative integration checkpoint
- `codex/testing-ubode-tuning`: experimental ubODE series on the integration candidate
- `audit/donor-feature-inventory`: donor audit and provenance checkpoint
- `fix/continuum-runtime-stabilization`: preserved unfinished historical stabilization work; not a deployment recommendation

## Donors and provenance

The integration draws on work from:

- OpenSimulator upstream
- GuntharDeNiro/opensim
- Mobius-Team/Mobius
- OpenSim-NGC/OpenSim-Tranquillity
- WhiteCoreSim/WhiteCore-Dev as a first-class behavioural and viewer-protocol reference; its divergent architecture still rules out wholesale source-tree ports
- WhiteCore directory/search behaviour as the completeness reference for hardening OpenSimSearch across people, places, land, events, classifieds, map results, privacy, paging, and grid-scale indexing
- Previous OpenSim Continuum and opensim-enhanced branches
- Original repositories for Gloebit, HoloPhysicsGuard, MoneyServer, OpenSimSearch, OpenSimTide, OpenSimWeather, and recovered addons

Mobius is the original lineage for Display Names, Experiences, and Abuse Reports. Tranquillity provides the traceable enhanced Experience and identity service implementation used as the production base. Gunthar provides the closest active OpenSim-derived fixes, scripting work, RegionWeb, rendering changes, physics experiments, and optional modules.

Licensing and attribution must be reviewed per addon and asset. The OpenSimulator-derived code is BSD licensed; third-party modules, media, fonts, JavaScript, service SDKs, and external APIs may carry separate terms. See [LICENSE.txt](LICENSE.txt), [CONTRIBUTORS.txt](CONTRIBUTORS.txt), `ThirdPartyLicenses`, and the license files inside individual addons.

## Deliberately deferred or excluded

- OpenSim-Grid-Interface and WhiteCore WebUI are deferred to a later portal phase.
- WhiteCore WebUI is not copied into Robust because its service, persistence, authentication, and page architecture is incompatible with current OpenSim.
- Donor-specific endpoints, credentials, forced defaults, curated destinations, destructive update/reset scripts, and obsolete architecture rewrites are excluded.
- The separate local checkout explicitly excluded from this work was not used.

## Further documentation

- [Donor feature inventory and disposition record](doc/donor-feature-inventory.md)
- [WhiteCore-to-Continuum improvement audit](doc/whitecore-continuum-improvement-audit.md)
- [Donor feature testing handoff](doc/donor-feature-test-handoff.md)
- [Abuse Reports](doc/AbuseReports.md)
- [MoneyServer documentation](docs/MoneyServer/README.md)
- [ContinuumEconomy compatibility contract](doc/continuum-economy-compatibility-contract.md)
- [ContinuumEconomy production-test runbook](doc/continuum-economy-production-test.md)
- [ContinuumEconomy development package](addon-modules/ContinuumEconomy/README.md)
- [OpenSimWeather documentation](addon-modules/OpenSimWeather/README.md)
- [OpenSimTide documentation](addon-modules/OpenSimTide/README.md)
- [HoloPhysicsGuard documentation](addon-modules/HoloPhysicsGuard/README.md)
- [OpenSimMarketplace documentation](addon-modules/OpenSimMarketplace/README.md)

For upstream OpenSimulator configuration and operational documentation, see [opensimulator.org](http://opensimulator.org/).
