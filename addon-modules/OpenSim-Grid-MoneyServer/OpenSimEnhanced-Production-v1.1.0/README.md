# Casperia MoneyServer Production Fix v1.1.0

This package supersedes **v1.0.0**. Do not deploy v1.0.0: it avoided the console problem by disabling periodic diagnostics. v1.1.0 keeps the diagnostics and fixes the console/prompt integration properly.

## What this build changes

### Completed purchase-limit TODO

The following existing settings now work on successful viewer currency purchases:

```ini
[MoneyServer]
TotalDay = 100
TotalWeek = 250
TotalMonth = 500
```

Behavior:

- `0` disables that individual day, week, or month limit.
- Only successful `BuyMoney` transactions (`type = 5010`, `status = 0`) count.
- Gifts, object payments, land sales, normal avatar transfers, and administrative balance changes do not count.
- Limits are checked before the balance changes.
- Balance credit and transaction recording commit atomically in one MySQL transaction.
- Rejected purchases change neither the balance nor the transaction ledger.
- Calendar periods use UTC. The week starts Monday at 00:00 UTC.
- A purchase that would exceed `CurrencyMaximum` is rejected; the purchase path does not trim an existing balance.
- A repeated viewer confirmation UUID is idempotent and cannot credit the same purchase twice.

Purchases made by the old/Copilot build count only when that build created valid successful `BuyMoney` rows. In most cases, reliable limit accounting begins with the first purchase after v1.1.0 is deployed.

### Fixed command-prompt movement

MoneyServer creates a `LocalConsole`, but the upstream MoneyServer startup did not register OpenSim's log4net Console appender with it. Background diagnostics therefore wrote directly through the active prompt line.

v1.1.0 calls:

```csharp
RegisterCommonAppenders(Config.Configs["Startup"]);
```

OpenSim's normal `LocalConsole.Output()` behavior is then used:

1. Clear the active `MoneyServer #` prompt row.
2. Print the incoming diagnostics or log message.
3. Redraw `MoneyServer #` on the next line.
4. Preserve any partially typed command.

Periodic diagnostics remain enabled. No OpenSim core file is modified.

### Purchase controls retained and made effective

The existing purchase settings are still used:

```ini
CurrencyOnOff = on
CurrencyGroupOnly = false
CurrencyGroupID = "00000000-0000-0000-0000-000000000000"
UserMailLock = false
CurrencyMaximum = 10000
```

`CurrencyOnOff` must be `on` for viewer purchases. The installer deliberately does not change it, your database connection, ports, certificates, banker UUID, or any other live configuration.

## Production installation

Stop **MoneyServer only**. Robust and the region simulators may remain online.

Extract this ZIP and run from Command Prompt:

```bat
cd /d S:\Casperia-MoneyServer-ProductionFix-v1.1.0

powershell -NoProfile -ExecutionPolicy Bypass -File ".\Install-CasperiaMoneyServer-v1.1.0.ps1" ^
  -OpenSimRoot "S:\Github\opensim-enhanced" ^
  -TotalDay 100 ^
  -TotalWeek 250 ^
  -TotalMonth 500
```

Use the actual source-tree path that builds your live MoneyServer. The installer also accepts an existing clean checkout instead of cloning:

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File ".\Install-CasperiaMoneyServer-v1.1.0.ps1" ^
  -OpenSimRoot "S:\Github\opensim-enhanced" ^
  -CleanSourceRoot "S:\Github\opensimcurrencyserver-dotnet" ^
  -TotalDay 100 ^
  -TotalWeek 250 ^
  -TotalMonth 500
```

The installer:

1. Obtains a clean copy of the GitHub module set.
2. Applies only the four reviewed source replacements in `Overlay`.
3. Refuses to continue while MoneyServer is running.
4. Creates `_moneyserver_backup_YYYYMMDD-HHMMSS` inside the OpenSim source tree.
5. Preserves the current MoneyServer source, binaries, and `bin\MoneyServer.ini` in that checkpoint.
6. Replaces the three Copilot-era module directories with clean upstream modules plus the fixes.
7. Updates only `TotalDay`, `TotalWeek`, and `TotalMonth` unless `-DoNotUpdateIni` is used.
8. Runs `runprebuild.bat` and builds `MoneyServer.csproj` in Release mode.
9. Checks that the expected DLLs were rebuilt.
10. Automatically attempts rollback if installation or compilation fails.

After a successful build, check the live file:

```ini
[MoneyServer]
CurrencyOnOff = on
TotalDay = 100
TotalWeek = 250
TotalMonth = 500
```

Then start MoneyServer normally from `bin` and follow `VALIDATION.md`.

## Build status

The supplied C# source passed structural/static checks in the artifact environment. That environment does not contain the .NET SDK or your matching OpenSim source tree, so it could not perform the final compilation. The installer performs the real build against your installed OpenSim tree and attempts rollback instead of leaving a failed partial deployment.
