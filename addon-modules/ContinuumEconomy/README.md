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
database before running an eventual migration tool; no command-line migration
tool is published yet.
