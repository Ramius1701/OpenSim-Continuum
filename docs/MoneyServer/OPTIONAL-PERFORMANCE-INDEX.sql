-- Optional for a large, long-running transactions table.
-- Inspect existing indexes first and do not create an equivalent duplicate.
SHOW INDEX FROM transactions;

CREATE INDEX idx_transactions_receiver_type_status_time
    ON transactions (receiver, type, status, time);
