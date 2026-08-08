# ContinuumEconomy

ContinuumEconomy is the production successor being developed from the current
DTL/NSL-compatible MoneyServer stack. It retains OpenSim's `IMoneyModule`
extension boundary while adding selected WhiteCore economy behaviour through
independently implemented Continuum services.

The existing MoneyServer remains an independent deployable compatibility
module. It does not load this assembly and has no ContinuumEconomy switch.
ContinuumEconomy is deployed with its own `ContinuumEconomy.Service.exe`,
`ContinuumEconomy.ini`, and separately selected `ContinuumEconomyModule` region
connector. It never loads or redirects the compatibility MoneyServer.

The first assembly provides atomic MySQL, PostgreSQL and SQLite ledgers with deterministic
account locking, unique transaction IDs, request fingerprints, idempotent
results, 64-bit balances, and independent tables that cannot alter a deployed
legacy MoneyServer database.

Administrative credits, initial balances, viewer currency purchases and group
accounts use audited adjustment or registration operations behind explicit
service authorization. A zero UUID cannot mint currency through the ordinary
transfer API.

Donor lineage:

- DTL/NSL MoneyServer: deployed protocol and region compatibility base.
- WhiteCore `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa`: behavioural reference for
  user/group accounting, history, fees, purchase controls and scheduling.
- Gunthar-derived RegionCurrency: optional wallet/admin interface; its local
  file ledger is not used as the grid authority.
- Continuum: independent transactional implementation and hardening.

Gloebit is excluded. Use a separate production-test deployment and complete
`doc/continuum-economy-production-test.md` before considering a live cutover.

`ContinuumEconomy.Migrate verify` is a read-only readiness check for the selected
provider. It is not run by MoneyServer.

`ContinuumEconomy.Migrate self-test --confirm=RUN-ON-DEDICATED-TEST-DATABASE`
requires a database name containing `test`. It performs real atomic, replay,
conflict, concurrent overspend, purchase-hold and history tests using unique
UUIDs. It never deletes rows and must never be pointed at production.

The legacy importer has two explicit operations. `Analyze()` only reads the
legacy `balances` and `transactions` tables. `Import()` requires an empty target,
rejects invalid UUIDs and negative balances, copies balances in one repeatable
read transaction, archives transaction history separately, reconciles every
balance, and commits an import manifest. Stop MoneyServer and snapshot the
database before running the command-line migration tool; it must never be used
against a live service.

## Offline migration tool

`ContinuumEconomy.Migrate analyze` is read-only. It reports legacy account and
history counts, the total balance, invalid accounts and whether the new ledger
is empty. The connection string is accepted only through the
`CONTINUUM_ECONOMY_CONNECTION_STRING` environment variable so credentials do
not appear in the process command line.

`CONTINUUM_ECONOMY_STORAGE_PROVIDER` selects `MySQL`, `PostgreSQL`, or `SQLite`
and defaults to `MySQL` for the current migration utility. Provider aliases use
the same names as OpenSim (`OpenSim.Data.MySQL.dll`,
`OpenSim.Data.PGSQL.dll`, and `OpenSim.Data.SQLite.dll`). All three providers
implement the same ledger, account and purchase contracts and pass the shared
`EconomyAcceptanceSuite`; there is no silent provider fallback.

The `import` operation additionally requires all of these literal flags:

- `--moneyserver-stopped`
- `--database-snapshot-complete`
- `--confirm=IMPORT-LEGACY-MONEYSERVER`

They do not replace operational verification. The importer independently
rejects invalid legacy accounts and any non-empty target ledger.

`ContinuumEconomy.Migrate initialize` creates or upgrades only the independent
`continuum_economy_*` tables and requires
`--confirm=CREATE-CONTINUUM-ECONOMY-SCHEMA`. It does not alter legacy tables,
import balances or select the new ledger for MoneyServer.

The ledger history API is account-scoped, newest-first and capped at 500 rows
per request. It returns direction, counterparty, resulting balance, status,
region/object context and failure reason without granting callers direct SQL
access. Administrative APIs must apply their own authorization before exposing
this data.

Privileged credits and debits use a separate adjustment operation. They require
a non-zero actor UUID and explicit reason, reserve a globally unique operation
ID, update one account atomically and retain both successful and
insufficient-funds outcomes for safe retries. Resident transfer calls cannot
reach this primitive directly, and the zero UUID is never treated as a mint.

Object and land purchases use authorization holds. Authorization reserves
spendable funds without crediting the seller; successful delivery captures the
hold atomically, while failed delivery cancels it without a reverse transfer.
Ordinary transfers subtract active holds when checking available funds, which
prevents concurrent spending from consuming money reserved for a purchase.

Continuum also implements the additive `IReservedMoneyModule` contract for
core fees. Upload inventory delivery, both official Groups implementations and
classified creation reserve the charge before granting the benefit, capture it
only after the downstream operation succeeds, and cancel it on a reported
failure. MoneyServer Compatibility and unrelated money modules retain the
existing `IMoneyModule` behavior. A benefit followed by two failed idempotent
capture attempts is logged with its reservation UUID and remains visible to the
operator `holds` report for reconciliation; it is never silently reversed or
replayed under a new ID.

Authenticated transfers, direct charges, object-purchase authorizations and fee
reservations recognize the service's explicit `Invalid session` response. A
still-connected root agent is re-registered once and the identical request,
including its original transaction or reservation UUID, is retried. Malformed
requests and transport outages are not treated as expired sessions.

The authenticated service requires a unique shared secret of at least 32
characters. The connector adds that secret and a UUID operation key to every
region-originated request. Resident transfers additionally require the avatar's
current session and secure-session UUIDs. Viewer currency purchase requests do
not receive the region secret; they are authenticated with the secure session
recorded during `ClientLogin`.

The region connector registers no inbound money XML-RPC handlers. In
particular, the legacy `SendMoney`, `MoveMoney`, `AddBankerMoney`,
`GetBalance`, `UpdateBalance`, `UserAlert`, and `OnMoneyTransfered` names are
not exposed on a simulator's public HTTP port. Trusted force-transfer and
administrative-credit operations exist only on the separately deployed service
and require its configured region secret; untrusted callers cannot turn a
caller-supplied access code into a trusted request.

Object sales use `AuthorizePurchase`, deliver through OpenSim's buy/sell module,
then call `CapturePurchase`. Exceptions call `CancelPurchase`, leaving the
seller uncredited and releasing the buyer's hold. Direct transfers remain
atomic and idempotent.

Land preflight and preparation require the secure session established by
`ClientLogin` and validate the requested amount against authoritative available
funds. The existing OpenSim land-sale transaction remains responsible for the
actual debit and ownership change; the preparation calls do not debit twice.

Successful transfers return both resulting balances. The connector immediately
updates sender and receiver viewers that are connected to that simulator. It
does not send session data or balance callbacks to region URLs supplied by a
login request; a resident on another simulator reads the authoritative balance
on its next normal balance request.

## Build and deployment

Run `runprebuild.bat`, then build `OpenSim.sln` in Release configuration. Copy
`ContinuumEconomy.ini.example` to `bin/ContinuumEconomy.ini` and configure the
provider, connection string, system actor, limits and a new shared secret. Copy
the `[Economy]` settings from `OpenSim.ini.example` into every simulator and use
the same secret there.

Initialize the selected provider with the guarded migration command before
starting `bin/ContinuumEconomy.Service.exe`. Select exactly one authority:

`EconomyModule = ContinuumEconomyModule` for ContinuumEconomy, or
`EconomyModule = DTLNSLMoneyModule` for MoneyServer Compatibility—never both.

For a disposable test service, set `CONTINUUM_ECONOMY_TEST_SECRET` to the same
32-or-more-character test secret. Optionally set `CONTINUUM_ECONOMY_TEST_URL`
(the default is `http://127.0.0.1:18119/`), then run
`python addon-modules/ContinuumEconomy/tests/xmlrpc_smoke.py`. It uses only the
Python standard library, generates unique UUIDs, and intentionally retains its
audited test rows. It exercises transaction lookup, charges, force/move
transfers, controlled credits, replay safety, final balances and invalid-secret
rejection in addition to the viewer purchase paths. Never point it at a
production currency service.

`ContinuumEconomy.Migrate holds` is a read-only operational report of
authorized purchases that have not been captured or cancelled. It defaults to
holds older than 15 minutes, accepts `--older-than-minutes=N`, caps output at
500 rows and never resolves a hold automatically.

Manual `capture-hold` and `cancel-hold` commands require the exact purchase and
buyer UUIDs, a delivery-success or delivery-failure evidence assertion, and a
different literal confirmation for each outcome. Buyer binding is checked
again inside the locked database operation.

Group economy accounts use account type 100, distinct from resident and legacy
avatar classifications. `register-group` requires explicit operation, group and
actor UUIDs, a name and literal confirmation. Registration reserves the global
operation ID and writes an immutable provenance row; an existing UUID with a
different account class is rejected.
