# Focused production validation

Do these checks immediately after starting v1.1.0.

## 1. Prompt redraw

Leave the console sitting at:

```text
MoneyServer #
```

Wait for the normal periodic diagnostics or another background log message. Expected behavior:

```text
DIAGNOSTICS
...
Process memory: Physical ...
...
MoneyServer #
```

The prompt must be redrawn below the new information. It must not remain embedded at the beginning of a diagnostics line. Type part of a harmless command before the next background message and confirm that the typed text is restored with the prompt.

## 2. Basic viewer purchase

Before buying, record the avatar's balance. Buy a small amount through the viewer.

Expected:

- Viewer purchase reports success.
- Balance increases by exactly the requested amount.
- One new successful `BuyMoney` row is created.
- MoneyServer remains running.

## 3. Daily limit

With `TotalDay = 100`, use a test avatar whose purchase total for the current UTC day is zero:

```text
Buy 60  -> accepted; accumulated purchase total 60
Buy 40  -> accepted; accumulated purchase total 100
Buy 1   -> rejected; balance unchanged
```

## 4. Transaction-type isolation

Give the test avatar money from another avatar or object. The gift/payment must not increase the viewer-purchase total. A viewer purchase is `BuyMoney = 5010`; a gift is `Gift = 5001`.

## 5. Maximum balance

Set a safe temporary test value if necessary. A viewer purchase that would put the avatar above `CurrencyMaximum` must be rejected before any balance change. The existing balance must not be trimmed by the quote or rejected purchase.

## 6. Database check

Replace the UUID below and run against the MoneyServer database:

```sql
SET @avatar_uuid = '00000000-0000-0000-0000-000000000000';

SELECT
    UUID,
    receiver,
    amount,
    receiverBalance,
    type,
    status,
    FROM_UNIXTIME(time) AS transaction_time,
    description
FROM transactions
WHERE receiver = @avatar_uuid
  AND type = 5010
ORDER BY time DESC
LIMIT 20;
```


Before relying on atomic rollback, confirm both tables use InnoDB:

```sql
SELECT TABLE_NAME, ENGINE
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME IN ('balances', 'transactions');
```

Both rows should report `InnoDB`. The current upstream module creates these tables as InnoDB.

A successful viewer purchase should show:

```text
type        = 5010
status      = 0
description = Viewer currency purchase
```

## 7. Regression checks

After the focused purchase test succeeds, check the functions you use in production: avatar payments, object/vendor payments, land purchase, insufficient-funds rejection, upload/group charges, restart persistence, and balance notifications.
