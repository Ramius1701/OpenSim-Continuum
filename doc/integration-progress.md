# OpenSim Continuum integration progress

Last updated: 2026-08-09

Branch: protected `master`; active hardening branch `agent/complete-runtime-readiness`

OpenSim upstream synchronized through `a87969840ce2abde50309229347fe7257456e62e`

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
| MoneyServer Compatibility | Original service, datastore and DTL/NSL connector remain separately deployable; selection guard prevents registration when unselected; unused prototype mutation APIs are no longer public; simulated land-preparation endpoints that could succeed without a debit are not registered; public legacy request bodies are bounded; viewer currency purchases default off | Complete Release build succeeds; ContinuumEconomy does not load or redirect MoneyServer | Run the legacy compatibility matrix against a cloned MoneyServer database before enabling viewer purchases; verify land settlement through the authenticated region `TransferMoney` path and oversized/chunked request rejection |
| Display Names and aliases | Code-complete production-test candidate: grid service, all-provider persistence, login/CAPS/search/LSL compatibility, explicit requested-ID cache refresh, and authoritative root-agent `DisplayNameUpdate` propagation after crossing/relogin/restart | Complete Release build passed after authoritative root-agent propagation fix | Runtime certification only: verify nameplates, Nearby, search, relog, restart, crossings, separate simulator processes and Hypergrid boundaries |
| Experiences | Grid service, providers, complete viewer capability family, SL-compatible collection/profile LLSD, region/parcel policy and scripting integration are present; grid and standalone profiles select the required service connector; Robust request bodies and KVP pagination are bounded; MySQL now matches SQLite/PostgreSQL for empty ID/group queries, nullable profile fields and deterministic bounded KVP pages | Complete Release build passed after capability discovery, empty-list typing and safe profile/search serialization fixes | Runtime certification only: verify all floater tabs, permissions, KVP, estate/parcel policy, restarts, malformed/oversized requests and Hypergrid isolation |
| Abuse Reports | Grid service, providers, CAPS and administrative console workflow are present; shipped connector selection now registers storage without the obsolete undocumented Messaging gate; simulator and Robust independently enforce payload limits, including chunked private-service requests | Complete Release build passed with 0 warnings and 0 errors | Runtime certification only: submit with/without screenshot, restart, retrieve/update administratively, exercise two simulators and test malformed/oversized input |
| Search | Optional OpenSimSearch viewer-directory client is present for places, land, events, classifieds and map requests; request timeout and malformed-response handling are bounded. The historical unlicensed PHP/MySQL crawler is intentionally not bundled | Module and complete Release build required for this checkpoint | A compatible external backend is still required; test places, land, events, classifieds, map, privacy, paging, deletion and propagation. Authenticated native indexing/admin is deferred to OpenSim-Grid-Interface |
| Weather and Tide | Optional modules are packaged; Weather 0.3.4 is reconciled to the Gunthar 0.3.3 behavior base with bounded timers, serialized transitions, rollback and generated-object cleanup. Tide validates unsafe configuration, restores the original region water height on shutdown and is disabled by default | Complete Release build succeeds with 0 warnings and 0 errors | Run long-duration weather transitions, shutdown/restart cleanup, crossings, viewer rendering, tide-cycle and adjacent-region water-seam tests |
| GroupAutoInvite | Optional module is packaged; delayed requests are bounded, shutdown unsubscribes events, and failed service requests remain retryable | Complete Release build succeeds with 0 warnings and 0 errors | Verify membership, invitation authorization, crossings, logout, Groups outage/recovery and Hypergrid visitor policy |
| OpenSimMarketplace | Optional Direct Delivery service is packaged; request/body/inventory limits, operation idempotency, durable receipt ledger, placeholder-secret rejection and handler teardown are present | Complete Release build succeeds with 0 warnings and 0 errors | Deploy behind HTTPS and run forged request, nested inventory, permissions, duplicate delivery, restart, outage and website/MySQL workflow tests |
| HoloPhysicsGuard | Optional conservative physics sleeper is packaged and disabled/report-only by default; timer re-entry and interval overflow are bounded | Complete Release build succeeds with 0 warnings and 0 errors | PersistSleep remains MySQL/MariaDB-only; run native-physics false-positive, restart, database-loss and multi-region tests before any provider expansion |
| RegionCurrency | Deprecated compatibility copy retained only for deployments made against the earlier split; it disables itself whenever RegionWeb is enabled and is not a ledger/backend | Clean complete Release build succeeds with four known CS9193 warnings and zero errors | Compatibility regression only; do not use for new deployments or enable beside RegionWeb |
| RegionWeb | Canonical combined Gunthar-derived per-region website, estate tools and optional wallet presentation, separate from grid-wide WhiteCore WebUI; authenticated routes require HTTPS/Secure cookies, request bodies are bounded, root capture is prevented, and purchases/transfers/real-money integration fail closed | Complete Release build succeeds with zero warnings and zero errors after the focused change | Test public pages, Estate Admin and wallet together against exactly one selected economy backend; include escaping, authorization, token expiry, oversized bodies, proxy HTTPS, concurrent requests, Windows paths and sandbox-only PayPal acknowledgement |
| Other recovered tooling | Recovered Windows first-run wizard is quarantined: its launcher makes no changes, direct execution requires an explicit unsafe-development switch, optional features default off, and embedded example credentials were removed | Complete Release build succeeds; PowerShell syntax validation required | Do not use for production; any future rehabilitation requires secret-lifecycle, non-destructive repeatability, clean-VM and failure-cleanup tests |

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

The latest clean complete Release build passed with the four known CS9193
warnings and zero errors after synchronizing official OpenSimulator master.
Generated
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
