-- ContinuumEconomy schema 1. Does not alter legacy MoneyServer tables.
CREATE TABLE IF NOT EXISTS `continuum_economy_accounts` (
  `account_id` char(36) NOT NULL, `balance` bigint NOT NULL DEFAULT 0,
  `account_type` tinyint unsigned NOT NULL DEFAULT 0,
  `created_utc` timestamp(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_utc` timestamp(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS `continuum_economy_transactions` (
  `transaction_id` char(36) NOT NULL, `request_hash` char(64) NOT NULL,
  `sender_id` char(36) NOT NULL, `receiver_id` char(36) NOT NULL,
  `amount` bigint NOT NULL, `transaction_type` int NOT NULL,
  `region_id` char(36) NOT NULL, `object_id` char(36) NOT NULL,
  `description` varchar(255) NOT NULL DEFAULT '', `status` tinyint unsigned NOT NULL,
  `sender_balance` bigint NOT NULL, `receiver_balance` bigint NOT NULL,
  `failure_reason` varchar(255) NOT NULL DEFAULT '',
  `created_utc` timestamp(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`transaction_id`),
  KEY `idx_continuum_economy_sender_time` (`sender_id`,`created_utc`),
  KEY `idx_continuum_economy_receiver_time` (`receiver_id`,`created_utc`),
  KEY `idx_continuum_economy_type_time` (`transaction_type`,`created_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
