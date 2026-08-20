# OpenSim Continuum

OpenSim Continuum is an OpenSimulator distribution that reconciles selected fixes, services, scripting capabilities, and optional modules from the wider OpenSim ecosystem onto a clean OpenSim Dev base.

The clean donor-integration baseline is OpenSim Dev commit `247b9182c1ca0f11743de06a2808f003bc8e2a90` from 2026-08-01. The repository has subsequently synchronized OpenSim upstream through `58b7b39db676a166c68d47256f50588cdf330630` while retaining the audited Continuum feature set. The active hardening branch remains an **alpha integration build**, not a production-approved release. The complete solution builds cleanly, but cross-simulator Display Names, viewer currency purchasing, Experiences, Abuse Reports, search propagation, Hypergrid behavior, voice, and other distributed paths still require the recorded live-grid verification. Do not deploy it as a production replacement. The required tests are described in [the donor feature testing handoff](doc/donor-feature-test-handoff.md), [the integration progress ledger](doc/integration-progress.md), and [the ContinuumEconomy runbook](doc/continuum-economy-production-test.md).

## Project status — alpha

- Target runtime: .NET 8
- Required deployment targets: Windows standalone with SQLite; Windows grid mode
  with Robust using MySQL/MariaDB or PostgreSQL
- Original baseline build: successful with four known CS9193 compiler warnings
- The four CS9193 warnings have been corrected without changing their quaternion calculations
- Latest complete Release build: successful with zero warnings and zero errors
- Grid-wide Display Names, Experiences, and Abuse Reports are enabled in the Continuum grid/Robust example profiles
- Offline IM and viewer mute lists are selected end-to-end in Standalone and Grid examples; Grid routes storage through the authenticated ROBUST private endpoint
- Public web, economy, automatic permission grants, and experimental modules remain explicit opt-ins
- OpenSim-Grid-Interface and WhiteCore WebUI work is intentionally deferred until the simulator, Robust services, and addons are complete
- OpenSim-Grid-Interface checkpoint `ecb377d42b5a1f0ec7a969a65f48596c8e5dbe87` is the secondary portal donor: use it to fill demonstrated WhiteCore WebUI gaps while retaining one WhiteCore-led portal shell, route map and user experience
- When that portal phase begins, the target is the actual WhiteCore-Dev integrated WebUI—including its C# page sources, HTML templates and static assets—not a newly designed substitute; only incompatible WhiteCore backend/service boundaries are to be adapted to OpenSim/Robust

Do not replace a running production installation in place. Build a separate test deployment, back up all databases and configuration, and rehearse every migration against a copy of production data.

## Integrated functionality

### Identity, accounts, and grid services

- Mutable Display Names with persistence, CAPS, search, caching, and Hypergrid handling
- User alias service and compatible OAR identity resolution
- Hypergrid stale service-URL, circuit, and cached-identity repair
- RSA key authentication and Terms of Service acceptance support
- `InternalPort = MATCHING` region configuration support
- Viewer Abuse Report submission, screenshot handling, Robust service, and SQLite, MySQL/MariaDB, and PostgreSQL storage providers
- Mobius parcel auto-return details and in-world terrain console commands

### Experiences and scripting

- Tranquillity/Mobius Experience service, connectors, CAPS, estate/parcel allowed, blocked and trusted controls, SQLite, MySQL/MariaDB, and PostgreSQL providers, and restart handling
- Second Life-compatible Experiences floater contracts for Search, Allowed, Blocked, Admin, Contributor, and Owned; viewer-local Events remain viewer-managed
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

- Gunthar RegionWeb combined per-region website, protected estate tools and optional wallet portal
- GroupAutoInvite
- Deprecated RegionCurrency compatibility portal for deployments made against
  the earlier split; it disables itself when RegionWeb is enabled
- OpenSimMarketplace direct-delivery addon
- Gloebit money module
- MoneyServer Compatibility: the maintained DTL/NSL-compatible service and region module for grids that need the established protocol
- ContinuumEconomy: a separately named service and region module with atomic MySQL/MariaDB, PostgreSQL and SQLite providers, idempotent operations, viewer currency purchase controls, and delivery-safe object purchase holds; it is ready for the production-test runbook, not live cutover
- HoloPhysicsGuard
- OpenSimSearch viewer-directory compatibility client (requires a separately deployed compatible search service)
- OpenSimTide
- OpenSimWeather
- Viewer Abuse Reports

These are independent optional components. They are not all required for a grid, and enabling multiple economy modules simultaneously is not supported unless their interaction has been explicitly designed and tested.

HoloPhysicsGuard `PersistSleep` remains MySQL/MariaDB-only; SQLite and
PostgreSQL deployments are limited to its non-persistent `ReportOnly` mode until
dedicated providers are implemented and certified.

### Rendering, physics, and tooling

- Warp3D alpha texture-card sprite rendering, disabled by default
- Dedicated experimental ubODE tuning branch for social collision, contact, bounce, buoyancy, and water behavior
- Recovered Windows first-run setup tooling, quarantined because it can overwrite configuration and persist credentials; it is not a supported production setup path

## Safe defaults

The following components require deliberate opt-in:

- `[RegionWeb] Enabled = false`
- `[ScriptExperiences] Enabled = false`
- `[OpenSimMarketplace] Enabled = false`
- Recovered economy, search, environment, and protection modules through their own configuration sections
- Warp3D flat-card sprite rendering through its renderer flags

RegionWeb is the canonical combined Gunthar-derived per-region site, estate
control room and optional wallet presentation; it is not WhiteCore WebUI and it
is not an economy backend. Public pages may be served independently, while
Estate Admin and wallet routes require HTTPS by default. RegionCurrency is only
a deprecated compatibility path for deployments made against the earlier split
and automatically disables itself whenever RegionWeb is enabled.

`[ScriptExperiences]` controls the separate Experience-Lite automatic permission/KVP layer; it is not the grid-wide Experience service itself. The grid-wide service and its Robust endpoint are enabled in the Continuum grid examples. Keep automatic grants restricted to trusted estate managers, owners, or objects during testing.

The viewer service also requires an explicit `[Modules]` selection. The grid
example selects `RemoteExperienceServicesConnector`; standalone selects
`LocalExperienceServicesConnector`. Omitting this selection prevents Experience
CAPS registration and hides the Parcel and Region Experience tabs.

Never commit production passwords, API keys, database connection strings, marketplace credentials, or external economy credentials. Experience-Lite automatic grants must be restricted to explicit trusted owner or object UUIDs and must not grant debit permission.

## Database support

OpenSim core retains its normal datastore support. Continuum requires SQLite for
standalone deployments and both MySQL/MariaDB and PostgreSQL for grid/Robust
deployments. Imported services must not be described as generally ready while
they provide only a MySQL implementation.

The Experience service, Abuse Reports, and aliases now include SQLite,
MySQL/MariaDB, and PostgreSQL provider paths; their clean-install and upgrade
migrations still require runtime certification on every database. MoneyServer
Compatibility remains MySQL-specific by design. ContinuumEconomy provides its
own service, region module, migrations and shared acceptance contract for
SQLite, MySQL/MariaDB and PostgreSQL. It still requires the recorded simulator
and recovery tests in its production-test runbook. For every service migration, test both a clean
database and an upgrade from the exact schema used by the target deployment.

## Building

See [BUILDING.md](BUILDING.md) for the upstream build prerequisites and platform details.

On Windows, build from the root of the checkout or isolated worktree you intend
to test. Do not mix binaries from another OpenSim checkout:

```powershell
.\runprebuild.bat
dotnet restore OpenSim.sln
dotnet build OpenSim.sln --configuration Release --no-restore
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

The recovered Windows setup wizard is quarantined and its launcher exits without
making changes. It is not a supported production setup path. Configure the
simulator and Robust services manually from the supplied examples and review all
credentials, service URIs, database providers, and optional-module settings.

## Branches used for testing

- `development`: the single active integration and production-hardening branch;
  all current work is verified here before promotion.
- `main`: the public stable checkpoint; it intentionally advances only after a
  development checkpoint passes the controlled runtime gate.

Historical preservation uses tags instead of extra working branches. The clean
OpenSim Dev baseline is retained by `opensim-dev-baseline-2026-08-01` and
`opensim-dev-baseline-2026-08-01-build-clean`; the unfinished earlier runtime
work is retained by `archive/runtime-stabilization-065769b`.

## Donors and provenance

The integration draws on work from:

- OpenSimulator upstream
- GuntharDeNiro/opensim
- Mobius-Team/Mobius
- OpenSim-NGC/OpenSim-Tranquillity
- WhiteCoreSim/WhiteCore-Dev as a first-class behavioural and viewer-protocol reference; its divergent architecture still rules out wholesale source-tree ports
- the local `S:\Github\opensim-lickx` archival snapshot as a donor/reference for the optional Lickx script API, OpenSimMutelist, the DTL/NSL-derived `opensim.currency-lickx` lineage, and historical OpenSimSearch helper files
- WhiteCore directory/search behaviour as the completeness reference for hardening OpenSimSearch across people, places, land, events, classifieds, map results, privacy, paging, and grid-scale indexing
- Previous OpenSim Continuum and opensim-enhanced branches
- Original repositories for Gloebit, HoloPhysicsGuard, MoneyServer, OpenSimSearch, OpenSimTide, OpenSimWeather, and recovered addons

Mobius is the original lineage for Display Names, Experiences, and Abuse Reports. Tranquillity provides the traceable enhanced Experience and identity service implementation used as the production base. Gunthar provides the closest active OpenSim-derived fixes, scripting work, RegionWeb, rendering changes, physics experiments, and optional modules.

`opensim-lickx` is the user-preserved last available copy of a project that is no longer on GitHub. The untouched local archive is recorded at commit `6614599b7506b63861763e6cae5eefde861f8749`. It has no configured upstream remote, so that commit identifies the preserved tree rather than complete original history. Its OpenSim-derived code uses the BSD-3-Clause project license; the bundled `opensim.currency-lickx` addon separately declares the MIT license and DTL/NSL lineage. It is audit-only until each candidate is compared with current Continuum equivalents. Any selected code must retain its notices and enter Continuum through a separately reviewed commit; the archive itself must not be rewritten.

Licensing and attribution must be reviewed per addon and asset. The OpenSimulator-derived code is BSD licensed; third-party modules, media, fonts, JavaScript, service SDKs, and external APIs may carry separate terms. See [LICENSE.txt](LICENSE.txt), [CONTRIBUTORS.txt](CONTRIBUTORS.txt), `ThirdPartyLicenses`, and the license files inside individual addons.

## Deliberately deferred or excluded

- OpenSim-Grid-Interface and WhiteCore WebUI are deferred to a later portal phase.
- WhiteCore WebUI is not copied into Robust because its service, persistence, authentication, and page architecture is incompatible with current OpenSim.
- Donor-specific endpoints, credentials, forced defaults, curated destinations, destructive update/reset scripts, and obsolete architecture rewrites are excluded.
- The separate local checkout explicitly excluded from this work was not used.

## Further documentation

- [Donor feature inventory and disposition record](doc/donor-feature-inventory.md)
- [2026-08-14 donor re-baseline and mandatory comparison sequence](doc/donor-rebaseline-2026-08-14.md)
- [Display Names donor-lineage and parity audit](doc/parity-display-names.md)
- [Experiences donor-lineage and 1.x parity audit](doc/parity-experiences.md)
- [Abuse Reports donor-lineage and WhiteCore workflow parity audit](doc/parity-abuse-reports.md)
- [MoneyServer Compatibility and ContinuumEconomy donor-parity audit](doc/parity-economy.md)
- [OpenSimSearch and future grid directory donor-parity audit](doc/parity-search.md)
- [RegionWeb, OpenSimWeather, and OpenSimTide donor-parity audit](doc/parity-regionweb-environment.md)
- [User aliases, mute lists, and Groups donor-parity audit](doc/parity-social-services.md)
- [OpenSimMarketplace and WebRTC/Janus voice donor-parity audit](doc/parity-marketplace-voice.md)
- [ubODE physics and LSL/OSSL scripting donor-parity audit](doc/parity-physics-scripting.md)
- [WhiteCore-to-Continuum improvement audit](doc/whitecore-continuum-improvement-audit.md)
- [Donor feature testing handoff](doc/donor-feature-test-handoff.md)
- [Current integration progress and release gates](doc/integration-progress.md)
- [Abuse Reports](doc/AbuseReports.md)
- [OpenSimSearch deployment boundary and runtime gate](doc/OpenSimSearch.md)
- [MoneyServer documentation](docs/MoneyServer/README.md)
- [ContinuumEconomy compatibility contract](doc/continuum-economy-compatibility-contract.md)
- [ContinuumEconomy production-test runbook](doc/continuum-economy-production-test.md)
- [ContinuumEconomy development package](addon-modules/ContinuumEconomy/README.md)
- [OpenSimWeather documentation](addon-modules/OpenSimWeather/README.md)
- [OpenSimTide documentation](addon-modules/OpenSimTide/README.md)
- [HoloPhysicsGuard documentation](addon-modules/HoloPhysicsGuard/README.md)
- [OpenSimMarketplace documentation](addon-modules/OpenSimMarketplace/README.md)

For upstream OpenSimulator configuration and operational documentation, see [opensimulator.org](http://opensimulator.org/).
