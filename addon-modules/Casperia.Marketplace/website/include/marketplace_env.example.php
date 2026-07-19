<?php
declare(strict_types=1);

/**
 * Casperia Marketplace v2.0.0
 *
 * Copy to include/marketplace_env.php and replace every secret/path placeholder.
 * Keep this file server-side.
 */

define('MP_CURRENCY_LABEL', 'Grid Credits');

define('MP_OPENSIM_BASE_URL', 'http://127.0.0.1:8004');
define('MP_OPENSIM_USERNAME', 'casperia-marketplace');
define('MP_OPENSIM_PASSWORD', 'CHANGE_THIS_TO_THE_SAME_OPENSIM_BASIC_AUTH_SECRET');
define('MP_OPENSIM_REQUIRE_HTTPS', false);
define('MP_OPENSIM_HTTP_TIMEOUT_SECONDS', 20);

define('MP_MAX_CART_ITEMS', 10);
define('MP_MAX_IMAGES_PER_LISTING', 8);
define('MP_MAX_IMAGE_BYTES', 8 * 1024 * 1024);

/**
 * Store images OUTSIDE the document root.
 * Example Windows path:
 * S:\CasperiaData\Marketplace\listing-images
 */
define('MP_IMAGE_STORAGE_ROOT', 'S:\\CasperiaData\\Marketplace\\listing-images');

define('MP_PAYMENT_PROVIDER', 'manual');

/** 500 = 5.00%. Seller-specific non-zero values override this. */
define('MP_DEFAULT_COMMISSION_BASIS_POINTS', 0);

