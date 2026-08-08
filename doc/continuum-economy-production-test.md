# ContinuumEconomy production-test runbook

ContinuumEconomy is a production-test candidate, not a production-approved
currency authority. Its separately named service, region connector, three
storage providers, guarded migration tool and shared acceptance suite exist.
This runbook records the live simulator tests still required before cutover.
Build from the repository root (the Codex integration worktree is
`S:\Github\OpenSim-Continuum-complete`).

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
| Login/restart | Balance appears at login and remains identical after ContinuumEconomy.Service, Robust, region, and full-grid restarts. |
| Resident payment | Pay another resident; verify both balances, viewer notification, history, and safe retry of the same request. |
| Script payment | Verify `money()` delivery, `llGiveMoney`, debit permission denial, insufficient funds, and duplicate request handling. |
| Object sale | Buy copy/original/contents; delivery failure cancels the hold, success captures exactly once, and concurrent spend cannot consume held funds. |
| Land | Test authorized and rejected parcel purchases, ownership delivery, rollback, and history. |
| Fees | Test upload and group-creation fees at zero and non-zero settings, including insufficient funds. |
| Currency purchase | Test allowed banker IPs, invalid credentials, per-period credit limits, maximum balance, and audited actor/reason. PayPal or real-money processing is out of scope. |
| Stipends | Run the same stipend job twice and prove the stable operation ID prevents a second credit. |
| Groups | Register a group, verify account type 100, membership fee behavior, group payments, history, and rejection of an existing resident-class UUID. |
| Web/API | Verify balance and paged history authorization; no connection string, access key, or unrelated resident data may leak. |
| Multi-region | Simultaneously spend one balance from two regions; total committed debit must never exceed available funds. |
| Failure recovery | Interrupt ContinuumEconomy.Service/database during authorize, delivery, capture, transfer, and credit; restart and retry using the same IDs. Inspect old holds with `holds`. |
| Hypergrid | Confirm the local grid remains currency authority, foreign identities cannot gain banker privileges, and local balances are not disclosed remotely. |
| Performance | Run representative concurrency and history loads while measuring database locks, latency, errors, and connection use. |

Also rerun the complete simulator regression suite: inventory creation and script paste/compile, assets, login, teleport, crossings, attachments, parcel/region panels, Display Names across restart, Experiences, Abuse Reports, search, shutdown, and restart.

## Rollback boundary

Before the first Continuum transaction, rollback is restoring the snapshot and legacy configuration. After Continuum accepts any transaction, do not merely flip the config back: stop writes, retain both databases and logs, reconcile every post-cutover operation, and execute an approved data migration or accounting correction. Never replay successful transfers with new UUIDs.

## Release decision

Promotion requires a clean Release build, a passing dedicated-provider self-test, every applicable matrix row recorded, no unresolved authorized holds, restart persistence, and an operator-approved backup and reconciliation rehearsal. Compile success and the automated XML-RPC smoke test alone are insufficient.
