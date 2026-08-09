# OpenSim Continuum integration progress

Last updated: 2026-08-09

Branch: protected `master`; active hardening branch `agent/complete-runtime-readiness`

OpenSim upstream synchronized through `a87969840ce2abde50309229347fe7257456e62e`

This ledger distinguishes code completion from runtime certification. A feature
is not production-approved merely because it compiles or passes an isolated
test. The branch remains an alpha integration build until every applicable live
grid gate here and in `donor-feature-test-handoff.md` passes.

The four baseline CS9193 warnings were removed by passing local quaternion
variables with the explicit `in` modifier to the `ref readonly` conjugation API
without changing calculations.
The complete Release build is now expected to be warning-free.

## Current checkpoint

| Area | Implemented | Automated evidence | Remaining live gate |
|---|---|---|---|
| ContinuumEconomy storage | Independent SQLite, MySQL/MariaDB and PostgreSQL ledgers, migrations, account registration, audited adjustments, idempotent transfers, holds and history | Shared acceptance suite passed for SQLite and PostgreSQL; PostgreSQL was repeated from a clean schema on 2026-08-08; MySQL previously passed its provider suite | Repeat the current shared suite against the exact production MySQL/MariaDB version and settings |
| ContinuumEconomy service | Separate service, authenticated region calls, sessions, transfers, currency purchase controls, object/fee holds and land preparation | SQLite and PostgreSQL XML-RPC wire tests passed; PostgreSQL covers trusted transfer/credit operations, captured/cancelled fee reservations, replay, final balances and invalid-secret rejection; both providers retained balances and replay safety across a full service stop/start | Outage, concurrency, viewer and multi-region testing on a cloned grid |
| ContinuumEconomy connector | Code-complete production-test candidate: separately selected module, viewer events, OpenSim money hooks, post-commit LSL `money()` delivery, payer failure feedback, immediate local balance refresh, delivery-safe paid object sales, reservation-safe uploads/groups/classifieds, ledger-free zero-price object delivery, ordered land settlement, exact-debit enforcement; no inbound legacy money RPC exposure on simulator HTTP ports; per-avatar login failures cannot disable the shared simulator module; script-supplied payment UUIDs are preserved; removed regions are purged and unregister their money interface; `Close()` independently tears down every remaining region; connected residents recover service sessions after a service-only restart and authenticated retries retain the same ID; balance coverage and economy-data paths require both an enabled connector and a configured service URL; coverage, forced-transfer, administrative credit and charge boundaries reject invalid identities and negative amounts before network submission | Focused connector and complete Release builds passed after symmetric region teardown hardening; PostgreSQL fee-reservation and invalid-session wire contracts passed; focused .NET 8 land regressions passed rejection of missing/partial debits, exact-debit transfer and zero-price transfer | Runtime certification only: execute the cloned-grid matrix for failure injection, viewer/script payments, crossings, region removal/reload, backend replacement, remote recipients and negative public-port probes |
| MoneyServer Compatibility | Original service, datastore and DTL/NSL connector remain separately deployable; selection/configuration fail closed and one resident login failure cannot disable every region. Simulator XML-RPC exposure is limited to the required callbacks; unsafe prototype endpoints, recovered inbound callback-test handlers and the self-asserted legacy web-session/history API remain unregistered; bodies are bounded and viewer purchases default off. Region hooks fully tear down. Cleanup/stipends are single-flight, shutdown disposes timers first, and startup creates one database pool. Pool exhaustion releases the allocator lock during short waits and fails after 30 seconds. Legacy currency and account orchestration no longer reserves unused outer connections. Transfers lock the pending ledger row and both balances, then debit, credit and complete the ledger in one database transaction; failure rolls back balances. Shared balance boundaries reject invalid IDs and negative amounts. Cancellation changes only a pending row. Login rejects malformed identity/session fields, publishes a session only after durable account setup, and fails closed when account refresh fails; paired session reads, writes, notifications, viewer currency checks and logout are synchronized. Per-request certificate identity is isolated across concurrent requests. Resident transfer, logout, balance, transaction-detail and cancellation boundaries reject missing maps, mistyped fields and invalid UUIDs without throwing. Direct transfers require valid, distinct resident accounts. Privileged force, banker-credit, script-transfer and charge handlers apply the same malformed-request boundary. Force transfers require an exact trusted IP and valid distinct accounts; force/banker handlers reject malformed, negative and disabled zero-amount operations before inserting ledger rows. Authenticated script transfers preserve legacy system mint/burn compatibility while rejecting malformed, self-targeted, negative and disallowed zero-value operations before ledger insertion. Charges require a valid resident or configured banker identity and valid amount; a failed debit fails closed instead of attempting an unrelated banker credit. Connector service readiness, crossing-safe charges and accurate balance results are enforced | Focused compatibility service/connector and complete Release builds pass; ContinuumEconomy does not load or redirect MoneyServer | Run the legacy compatibility matrix against a cloned MoneyServer database; verify account/history and balance replies, allowed/rejected privileged sources and values, callbacks/negative probes, cancel/accept races, pool saturation, debit/replay concurrency, missing service URL, charge crossings, failure balance preservation, reload, authenticated land settlement, oversized/chunked rejection, timers, shutdown and restart |
| Display Names and aliases | Code-complete production-test candidate: grid service, all-provider persistence, login/CAPS/search/LSL compatibility, explicit requested-ID cache refresh, authoritative root-agent `DisplayNameUpdate` propagation after crossing/relogin/restart, bounded CAPS mutation/lookup requests, parameterized SQLite account/display-name search, authoritative serialized Robust validation/throttling across simulators, and bounded Hypergrid account/negative cache lifetimes | Complete Release build passed after authoritative root-agent propagation fix | Runtime certification only: verify nameplates, Nearby, search (including quotes/Unicode), weekly throttle and simultaneous rename attempts, relog, restart, crossings, separate simulator processes, malformed/oversized CAPS and Hypergrid boundaries |
| Experiences | Grid service, providers, complete viewer capability family, SL-compatible collection/profile LLSD, region/parcel policy and scripting integration are present; grid and standalone profiles select the required service connector; public CAPS and Robust request bodies, profile fields, query fan-out and KVP pagination are bounded; permission cache access is serialized across login/logout, CAPS, parcel and script threads; authoritative forget no longer depends on a local cache hit; the service rejects invalid profiles, owner replacement and permission writes for missing Experiences; KVP rejects missing/disabled/suspended Experiences, uses consistent UTF-8 byte quotas on every provider and serializes quota-check/write operations; MySQL matches SQLite/PostgreSQL behavior | Focused Experience service and Robust handler builds and the complete Release build passed after authoritative validation and KVP enforcement | Runtime certification only: verify all floater tabs, simultaneous permission changes, permissions, KVP quota/races, estate/parcel policy, crossings/logouts, restarts, malformed/oversized requests and Hypergrid isolation |
| Abuse Reports | Grid service, providers, CAPS and administrative console workflow are present; shipped connector selection now registers storage without the obsolete undocumented Messaging gate; simulator and Robust independently enforce payload limits, including chunked private-service requests; all fixed-width context fields are validated; MySQL report text capacity matches the bounded service contract and moderation null handling is consistent across SQLite, MySQL and PostgreSQL | Focused service/provider builds and the complete Release build passed after the provider-parity migration | Runtime certification only: submit short and maximum-size reports with/without screenshot on every provider, restart, retrieve/update administratively, exercise two simulators and test malformed/oversized input |
| Search | Optional OpenSimSearch viewer-directory client is present for places, land, events, classifieds and map requests; endpoint schemes and request timeouts are validated, external failure text is not relayed, malformed result conversion is contained, result processing is capped to viewer paging limits, and region-list reads are synchronized. The historical unlicensed PHP/MySQL crawler is intentionally not bundled | Focused module and complete Release builds passed after response-boundary hardening | A compatible external backend is still required; test places, land, events, classifieds, map, privacy, paging, deletion, malformed/oversized replies and propagation. Authenticated native indexing/admin is deferred to OpenSim-Grid-Interface |
| Prim script create/edit | The viewer `RezScript` creation, task-inventory permission, CAPS upload, raw asset storage and compilation route remains based on current OpenSim Dev; donor Experience integration is limited to associating and authorizing the selected Experience during CAPS save. Programmatic creation from supplied script text now stores UTF-8 instead of lossy ASCII, preserving Unicode in generated or pasted source | Asset-level UTF-8 regression added; focused Region Framework and complete Release builds passed with zero errors | In-world test: create a script in a prim, replace all source by paste (ASCII and Unicode), save/compile/run it, close/reopen the editor, restart the region, then repeat with an Experience selected and cleared |
| Weather and Tide | Optional modules are packaged; Weather 0.3.4 is reconciled to the Gunthar 0.3.3 behavior base with bounded timers, serialized transitions, rollback and generated-object cleanup; entry notices use one cancellable non-blocking timer per resident and all client events are detached on either removal or close. Tide validates unsafe configuration, restores the original region water height on shutdown, uses UTC cycle timing, emits script-facing levels with an invariant decimal point, honors the exact configured warning count and is disabled by default | Focused Weather, Tide and complete Release builds passed after lifecycle and script-channel hardening | Run long-duration weather transitions, login/crossing bursts, logout-before-notice, shutdown/restart cleanup, viewer rendering, locale-sensitive scripted tide parsing, exact warning counts, tide-cycle and adjacent-region water-seam tests |
| GroupAutoInvite | Optional module is packaged; delayed requests use one cancellable non-blocking timer per resident, duplicate crossing jobs are suppressed, logout/removal cancels pending work, shutdown unsubscribes events, and failed service requests remain retryable | Focused module and complete Release builds passed after delayed-job lifecycle hardening | Verify membership, invitation authorization, rapid crossings, logout-before-delay, region shutdown, login bursts, Groups outage/recovery and Hypergrid visitor policy |
| Groups | OpenSim Dev Groups remains the service/module base, with reservation-safe creation/enrollment fees and Gunthar-compatible invitation text. Shared modules fully detach on removal/close; cross-region routing uses locked snapshots and tolerates the last region disappearing. Local-grid/HG connectors validate HTTP(S) endpoints and contain transport/malformed replies. Cache request coalescing synchronizes in-flight keys; failed group updates return failure, null member/role results are normalized to empty cached collections, and role lookups no longer retry forever during an outage | Focused Groups and complete Release builds pass with zero errors | Test create/join fees, roles, notices, invitations, ejections, group chat, concurrent identical profile/member/role requests, crossings, removal/reload, invalid endpoints, timeout/malformed replies, service outage/recovery, Hypergrid trust boundaries and shutdown with connected residents |
| Offline IM | OpenSim Dev's local/Robust offline-message service remains the implementation base. The shared region connector owns exactly one undelivered-message subscription across hosted regions, unregisters callbacks during removal/close, tolerates a temporarily absent transfer module, snapshots region lookup safely, and contains service exceptions. Local storage serializes count/store, performs maintenance per configured store and removes unreadable poison rows. Remote endpoints must be absolute HTTP(S), trailing paths are normalized, and transport/malformed-response failures return controlled service failure instead of escaping through simulator events | Focused OfflineIM and complete Release builds pass with zero errors | Test direct, object, inventory-offer, group notice and invitation delivery across two regions; include concurrent sends at the exact cap, recipient login, corrupt-row recovery, duplicate retrieval, simulator/Robust restart, invalid URL, timeout/malformed response, service outage/recovery and database-provider parity |
| OpenSimMarketplace | Optional Direct Delivery service is packaged; request/body/inventory limits, deterministic operation idempotency, durable receipt ledger, placeholder-secret rejection and handler teardown are present; crash-truncated ledger tails cannot consume the next receipt, and first-valid receipt bindings cannot be replaced by later conflicting duplicate IDs | Focused module and complete Release builds passed after ledger recovery hardening | Deploy behind HTTPS and run forged request, nested inventory, permissions, duplicate/conflicting delivery IDs, truncated-ledger restart, disk-full, outage and website/MySQL workflow tests |
| HoloPhysicsGuard | Optional conservative physics sleeper is packaged and disabled/report-only by default; timer re-entry and interval overflow are bounded. PersistSleep validates its MySQL/MariaDB provider and sleep table during startup, so inherited SQLite/PostgreSQL settings or a missing manually managed table disable the module instead of causing recurring timer failures | Focused module and complete Release builds pass with zero errors | PersistSleep remains intentionally MySQL/MariaDB-only; run native-physics false-positive, sleep/wake, restart, database-loss and multi-region tests before any provider expansion |
| RegionCurrency | Deprecated compatibility copy retained only for deployments made against the earlier split; it disables itself whenever RegionWeb is enabled, is not a ledger/backend, refuses to start when its HTTP path is already owned, and removes partial registrations after startup failure | Focused compatibility-module and complete Release builds passed after handler-ownership hardening | Compatibility regression only; verify occupied-path failure and teardown, but do not use for new deployments or enable beside RegionWeb |
| RegionWeb | Canonical combined Gunthar-derived per-region website, estate tools and optional wallet presentation, separate from grid-wide WhiteCore WebUI; authenticated routes require HTTPS/Secure cookies, request bodies are bounded, root capture is prevented, purchases/transfers/real-money integration fail closed, and portal HTTP path ownership is verified before startup with partial-registration cleanup | Focused RegionWeb and complete Release builds passed after handler-ownership hardening | Test public pages, Estate Admin and wallet together against exactly one selected economy backend; include occupied-path failure, teardown, escaping, authorization, token expiry, oversized bodies, proxy HTTPS, concurrent requests, Windows paths and sandbox-only PayPal acknowledgement |
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

The latest complete incremental Release build passed with one known CS9193
warning and zero errors. The latest clean complete Release build passed with
the four known CS9193 warnings and zero errors after synchronizing official
OpenSimulator master.
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
