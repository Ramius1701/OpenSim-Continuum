-- Optional for a large/long-running transaction table.
-- Check existing indexes first; do not create a duplicate equivalent index.
SHOW INDEX FROM transactions;

CREATE INDEX idx_transactions_receiver_type_status_time
    ON transactions (receiver, type, status, time);
