# OpenSim Continuum MoneyServer

This package preserves the reconciled DTL/NSL MoneyServer as an independent,
working compatibility subsystem. It is not a host for ContinuumEconomy.
ContinuumEconomy is developed and tested separately and must not alter this
service or its region module. The uploaded archive remains the functional
baseline; ManfredAabye's repository was used only as a reference.

## Included components

- `OpenSim.Data.MySQL.MySQLMoneyDataWrapper`
- `MoneyServer`
- `OpenSim.Modules.Currency`
- Net 8 `prebuild.xml` definitions
- A canonical `MoneyServer.ini.example`
- The region-side `[Economy]` configuration sample

## Preserved enhancements

- Viewer currency purchases recorded as successful `BuyMoney` transactions.
- Atomic balance credit and ledger insert.
- Quote-confirmation idempotency.
- UTC daily, Monday-based weekly, and monthly purchase limits.
- Maximum balance enforcement.
- Optional group and registered-email purchase gates.
- Banker endpoint source-IP allowlist.
- MoneyServer console appender registration.
- Optional scheduled stipends.

## Corrections made during reconciliation

1. Currency quote and purchase requests now require the avatar's active secure session. The previous purchase endpoints accepted an avatar UUID without authenticating the supplied session.
2. Raw quote request logging that could expose a secure session ID was removed.
3. `CurrencyMaximum = 0` now consistently disables the maximum instead of allowing `UpdateBalance()` to reduce an online user's balance to zero.
4. Stipends now use `DoAddMoney()` rather than `DoTransfer()`. A system sender (`UUID.Zero`) does not have a normal balance and is not a valid funded transfer sender.
5. Stipends are recorded with `TransactionType.StipendBasic` instead of transaction type `None`.
6. Stipend recipients are validated and deduplicated.
7. Each stipend avatar/cycle uses a deterministic transaction UUID. Retries, missing state files, or restarts therefore cannot credit the same cycle twice.
8. Stipend timer re-entry is blocked, invalid intervals disable the feature, and state is stored beside the executable.
9. `BankerAllowedIPs` now normalizes IPv4-mapped IPv6 addresses and ignores invalid entries.
10. Database-side `giveMoney()` now applies a credit only while its transaction is still pending. This prevents a retry or concurrent stipend worker from crediting a successful transaction again.
11. Raw XML handlers explicitly disable external XML resolution, land/currency logging no longer exposes secure-session credentials or full email addresses, and unused raw-request dump helpers were removed.
12. Configuration comments were corrected to match actual behavior.

## Deliberately excluded

- Generated `.csproj`, `obj`, DLL, PDB, NuGet, and apphost files.
- `SineWaveCert.pfx` and `server_cert.p12`; private certificates must never be redistributed.
- Legacy `prebuild-*.xml` files superseded by the current `prebuild.xml` definitions.
- The incomplete `OpenSimEnhanced-Production-v1.1.0` installer/documentation directory, which referred to files not present in the archive.
- The generic `addon-modules/README` file.

## Important configuration behavior

`CurrencyOnOff` must be exactly `on` to enable viewer purchases. `TotalDay`, `TotalWeek`, `TotalMonth`, and `CurrencyMaximum` use `0` to disable the corresponding limit. Purchase totals count successful `BuyMoney` ledger entries only.

The region process must also load `DTLNSLMoneyModule`. Copy the settings from
`addon-modules/OpenSim-Modules-Currency/config/OpenSim.ini.sample` into the
region's active `OpenSim.ini` (or an included file), set its MoneyServer URL to
the running service, and confirm the region log contains both
`Plugin Loaded: DTLNSLMoneyModule` and a successful `LoginMoneyServer` message
for the avatar. Merely copying newly compiled DLLs does not replace an existing
`MoneyServer.ini` or enable the region module.

`UserMailLock` checks only for a non-empty `UserAccounts.email` value. It is not email ownership verification.

`CurrencyGroupOnly` requires the group membership table to be available in the database used by MoneyServer. `CurrencyGroupID`, not the descriptive group name, is enforced.

`BankerAllowedIPs` must contain every exact trusted source address that calls `AddBankerMoney`. Loopback defaults work only when the caller and MoneyServer are on the same host.

Stipend cycles are anchored by `AnchorDateUtc`. With `IntervalDays = 7` and `AnchorDateUtc = 1970-01-05`, each cycle begins Monday UTC. The ledger is the idempotency authority; `stipend_lastcycle.txt` only avoids redundant checks after a fully successful cycle.

## Validation boundary

The solution and MoneyServer sources compile in the Continuum integration
branch, but compilation is not runtime certification. MoneyServer must pass its
original viewer, transfer, object-sale, stipend, restart, and database contract
without ContinuumEconomy loaded. Its legacy storage wrapper is MySQL-specific.
ContinuumEconomy has a separate acceptance harness and remains experimental;
it must not be deployed as a MoneyServer backend.
