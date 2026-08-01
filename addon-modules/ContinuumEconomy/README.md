# ContinuumEconomy

ContinuumEconomy is the production successor being developed from the current
DTL/NSL-compatible MoneyServer stack. It retains OpenSim's `IMoneyModule`
extension boundary while adding selected WhiteCore economy behaviour through
independently implemented Continuum services.

The existing MoneyServer remains the deployable compatibility baseline. This
package is not selected by any configuration profile yet.

The first assembly provides an atomic MySQL ledger with deterministic account
locking, unique transaction IDs, request fingerprints, idempotent results,
64-bit balances, and independent tables that cannot alter a deployed legacy
MoneyServer database.

Administrative credits, group accounts, scheduled payments, legacy RPC adapters
and the region module will be added behind explicit service authorization. A
zero UUID cannot be used to mint currency through the ordinary transfer API.

Donor lineage:

- DTL/NSL MoneyServer: deployed protocol and region compatibility base.
- WhiteCore `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa`: behavioural reference for
  user/group accounting, history, fees, purchase controls and scheduling.
- Gunthar-derived RegionCurrency: optional wallet/admin interface; its local
  file ledger is not used as the grid authority.
- Continuum: independent transactional implementation and hardening.

Gloebit is excluded. Do not point a production region at this package until the
legacy importer, authentication, adapters and failure tests are complete.

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

The `import` operation additionally requires all of these literal flags:

- `--moneyserver-stopped`
- `--database-snapshot-complete`
- `--confirm=IMPORT-LEGACY-MONEYSERVER`

They do not replace operational verification. The importer independently
rejects invalid legacy accounts and any non-empty target ledger.

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
