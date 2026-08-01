-- ContinuumEconomy schema 3. Adds globally reserved operation IDs and audited
-- privileged balance adjustments. It does not alter legacy MoneyServer tables.
CREATE TABLE IF NOT EXISTS `continuum_economy_operations` (
  `operation_id` char(36) NOT NULL,
  `request_hash` char(64) NOT NULL,
  `operation_kind` tinyint unsigned NOT NULL,
  `created_utc` timestamp(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`operation_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `continuum_economy_adjustments` (
  `operation_id` char(36) NOT NULL,
  `request_hash` char(64) NOT NULL,
  `account_id` char(36) NOT NULL,
  `actor_id` char(36) NOT NULL,
  `amount` bigint NOT NULL,
  `adjustment_kind` tinyint unsigned NOT NULL,
  `transaction_type` int NOT NULL,
  `reason` varchar(255) NOT NULL,
  `status` tinyint unsigned NOT NULL,
  `resulting_balance` bigint NOT NULL,
  `failure_reason` varchar(255) NOT NULL DEFAULT '',
  `created_utc` timestamp(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`operation_id`),
  KEY `idx_continuum_economy_adjustment_account_time` (`account_id`,`created_utc`),
  KEY `idx_continuum_economy_adjustment_actor_time` (`actor_id`,`created_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
