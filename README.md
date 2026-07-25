# OpenSim Continuum

[![OpenSim Continuum Build](https://github.com/Ramius1701/OpenSim-Continuum/actions/workflows/msbuildnet.yml/badge.svg)](https://github.com/Ramius1701/OpenSim-Continuum/actions/workflows/msbuildnet.yml)

OpenSim Continuum is a maintained downstream fork of the official
[OpenSimulator](https://github.com/opensim/opensim) development branch.

It combines current OpenSimulator development code with selected grid,
identity, scripting, environment, simulator, web, economy, and reliability
enhancements. Official OpenSimulator remains the authoritative upstream
baseline.

## Project status

| Item | Status |
|---|---|
| Upstream baseline | `upstream/master` |
| Maintained branch | `master` |
| Repository model | GitHub-recognized fork of `opensim/opensim` |
| Initial consolidation checkpoint | `continuum-initial-build-clean` |
| Windows build | Successful |
| GitHub Actions | Build-only validation on .NET 8 |

The complete solution has been generated and compiled successfully, including
OpenSim, Robust, MoneyServer, and the included add-on modules.

A successful compile confirms source integration. It does not automatically
confirm that every optional service or feature is ready for production.
Database migrations, viewer CAPS, Hypergrid behavior, economy services, and
region modules should be tested in a controlled environment before deployment.

## Project goals

- Stay close enough to official OpenSimulator to accept continuing upstream work.
- Preserve useful enhancements that are difficult to maintain as loose patches.
- Keep optional functionality in `addon-modules` whenever practical.
- Avoid grid-specific hardcoding.
- Support standalone and Robust/grid deployments.
- Retain Windows build and deployment support.
- Provide configuration examples without silently enabling services.

## Included enhancements

### Display Names and identity

- Viewer-compatible Display Names for local users.
- Display Name CAPS and viewer protocol handling.
- Display Name storage and account-service integration.
- Hypergrid Display Name lookup and federation.
- Single-name and `username` login handling.
- Terms-of-service acceptance during login.
- RSA-key authentication support.
- `InternalPort = MATCHING` region configuration support.

### Abuse Reports

- Viewer Abuse Reports CAPS.
- Local and remote service connectors.
- Robust handlers.
- MySQL storage and migrations.
- Region-side submission support.

### Parcel, terrain, inventory, and object control

- `osTriggerSoundAtPos`
- Parcel auto-return access through `PARCEL_DETAILS_OBJECT_RETURN`
- In-world terrain console commands
- Script-controlled terrain textures and height ranges
- Script-controlled parcel sale state
- Object return by object ID or owner
- Scripted ownership transfer
- Direct inventory delivery to an avatar
- Nested destination-folder support
- Active-group matching
- Filtered attachment queries
- Synchronous notecard text searching
- Sculpt-map animation support

### Expanded LSL and OSSL compatibility

Additional functions include:

- `llSignRSA`
- `llVerifyRSA`
- `llGetRegionTimeOfDay`
- `llSetAgentRot`
- `llTransferOwnership`
- `llReturnObjectsByID`
- `llReturnObjectsByOwner`
- `llSetGroundTexture`
- `llGiveAgentInventory`
- `llMatchGroup`
- `llSetParcelForSale`
- `llGetAttachedListFiltered`
- `llFindNotecardTextSync`
- `llSetSculptAnim`
- `llSetLinkRenderMaterial`
- `llSetLinkGLTFOverrides`

The GLTF override support includes per-face overrides, base-color and alpha,
metallic and roughness, emissive state, double-sided state, extension JSON,
compact LLSD encoding, and supporting asset parsing.

The separate `PRIM_GLTF_*` primitive-parameter subsystem is not included.

### Combat2 scripting

- `llDamage`
- `llAdjustDamage`
- `llDetectedDamage`
- `llDetectedRezzer`
- Object-health support
- `on_damage`
- `final_damage`
- `on_death`

### EEP environment scripting

- `llGetEnvironment`
- `llReplaceEnvironment`
- `llSetEnvironment`
- `llReplaceAgentEnvironment`
- `llSetAgentEnvironment`
- Region and parcel sky/water access
- Per-agent environment overrides for trusted Experience-Lite scripts

### Pathfinding

- `llCreateCharacter`
- `llUpdateCharacter`
- `llDeleteCharacter`
- `llExecCharacterCmd`
- `llNavigateTo`
- `llWanderWithin`
- `llPatrolPoints`
- `llPursue`
- `llEvade`
- `llFleeFrom`
- `llGetStaticPath`
- `llGetClosestNavPoint`

The implementation provides approximate region-local A* navigation using
terrain, scene queries, obstacle bounds, and OpenSim keyframe motion. It is not
a physics-engine-native navigation service.

### Experience-Lite

Experience-Lite provides configurable grid-local trust and persistent
per-region/per-owner key-value storage. It is intentionally smaller than the
complete Second Life Experience service.

Supported functions include experience permissions, trust checks, key-value
storage, `llSitOnLink`, and `llOpenFloater`.

`llOpenFloater` remains a stub because OpenSimulator does not currently provide
the required viewer-hosted floater service.

### Sit targets and avatar animation

- Enforcement of scripted-only sit targets
- Storage and lookup for LSL sit flags
- Configurable male and female walk-animation overrides
- Movement-animation resend protection

### Region crossing and attachment reliability

- Configurable transfer and cleanup timeouts
- Optional preservation of crossing velocity
- Configurable velocity limits
- Reduced attachment detach/reattach flashing
- Script-state restoration protection
- Duplicate and failed attachment cleanup
- Coordinated queued attachment-script restarts

### Background map-tile generation

- Background rendering
- Non-blocking region startup and grid registration
- Configurable startup delay
- Configurable rendering-thread stack size
- Optional wait-for-empty behavior

## Included add-on modules

All modules are under `addon-modules`. They are generated into the solution but
are not necessarily enabled by default.

- **Gloebit** — optional Gloebit economy integration.
- **GroupAutoInvite** — configurable automatic group invitations.
- **HoloPhysicsGuard** — reduces idle physics load when regions are empty.
- **OpenSim Marketplace** — portable Direct Delivery marketplace system.
- **OpenSimMutelist** — external mute-list service integration.
- **OpenSimSearch** — external viewer search integration.
- **OpenSimTide** — configurable tide and water-level simulation.
- **OpenSimWeather** — experimental rain, snow, storms, lightning, thunder,
  wind, clouds, textures, emitters, and environmental settings.
- **RegionCurrency** — optional web front end for an existing `IMoneyModule`.
- **RegionWeb** — per-region web pages and protected estate administration.

Detailed Marketplace documentation is located at:

```text
addon-modules/OpenSimMarketplace/README.md
```

## MoneyServer enhancements

The included MoneyServer integration provides:

- MoneyServer, region currency module, and MySQL data wrapper.
- Viewer currency purchases without an external `currency.php` helper.
- Configurable daily, weekly, and monthly purchase limits.
- Accounting based only on successful purchases.
- Atomic balance credit and transaction-ledger recording.
- Idempotent confirmation UUID handling.
- `CurrencyMaximum` enforcement.
- Retained banker, transfer, group, email-lock, object-payment, upload-charge,
  and land-sale controls.
- Correct console redraw behavior for the `MoneyServer #` prompt.

The repository does not replace a live `bin/MoneyServer.ini`. Review the
included examples and validation documents before production use.

## Building

### Requirements

- .NET 8 SDK or a newer SDK capable of targeting .NET 8
- Visual Studio 2022 or later is optional on Windows

### Windows

```bat
runprebuild.bat
compile.bat
```

Equivalent direct build:

```bat
runprebuild.bat
dotnet build OpenSim.sln --configuration Release
```

### Linux or macOS

```bash
./runprebuild.sh
dotnet build OpenSim.sln --configuration Release
```

See `BUILDING.md` for the official base requirements.

## Configuration

OpenSim Continuum does not install live configuration automatically.

Review:

- `bin/OpenSim.ini.example`
- `bin/Robust.ini.example`
- `bin/Robust.HG.ini.example`
- `bin/config-include/GridCommon.ini.example`
- module-specific `.ini.example` files under `addon-modules`

Optional modules should remain disabled until dependencies, database schema,
service endpoints, credentials, and runtime behavior have been validated.

## Keeping current with official OpenSimulator

The maintained branch is `master`. Official OpenSimulator is tracked through
the `upstream` remote.

```bat
git checkout master
git status
git fetch upstream --prune
git tag -a continuum-pre-upstream-YYYYMMDD -m "Checkpoint before upstream merge"
git merge --no-ff upstream/master
runprebuild.bat
dotnet build OpenSim.sln --configuration Release
```

Resolve and test upstream conflicts rather than replacing Continuum history.

## Repository model

| Reference | Purpose |
|---|---|
| `master` | Maintained OpenSim Continuum integration |
| `origin/master` | Published Continuum branch |
| `upstream/master` | Official OpenSimulator development branch |
| `continuum-initial-build-clean` | Initial consolidation/build checkpoint |
| `continuum-original-fork-master` | Preserved pre-consolidation fork state |
| `continuum-region-scripting-archive` | Preserved region/scripting history |

Historical donor and feature branches are not part of the active branch model.
Relevant source history remains available through Git history and preservation
tags.

## Known limitations

- Some added database migrations are MySQL-specific.
- Experience-Lite is not full Second Life Experience compatibility.
- `llOpenFloater` is currently a stub.
- `PRIM_GLTF_*` primitive parameters are not included.
- Pathfinding is region-local and approximate.
- Weather remains experimental.
- PayPal return handling is not a server-to-server webhook flow.
- A listed feature may still be disabled in configuration.
- Build success does not replace controlled runtime testing.

## Attribution and support

OpenSim Continuum retains the OpenSimulator license and source history.
Historical source provenance remains available in Git history.

Report Continuum-specific problems in this repository. Problems reproducible
on an unmodified official OpenSimulator build should be reported to the
official OpenSimulator project.

## License

OpenSim Continuum is distributed under the same BSD-style license as
OpenSimulator. See `LICENSE.txt`.
