# OpenSim Enhanced

OpenSim Enhanced is a maintained downstream integration of the latest official
OpenSimulator development code with additional grid, viewer, scripting,
environment, simulator, web, and reliability features.

Official OpenSimulator remains the authoritative upstream baseline. Enhanced
features are retained as discrete commit stacks so that new upstream changes
can continue to be merged, reviewed, built, and tested.

## Integration status

| Component | Status |
|---|---|
| Official OpenSim baseline | `upstream/master` at `1b428a1825cf` |
| Grid and identity enhancements | Integrated |
| Region, scripting, and simulator enhancements | Integrated |
| Combined development branch | `enhanced/integration` |
| Integration date | 2026-07-19 |
| Build checkpoints | Annotated tags named `opensim-enhanced-build-clean-*` |

A successful compile is the first checkpoint. New services, database
migrations, viewer CAPS, Hypergrid behavior, and optional modules still require
controlled runtime testing before production deployment.

# Features and enhancements

## Display Names and identity services

- Viewer-compatible Display Names for local-grid users.
- Display Name CAPS and viewer protocol handling.
- Display Name data storage and account-service plumbing.
- Hypergrid Display Name lookup and federation.
- Single-name and `username` login handling.
- Terms-of-service acceptance during login.
- RSA-key authentication support.
- `InternalPort = MATCHING` region configuration support.

These features are configurable and are not assumed to be enabled merely
because the code compiles.

## Abuse Reports

- Viewer Abuse Reports CAPS.
- Abuse-report service interfaces.
- Local and remote service connectors.
- Robust server handlers.
- MySQL storage and migrations.
- Region-side submission support.

## Parcel, terrain, inventory, and object control

- `osTriggerSoundAtPos`.
- Parcel auto-return get/set support through
  `PARCEL_DETAILS_OBJECT_RETURN`.
- In-world terrain console commands for loading terrain textures, elevating,
  lowering, and filling terrain.
- Script-controlled terrain layer textures and height ranges.
- Script-controlled parcel sale state.
- Scripted object return by object ID.
- Scripted object return by owner.
- Scripted object ownership transfer.
- Direct delivery of inventory items to an avatar.
- Nested destination-folder support for delivered inventory.
- Active-group matching.
- Filtered attachment queries.
- Synchronous notecard text searching.
- Sculpt-map animation state and playback fallback.

## Expanded LSL and OSSL compatibility

The enhanced scripting layer adds 55 LSL/OSSL functions together with the
required constants, helpers, events, and script-engine support.

### Security and avatar control

- `llSignRSA`
- `llVerifyRSA`
- `llGetRegionTimeOfDay`
- `llSetAgentRot`
- `llTransferOwnership`

### Land, objects, inventory, and attachments

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

## GLTF and PBR material overrides

- `llSetLinkGLTFOverrides`
- Per-face GLTF override handling.
- Base-color and alpha overrides.
- Metallic and roughness overrides.
- Emissive overrides.
- Double-sided material state.
- Extension JSON support.
- Compact LLSD notation encoding.
- GLTF asset-data parsing required by the override system.

The separate `PRIM_GLTF_*` primitive-parameter subsystem is not included.

## Combat2 scripting

- `llDamage`
- `llAdjustDamage`
- `llDetectedDamage`
- `llDetectedRezzer`
- Object-health support through dynamic attributes.
- Extended `llGetHealth` behavior for supported object targets.
- Script-engine registration for:
  - `on_damage`
  - `final_damage`
  - `on_death`
- Correct rezzer tracking for `llDetectedRezzer`.

## EEP environment scripting

- `llGetEnvironment`
- `llReplaceEnvironment`
- `llSetEnvironment`
- `llReplaceAgentEnvironment`
- `llSetAgentEnvironment`
- Region-scoped sky and water access.
- Parcel-scoped sky and water access.
- Per-agent environment overrides for trusted Experience-Lite scripts.
- Supporting sky, water, day-cycle, texture, and environment constants.

## Pathfinding

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

The implementation provides approximate, region-local A* navigation using
terrain, scene queries, obstacle bounds, and OpenSim keyframe motion. It is not
a physics-engine-native navigation service and requires runtime validation on
different terrain layouts and variable-sized regions.

## Experience-Lite

- `llRequestExperiencePermissions`
- `llReleaseExperiencePermissions`
- `llIsExperienceTrusted`
- `llGetExperiencePermissions`
- `llExperienceCanAutoGrant`
- `llAgentInExperience`
- `llGetExperienceDetails`
- `llGetExperienceErrorMessage`
- `llCreateKeyValue`
- `llReadKeyValue`
- `llUpdateKeyValue`
- `llDeleteKeyValue`
- `llKeyCountKeyValue`
- `llKeysKeyValue`
- `llDataSizeKeyValue`
- `llGetExperienceKeyValueStoreStats`
- `llSitOnLink`
- `llOpenFloater`

Experience-Lite provides configurable grid-local trust and persistent
per-region/per-owner key-value storage. It is intentionally smaller than the
complete Second Life Experience service.

`llOpenFloater` remains a stub because OpenSim does not currently provide the
required viewer-hosted floater service.

The script engine also recognizes:

- `experience_permissions`
- `experience_permissions_denied`

## Sit-target and avatar-animation enhancements

- Real enforcement of scripted-only sit targets.
- Working storage and lookup for LSL sit flags.
- Configurable region-wide male walk-animation override.
- Configurable region-wide female walk-animation override.
- Movement-animation resend protection after region crossings.
- Movement-animation resend protection after movement updates.

## Region crossings and attachment reliability

- Configurable agent-transfer update timeout.
- Optional preservation of avatar velocity during crossings.
- Configurable limits on crossing velocity.
- Configurable source cleanup delay.
- Configurable attachment cleanup delay.
- Reduced visible attachment detach/reattach flashing.
- Exception protection around attachment script-state restoration.
- Replacement of stale duplicate attachments.
- Removal of failed incoming attachment entries.
- Coordination of queued attachment script restarts during crossings.

## Map-tile generation

- Background map-tile generation.
- Region startup no longer has to block on map rendering.
- Grid registration no longer has to wait for map rendering.
- Configurable map-tile startup delay.
- Configurable map-rendering thread stack size.
- Optional wait-for-empty behavior.

## Weather module

An experimental, configurable region-weather system supporting:

- Rain.
- Snow.
- Storms.
- Lightning.
- Thunder.
- Wind integration.
- Cloud integration.
- Region chat commands.
- Automatic weather cycling.
- Configurable emitter behavior.
- Configurable weather textures.
- Configurable environmental settings.

Weather is optional and requires region-specific validation for variable-sized
regions, terrain differences, building penetration, emitter limits, textures,
and environmental behavior.

## Group Auto Invite module

- Automatically invites arriving avatars to a configured group.
- Supports a configurable invitation message.
- Uses a small groups-interface extension.

## Region Web module

- Per-region home pages.
- Blog and news content.
- LSL scripting reference pages.
- OSSL scripting reference pages.
- Feature-guide pages.
- Inventory-backed image presentation.
- Protected estate administration interface.
- Review of selected OpenSim configuration.
- Editing of selected OpenSim configuration.
- Reloading of selected OpenSim configuration.

Region Web operates independently from the currency web interface.

## Region Currency module

An optional web front end for an existing `IMoneyModule`:

- Avatar wallet and balance display.
- Transaction statements.
- User-to-user transfers.
- Purchase requests.
- Currency grants.
- Administrative money controls.
- CSV exports.
- Optional PayPal Checkout integration.

Region Currency is disabled by default. It is not the underlying currency
backend. PayPal is also disabled by default and configured for sandbox use
until an operator explicitly supplies credentials and changes the
configuration.

Real-money processing requires a separate security, accounting,
failure-recovery, and legal review.

## MoneyServer production enhancements

- Complete grid MoneyServer, region currency module, and MySQL money-data wrapper.
- Viewer currency purchases without an external currency.php helper.
- Configurable successful-purchase limits through TotalDay, TotalWeek, and TotalMonth.
- Purchase-limit accounting restricted to successful BuyMoney transactions.
- UTC calendar periods with Monday-based weekly accounting.
- Atomic balance credit and transaction-ledger recording.
- Idempotent viewer confirmation UUID handling to prevent duplicate credits.
- CurrencyMaximum enforcement before changing the balance.
- Existing group, email-lock, banker, transfer, object-payment, upload-charge, and land-sale controls retained.
- OpenSim console-appender registration so background diagnostics move and redraw the MoneyServer # prompt correctly.
- Focused production validation and SQL verification documents included with the module.

The source integration does not alter a live in\MoneyServer.ini. Operators
must review the included example and validation documents before activating or
replacing a production MoneyServer.
## Important limitations

- New enhanced database migrations are currently MySQL-only unless equivalent
  SQLite or PostgreSQL migrations are added.
- Experience-Lite is not complete Second Life Experience-service
  compatibility.
- The separate `PRIM_GLTF_*` primitive-parameter implementation is not
  included.
- The temporary cloud-avatar appearance fallback is not included.
- Dead or duplicate legacy hooks were not included.
- The PayPal return path credits after the browser return flow rather than a
  server-to-server webhook, so interrupted returns require operational
  handling.
- A feature appearing in this README does not mean it is enabled in a
  production configuration.

## Branch model

| Branch | Purpose |
|---|---|
| `feature/grid-services-enhancements` | Preserved grid, identity, login, abuse-report, parcel, and service enhancements |
| `feature/region-scripting-enhancements` | Preserved region, scripting, environment, module, and simulator enhancements |
| `enhanced/integration` | Official OpenSim plus all OpenSim Enhanced features |
| `upstream/master` | Official OpenSimulator development history |
| `origin/*` | Published OpenSim Enhanced branches |

Production or release work should be based on tested commits from
`enhanced/integration`, not directly on an individual feature branch.

## Keeping current with official OpenSim

Official changes are merged rather than replacing the enhanced repository:

```bat
git checkout enhanced/integration
git status
git fetch upstream --prune
git merge --no-ff upstream/master
runprebuild.bat
dotnet build OpenSim.sln -c Release
```

Create a safety tag before each upstream merge and create a new build-clean tag
only after the complete solution builds and controlled tests pass.

## Building

On Windows:

```bat
runprebuild.bat
dotnet build OpenSim.sln -c Release
```

See `BUILDING.md` for base build requirements. Configuration examples for
enhanced services and modules remain with their respective source and
`bin/*.ini.example` files.

## Attribution and support

OpenSim Enhanced retains the OpenSimulator license and source history.
Historical source provenance remains available in the Git commit history.

Report problems caused by enhanced features to this repository. Problems that
are reproducible on an unmodified official OpenSim build should be reported to
the official OpenSimulator project.

## Additional grid add-on modules

The following optional add-on modules are included under `addon-modules` and
are generated into the OpenSim Enhanced solution by `runprebuild.bat`:

- **Gloebit** — optional Gloebit economy integration.
- **OpenSimMutelist** — external mute-list service integration.
- **OpenSimProfile** — external profile, picks, classifieds, notes and interests integration.
- **OpenSimSearch** — external viewer search service integration.
- **OpenSimTide** — configurable per-region tide and water-level simulation.

These modules are not enabled automatically. Their supplied configuration
examples must be reviewed and merged into the appropriate Robust, GridCommon,
or region configuration before runtime activation.
