-- ContinuumEconomy schema 4. Authorization holds prevent payment capture before
-- object or land delivery succeeds. Legacy MoneyServer tables are untouched.
CREATE TABLE IF NOT EXISTS `continuum_economy_purchases` (
  `purchase_id` char(36) NOT NULL, `request_hash` char(64) NOT NULL,
  `buyer_id` char(36) NOT NULL, `seller_id` char(36) NOT NULL,
  `amount` bigint NOT NULL, `transaction_type` int NOT NULL,
  `region_id` char(36) NOT NULL, `object_id` char(36) NOT NULL,
  `description` varchar(255) NOT NULL DEFAULT '', `state` tinyint unsigned NOT NULL,
  `buyer_balance` bigint NOT NULL, `seller_balance` bigint NOT NULL,
  `failure_reason` varchar(255) NOT NULL DEFAULT '',
  `created_utc` timestamp(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `completed_utc` timestamp(6) NULL,
  PRIMARY KEY (`purchase_id`),
  KEY `idx_continuum_economy_purchase_buyer_state` (`buyer_id`,`state`),
  KEY `idx_continuum_economy_purchase_seller_time` (`seller_id`,`created_utc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
