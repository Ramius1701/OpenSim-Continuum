-- Replace with the avatar UUID being tested.
SET @avatar_uuid = '00000000-0000-0000-0000-000000000000';
SET @buy_money_type = 5010;
SET @stipend_type = 10000;
SET @success_status = 0;

SELECT
    UUID,
    sender,
    receiver,
    amount,
    senderBalance,
    receiverBalance,
    type,
    status,
    FROM_UNIXTIME(time) AS transaction_time,
    description
FROM transactions
WHERE receiver = @avatar_uuid
  AND type IN (@buy_money_type, @stipend_type)
ORDER BY time DESC
LIMIT 100;

SELECT
    COALESCE(SUM(CASE WHEN time >= UNIX_TIMESTAMP(UTC_DATE()) THEN amount ELSE 0 END), 0) AS total_today_utc,
    COALESCE(SUM(CASE WHEN time >= UNIX_TIMESTAMP(DATE_SUB(UTC_DATE(), INTERVAL WEEKDAY(UTC_DATE()) DAY)) THEN amount ELSE 0 END), 0) AS total_week_utc,
    COALESCE(SUM(CASE WHEN time >= UNIX_TIMESTAMP(DATE_FORMAT(UTC_DATE(), '%Y-%m-01')) THEN amount ELSE 0 END), 0) AS total_month_utc
FROM transactions
WHERE receiver = @avatar_uuid
  AND type = @buy_money_type
  AND status = @success_status;
