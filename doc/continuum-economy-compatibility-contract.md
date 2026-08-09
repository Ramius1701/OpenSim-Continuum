# ContinuumEconomy compatibility contract

This checkpoint freezes the externally visible behavior that the new
ContinuumEconomy must implement independently. The DTL/NSL-compatible
MoneyServer is maintained separately as MoneyServer Compatibility; it is not
modernized into, or used as a host for, ContinuumEconomy.

## Existing component boundary

| Component | Current responsibility | Continuum target |
|---|---|---|
| `OpenSim.Modules.Currency` | Compatibility region `IMoneyModule`, viewer events, land/object/script payment integration and callbacks. | Preserve for MoneyServer Compatibility; implement a separately named Continuum region connector. |
| `MoneyServer` | Independent compatibility service, XML-RPC/helper endpoints, policy and stipends. | Preserve as MoneyServer Compatibility; do not load ContinuumEconomy. |
| `OpenSim.Data.MySQL.MySQLMoneyDataWrapper` | Legacy balances, users, transactions and sales tables. | Read-only import/reconciliation source after cutover; never silently mix ledgers. |
| RegionWeb | Canonical optional website, wallet, purchase and administration presentation. | Client of authenticated economy APIs, never direct ledger owner. |
| RegionCurrency | Deprecated compatibility copy from the earlier split. | Disabled whenever RegionWeb is enabled; never a direct ledger owner. |

## Legacy endpoint surface

MoneyServer Compatibility currently registers the following endpoints. The new
Continuum service must provide deliberately versioned equivalents or an
explicit compatibility facade without sharing the legacy process:

- session and balance: `ClientLogin`, `ClientLogout`, `GetBalance`;
- transaction lifecycle: `TransferMoney`, `GetTransaction`, `CancelTransfer`;
- privileged/system paths: `ForceTransferMoney`, `PayMoneyCharge`,
  `AddBankerMoney`, `SendMoney`, `MoveMoney`;
- web session/history: `WebLogin`, `WebLogout`, `WebGetBalance`,
  `WebGetTransaction`, `WebGetTransactionNum`;
- land: `preflightBuyLandPrep`, `buyLandPrep`, `/landtool.php`;
- viewer currency purchase: `getCurrencyQuote`, `buyCurrency`,
  `/currency.php`; and
- simulator callbacks: `OnMoneyTransfered`, `UpdateBalance`, `UserAlert`.

These names are a migration compatibility requirement, not approval of their
current authentication or payload design. New native APIs require service
authentication, replay protection, bounded input and transaction idempotency.

## Region behavior to preserve

- viewer balance requests and balance-change notifications;
- resident and object payments, including script `money()` delivery;
- `llGiveMoney`/`llTransferLindenDollars` integration through `IMoneyModule`;
- land validation and ownership transfer only after a committed debit;
- object purchases only after price, sale state and ownership are revalidated;
- upload, group creation, classified and directory charges;
- useful insufficient-funds and transaction-description messages; and
- local-grid authority for Hypergrid visitors.

## New ledger invariants

1. A non-zero transaction ID identifies exactly one canonical request.
2. Repeating the same request returns the stored result without moving money.
3. Reusing an ID with changed fields is rejected as a conflict.
4. Debit, credit, resulting balances and audit record commit atomically.
5. Account rows are locked in deterministic order.
6. Ordinary transfers cannot mint or destroy currency.
7. Administrative credit/debit uses separate authenticated operations.
8. Every committed change has immutable actor, reason, type and resulting balances.
9. Failed delivery after a purchase uses a linked compensating transaction; history is never deleted.
10. PayPal donations, currency orders and future region billing remain distinct from the in-world ledger.

## Migration rule

The first Continuum tables use the `continuum_economy_` prefix. Import tooling
must copy a consistent snapshot, reconcile every account against legacy
transaction totals, produce a signed/reportable discrepancy list, and require
an explicit cutover. It must never update the old and new ledgers concurrently
without a designed dual-write protocol and reconciliation test.

The importer must run while the legacy MoneyServer is stopped. Transaction
history is archived in `continuum_economy_legacy_transactions`; it is not replayed
into the new ledger and therefore cannot change imported balances. A completed
manifest records source account count, total balance and history-row count.
The offline tool accepts database credentials only through an environment
variable, provides a read-only `analyze` mode, and requires explicit stopped
service, completed snapshot and destructive-import confirmation flags.
Schema initialization is a separate confirmed command and creates only
`continuum_economy_*` tables; it neither imports balances nor changes the active
MoneyServer configuration or runtime backend.

## Donor boundary

WhiteCore supplies behavior and schema requirements for group accounting,
history, fees, limits and scheduling. Its service registry, generic database
connector and financial transaction implementation are not copied. Gunthar's
RegionCurrency and local-ledger attempt inform integration and usability, but a
per-region file is not authoritative. Gloebit is excluded.
