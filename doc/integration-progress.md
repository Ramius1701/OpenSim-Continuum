# OpenSim Continuum integration progress

Last updated: 2026-08-08

Branch: `codex/complete-opensim-feature-set`

OpenSim Dev base: `247b9182c1ca0f11743de06a2808f003bc8e2a90`

This ledger distinguishes code completion from runtime certification. A feature
is not production-approved merely because it compiles or passes an isolated
test. The branch remains an alpha integration build until every applicable live
grid gate here and in `donor-feature-test-handoff.md` passes.

## Current checkpoint

| Area | Implemented | Automated evidence | Remaining live gate |
|---|---|---|---|
| ContinuumEconomy storage | Independent SQLite, MySQL/MariaDB and PostgreSQL ledgers, migrations, account registration, audited adjustments, idempotent transfers, holds and history | Shared acceptance suite passed for SQLite and PostgreSQL; MySQL previously passed its provider suite | Repeat the current shared suite against the exact production MySQL/MariaDB version and settings |
| ContinuumEconomy service | Separate service, authenticated region calls, sessions, transfers, currency purchase controls, object-sale holds and land preparation | SQLite XML-RPC wire test passed login, credit, transfer/replay/conflict, balances, currency purchase/replay, object authorize/capture/cancel, land preflight and invalid-session rejection | Restart, outage, concurrency, viewer and multi-region testing on a cloned grid |
| ContinuumEconomy connector | Separately selected module, viewer events, OpenSim money hooks, delivery-safe object sales and local balance notifications | Connector and complete Release solution build successfully | Exercise every viewer payment/fee path, crossings and remote-simulator recipients |
| MoneyServer Compatibility | Original service, datastore and DTL/NSL connector remain separately deployable; selection guard prevents registration when unselected | Complete Release build succeeds; ContinuumEconomy does not load or redirect MoneyServer | Run the legacy compatibility matrix against a cloned MoneyServer database |
| Display Names and aliases | Grid service, CAPS, persistence, search and compatibility paths are integrated | Complete solution builds | Verify nameplates, Nearby, search, relog, restart, crossings, multiple regions and Hypergrid boundaries |
| Experiences | Grid service, providers, CAPS, region policy and scripting integration are present | SQLite, MySQL/MariaDB and PostgreSQL paths compile | Verify viewer panels, permissions, KVP, estate/parcel policy, restarts and Hypergrid isolation |
| Abuse Reports | Grid service, providers, CAPS and administrative console workflow are present | All three provider paths compile | Submit, restart, retrieve/update administratively and test malformed/oversized input |
| Search | Optional OpenSimSearch integration and donor-derived completeness work are present | Complete Release build succeeds | Test people, places, land, events, classifieds, map, privacy, paging and propagation |
| Weather and Tide | Optional modules are packaged; Weather is reconciled to the Gunthar 0.3.3 behavior base | Both modules build | Run long-duration region, transition, crossing and viewer-rendering tests |
| Other recovered addons | Documented optional components are packaged | Complete Release build succeeds | Run each addon's handoff matrix and keep it disabled unless selected |

## ContinuumEconomy verified commits

- `f05c86cd08`: preserve MoneyServer Compatibility independently.
- `e8c4e4f51b`: document the separate economy architecture.
- `ffa77e9694`: complete the SQLite provider.
- `673e49bb00`: complete PostgreSQL and shared acceptance coverage.
- `1d71b35924`: add the deployable service and region connector.
- `21414c2f23`: harden land preparation and safe local balance updates.

The latest complete Release build passed with one known CS9193 warning in
upstream-derived viewer CAPS code and no errors. Generated
`bin/*.runtimeconfig.json` files and `.audit/` material are build/test artifacts
and intentionally remain untracked.

## Release sequence

1. Build from this isolated candidate checkout or a fresh clone of the branch;
   do not mix its binaries into an older OpenSim build directory.
2. Certify clean and upgrade migrations on every supported database.
3. Run the complete single-region viewer matrix.
4. Run multi-region, restart, service-outage and concurrency tests.
5. Run Hypergrid trust-boundary tests with a separate test grid.
6. Record results and defects here. Failed rows stay open until their fix and
   regression test are committed.
7. Promote only after no release-blocking rows remain. Production approval is a
   separate decision from implementation completion.

## Explicit boundaries

- `S:\Github\Casperia` is excluded and must not be used as a donor or worktree.
- WhiteCore WebUI and OpenSim-Grid-Interface are deferred until OpenSim-side
  runtime testing is complete.
- Gloebit is not part of ContinuumEconomy.
- PayPal donations, real-money settlement and future region billing are outside
  the in-world ledger and require a separate integration.
- Cross-region balance propagation does not trust arbitrary region URLs supplied
  at login. Local viewers receive immediate updates; other regions obtain the
  authoritative value through normal balance requests.
