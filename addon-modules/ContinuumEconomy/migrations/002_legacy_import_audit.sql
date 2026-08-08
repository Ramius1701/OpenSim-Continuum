-- ContinuumEconomy schema 2: one-time legacy import audit records.
CREATE TABLE IF NOT EXISTS `continuum_economy_imports` (
  `import_id` char(36) NOT NULL,
  `source_name` varchar(64) NOT NULL,
  `account_count` bigint NOT NULL,
  `balance_total` decimal(65,0) NOT NULL,
  `transaction_count` bigint NOT NULL,
  `completed_utc` timestamp(6) NOT NULL,
  PRIMARY KEY (`import_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS `continuum_economy_legacy_transactions` (
  `legacy_transaction_id` varchar(36) NOT NULL,
  `import_id` char(36) NOT NULL,
  `sender_id` varchar(64) NOT NULL,
  `receiver_id` varchar(64) NOT NULL,
  `amount` bigint NOT NULL,
  `sender_balance` bigint NOT NULL,
  `receiver_balance` bigint NOT NULL,
  `object_id` varchar(64) NOT NULL,
  `object_name` varchar(255) NOT NULL,
  `region_handle` varchar(36) NOT NULL,
  `region_id` varchar(36) NOT NULL,
  `transaction_type` int NOT NULL,
  `created_unix` bigint NOT NULL,
  `legacy_status` int NOT NULL,
  `common_name` varchar(128) NOT NULL,
  `description` varchar(255) NOT NULL,
  PRIMARY KEY (`legacy_transaction_id`),
  KEY `idx_ce_legacy_sender` (`sender_id`),
  KEY `idx_ce_legacy_receiver` (`receiver_id`),
  KEY `idx_ce_legacy_time` (`created_unix`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
