# v1.1.0

- Rebased deployment workflow on a clean GitHub module checkout.
- Implemented `TotalDay`, `TotalWeek`, and `TotalMonth` against successful `BuyMoney` transactions.
- Added atomic balance credit plus ledger insertion.
- Added per-avatar serialization so simultaneous purchases cannot bypass limits.
- Added confirmation UUID idempotency.
- Removed the incorrect hard-coded Gift/5001 purchase accounting approach.
- Prevented quote processing from changing an avatar balance.
- Enforced `CurrencyMaximum` before purchase credit.
- Corrected `CurrencyOnOff`, group-only, and email-lock purchase checks on both purchase endpoints.
- Connected MoneyServer's log4net Console appender to `LocalConsole` so background output moves/redraws the prompt.
- Retained periodic diagnostics; removed the v1.0.0 disable-diagnostics workaround.
- Added automatic installation rollback on build failure.
