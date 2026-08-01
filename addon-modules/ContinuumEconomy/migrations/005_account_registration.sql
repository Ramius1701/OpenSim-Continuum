-- ContinuumEconomy schema 5. Audits creation of group/system economy accounts.
CREATE TABLE IF NOT EXISTS `continuum_economy_account_registrations` (
  `operation_id` char(36) NOT NULL, `request_hash` char(64) NOT NULL,
  `account_id` char(36) NOT NULL, `actor_id` char(36) NOT NULL,
  `account_type` tinyint unsigned NOT NULL, `display_name` varchar(255) NOT NULL,
  `created_utc` timestamp(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (`operation_id`), KEY `idx_continuum_economy_registration_account` (`account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
