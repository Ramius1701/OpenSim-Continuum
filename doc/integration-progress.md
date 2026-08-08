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
| ContinuumEconomy storage | Independent SQLite, MySQL/MariaDB and PostgreSQL ledgers, migrations, account registration, audited adjustments, idempotent transfers, holds and history | Shared acceptance suite passed for SQLite and PostgreSQL; PostgreSQL was repeated from a clean schema on 2026-08-08; MySQL previously passed its provider suite | Repeat the current shared suite against the exact production MySQL/MariaDB version and settings |
| ContinuumEconomy service | Separate service, authenticated region calls, sessions, transfers, currency purchase controls, object/fee holds and land preparation | SQLite and PostgreSQL XML-RPC wire tests passed; PostgreSQL covers trusted transfer/credit operations, captured/cancelled fee reservations, replay, final balances and invalid-secret rejection; both providers retained balances and replay safety across a full service stop/start | Outage, concurrency, viewer and multi-region testing on a cloned grid |
| ContinuumEconomy connector | Code-complete production-test candidate: separately selected module, viewer events, OpenSim money hooks, post-commit LSL `money()` delivery, payer failure feedback, immediate local balance refresh, delivery-safe paid object sales, reservation-safe uploads/groups/classifieds, ledger-free zero-price object delivery, ordered land settlement, exact-debit enforcement; no inbound legacy money RPC exposure on simulator HTTP ports; per-avatar login failures cannot disable the shared simulator module; script-supplied payment UUIDs are preserved; removed regions are purged; connected residents recover service sessions after a service-only restart and authenticated retries retain the same ID | Complete Release build passed; PostgreSQL fee-reservation and invalid-session wire contracts passed; focused .NET 8 land regressions passed rejection of missing/partial debits, exact-debit transfer and zero-price transfer | Runtime certification only: execute the cloned-grid matrix for failure injection, viewer/script payments, crossings, region removal, remote recipients and negative public-port probes |
| MoneyServer Compatibility | Original service, datastore and DTL/NSL connector remain separately deployable; selection guard prevents registration when unselected | Complete Release build succeeds; ContinuumEconomy does not load or redirect MoneyServer | Run the legacy compatibility matrix against a cloned MoneyServer database |
| Display Names and aliases | Code-complete production-test candidate: grid service, all-provider persistence, login/CAPS/search/LSL compatibility, explicit requested-ID cache refresh, and authoritative root-agent `DisplayNameUpdate` propagation after crossing/relogin/restart | LindenCaps project builds; full solution build remains required for this checkpoint | Runtime certification only: verify nameplates, Nearby, search, relog, restart, crossings, separate simulator processes and Hypergrid boundaries |
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
- `ece345a7a5`: record SQLite/PostgreSQL restart certification.
- `beb337fb9f`: add the reproducible service wire-test client.
- `20f1729fb3`: order land settlement, require an exact committed debit and preserve zero-price sales.
- `f336752f69`: refresh test-only adapters for current Dev interfaces.

The latest complete Release build passed with the four known CS9193 warnings
and no errors. Generated
`bin/*.runtimeconfig.json` files and `.audit/` material are build/test artifacts
and intentionally remain untracked.

### Provider certification evidence

| Date | Provider | Database | Result |
|---|---|---|---|
| 2026-08-08 | SQLite | Disposable local file | Schema initialization, XML-RPC workflow, service restart persistence and replay safety passed |
| 2026-08-08 | PostgreSQL | Fresh local `continuum_economy_pg_test` database | Initialization, verification, all 11 shared acceptance checks, XML-RPC workflow, service restart persistence and replay safety passed |
| 2026-08-08 | MariaDB 11.8.8 | Local server on port 3306 | Not rerun: server requires credentials not stored in the repository. Authentication was not weakened and no unrelated secrets were inspected |

The PostgreSQL test database is disposable and contains only generated test
UUIDs. Acceptance rows are retained until the local test database is explicitly
removed so the result can be inspected. This is not a production database.

### Land settlement evidence

On 2026-08-08 a disposable worktree generated the normally excluded
`OpenSim.Region.CoreModules.Tests` assembly under .NET 8. The focused
`TestPaidLandSaleRequiresExactCommittedDebit` and
`TestFreeLandSaleDoesNotRequireLedgerDebit` cases both passed. The first case
also proves that economy handlers execute before ownership finalization, so
module discovery order cannot transfer paid land before settlement.

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
