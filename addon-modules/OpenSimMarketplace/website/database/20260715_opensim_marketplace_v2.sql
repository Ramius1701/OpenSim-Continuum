-- OpenSim Marketplace v2.1.0
-- Direct Delivery marketplace schema.
-- Import explicitly. Runtime PHP never creates or alters tables.

SET NAMES utf8mb4;


-- v2 is not an in-place upgrade of the unpublished v1 warehouse-object prototype.
-- Refuse to run over the old schema instead of silently leaving incompatible tables.
DROP PROCEDURE IF EXISTS ws_market_v2_preflight;
DELIMITER $$
CREATE PROCEDURE ws_market_v2_preflight()
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'ws_market_listings'
          AND COLUMN_NAME = 'object_id'
    ) THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'OpenSim Marketplace v1 prototype tables detected. Back up and remove the v1 ws_market_* tables before importing Marketplace v2.';
    END IF;
END$$
DELIMITER ;

CALL ws_market_v2_preflight();
DROP PROCEDURE ws_market_v2_preflight;

CREATE TABLE IF NOT EXISTS ws_market_schema (
    component VARCHAR(64) NOT NULL,
    schema_version INT NOT NULL,
    applied_at DATETIME NOT NULL,
    PRIMARY KEY (component)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_categories (
    id INT UNSIGNED NOT NULL AUTO_INCREMENT,
    parent_id INT UNSIGNED NULL,
    name VARCHAR(120) NOT NULL,
    slug VARCHAR(140) NOT NULL,
    sort_order INT NOT NULL DEFAULT 0,
    active TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_category_slug (slug),
    KEY ix_market_category_parent (parent_id, active, sort_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_sellers (
    seller_id CHAR(36) NOT NULL,
    store_name VARCHAR(120) NOT NULL,
    store_slug VARCHAR(140) NOT NULL,
    bio TEXT NOT NULL,
    status ENUM('pending','approved','suspended','rejected') NOT NULL DEFAULT 'pending',
    commission_basis_points INT UNSIGNED NOT NULL DEFAULT 0,
    applied_at DATETIME NOT NULL,
    approved_at DATETIME NULL,
    approved_by CHAR(36) NULL,
    updated_at DATETIME NOT NULL,
    PRIMARY KEY (seller_id),
    UNIQUE KEY uq_market_store_slug (store_slug),
    KEY ix_market_seller_status (status, updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_listings (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    listing_uuid CHAR(36) NOT NULL,
    seller_id CHAR(36) NOT NULL,
    category_id INT UNSIGNED NOT NULL,
    source_folder_id CHAR(36) NOT NULL,
    active_version_id BIGINT UNSIGNED NULL,
    title VARCHAR(120) NOT NULL,
    slug VARCHAR(150) NOT NULL,
    short_description VARCHAR(500) NOT NULL,
    description MEDIUMTEXT NOT NULL,
    keywords VARCHAR(500) NOT NULL DEFAULT '',
    price BIGINT UNSIGNED NOT NULL DEFAULT 0,
    maturity ENUM('general','moderate','adult') NOT NULL DEFAULT 'general',
    quantity_limit INT UNSIGNED NULL,
    reserved_count INT UNSIGNED NOT NULL DEFAULT 0,
    sold_count INT UNSIGNED NOT NULL DEFAULT 0,
    redelivery_enabled TINYINT(1) NOT NULL DEFAULT 1,
    status ENUM('draft','pending','published','rejected','archived') NOT NULL DEFAULT 'draft',
    rejection_reason VARCHAR(1000) NOT NULL DEFAULT '',
    created_at DATETIME NOT NULL,
    updated_at DATETIME NOT NULL,
    published_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_listing_uuid (listing_uuid),
    UNIQUE KEY uq_market_listing_slug (slug),
    KEY ix_market_listing_public (status, maturity, category_id, published_at),
    KEY ix_market_listing_seller (seller_id, status, updated_at),
    KEY ix_market_listing_source (seller_id, source_folder_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_listing_versions (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    version_uuid CHAR(36) NOT NULL,
    listing_id BIGINT UNSIGNED NOT NULL,
    seller_id CHAR(36) NOT NULL,
    source_folder_id CHAR(36) NOT NULL,
    snapshot_folder_id CHAR(36) NOT NULL,
    source_fingerprint CHAR(64) NOT NULL,
    snapshot_fingerprint CHAR(64) NOT NULL,
    source_name VARCHAR(255) NOT NULL,
    source_description VARCHAR(1024) NOT NULL DEFAULT '',
    item_count INT UNSIGNED NOT NULL,
    folder_count INT UNSIGNED NOT NULL,
    permissions_json JSON NOT NULL,
    created_at DATETIME NOT NULL,
    created_by CHAR(36) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_version_uuid (version_uuid),
    UNIQUE KEY uq_market_version_snapshot (snapshot_folder_id),
    KEY ix_market_version_listing (listing_id, id),
    KEY ix_market_version_seller (seller_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_listing_images (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    listing_id BIGINT UNSIGNED NOT NULL,
    storage_name VARCHAR(100) NOT NULL,
    mime_type VARCHAR(80) NOT NULL,
    byte_size INT UNSIGNED NOT NULL,
    width_px INT UNSIGNED NOT NULL,
    height_px INT UNSIGNED NOT NULL,
    sort_order INT NOT NULL DEFAULT 0,
    alt_text VARCHAR(255) NOT NULL DEFAULT '',
    created_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_image_storage (storage_name),
    KEY ix_market_image_listing (listing_id, sort_order, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_orders (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    order_uuid CHAR(36) NOT NULL,
    buyer_id CHAR(36) NOT NULL,
    recipient_id CHAR(36) NOT NULL,
    gift_message VARCHAR(500) NOT NULL DEFAULT '',
    status ENUM('payment_pending','approved','delivering','delivered','delivery_failed','cancelled','declined') NOT NULL,
    payment_provider VARCHAR(60) NOT NULL,
    total_amount BIGINT UNSIGNED NOT NULL,
    currency_label VARCHAR(60) NOT NULL,
    created_at DATETIME NOT NULL,
    approved_at DATETIME NULL,
    approved_by CHAR(36) NULL,
    delivered_at DATETIME NULL,
    updated_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_order_uuid (order_uuid),
    KEY ix_market_order_buyer (buyer_id, created_at),
    KEY ix_market_order_recipient (recipient_id, created_at),
    KEY ix_market_order_status (status, updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_order_items (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    order_id BIGINT UNSIGNED NOT NULL,
    listing_id BIGINT UNSIGNED NOT NULL,
    listing_version_id BIGINT UNSIGNED NOT NULL,
    seller_id CHAR(36) NOT NULL,
    title VARCHAR(120) NOT NULL,
    unit_price BIGINT UNSIGNED NOT NULL,
    fee_amount BIGINT UNSIGNED NOT NULL DEFAULT 0,
    seller_net BIGINT UNSIGNED NOT NULL,
    delivery_status ENUM('pending','delivered','failed') NOT NULL DEFAULT 'pending',
    delivered_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_order_listing (order_id, listing_id),
    KEY ix_market_order_item_order (order_id, id),
    KEY ix_market_order_item_seller (seller_id, id),
    KEY ix_market_order_item_listing (listing_id, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_deliveries (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    delivery_uuid VARCHAR(128) NOT NULL,
    order_item_id BIGINT UNSIGNED NULL,
    listing_version_id BIGINT UNSIGNED NOT NULL,
    recipient_id CHAR(36) NOT NULL,
    delivery_type ENUM('original','redelivery','test') NOT NULL,
    status ENUM('pending','delivered','failed') NOT NULL DEFAULT 'pending',
    attempts INT UNSIGNED NOT NULL DEFAULT 0,
    destination_folder_id CHAR(36) NULL,
    result_message VARCHAR(1000) NOT NULL DEFAULT '',
    last_attempt_at DATETIME NULL,
    delivered_at DATETIME NULL,
    created_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_delivery_uuid (delivery_uuid),
    KEY ix_market_delivery_item (order_item_id, delivery_type, created_at),
    KEY ix_market_delivery_recipient (recipient_id, created_at),
    KEY ix_market_delivery_status (status, last_attempt_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_payments (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    order_id BIGINT UNSIGNED NOT NULL,
    provider VARCHAR(60) NOT NULL,
    provider_reference VARCHAR(190) NULL,
    amount BIGINT UNSIGNED NOT NULL,
    status ENUM('pending','approved','declined','cancelled','refunded') NOT NULL DEFAULT 'pending',
    raw_reference VARCHAR(500) NOT NULL DEFAULT '',
    created_at DATETIME NOT NULL,
    updated_at DATETIME NOT NULL,
    approved_at DATETIME NULL,
    approved_by CHAR(36) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_payment_provider_ref (provider, provider_reference),
    KEY ix_market_payment_order (order_id, id),
    KEY ix_market_payment_status (status, updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_seller_ledger (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    seller_id CHAR(36) NOT NULL,
    order_item_id BIGINT UNSIGNED NOT NULL,
    gross_amount BIGINT UNSIGNED NOT NULL,
    fee_amount BIGINT UNSIGNED NOT NULL,
    net_amount BIGINT UNSIGNED NOT NULL,
    settlement_status ENUM('unsettled','settled','void') NOT NULL DEFAULT 'unsettled',
    created_at DATETIME NOT NULL,
    settled_at DATETIME NULL,
    settlement_reference VARCHAR(190) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_ledger_order_item (order_item_id),
    KEY ix_market_ledger_seller (seller_id, settlement_status, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_reviews (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    listing_id BIGINT UNSIGNED NOT NULL,
    buyer_id CHAR(36) NOT NULL,
    order_item_id BIGINT UNSIGNED NOT NULL,
    rating TINYINT UNSIGNED NOT NULL,
    title VARCHAR(120) NOT NULL,
    body TEXT NOT NULL,
    status ENUM('published','hidden') NOT NULL DEFAULT 'published',
    seller_response TEXT NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NOT NULL,
    responded_at DATETIME NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_market_review_buyer_listing (buyer_id, listing_id),
    KEY ix_market_review_listing (listing_id, status, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS ws_market_audit (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    actor_id CHAR(36) NULL,
    action_name VARCHAR(120) NOT NULL,
    entity_type VARCHAR(80) NOT NULL,
    entity_id VARCHAR(190) NOT NULL,
    details_json JSON NOT NULL,
    created_at DATETIME NOT NULL,
    PRIMARY KEY (id),
    KEY ix_market_audit_entity (entity_type, entity_id, created_at),
    KEY ix_market_audit_actor (actor_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO ws_market_categories (parent_id, name, slug, sort_order, active)
VALUES
(NULL, 'Apparel', 'apparel', 10, 1),
(NULL, 'Avatar Accessories', 'avatar-accessories', 20, 1),
(NULL, 'Buildings and Structures', 'buildings-structures', 30, 1),
(NULL, 'Furniture and Decor', 'furniture-decor', 40, 1),
(NULL, 'Gadgets and Technology', 'gadgets-technology', 50, 1),
(NULL, 'Scripts and Tools', 'scripts-tools', 60, 1),
(NULL, 'Textures and Building Components', 'textures-building', 70, 1),
(NULL, 'Vehicles', 'vehicles', 80, 1),
(NULL, 'Other', 'other', 900, 1)
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    sort_order = VALUES(sort_order),
    active = VALUES(active);

INSERT INTO ws_market_schema (component, schema_version, applied_at)
VALUES ('opensim_marketplace', 2, NOW())
ON DUPLICATE KEY UPDATE
    schema_version = VALUES(schema_version),
    applied_at = VALUES(applied_at);
