# MoneyServer validation checklist

## Build validation

From the OpenSim Continuum repository root:

```bat
call runprebuild.bat
dotnet build addon-modules\OpenSim-Data-MySQL-MySQLMoneyDataWrapper\OpenSim.Data.MySQL.MySQLMoneyDataWrapper.csproj -c Release
dotnet build addon-modules\OpenSim-Modules-Currency\OpenSim.Modules.Currency.csproj -c Release
dotnet build addon-modules\OpenSim-Grid-MoneyServer\MoneyServer.csproj -c Release
dotnet build OpenSim.sln -c Release
```

Compilation proves API compatibility only. It does not prove transaction behavior.

## Controlled runtime checks

Use a test avatar and a backup of the money database.

1. Start MoneyServer and one test region. Confirm the MoneyServer console prompt remains readable when asynchronous log lines appear.
2. Log in the test avatar and confirm the initial balance is returned.
3. With `CurrencyOnOff = "off"`, confirm quote/purchase is rejected.
4. Enable purchasing and buy an amount within every configured limit. Confirm exactly one balance credit and one successful `BuyMoney` row.
5. Repeat the same viewer confirmation UUID. Confirm no second credit occurs.
6. Submit a request with another or invalid secure session UUID. Confirm it is rejected and the balance is unchanged.
7. Test the daily, weekly, monthly, and maximum-balance boundaries individually.
8. Set each limit to `0` and confirm that limit is disabled. In particular, confirm `CurrencyMaximum = 0` does not reduce an online user's existing balance.
9. Test `CurrencyGroupOnly` and `UserMailLock` only after confirming MoneyServer can read the corresponding database tables.
10. Test `AddBankerMoney` first from an unlisted IP, then from an explicitly listed trusted IP.
11. Enable stipends with two test avatars and a small amount. Confirm transaction type `10000`, one credit per avatar, and no duplicate credit after restart or manual deletion of `stipend_lastcycle.txt`. Repeat the same pending transaction concurrently in a test environment and confirm only one balance credit occurs.
12. Review the logs and confirm no secure-session UUID or full email address is printed during quote, purchase, or land-preparation requests.
13. Keep the production service stopped until all database and viewer checks pass.
