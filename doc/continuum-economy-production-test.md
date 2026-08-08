# ContinuumEconomy production-test runbook

ContinuumEconomy is a production-test candidate, not a production-approved
currency authority. Its separately named service, region connector, three
storage providers, guarded migration tool and shared acceptance suite exist.
This runbook records the live simulator tests still required before cutover.
Build from the repository root (the Codex integration worktree is
`S:\Github\OpenSim-Continuum-complete`).

## Automated checkpoint

As of commit `21414c2f23`, the complete Release solution builds and the SQLite
XML-RPC smoke test covers authenticated login, initial balance, transfer replay
and conflict handling, balance reads, controlled currency purchase, object-sale
authorization/capture/cancellation, land preflight and invalid secure-session
rejection. The shared ledger acceptance suite passed on SQLite and PostgreSQL.
On 2026-08-08 PostgreSQL was repeated from a clean schema: all 11 acceptance
checks and the XML-RPC workflow passed. The PostgreSQL wire test now also covers
transaction lookup, charges, force/move transfers, controlled region credits,
banker credits, operation replay, exact final balances and invalid shared-secret
rejection. Fixed SQLite and PostgreSQL test balances
plus transaction replay state survived complete service stop/start cycles. These
checks establish test readiness; they do not replace the live matrix below.
Repeat the current suite on the exact MySQL/MariaDB production-test version
before promotion.

Focused land-core regressions also pass under .NET 8: missing and partial debits
leave paid parcel ownership unchanged, an exact committed debit permits the
transfer, and a zero-price parcel transfers without a ledger debit. Live viewer
testing must still verify the complete seller credit, parcel update and failure
messaging path.

The portable wire-test client is
`addon-modules/ContinuumEconomy/tests/xmlrpc_smoke.py`. It refuses to run without
an explicitly supplied `CONTINUUM_ECONOMY_TEST_SECRET`; use it only with a
disposable service/database because generated audit rows are retained. It also
proves captured and cancelled fee reservations against the configured provider.

## Required cutover gates

1. Clone the production configuration and databases into an isolated test grid. Never test against the live currency database.
2. Stop the cloned MoneyServer and all cloned regions, take a database snapshot, and retain the legacy binaries and configuration.
3. Set `CONTINUUM_ECONOMY_CONNECTION_STRING` only in the service account environment. Do not put credentials in scripts or source control.
4. Run `ContinuumEconomy.Migrate analyze`, resolve every invalid UUID or reconciliation mismatch, then run the guarded `initialize` and `import` operations described in the addon README.
5. Run `ContinuumEconomy.Migrate verify`. This validates only the experimental Continuum ledger; MoneyServer Compatibility neither loads nor validates it.
6. Against a separate database whose name contains `test`, run `ContinuumEconomy.Migrate self-test --confirm=RUN-ON-DEDICATED-TEST-DATABASE`. It checks audited credit, replay safety, conflicting request detection, concurrent overspend prevention, purchase holds/capture, and history. Unique test rows are retained.
7. Assign a dedicated, non-zero `SystemActorID`, generate a unique 32-or-more-character `RegionSharedSecret`, start `ContinuumEconomy.Service.exe`, and select `EconomyModule = ContinuumEconomyModule`. Never enable MoneyServer Compatibility and ContinuumEconomy as simultaneous authorities in one region.
8. Register existing group UUIDs with the guarded `register-group` command before testing group balances. New group creation charges are routed through `IMoneyModule`; automatic group-account classification remains a release gate and groups must not be treated as residents.

## Acceptance matrix

Record transaction UUIDs, balances, relevant logs, and pass/fail for every row.

| Area | Required test |
|---|---|
| Login/restart | Balance appears at login and remains identical after ContinuumEconomy.Service, Robust, region, and full-grid restarts. Restart only ContinuumEconomy.Service while residents remain connected: the first normal balance request must restore the authenticated service session without a relog, and an actual outage must return a failed balance response rather than a successful zero balance. Reject one login (including an NPC or deliberately invalid session) and prove existing and subsequent residents on every region of that simulator can still read balances and transact. |
| Resident payment | Pay another resident; verify both balances refresh immediately on the originating simulator, the payer receives a visible failure alert when rejected, history is correct, and retry of the same request is safe. |
| Script payment | Pay an object and prove the LSL `money()` event fires exactly once only after the authoritative transfer succeeds. Verify `llGiveMoney`, debit permission denial and insufficient funds. Force an ambiguous response/timeout, retry the same `llGiveMoney` transaction UUID, and prove the supplied UUID reaches the ledger and prevents a second debit. |
| Object sale | Buy zero-price and paid copy/original/contents. Zero-price delivery must not create a ledger row. Paid delivery failure cancels the hold, success captures exactly once, and concurrent spend cannot consume held funds. |
| Land | Test zero-price, authorized, insufficient-funds, service-failure and balance-race parcel purchases. Paid ownership must move only when `amountDebited` exactly equals the validated price; failed or partial debits must leave ownership unchanged. Verify history and seller credit. |
| Fees | Test uploads, group creation, group enrollment and new classifieds at zero and non-zero settings, including insufficient funds. For each operation prove the fee is reserved before the benefit, captured once after success, and cancelled after a forced downstream failure. Cross or log out during a fee request and prove the simulator neither throws nor submits a charge against a stale region presence. Force both capture attempts to fail after a benefit and verify the reservation UUID is logged and appears in `holds` for reconciliation. |
| Currency purchase | Test allowed banker IPs, invalid credentials, per-period credit limits, maximum balance, and audited actor/reason. PayPal or real-money processing is out of scope. |
| Stipends | Run the same stipend job twice and prove the stable operation ID prevents a second credit. |
| Groups | Register a group, verify account type 100, membership fee behavior, group payments, history, and rejection of an existing resident-class UUID. |
| Web/API | Verify balance and paged history authorization; no connection string, access key, or unrelated resident data may leak. |
| Simulator RPC exposure | Probe each simulator public HTTP port and confirm the legacy `SendMoney`, `MoveMoney`, `AddBankerMoney`, `GetBalance`, `UpdateBalance`, `UserAlert`, and `OnMoneyTransfered` XML-RPC methods are not registered. Confirm direct calls to privileged service methods with a missing or invalid region secret fail without changing balances. |
| Multi-region | Simultaneously spend one balance from two regions; total committed debit must never exceed available funds. Confirm a locally connected recipient receives an immediate balance update and a recipient on another simulator reads the committed balance on its next balance request. Remove/restart one region in a multi-region simulator and prove stale presences and objects in that region can no longer be resolved by the shared connector. |
| Failure recovery | Restart ContinuumEconomy.Service between balance validation and each authenticated transfer, direct charge, object authorization and fee reservation. Prove the connector restores the resident session once and retries the identical transaction/reservation UUID. Interrupt the database during authorize, delivery, capture, transfer, and credit; restart and retry using the same IDs. Malformed requests must not trigger session recovery. Inspect old holds with `holds`. |
| Hypergrid | Confirm the local grid remains currency authority, foreign identities cannot gain banker privileges, and local balances are not disclosed remotely. |
| Performance | Run representative concurrency and history loads while measuring database locks, latency, errors, and connection use. |

Also rerun the complete simulator regression suite: inventory creation and script paste/compile, assets, login, teleport, crossings, attachments, parcel/region panels, Display Names across restart, Experiences, Abuse Reports, search, shutdown, and restart.

## Rollback boundary

Before the first Continuum transaction, rollback is restoring the snapshot and legacy configuration. After Continuum accepts any transaction, do not merely flip the config back: stop writes, retain both databases and logs, reconcile every post-cutover operation, and execute an approved data migration or accounting correction. Never replay successful transfers with new UUIDs.

## Release decision

Promotion requires a clean Release build, a passing dedicated-provider self-test, every applicable matrix row recorded, no unresolved authorized holds, restart persistence, and an operator-approved backup and reconciliation rehearsal. Compile success and the automated XML-RPC smoke test alone are insufficient.
