# Economy donor-parity audit

## Product boundary

Continuum has two deliberately separate economy products:

1. **MoneyServer Compatibility** preserves the established DTL/NSL protocol,
   schema and deployment shape for existing grids.
2. **ContinuumEconomy** is the new ledger, service and separately named region
   connector. It may reproduce proven viewer behavior, but it does not load or
   redirect the compatibility service and never shares its live tables.

They should provide equivalent resident-facing basics where intended, but they
are not the same implementation and are never simultaneous authorities in one
region.

## Lineage and authority

- The recovered `opensim-lickx` archive at
  `6614599` is the exact three-part DTL/NSL evidence: region currency module,
  MoneyServer process, and MySQL data wrapper. Bundled certificates and the
  historical PHP helpers are not trusted deployment assets.
- Current Continuum MoneyServer Compatibility is a heavily adapted descendant,
  not a byte-for-byte preservation. The adaptations require a legacy acceptance
  matrix because the diff spans all three components.
- Gunthar revision `6c7021cc` contains OpenSim's sample money module and no
  production grid ledger. It is not an authority for ContinuumEconomy storage.
  RegionCurrency remains presentation/compatibility evidence only.
- WhiteCore revision `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` is behavioral
  evidence for resident and group balances, history, purchase controls,
  stipends and scheduled fees. WhiteCore's own `Economy.ini` warns that its
  BaseCurrency implementation is not for production or real money.
- Gloebit and Podex are excluded.

## Current parity and missing behavior

| Slice | MoneyServer Compatibility | ContinuumEconomy | Decision |
|---|---|---|---|
| Viewer balance and payments | Established DTL/NSL protocol, substantially hardened | Separately implemented | Both require the same viewer matrix; no shared authority. |
| Object, land and script payments | Present | Present with authorization/capture holds | Preserve compatibility behavior; prefer Continuum's delivery-safe hold model for the new product. |
| Upload, group creation and classified fees | Compatibility hooks present | Reservation-aware hooks present | Verify success-only charging and replay behavior. |
| Resident transaction history | Legacy endpoints retained only where authenticated/safe | Bounded immutable ledger history | Retain; expose through authenticated UI later. |
| MySQL/MariaDB | Required legacy backend | Required provider | Certify the actual production server/version. |
| PostgreSQL and SQLite | Not part of the preserved legacy product | Independent providers present | Required for ContinuumEconomy; provider parity is a release gate. |
| Currency purchase limits | Legacy quote/buy behavior, disabled by default | Daily/weekly/monthly limits present, disabled by default | Keep purchases opt-in and test limits/replay. Real-money settlement is out of ledger scope. |
| Initial/periodic stipends | Compatibility has a Continuum-added scheduler | Replay-safe day/week/month scheduler with anchor, eligibility and balance-limit policy | Keep the two implementations separate and certify restart/replay behavior on all providers. |
| Group balances and accounting | No complete WhiteCore-equivalent group ledger | Typed group accounts, transfers, bounded history and permission-checked viewer Summary/Details/Transactions are present | Liabilities and dividends remain separate missing scheduled behaviors; do not infer them from resident balances. |
| Recurring directory/group charges | Partial core fee hooks, not WhiteCore scheduling | Missing scheduler | Port behavior only after group accounting and idempotent job ownership are designed. |
| Group dividends/liabilities | Missing | Missing | Defer until group accounting is proven; never infer liability from a resident balance. |
| Purchase history and administrator reporting | Legacy history exists | Ledger/history exists, dedicated UI absent | WhiteCore WebUI/API phase should consume authenticated Continuum APIs, never database tables directly. |
| RegionCurrency | Deprecated compatibility portal | Not a backend | Keep disabled beside RegionWeb and exclude from new deployments. |
| PayPal donations/region billing | Not ledger responsibility | Not ledger responsibility | Treat donations and future invoices as external accounting events; no automatic minting without an explicit audited operation. |

## WhiteCore behavior worth adapting

The highest-value remaining WhiteCore findings are:

1. separate group accounts and group transaction history;
2. auditable group fees, land fees and object fees;
3. scheduled directory charges and group payments with single-job ownership;
4. configurable new-user and periodic stipends with eligibility policy;
5. bounded resident/group history and purchase reporting; and
6. administrator controls through the integrated WebUI.

These are behavioral requirements, not permission to copy WhiteCore's generic
data connector, RPC authentication, scheduler or integer ledger unchanged.
ContinuumEconomy's atomic, idempotent provider boundary remains authoritative.

## Compatibility, security and provenance

- **Robust/grid:** the economy authority is a dedicated private service. Region
  modules are clients; Robust, RegionWeb and WebUI do not own balances.
- **Databases:** MoneyServer Compatibility stays MySQL-specific. The new product
  must pass identical semantics on SQLite, MySQL/MariaDB and PostgreSQL.
- **Windows:** services, migrations, timers, certificate selection and restart
  recovery must pass under the supported Windows deployment.
- **Hypergrid:** currency is local-grid authority. A foreign visitor may use a
  local account only under explicit local policy; balances and privileged
  operations are not federated.
- **Viewer:** current Firestorm requires the established economy data, balance,
  transfer, object/land purchase and currency quote/buy flows; no custom viewer
  should be required.
- **Licensing:** preserve DTL/NSL/lickx notices for compatibility-derived files
  and WhiteCore BSD notices for any code actually ported. Record exact source
  commits for every future port.

## Required release gates

### MoneyServer Compatibility

- Run the original lickx-visible login, logout, balance, resident/object/script
  transfer, land, quote/buy, callback and history flows against a disposable
  clone of a real legacy schema.
- Prove existing balances and transaction history survive upgrade unchanged.
- Exercise insufficient funds, malformed requests, replay, cancellation races,
  pool exhaustion, service restart and concurrent debit attempts.
- Prove every Continuum hardening change preserves valid legacy requests while
  rejecting unsafe behavior.

### ContinuumEconomy

- Run the shared acceptance suite and wire tests from clean and upgraded schemas
  on SQLite, production-matched MySQL/MariaDB and PostgreSQL.
- Test multi-region login, crossings, simultaneous payments, object delivery,
  land ownership, all fee hooks, outage/retry and full-grid restart.
- Verify operation IDs are replay-safe and conflicting reuse cannot move funds.
- Complete the controlled migration rehearsal with snapshot, analysis, import,
  reconciliation and rollback to untouched MoneyServer binaries/configuration.
- Before group accounting advances, define group authority, roles, liabilities,
  scheduled-job ownership, deletion and Hypergrid boundaries and then apply the
  same three-provider acceptance standard.

## Recommendation

MoneyServer Compatibility is a compatibility module, not the base class or
runtime host for ContinuumEconomy. ContinuumEconomy is the production direction,
but remains a controlled-test candidate until the database and live-grid gates
pass. The next economy implementation slice should be a donor-informed group
accounting design and tests; it must not modify the legacy product.
