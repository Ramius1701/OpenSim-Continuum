<?php
declare(strict_types=1);

$marketplaceRoot = __DIR__;
$siteRoot = dirname(__DIR__);

if (session_status() !== PHP_SESSION_ACTIVE) {
    session_start();
}
$marketEnv = $siteRoot . '/include/marketplace_env.php';

if (!is_file($marketEnv)) {
    http_response_code(503);
    exit(
        'OpenSim Marketplace is not configured. ' .
        'Copy include/marketplace_env.example.php to include/marketplace_env.php and edit it.'
    );
}

require_once $marketEnv;

$hostAdapter = defined('MP_HOST_ADAPTER_FILE')
    ? (string)MP_HOST_ADAPTER_FILE
    : $siteRoot . '/include/marketplace_host.php';

if (!is_file($hostAdapter)) {
    http_response_code(503);
    exit(
        'OpenSim Marketplace host integration is not configured. ' .
        'Copy include/marketplace_host.example.php to include/marketplace_host.php and adapt it to the host website.'
    );
}

require_once $hostAdapter;

$requiredHostFunctions = [
    'mp_host_db',
    'mp_host_current_user_id',
    'mp_host_current_user',
    'mp_host_login_url',
    'mp_host_is_admin',
];

foreach ($requiredHostFunctions as $requiredHostFunction) {
    if (!function_exists($requiredHostFunction)) {
        throw new RuntimeException(
            'Marketplace host adapter must define ' . $requiredHostFunction . '().'
        );
    }
}

function mp_db(): mysqli
{
    $conn = mp_host_db();

    if (!$conn instanceof mysqli) {
        throw new RuntimeException(
            'Marketplace host adapter did not return a mysqli connection.'
        );
    }

    mysqli_report(
        MYSQLI_REPORT_ERROR |
        MYSQLI_REPORT_STRICT
    );

    $conn->set_charset('utf8mb4');

    return $conn;
}

function mp_h(mixed $value): string
{
    return htmlspecialchars(
        (string)$value,
        ENT_QUOTES | ENT_SUBSTITUTE,
        'UTF-8'
    );
}

function mp_uuid(string $value): bool
{
    return preg_match(
        '/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i',
        $value
    ) === 1 &&
        strtolower($value) !==
            '00000000-0000-0000-0000-000000000000';
}

function mp_uuid_v4(): string
{
    $bytes = random_bytes(16);
    $bytes[6] = chr((ord($bytes[6]) & 0x0f) | 0x40);
    $bytes[8] = chr((ord($bytes[8]) & 0x3f) | 0x80);
    $hex = bin2hex($bytes);

    return sprintf(
        '%s-%s-%s-%s-%s',
        substr($hex, 0, 8),
        substr($hex, 8, 4),
        substr($hex, 12, 4),
        substr($hex, 16, 4),
        substr($hex, 20, 12)
    );
}

function mp_slug(string $value): string
{
    $slug = strtolower(trim($value));
    $slug = preg_replace('/[^a-z0-9]+/', '-', $slug) ?? '';
    $slug = trim($slug, '-');

    return $slug !== '' ? $slug : 'item';
}

function mp_csrf_token(): string
{
    if (empty($_SESSION['mp_csrf'])) {
        $_SESSION['mp_csrf'] = bin2hex(
            random_bytes(32)
        );
    }

    return (string)$_SESSION['mp_csrf'];
}

function mp_require_csrf(): void
{
    $provided = (string)($_POST['csrf'] ?? '');
    $stored = (string)($_SESSION['mp_csrf'] ?? '');

    if ($provided === '' ||
        $stored === '' ||
        !hash_equals($stored, $provided)) {
        http_response_code(403);

        throw new RuntimeException(
            'Invalid or expired marketplace form token.'
        );
    }
}

function mp_flash(string $type, string $message): void
{
    $_SESSION['mp_flash'] = [
        'type' => $type,
        'message' => $message,
    ];
}

function mp_take_flash(): ?array
{
    $value = $_SESSION['mp_flash'] ?? null;
    unset($_SESSION['mp_flash']);

    return is_array($value) ? $value : null;
}

function mp_redirect(string $path): never
{
    header('Location: ' . $path);
    exit;
}

function mp_session_user_candidate(): string
{
    $candidate = strtolower(
        trim((string)mp_host_current_user_id())
    );

    return mp_uuid($candidate) ? $candidate : '';
}

function mp_stmt_rows(
    mysqli $db,
    string $sql,
    string $types = '',
    array $params = []
): array {
    $stmt = $db->prepare($sql);

    if ($types !== '') {
        $stmt->bind_param($types, ...$params);
    }

    $stmt->execute();
    $result = $stmt->get_result();
    $rows = $result
        ? $result->fetch_all(MYSQLI_ASSOC)
        : [];

    $stmt->close();

    return $rows;
}

function mp_stmt_row(
    mysqli $db,
    string $sql,
    string $types = '',
    array $params = []
): ?array {
    $rows = mp_stmt_rows(
        $db,
        $sql,
        $types,
        $params
    );

    return $rows[0] ?? null;
}

function mp_stmt_exec(
    mysqli $db,
    string $sql,
    string $types = '',
    array $params = []
): int {
    $stmt = $db->prepare($sql);

    if ($types !== '') {
        $stmt->bind_param($types, ...$params);
    }

    $stmt->execute();
    $affected = $stmt->affected_rows;
    $stmt->close();

    return $affected;
}

function mp_current_user(mysqli $db): ?array
{
    $user = mp_host_current_user($db);

    if ($user === null) {
        return null;
    }

    foreach (['PrincipalID', 'FirstName', 'LastName', 'UserLevel'] as $requiredKey) {
        if (!array_key_exists($requiredKey, $user)) {
            throw new RuntimeException(
                'Marketplace host adapter user record is missing ' . $requiredKey . '.'
            );
        }
    }

    if (!mp_uuid((string)$user['PrincipalID'])) {
        throw new RuntimeException(
            'Marketplace host adapter returned an invalid PrincipalID.'
        );
    }

    return $user;
}

function mp_require_user(mysqli $db): array
{
    $user = mp_current_user($db);

    if (!$user) {
        $returnUrl = (string)(
            $_SERVER['REQUEST_URI'] ??
            '/marketplace/'
        );
        mp_redirect(mp_host_login_url($returnUrl));
    }

    return $user;
}

function mp_is_admin(array $user): bool
{
    return mp_host_is_admin($user);
}

function mp_require_admin(mysqli $db): array
{
    $user = mp_require_user($db);

    if (!mp_is_admin($user)) {
        http_response_code(403);
        exit('Marketplace administrator access required.');
    }

    return $user;
}

function mp_user_name(array $user): string
{
    return trim(
        (string)($user['FirstName'] ?? '') .
        ' ' .
        (string)($user['LastName'] ?? '')
    );
}

function mp_money(int $amount): string
{
    $label = defined('MP_CURRENCY_LABEL')
        ? (string)MP_CURRENCY_LABEL
        : 'Grid Credits';

    return number_format($amount) . ' ' . $label;
}

function mp_cart_ids(): array
{
    $raw = $_SESSION['mp_cart'] ?? [];

    if (!is_array($raw)) {
        return [];
    }

    $ids = [];

    foreach ($raw as $value) {
        $id = (int)$value;

        if ($id > 0 && !in_array($id, $ids, true)) {
            $ids[] = $id;
        }
    }

    $maximum = defined('MP_MAX_CART_ITEMS')
        ? max(1, min(50, (int)MP_MAX_CART_ITEMS))
        : 10;

    return array_slice($ids, 0, $maximum);
}

function mp_cart_save(array $ids): void
{
    $_SESSION['mp_cart'] = array_values(
        array_unique(
            array_map('intval', $ids)
        )
    );
}

final class MarketplaceOpenSimClient
{
    private string $base;

    public function __construct()
    {
        $this->base = rtrim(
            (string)MP_OPENSIM_BASE_URL,
            '/'
        );

        $parts = parse_url($this->base);

        if (!$parts ||
            !in_array(
                strtolower(
                    (string)($parts['scheme'] ?? '')
                ),
                ['http', 'https'],
                true
            )) {
            throw new RuntimeException(
                'MP_OPENSIM_BASE_URL must be HTTP or HTTPS.'
            );
        }

        if (defined('MP_OPENSIM_REQUIRE_HTTPS') &&
            MP_OPENSIM_REQUIRE_HTTPS &&
            strtolower(
                (string)$parts['scheme']
            ) !== 'https') {
            throw new RuntimeException(
                'Marketplace OpenSim transport requires HTTPS.'
            );
        }
    }

    public function inventory(
        string $sellerId,
        string $action = 'list'
    ): array {
        return $this->post(
            (string)MP_OPENSIM_INVENTORY_PATH,
            [
                'action' => $action,
                'seller_id' => $sellerId,
            ]
        );
    }

    public function inspect(
        string $sellerId,
        string $sourceFolderId
    ): array {
        return $this->post(
            (string)MP_OPENSIM_INSPECT_PATH,
            [
                'seller_id' => $sellerId,
                'source_folder_id' => $sourceFolderId,
            ]
        );
    }

    public function snapshot(
        string $versionKey,
        string $sellerId,
        string $sourceFolderId
    ): array {
        return $this->post(
            (string)MP_OPENSIM_SNAPSHOT_PATH,
            [
                'version_key' => $versionKey,
                'seller_id' => $sellerId,
                'source_folder_id' => $sourceFolderId,
            ]
        );
    }

    public function deliver(
        string $deliveryId,
        string $sellerId,
        string $snapshotFolderId,
        string $snapshotFingerprint,
        string $recipientId
    ): array {
        return $this->post(
            (string)MP_OPENSIM_DELIVERY_PATH,
            [
                'delivery_id' => $deliveryId,
                'seller_id' => $sellerId,
                'snapshot_folder_id' => $snapshotFolderId,
                'snapshot_fingerprint' => $snapshotFingerprint,
                'recipient_id' => $recipientId,
            ]
        );
    }

    private function post(
        string $path,
        array $payload
    ): array {
        if (!function_exists('curl_init')) {
            throw new RuntimeException(
                'PHP cURL is required for Marketplace OpenSim calls.'
            );
        }

        $url = $this->base . $path;
        $body = json_encode(
            $payload,
            JSON_UNESCAPED_SLASHES |
            JSON_THROW_ON_ERROR
        );

        $ch = curl_init($url);

        if ($ch === false) {
            throw new RuntimeException(
                'Unable to initialize Marketplace HTTP client.'
            );
        }

        $timeout = defined(
            'MP_OPENSIM_HTTP_TIMEOUT_SECONDS'
        )
            ? max(
                5,
                min(
                    120,
                    (int)MP_OPENSIM_HTTP_TIMEOUT_SECONDS
                )
            )
            : 20;

        curl_setopt_array(
            $ch,
            [
                CURLOPT_POST => true,
                CURLOPT_POSTFIELDS => $body,
                CURLOPT_RETURNTRANSFER => true,
                CURLOPT_FOLLOWLOCATION => false,
                CURLOPT_CONNECTTIMEOUT => 5,
                CURLOPT_TIMEOUT => $timeout,
                CURLOPT_HTTPAUTH => CURLAUTH_BASIC,
                CURLOPT_USERPWD =>
                    (string)MP_OPENSIM_USERNAME .
                    ':' .
                    (string)MP_OPENSIM_PASSWORD,
                CURLOPT_HTTPHEADER => [
                    'Content-Type: application/json',
                    'Accept: application/json',
                ],
                CURLOPT_USERAGENT =>
                    'OpenSimMarketplacePortal/2.1.0',
                CURLOPT_SSL_VERIFYPEER => true,
                CURLOPT_SSL_VERIFYHOST => 2,
            ]
        );

        $raw = curl_exec($ch);
        $status = (int)curl_getinfo(
            $ch,
            CURLINFO_RESPONSE_CODE
        );
        $error = curl_error($ch);

        curl_close($ch);

        if (!is_string($raw)) {
            throw new RuntimeException(
                'OpenSim Marketplace endpoint failed: ' .
                ($error !== '' ? $error : 'unknown cURL error')
            );
        }

        try {
            $json = json_decode(
                $raw,
                true,
                64,
                JSON_THROW_ON_ERROR
            );
        } catch (JsonException $e) {
            throw new RuntimeException(
                'OpenSim Marketplace endpoint returned invalid JSON ' .
                '(HTTP ' . $status . ').',
                0,
                $e
            );
        }

        if (!is_array($json)) {
            throw new RuntimeException(
                'OpenSim Marketplace response was not an object.'
            );
        }

        $json['_http_status'] = $status;

        return $json;
    }
}

interface MarketplacePaymentProvider
{
    public function name(): string;

    public function initialOrderStatus(int $total): string;

    public function initialPaymentStatus(int $total): string;
}

final class ManualMarketplacePaymentProvider
    implements MarketplacePaymentProvider
{
    public function name(): string
    {
        return 'manual';
    }

    public function initialOrderStatus(int $total): string
    {
        return $total === 0
            ? 'approved'
            : 'payment_pending';
    }

    public function initialPaymentStatus(int $total): string
    {
        return $total === 0
            ? 'approved'
            : 'pending';
    }
}

function mp_payment_provider(): MarketplacePaymentProvider
{
    $configured = strtolower(
        trim(
            (string)(
                defined('MP_PAYMENT_PROVIDER')
                    ? MP_PAYMENT_PROVIDER
                    : 'manual'
            )
        )
    );

    return match ($configured) {
        '', 'manual' =>
            new ManualMarketplacePaymentProvider(),
        default =>
            throw new RuntimeException(
                'Unsupported Marketplace payment provider: ' .
                $configured
            ),
    };
}

final class MarketplaceService
{
    public function __construct(
        private mysqli $db,
        private MarketplaceOpenSimClient $os
    ) {
    }

    public function categories(): array
    {
        return mp_stmt_rows(
            $this->db,
            'SELECT id, parent_id, name, slug
             FROM ws_market_categories
             WHERE active = 1
             ORDER BY sort_order, name'
        );
    }

    public function seller(string $sellerId): ?array
    {
        return mp_stmt_row(
            $this->db,
            'SELECT
                 s.*,
                 CONCAT(u.FirstName, " ", u.LastName) AS avatar_name
             FROM ws_market_sellers s
             LEFT JOIN UserAccounts u
                 ON u.PrincipalID = s.seller_id
             WHERE s.seller_id = ?
             LIMIT 1',
            's',
            [$sellerId]
        );
    }

    public function applySeller(
        array $user,
        string $storeName,
        string $bio
    ): void {
        $sellerId = strtolower(
            (string)$user['PrincipalID']
        );

        $storeName = mb_substr(
            trim($storeName),
            0,
            120
        );

        if ($storeName === '') {
            $storeName =
                mp_user_name($user) . "'s Store";
        }

        $bio = mb_substr(
            trim($bio),
            0,
            10000
        );

        $existing = $this->seller($sellerId);
        $baseSlug = mp_slug($storeName);
        $slug = $existing
            ? (string)$existing['store_slug']
            : $this->uniqueStoreSlug($baseSlug);

        $stmt = $this->db->prepare(
            'INSERT INTO ws_market_sellers
             (
                 seller_id,
                 store_name,
                 store_slug,
                 bio,
                 status,
                 applied_at,
                 updated_at
             )
             VALUES (?, ?, ?, ?, "pending", NOW(), NOW())
             ON DUPLICATE KEY UPDATE
                 store_name = VALUES(store_name),
                 bio = VALUES(bio),
                 updated_at = NOW()'
        );

        $stmt->bind_param(
            'ssss',
            $sellerId,
            $storeName,
            $slug,
            $bio
        );

        $stmt->execute();
        $stmt->close();

        $this->audit(
            $sellerId,
            'seller.apply',
            'seller',
            $sellerId,
            [
                'store_name' => $storeName,
            ]
        );
    }

    public function initializeMerchantOutbox(
        string $sellerId
    ): array {
        $seller = $this->seller($sellerId);

        if (!$seller ||
            $seller['status'] !== 'approved') {
            throw new RuntimeException(
                'Approved seller status is required.'
            );
        }

        $result = $this->os->inventory(
            $sellerId,
            'ensure'
        );

        if (empty($result['ok'])) {
            throw new RuntimeException(
                'Marketplace inventory initialization failed: ' .
                (string)(
                    $result['message'] ??
                    'Unknown OpenSim inventory error'
                )
            );
        }

        return $result;
    }

    public function merchantInventory(
        string $sellerId
    ): array {
        $result = $this->os->inventory(
            $sellerId,
            'list'
        );

        if (empty($result['ok'])) {
            throw new RuntimeException(
                'Merchant Outbox synchronization failed: ' .
                (string)(
                    $result['message'] ??
                    'Unknown OpenSim inventory error'
                )
            );
        }

        return $result;
    }

    public function sellerListings(
        string $sellerId
    ): array {
        return mp_stmt_rows(
            $this->db,
            'SELECT
                 l.*,
                 c.name AS category_name,
                 v.version_uuid,
                 v.source_fingerprint,
                 v.snapshot_folder_id
             FROM ws_market_listings l
             JOIN ws_market_categories c
                 ON c.id = l.category_id
             LEFT JOIN ws_market_listing_versions v
                 ON v.id = l.active_version_id
             WHERE l.seller_id = ?
             ORDER BY l.updated_at DESC, l.id DESC',
            's',
            [$sellerId]
        );
    }

    public function listingForSeller(
        string $sellerId,
        int $listingId
    ): ?array {
        return mp_stmt_row(
            $this->db,
            'SELECT *
             FROM ws_market_listings
             WHERE id = ?
               AND seller_id = ?
             LIMIT 1',
            'is',
            [$listingId, $sellerId]
        );
    }

    public function saveListing(
        array $user,
        ?int $listingId,
        array $data
    ): int {
        $sellerId = strtolower(
            (string)$user['PrincipalID']
        );

        $seller = $this->seller($sellerId);

        if (!$seller ||
            $seller['status'] !== 'approved') {
            throw new RuntimeException(
                'Seller approval is required before creating listings.'
            );
        }

        $sourceFolderId = strtolower(
            trim(
                (string)(
                    $data['source_folder_id'] ?? ''
                )
            )
        );

        if (!mp_uuid($sourceFolderId)) {
            throw new RuntimeException(
                'Select a valid Merchant Outbox product folder.'
            );
        }

        $inspection = $this->os->inspect(
            $sellerId,
            $sourceFolderId
        );

        if (empty($inspection['ok'])) {
            throw new RuntimeException(
                'Product folder validation failed: ' .
                (string)(
                    $inspection['message'] ??
                    'Unknown inventory error'
                )
            );
        }

        $categoryId = (int)(
            $data['category_id'] ?? 0
        );

        if (!mp_stmt_row(
            $this->db,
            'SELECT id
             FROM ws_market_categories
             WHERE id = ?
               AND active = 1',
            'i',
            [$categoryId]
        )) {
            throw new RuntimeException(
                'Select a valid marketplace category.'
            );
        }

        $title = mb_substr(
            trim(
                (string)($data['title'] ?? '')
            ),
            0,
            120
        );

        if ($title === '') {
            $title = mb_substr(
                (string)(
                    $inspection['name'] ??
                    'Marketplace Item'
                ),
                0,
                120
            );
        }

        $short = mb_substr(
            trim(
                (string)(
                    $data['short_description'] ?? ''
                )
            ),
            0,
            500
        );

        $description = mb_substr(
            trim(
                (string)(
                    $data['description'] ?? ''
                )
            ),
            0,
            50000
        );

        if ($description === '') {
            $description = mb_substr(
                (string)(
                    $inspection['description'] ?? ''
                ),
                0,
                50000
            );
        }

        $keywords = mb_substr(
            trim(
                (string)(
                    $data['keywords'] ?? ''
                )
            ),
            0,
            500
        );

        $price = max(
            0,
            (int)($data['price'] ?? 0)
        );

        $maturity = strtolower(
            trim(
                (string)(
                    $data['maturity'] ?? 'general'
                )
            )
        );

        if (!in_array(
            $maturity,
            ['general', 'moderate', 'adult'],
            true
        )) {
            throw new RuntimeException(
                'Select a valid maturity rating.'
            );
        }

        $quantityText = trim(
            (string)(
                $data['quantity_limit'] ?? ''
            )
        );

        $quantityLimit = $quantityText === ''
            ? null
            : max(1, (int)$quantityText);

        $redelivery = !empty(
            $data['redelivery_enabled']
        )
            ? 1
            : 0;

        $this->db->begin_transaction();

        try {
            if ($listingId !== null) {
                $listing = $this->listingForSeller(
                    $sellerId,
                    $listingId
                );

                if (!$listing) {
                    throw new RuntimeException(
                        'Marketplace listing was not found.'
                    );
                }

                if ($listing['status'] === 'archived') {
                    throw new RuntimeException(
                        'Archived listings cannot be edited.'
                    );
                }

                $stmt = $this->db->prepare(
                    'UPDATE ws_market_listings
                     SET
                         category_id = ?,
                         source_folder_id = ?,
                         title = ?,
                         short_description = ?,
                         description = ?,
                         keywords = ?,
                         price = ?,
                         maturity = ?,
                         quantity_limit = ?,
                         redelivery_enabled = ?,
                         status = "draft",
                         rejection_reason = "",
                         updated_at = NOW()
                     WHERE id = ?
                       AND seller_id = ?'
                );

                $stmt->bind_param(
                    'isssssisiiis',
                    $categoryId,
                    $sourceFolderId,
                    $title,
                    $short,
                    $description,
                    $keywords,
                    $price,
                    $maturity,
                    $quantityLimit,
                    $redelivery,
                    $listingId,
                    $sellerId
                );

                $stmt->execute();
                $stmt->close();

                $id = $listingId;
            } else {
                $listingUuid = mp_uuid_v4();
                $slug = $this->uniqueListingSlug(
                    mp_slug($title)
                );

                $stmt = $this->db->prepare(
                    'INSERT INTO ws_market_listings
                     (
                         listing_uuid,
                         seller_id,
                         category_id,
                         source_folder_id,
                         title,
                         slug,
                         short_description,
                         description,
                         keywords,
                         price,
                         maturity,
                         quantity_limit,
                         redelivery_enabled,
                         status,
                         created_at,
                         updated_at
                     )
                     VALUES
                     (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, "draft", NOW(), NOW())'
                );

                $stmt->bind_param(
                    'ssissssssisii',
                    $listingUuid,
                    $sellerId,
                    $categoryId,
                    $sourceFolderId,
                    $title,
                    $slug,
                    $short,
                    $description,
                    $keywords,
                    $price,
                    $maturity,
                    $quantityLimit,
                    $redelivery
                );

                $stmt->execute();
                $id = (int)$stmt->insert_id;
                $stmt->close();
            }

            $this->db->commit();
        } catch (Throwable $e) {
            $this->db->rollback();
            throw $e;
        }

        $this->audit(
            $sellerId,
            'listing.save',
            'listing',
            (string)$id,
            [
                'source_folder_id' => $sourceFolderId,
                'fingerprint' =>
                    (string)(
                        $inspection['fingerprint'] ?? ''
                    ),
                'price' => $price,
                'maturity' => $maturity,
                'quantity_limit' => $quantityLimit,
            ]
        );

        return $id;
    }

    public function submitListing(
        string $sellerId,
        int $listingId
    ): void {
        $listing = $this->listingForSeller(
            $sellerId,
            $listingId
        );

        if (!$listing ||
            !in_array(
                $listing['status'],
                ['draft', 'rejected'],
                true
            )) {
            throw new RuntimeException(
                'Only draft or rejected listings can be submitted.'
            );
        }

        if ($this->imageCount($listingId) < 1) {
            throw new RuntimeException(
                'Add at least one product image before submitting the listing.'
            );
        }

        $inspection = $this->os->inspect(
            $sellerId,
            (string)$listing['source_folder_id']
        );

        if (empty($inspection['ok'])) {
            throw new RuntimeException(
                'Product folder validation failed: ' .
                (string)(
                    $inspection['message'] ??
                    'Unknown inventory error'
                )
            );
        }

        mp_stmt_exec(
            $this->db,
            'UPDATE ws_market_listings
             SET
                 status = "pending",
                 rejection_reason = "",
                 updated_at = NOW()
             WHERE id = ?
               AND seller_id = ?',
            'is',
            [$listingId, $sellerId]
        );

        $this->audit(
            $sellerId,
            'listing.submit',
            'listing',
            (string)$listingId,
            [
                'source_fingerprint' =>
                    (string)(
                        $inspection['fingerprint'] ?? ''
                    ),
            ]
        );
    }

    public function archiveListing(
        string $sellerId,
        int $listingId
    ): void {
        if (mp_stmt_exec(
            $this->db,
            'UPDATE ws_market_listings
             SET
                 status = "archived",
                 updated_at = NOW()
             WHERE id = ?
               AND seller_id = ?',
            'is',
            [$listingId, $sellerId]
        ) < 1) {
            throw new RuntimeException(
                'Marketplace listing was not found.'
            );
        }

        $this->audit(
            $sellerId,
            'listing.archive',
            'listing',
            (string)$listingId,
            []
        );
    }

    public function publishListing(
        string $adminId,
        int $listingId
    ): array {
        $this->db->begin_transaction();

        try {
            $listing = mp_stmt_row(
                $this->db,
                'SELECT l.*, s.status AS seller_status
                 FROM ws_market_listings l
                 JOIN ws_market_sellers s
                     ON s.seller_id = l.seller_id
                 WHERE l.id = ?
                 FOR UPDATE',
                'i',
                [$listingId]
            );

            if (!$listing ||
                $listing['status'] !== 'pending') {
                throw new RuntimeException(
                    'Only pending listings can be published.'
                );
            }

            if ($listing['seller_status'] !== 'approved') {
                throw new RuntimeException(
                    'The seller is not approved.'
                );
            }

            $versionUuid = mp_uuid_v4();

            $snapshot = $this->os->snapshot(
                $versionUuid,
                (string)$listing['seller_id'],
                (string)$listing['source_folder_id']
            );

            if (empty($snapshot['ok'])) {
                throw new RuntimeException(
                    'Marketplace snapshot failed: ' .
                    (string)(
                        $snapshot['message'] ??
                        'Unknown OpenSim inventory error'
                    )
                );
            }

            $permissions = json_encode(
                [
                    'copy' =>
                        (bool)(
                            $snapshot['copy'] ?? false
                        ),
                    'transfer' =>
                        (bool)(
                            $snapshot['transfer'] ?? false
                        ),
                    'modify' =>
                        (bool)(
                            $snapshot['modify'] ?? false
                        ),
                ],
                JSON_UNESCAPED_SLASHES |
                JSON_THROW_ON_ERROR
            );

            $stmt = $this->db->prepare(
                'INSERT INTO ws_market_listing_versions
                 (
                     version_uuid,
                     listing_id,
                     seller_id,
                     source_folder_id,
                     snapshot_folder_id,
                     source_fingerprint,
                     snapshot_fingerprint,
                     source_name,
                     source_description,
                     item_count,
                     folder_count,
                     permissions_json,
                     created_at,
                     created_by
                 )
                 VALUES
                 (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NOW(), ?)'
            );

            $sellerId = (string)$listing['seller_id'];
            $sourceFolderId =
                (string)$listing['source_folder_id'];
            $snapshotFolderId =
                (string)$snapshot['snapshot_folder_id'];
            $fingerprint =
                (string)$snapshot['source_fingerprint'];
            $snapshotFingerprint =
                (string)$snapshot['snapshot_fingerprint'];
            $sourceName =
                (string)$snapshot['name'];
            $sourceDescription =
                (string)$snapshot['description'];
            $itemCount =
                (int)$snapshot['item_count'];
            $folderCount =
                (int)$snapshot['folder_count'];

            $stmt->bind_param(
                'sisssssssiiss',
                $versionUuid,
                $listingId,
                $sellerId,
                $sourceFolderId,
                $snapshotFolderId,
                $fingerprint,
                $snapshotFingerprint,
                $sourceName,
                $sourceDescription,
                $itemCount,
                $folderCount,
                $permissions,
                $adminId
            );

            $stmt->execute();
            $versionId = (int)$stmt->insert_id;
            $stmt->close();

            mp_stmt_exec(
                $this->db,
                'UPDATE ws_market_listings
                 SET
                     active_version_id = ?,
                     status = "published",
                     rejection_reason = "",
                     published_at = NOW(),
                     updated_at = NOW()
                 WHERE id = ?',
                'ii',
                [$versionId, $listingId]
            );

            $this->db->commit();
        } catch (Throwable $e) {
            $this->db->rollback();
            throw $e;
        }

        $this->audit(
            $adminId,
            'listing.publish',
            'listing',
            (string)$listingId,
            [
                'version_uuid' => $versionUuid,
                'snapshot_folder_id' => $snapshotFolderId,
                'source_fingerprint' => $fingerprint,
                'snapshot_fingerprint' => $snapshotFingerprint,
            ]
        );

        return [
            'version_uuid' => $versionUuid,
            'version_id' => $versionId,
            'snapshot_folder_id' => $snapshotFolderId,
        ];
    }

    public function rejectListing(
        string $adminId,
        int $listingId,
        string $reason
    ): void {
        $reason = mb_substr(
            trim($reason),
            0,
            1000
        );

        if ($reason === '') {
            throw new RuntimeException(
                'A rejection reason is required.'
            );
        }

        if (mp_stmt_exec(
            $this->db,
            'UPDATE ws_market_listings
             SET
                 status = "rejected",
                 rejection_reason = ?,
                 updated_at = NOW()
             WHERE id = ?
               AND status = "pending"',
            'si',
            [$reason, $listingId]
        ) < 1) {
            throw new RuntimeException(
                'Pending listing was not found.'
            );
        }

        $this->audit(
            $adminId,
            'listing.reject',
            'listing',
            (string)$listingId,
            [
                'reason' => $reason,
            ]
        );
    }

    public function publicListings(
        string $query,
        int $categoryId,
        string $maturity
    ): array {
        $query = mb_substr(
            trim($query),
            0,
            120
        );

        $maturity = in_array(
            $maturity,
            ['general', 'moderate', 'adult', 'all'],
            true
        )
            ? $maturity
            : 'general';

        $where = [
            'l.status = "published"',
            's.status = "approved"',
            'l.active_version_id IS NOT NULL',
        ];

        $types = '';
        $params = [];

        if ($categoryId > 0) {
            $where[] = 'l.category_id = ?';
            $types .= 'i';
            $params[] = $categoryId;
        }

        if ($maturity !== 'all') {
            $rank = [
                'general' => 1,
                'moderate' => 2,
                'adult' => 3,
            ][$maturity];

            $allowed = match ($rank) {
                1 => ['general'],
                2 => ['general', 'moderate'],
                default =>
                    ['general', 'moderate', 'adult'],
            };

            $quoted = implode(
                ',',
                array_fill(
                    0,
                    count($allowed),
                    '?'
                )
            );

            $where[] = 'l.maturity IN (' . $quoted . ')';

            foreach ($allowed as $value) {
                $types .= 's';
                $params[] = $value;
            }
        }

        if ($query !== '') {
            $where[] =
                '(l.title LIKE ? OR
                  l.short_description LIKE ? OR
                  l.description LIKE ? OR
                  l.keywords LIKE ? OR
                  s.store_name LIKE ?)';

            $needle = '%' . $query . '%';

            for ($i = 0; $i < 5; $i++) {
                $types .= 's';
                $params[] = $needle;
            }
        }

        return mp_stmt_rows(
            $this->db,
            'SELECT
                 l.*,
                 c.name AS category_name,
                 s.store_name,
                 s.store_slug,
                 (
                     SELECT i.id
                     FROM ws_market_listing_images i
                     WHERE i.listing_id = l.id
                     ORDER BY i.sort_order, i.id
                     LIMIT 1
                 ) AS primary_image_id,
                 COALESCE(
                     (
                         SELECT AVG(r.rating)
                         FROM ws_market_reviews r
                         WHERE r.listing_id = l.id
                           AND r.status = "published"
                     ),
                     0
                 ) AS average_rating,
                 (
                     SELECT COUNT(*)
                     FROM ws_market_reviews r
                     WHERE r.listing_id = l.id
                       AND r.status = "published"
                 ) AS review_count
             FROM ws_market_listings l
             JOIN ws_market_categories c
                 ON c.id = l.category_id
             JOIN ws_market_sellers s
                 ON s.seller_id = l.seller_id
             WHERE ' .
                implode(' AND ', $where) .
             ' ORDER BY
                 l.published_at DESC,
                 l.sold_count DESC,
                 l.id DESC
             LIMIT 200',
            $types,
            $params
        );
    }

    public function publicListingBySlug(
        string $slug
    ): ?array {
        return mp_stmt_row(
            $this->db,
            'SELECT
                 l.*,
                 c.name AS category_name,
                 s.store_name,
                 s.store_slug,
                 v.version_uuid,
                 v.item_count,
                 v.folder_count,
                 COALESCE(AVG(r.rating), 0) AS average_rating,
                 COUNT(r.id) AS review_count
             FROM ws_market_listings l
             JOIN ws_market_categories c
                 ON c.id = l.category_id
             JOIN ws_market_sellers s
                 ON s.seller_id = l.seller_id
             JOIN ws_market_listing_versions v
                 ON v.id = l.active_version_id
             LEFT JOIN ws_market_reviews r
                 ON r.listing_id = l.id
                AND r.status = "published"
             WHERE l.slug = ?
               AND l.status = "published"
               AND s.status = "approved"
             GROUP BY l.id, c.id, s.seller_id, v.id
             LIMIT 1',
            's',
            [$slug]
        );
    }

    public function listingImages(int $listingId): array
    {
        return mp_stmt_rows(
            $this->db,
            'SELECT *
             FROM ws_market_listing_images
             WHERE listing_id = ?
             ORDER BY sort_order, id',
            'i',
            [$listingId]
        );
    }

    public function reviews(int $listingId): array
    {
        return mp_stmt_rows(
            $this->db,
            'SELECT
                 r.*,
                 CONCAT(u.FirstName, " ", u.LastName)
                     AS buyer_name
             FROM ws_market_reviews r
             LEFT JOIN UserAccounts u
                 ON u.PrincipalID = r.buyer_id
             WHERE r.listing_id = ?
               AND r.status = "published"
             ORDER BY r.created_at DESC',
            'i',
            [$listingId]
        );
    }

    public function storefront(
        string $storeSlug
    ): ?array {
        $seller = mp_stmt_row(
            $this->db,
            'SELECT
                 s.*,
                 CONCAT(u.FirstName, " ", u.LastName)
                     AS avatar_name
             FROM ws_market_sellers s
             LEFT JOIN UserAccounts u
                 ON u.PrincipalID = s.seller_id
             WHERE s.store_slug = ?
               AND s.status = "approved"
             LIMIT 1',
            's',
            [$storeSlug]
        );

        if (!$seller) {
            return null;
        }

        $seller['listings'] = mp_stmt_rows(
            $this->db,
            'SELECT
                 l.*,
                 (
                     SELECT i.id
                     FROM ws_market_listing_images i
                     WHERE i.listing_id = l.id
                     ORDER BY i.sort_order, i.id
                     LIMIT 1
                 ) AS primary_image_id
             FROM ws_market_listings l
             WHERE l.seller_id = ?
               AND l.status = "published"
               AND l.active_version_id IS NOT NULL
             ORDER BY l.published_at DESC, l.id DESC',
            's',
            [(string)$seller['seller_id']]
        );

        return $seller;
    }

    public function cartListings(array $ids): array
    {
        if ($ids === []) {
            return [];
        }

        $ids = array_values(
            array_unique(
                array_map('intval', $ids)
            )
        );

        $placeholders = implode(
            ',',
            array_fill(0, count($ids), '?')
        );

        return mp_stmt_rows(
            $this->db,
            'SELECT
                 l.*,
                 s.store_name,
                 (
                     SELECT i.id
                     FROM ws_market_listing_images i
                     WHERE i.listing_id = l.id
                     ORDER BY i.sort_order, i.id
                     LIMIT 1
                 ) AS primary_image_id
             FROM ws_market_listings l
             JOIN ws_market_sellers s
                 ON s.seller_id = l.seller_id
             WHERE l.id IN (' . $placeholders . ')
               AND l.status = "published"
               AND l.active_version_id IS NOT NULL
               AND s.status = "approved"
             ORDER BY l.title',
            str_repeat('i', count($ids)),
            $ids
        );
    }

    public function localUserByUuid(
        string $userId
    ): ?array {
        if (!mp_uuid($userId)) {
            return null;
        }

        return mp_stmt_row(
            $this->db,
            'SELECT
                 PrincipalID,
                 FirstName,
                 LastName,
                 UserLevel
             FROM UserAccounts
             WHERE PrincipalID = ?
             LIMIT 1',
            's',
            [strtolower($userId)]
        );
    }

    public function localUserByNameOrUuid(
        string $value
    ): ?array {
        $value = trim($value);

        if ($value === '') {
            return null;
        }

        if (mp_uuid(strtolower($value))) {
            return $this->localUserByUuid(
                strtolower($value)
            );
        }

        $normalized = preg_replace(
            '/\s+/',
            ' ',
            $value
        ) ?? $value;

        $matches = mp_stmt_rows(
            $this->db,
            'SELECT
                 PrincipalID,
                 FirstName,
                 LastName,
                 UserLevel
             FROM UserAccounts
             WHERE LOWER(
                 CONCAT(
                     TRIM(FirstName),
                     " ",
                     TRIM(LastName)
                 )
             ) = LOWER(?)
             LIMIT 2',
            's',
            [$normalized]
        );

        if (count($matches) > 1) {
            throw new RuntimeException(
                'More than one local account matched that name. Use the recipient UUID.'
            );
        }

        return $matches[0] ?? null;
    }

    public function createOrder(
        array $buyer,
        array $listingIds,
        string $recipientId,
        string $giftMessage
    ): array {
        $buyerId = strtolower(
            (string)$buyer['PrincipalID']
        );

        $recipientInput = trim($recipientId);

        if ($recipientInput === '') {
            $recipientId = $buyerId;
        } else {
            $recipient = $this->localUserByNameOrUuid(
                $recipientInput
            );

            if (!$recipient) {
                throw new RuntimeException(
                    'The gift recipient was not found as a local grid account.'
                );
            }

            $recipientId = strtolower(
                (string)$recipient['PrincipalID']
            );
        }

        $listingIds = array_values(
            array_unique(
                array_filter(
                    array_map('intval', $listingIds),
                    static fn (int $id): bool =>
                        $id > 0
                )
            )
        );

        $maximum = defined('MP_MAX_CART_ITEMS')
            ? max(1, min(50, (int)MP_MAX_CART_ITEMS))
            : 10;

        if ($listingIds === [] ||
            count($listingIds) > $maximum) {
            throw new RuntimeException(
                'The Marketplace cart must contain between 1 and ' .
                $maximum .
                ' listings.'
            );
        }

        $giftMessage = mb_substr(
            trim($giftMessage),
            0,
            500
        );

        $provider = mp_payment_provider();
        $orderUuid = mp_uuid_v4();
        $currency = defined('MP_CURRENCY_LABEL')
            ? (string)MP_CURRENCY_LABEL
            : 'Grid Credits';

        $this->db->begin_transaction();

        try {
            $orderLines = [];
            $total = 0;

            foreach ($listingIds as $listingId) {
                $listing = mp_stmt_row(
                    $this->db,
                    'SELECT
                         l.*,
                         s.status AS seller_status,
                         s.commission_basis_points,
                         v.snapshot_folder_id
                     FROM ws_market_listings l
                     JOIN ws_market_sellers s
                         ON s.seller_id = l.seller_id
                     JOIN ws_market_listing_versions v
                         ON v.id = l.active_version_id
                     WHERE l.id = ?
                     FOR UPDATE',
                    'i',
                    [$listingId]
                );

                if (!$listing ||
                    $listing['status'] !== 'published' ||
                    $listing['seller_status'] !== 'approved' ||
                    empty($listing['active_version_id'])) {
                    throw new RuntimeException(
                        'A cart listing is no longer available.'
                    );
                }

                if ($listing['quantity_limit'] !== null &&
                    (
                        (int)$listing['sold_count'] +
                        (int)$listing['reserved_count']
                    ) >= (int)$listing['quantity_limit']) {
                    throw new RuntimeException(
                        '"' .
                        (string)$listing['title'] .
                        '" is sold out.'
                    );
                }

                $price = (int)$listing['price'];

                $basisPoints = (int)(
                    $listing['commission_basis_points'] ?: (
                        defined(
                            'MP_DEFAULT_COMMISSION_BASIS_POINTS'
                        )
                            ? MP_DEFAULT_COMMISSION_BASIS_POINTS
                            : 0
                    )
                );

                $basisPoints = max(
                    0,
                    min(10000, $basisPoints)
                );

                $fee = intdiv(
                    $price * $basisPoints,
                    10000
                );

                $net = $price - $fee;
                $total += $price;

                $orderLines[] = [
                    'listing' => $listing,
                    'price' => $price,
                    'fee' => $fee,
                    'net' => $net,
                ];
            }

            $orderStatus =
                $provider->initialOrderStatus($total);

            $paymentStatus =
                $provider->initialPaymentStatus($total);

            $stmt = $this->db->prepare(
                'INSERT INTO ws_market_orders
                 (
                     order_uuid,
                     buyer_id,
                     recipient_id,
                     gift_message,
                     status,
                     payment_provider,
                     total_amount,
                     currency_label,
                     created_at,
                     approved_at,
                     updated_at
                 )
                 VALUES
                 (?, ?, ?, ?, ?, ?, ?, ?, NOW(),
                  IF(? = "approved", NOW(), NULL),
                  NOW())'
            );

            $providerName = $provider->name();

            $stmt->bind_param(
                'ssssssiss',
                $orderUuid,
                $buyerId,
                $recipientId,
                $giftMessage,
                $orderStatus,
                $providerName,
                $total,
                $currency,
                $orderStatus
            );

            $stmt->execute();
            $orderId = (int)$stmt->insert_id;
            $stmt->close();

            foreach ($orderLines as $line) {
                $listing = $line['listing'];

                $stmt = $this->db->prepare(
                    'INSERT INTO ws_market_order_items
                     (
                         order_id,
                         listing_id,
                         listing_version_id,
                         seller_id,
                         title,
                         unit_price,
                         fee_amount,
                         seller_net,
                         delivery_status
                     )
                     VALUES
                     (?, ?, ?, ?, ?, ?, ?, ?, "pending")'
                );

                $listingId =
                    (int)$listing['id'];
                $versionId =
                    (int)$listing['active_version_id'];
                $sellerId =
                    (string)$listing['seller_id'];
                $title =
                    (string)$listing['title'];
                $price =
                    (int)$line['price'];
                $fee =
                    (int)$line['fee'];
                $net =
                    (int)$line['net'];

                $stmt->bind_param(
                    'iiissiii',
                    $orderId,
                    $listingId,
                    $versionId,
                    $sellerId,
                    $title,
                    $price,
                    $fee,
                    $net
                );

                $stmt->execute();
                $stmt->close();

                mp_stmt_exec(
                    $this->db,
                    'UPDATE ws_market_listings
                     SET
                         reserved_count = reserved_count + 1,
                         updated_at = NOW()
                     WHERE id = ?',
                    'i',
                    [$listingId]
                );
            }

            $stmt = $this->db->prepare(
                'INSERT INTO ws_market_payments
                 (
                     order_id,
                     provider,
                     amount,
                     status,
                     created_at,
                     updated_at,
                     approved_at
                 )
                 VALUES
                 (?, ?, ?, ?, NOW(), NOW(),
                  IF(? = "approved", NOW(), NULL))'
            );

            $stmt->bind_param(
                'isiss',
                $orderId,
                $providerName,
                $total,
                $paymentStatus,
                $paymentStatus
            );

            $stmt->execute();
            $stmt->close();

            $this->db->commit();
        } catch (Throwable $e) {
            $this->db->rollback();
            throw $e;
        }

        $this->audit(
            $buyerId,
            'order.create',
            'order',
            $orderUuid,
            [
                'recipient_id' => $recipientId,
                'listing_ids' => $listingIds,
                'total_amount' => $total,
                'payment_provider' =>
                    $provider->name(),
            ]
        );

        if ($orderStatus === 'approved') {
            $this->deliverOrder($orderId);
        }

        return $this->orderByUuid(
            $orderUuid,
            $buyerId
        ) ?? [
            'order_uuid' => $orderUuid,
            'status' => $orderStatus,
        ];
    }

    public function orderByUuid(
        string $orderUuid,
        ?string $buyerId = null
    ): ?array {
        $sql =
            'SELECT *
             FROM ws_market_orders
             WHERE order_uuid = ?';

        $types = 's';
        $params = [$orderUuid];

        if ($buyerId !== null) {
            $sql .= ' AND buyer_id = ?';
            $types .= 's';
            $params[] = $buyerId;
        }

        $sql .= ' LIMIT 1';

        $order = mp_stmt_row(
            $this->db,
            $sql,
            $types,
            $params
        );

        if (!$order) {
            return null;
        }

        $order['items'] = mp_stmt_rows(
            $this->db,
            'SELECT
                 oi.*,
                 l.slug,
                 l.redelivery_enabled,
                 v.version_uuid,
                 v.snapshot_folder_id,
                 v.snapshot_fingerprint
             FROM ws_market_order_items oi
             JOIN ws_market_listings l
                 ON l.id = oi.listing_id
             JOIN ws_market_listing_versions v
                 ON v.id = oi.listing_version_id
             WHERE oi.order_id = ?
             ORDER BY oi.id',
            'i',
            [(int)$order['id']]
        );

        return $order;
    }

    public function buyerOrders(
        string $buyerId
    ): array {
        $orders = mp_stmt_rows(
            $this->db,
            'SELECT *
             FROM ws_market_orders
             WHERE buyer_id = ?
             ORDER BY created_at DESC, id DESC
             LIMIT 200',
            's',
            [$buyerId]
        );

        foreach ($orders as &$order) {
            $order['items'] = mp_stmt_rows(
                $this->db,
                'SELECT
                     oi.*,
                     l.slug,
                     l.redelivery_enabled
                 FROM ws_market_order_items oi
                 JOIN ws_market_listings l
                     ON l.id = oi.listing_id
                 WHERE oi.order_id = ?
                 ORDER BY oi.id',
                'i',
                [(int)$order['id']]
            );
        }

        unset($order);

        return $orders;
    }

    public function approvePayment(
        string $adminId,
        int $orderId,
        string $reference
    ): void {
        $reference = mb_substr(
            trim($reference),
            0,
            190
        );

        $this->db->begin_transaction();

        try {
            $order = mp_stmt_row(
                $this->db,
                'SELECT *
                 FROM ws_market_orders
                 WHERE id = ?
                 FOR UPDATE',
                'i',
                [$orderId]
            );

            if (!$order ||
                $order['status'] !== 'payment_pending') {
                throw new RuntimeException(
                    'Payment-pending order was not found.'
                );
            }

            mp_stmt_exec(
                $this->db,
                'UPDATE ws_market_payments
                 SET
                     provider_reference = NULLIF(?, ""),
                     raw_reference = ?,
                     status = "approved",
                     approved_at = NOW(),
                     approved_by = ?,
                     updated_at = NOW()
                 WHERE order_id = ?
                   AND status = "pending"',
                'sssi',
                [
                    $reference,
                    $reference,
                    $adminId,
                    $orderId,
                ]
            );

            mp_stmt_exec(
                $this->db,
                'UPDATE ws_market_orders
                 SET
                     status = "approved",
                     approved_at = NOW(),
                     approved_by = ?,
                     updated_at = NOW()
                 WHERE id = ?',
                'si',
                [$adminId, $orderId]
            );

            $this->db->commit();
        } catch (Throwable $e) {
            $this->db->rollback();
            throw $e;
        }

        $this->audit(
            $adminId,
            'payment.approve',
            'order',
            (string)$orderId,
            [
                'reference' => $reference,
            ]
        );

        $this->deliverOrder($orderId);
    }

    public function cancelOrder(
        string $adminId,
        int $orderId
    ): void {
        $this->db->begin_transaction();

        try {
            $order = mp_stmt_row(
                $this->db,
                'SELECT *
                 FROM ws_market_orders
                 WHERE id = ?
                 FOR UPDATE',
                'i',
                [$orderId]
            );

            if (!$order ||
                !in_array(
                    $order['status'],
                    [
                        'payment_pending',
                        'approved',
                        'delivery_failed',
                    ],
                    true
                )) {
                throw new RuntimeException(
                    'Order cannot be cancelled in its current state.'
                );
            }

            $items = mp_stmt_rows(
                $this->db,
                'SELECT *
                 FROM ws_market_order_items
                 WHERE order_id = ?
                 FOR UPDATE',
                'i',
                [$orderId]
            );

            foreach ($items as $item) {
                if ($item['delivery_status'] === 'delivered') {
                    throw new RuntimeException(
                        'An order with one or more delivered items cannot be cancelled.'
                    );
                }
            }

            foreach ($items as $item) {
                mp_stmt_exec(
                    $this->db,
                    'UPDATE ws_market_listings
                     SET
                         reserved_count =
                             IF(reserved_count > 0,
                                reserved_count - 1,
                                0),
                         updated_at = NOW()
                     WHERE id = ?',
                    'i',
                    [(int)$item['listing_id']]
                );
            }

            mp_stmt_exec(
                $this->db,
                'UPDATE ws_market_orders
                 SET
                     status = "cancelled",
                     updated_at = NOW()
                 WHERE id = ?',
                'i',
                [$orderId]
            );

            mp_stmt_exec(
                $this->db,
                'UPDATE ws_market_payments
                 SET
                     status = IF(
                         status = "approved",
                         "cancelled",
                         "cancelled"
                     ),
                     updated_at = NOW()
                 WHERE order_id = ?
                   AND status IN ("pending","approved")',
                'i',
                [$orderId]
            );

            $this->db->commit();
        } catch (Throwable $e) {
            $this->db->rollback();
            throw $e;
        }

        $this->audit(
            $adminId,
            'order.cancel',
            'order',
            (string)$orderId,
            []
        );
    }

    public function deliverOrder(int $orderId): void
    {
        $order = mp_stmt_row(
            $this->db,
            'SELECT *
             FROM ws_market_orders
             WHERE id = ?
             LIMIT 1',
            'i',
            [$orderId]
        );

        if (!$order) {
            throw new RuntimeException(
                'Marketplace order was not found.'
            );
        }

        if ($order['status'] === 'delivered') {
            return;
        }

        if (!in_array(
            $order['status'],
            ['approved', 'delivering', 'delivery_failed'],
            true
        )) {
            throw new RuntimeException(
                'Order is not approved for delivery.'
            );
        }

        mp_stmt_exec(
            $this->db,
            'UPDATE ws_market_orders
             SET
                 status = "delivering",
                 updated_at = NOW()
             WHERE id = ?',
            'i',
            [$orderId]
        );

        $items = mp_stmt_rows(
            $this->db,
            'SELECT
                 oi.*,
                 v.snapshot_folder_id,
                 v.snapshot_fingerprint
             FROM ws_market_order_items oi
             JOIN ws_market_listing_versions v
                 ON v.id = oi.listing_version_id
             WHERE oi.order_id = ?
             ORDER BY oi.id',
            'i',
            [$orderId]
        );

        $allDelivered = true;

        foreach ($items as $item) {
            if ($item['delivery_status'] === 'delivered') {
                continue;
            }

            $deliveryId =
                'market-' .
                (string)$order['order_uuid'] .
                '-' .
                (int)$item['id'];

            $success = $this->attemptDelivery(
                $deliveryId,
                (int)$item['id'],
                (int)$item['listing_version_id'],
                (string)$item['seller_id'],
                (string)$item['snapshot_folder_id'],
                (string)$item['snapshot_fingerprint'],
                (string)$order['recipient_id'],
                'original'
            );

            if ($success) {
                $this->completeOriginalDelivery(
                    $item
                );
            } else {
                $allDelivered = false;
            }
        }

        $remaining = mp_stmt_row(
            $this->db,
            'SELECT COUNT(*) AS remaining
             FROM ws_market_order_items
             WHERE order_id = ?
               AND delivery_status <> "delivered"',
            'i',
            [$orderId]
        );

        $allDelivered =
            $allDelivered &&
            (int)($remaining['remaining'] ?? 0) === 0;

        mp_stmt_exec(
            $this->db,
            $allDelivered
                ? 'UPDATE ws_market_orders
                   SET
                       status = "delivered",
                       delivered_at = NOW(),
                       updated_at = NOW()
                   WHERE id = ?'
                : 'UPDATE ws_market_orders
                   SET
                       status = "delivery_failed",
                       updated_at = NOW()
                   WHERE id = ?',
            'i',
            [$orderId]
        );
    }

    public function redeliver(
        string $buyerId,
        int $orderItemId
    ): array {
        $item = mp_stmt_row(
            $this->db,
            'SELECT
                 oi.*,
                 o.order_uuid,
                 o.buyer_id,
                 o.recipient_id,
                 o.status AS order_status,
                 l.redelivery_enabled,
                 v.snapshot_folder_id,
                 v.snapshot_fingerprint
             FROM ws_market_order_items oi
             JOIN ws_market_orders o
                 ON o.id = oi.order_id
             JOIN ws_market_listings l
                 ON l.id = oi.listing_id
             JOIN ws_market_listing_versions v
                 ON v.id = oi.listing_version_id
             WHERE oi.id = ?
             LIMIT 1',
            'i',
            [$orderItemId]
        );

        if (!$item ||
            strtolower((string)$item['buyer_id']) !==
                strtolower($buyerId)) {
            throw new RuntimeException(
                'Marketplace order item was not found.'
            );
        }

        if ($item['delivery_status'] !== 'delivered' ||
            empty($item['redelivery_enabled'])) {
            throw new RuntimeException(
                'This order item is not eligible for redelivery.'
            );
        }

        $deliveryId =
            'market-redelivery-' .
            (int)$orderItemId .
            '-' .
            str_replace('-', '', mp_uuid_v4());

        $ok = $this->attemptDelivery(
            $deliveryId,
            $orderItemId,
            (int)$item['listing_version_id'],
            (string)$item['seller_id'],
            (string)$item['snapshot_folder_id'],
            (string)$item['snapshot_fingerprint'],
            (string)$item['recipient_id'],
            'redelivery'
        );

        if (!$ok) {
            throw new RuntimeException(
                'Marketplace redelivery failed. The delivery is recorded for staff review.'
            );
        }

        return mp_stmt_row(
            $this->db,
            'SELECT *
             FROM ws_market_deliveries
             WHERE delivery_uuid = ?',
            's',
            [$deliveryId]
        ) ?? [];
    }

    public function testDelivery(
        string $sellerId,
        int $listingId
    ): array {
        $listing = mp_stmt_row(
            $this->db,
            'SELECT
                 l.*,
                 v.snapshot_folder_id,
                 v.snapshot_fingerprint,
                 v.id AS listing_version_id
             FROM ws_market_listings l
             JOIN ws_market_listing_versions v
                 ON v.id = l.active_version_id
             WHERE l.id = ?
               AND l.seller_id = ?
               AND l.status = "published"
             LIMIT 1',
            'is',
            [$listingId, $sellerId]
        );

        if (!$listing) {
            throw new RuntimeException(
                'A published listing version is required for test delivery.'
            );
        }

        $deliveryId =
            'market-test-' .
            $listingId .
            '-' .
            str_replace('-', '', mp_uuid_v4());

        $ok = $this->attemptDelivery(
            $deliveryId,
            null,
            (int)$listing['listing_version_id'],
            $sellerId,
            (string)$listing['snapshot_folder_id'],
            (string)$listing['snapshot_fingerprint'],
            $sellerId,
            'test'
        );

        if (!$ok) {
            throw new RuntimeException(
                'Marketplace test delivery failed.'
            );
        }

        return mp_stmt_row(
            $this->db,
            'SELECT *
             FROM ws_market_deliveries
             WHERE delivery_uuid = ?',
            's',
            [$deliveryId]
        ) ?? [];
    }

    private function attemptDelivery(
        string $deliveryId,
        ?int $orderItemId,
        int $listingVersionId,
        string $sellerId,
        string $snapshotFolderId,
        string $snapshotFingerprint,
        string $recipientId,
        string $type
    ): bool {
        $existing = mp_stmt_row(
            $this->db,
            'SELECT *
             FROM ws_market_deliveries
             WHERE delivery_uuid = ?
             LIMIT 1',
            's',
            [$deliveryId]
        );

        if ($existing &&
            $existing['status'] === 'delivered') {
            return true;
        }

        if (!$existing) {
            $stmt = $this->db->prepare(
                'INSERT INTO ws_market_deliveries
                 (
                     delivery_uuid,
                     order_item_id,
                     listing_version_id,
                     recipient_id,
                     delivery_type,
                     status,
                     attempts,
                     created_at
                 )
                 VALUES
                 (?, ?, ?, ?, ?, "pending", 0, NOW())'
            );

            $stmt->bind_param(
                'siiss',
                $deliveryId,
                $orderItemId,
                $listingVersionId,
                $recipientId,
                $type
            );

            $stmt->execute();
            $stmt->close();
        }

        try {
            $response = $this->os->deliver(
                $deliveryId,
                $sellerId,
                $snapshotFolderId,
                $snapshotFingerprint,
                $recipientId
            );

            $ok = !empty($response['ok']);
            $message = mb_substr(
                (string)(
                    $response['message'] ??
                    ($ok
                        ? 'Delivery completed.'
                        : 'Delivery failed.')
                ),
                0,
                1000
            );

            $destination = strtolower(
                trim(
                    (string)(
                        $response[
                            'destination_folder_id'
                        ] ?? ''
                    )
                )
            );

            mp_stmt_exec(
                $this->db,
                'UPDATE ws_market_deliveries
                 SET
                     status = ?,
                     attempts = attempts + 1,
                     destination_folder_id =
                         NULLIF(?, ""),
                     result_message = ?,
                     last_attempt_at = NOW(),
                     delivered_at =
                         IF(? = "delivered", NOW(), NULL)
                 WHERE delivery_uuid = ?',
                'sssss',
                [
                    $ok ? 'delivered' : 'failed',
                    $destination,
                    $message,
                    $ok ? 'delivered' : 'failed',
                    $deliveryId,
                ]
            );

            if (!$ok && $orderItemId !== null) {
                mp_stmt_exec(
                    $this->db,
                    'UPDATE ws_market_order_items
                     SET delivery_status = "failed"
                     WHERE id = ?
                       AND delivery_status <> "delivered"',
                    'i',
                    [$orderItemId]
                );
            }

            return $ok;
        } catch (Throwable $e) {
            mp_stmt_exec(
                $this->db,
                'UPDATE ws_market_deliveries
                 SET
                     status = "failed",
                     attempts = attempts + 1,
                     result_message = ?,
                     last_attempt_at = NOW()
                 WHERE delivery_uuid = ?',
                'ss',
                [
                    mb_substr(
                        $e->getMessage(),
                        0,
                        1000
                    ),
                    $deliveryId,
                ]
            );

            if ($orderItemId !== null) {
                mp_stmt_exec(
                    $this->db,
                    'UPDATE ws_market_order_items
                     SET delivery_status = "failed"
                     WHERE id = ?
                       AND delivery_status <> "delivered"',
                    'i',
                    [$orderItemId]
                );
            }

            return false;
        }
    }

    private function completeOriginalDelivery(
        array $item
    ): void {
        $this->db->begin_transaction();

        try {
            $current = mp_stmt_row(
                $this->db,
                'SELECT *
                 FROM ws_market_order_items
                 WHERE id = ?
                 FOR UPDATE',
                'i',
                [(int)$item['id']]
            );

            if (!$current) {
                throw new RuntimeException(
                    'Marketplace order item vanished during delivery completion.'
                );
            }

            if ($current['delivery_status'] === 'delivered') {
                $this->db->commit();
                return;
            }

            mp_stmt_exec(
                $this->db,
                'UPDATE ws_market_order_items
                 SET
                     delivery_status = "delivered",
                     delivered_at = NOW()
                 WHERE id = ?',
                'i',
                [(int)$item['id']]
            );

            mp_stmt_exec(
                $this->db,
                'UPDATE ws_market_listings
                 SET
                     reserved_count =
                         IF(reserved_count > 0,
                            reserved_count - 1,
                            0),
                     sold_count = sold_count + 1,
                     updated_at = NOW()
                 WHERE id = ?',
                'i',
                [(int)$item['listing_id']]
            );

            $stmt = $this->db->prepare(
                'INSERT IGNORE INTO ws_market_seller_ledger
                 (
                     seller_id,
                     order_item_id,
                     gross_amount,
                     fee_amount,
                     net_amount,
                     settlement_status,
                     created_at
                 )
                 VALUES
                 (?, ?, ?, ?, ?, "unsettled", NOW())'
            );

            $sellerId =
                (string)$item['seller_id'];
            $itemId =
                (int)$item['id'];
            $gross =
                (int)$item['unit_price'];
            $fee =
                (int)$item['fee_amount'];
            $net =
                (int)$item['seller_net'];

            $stmt->bind_param(
                'siiii',
                $sellerId,
                $itemId,
                $gross,
                $fee,
                $net
            );

            $stmt->execute();
            $stmt->close();

            $this->db->commit();
        } catch (Throwable $e) {
            $this->db->rollback();
            throw $e;
        }
    }

    public function saveReview(
        string $buyerId,
        int $listingId,
        int $rating,
        string $title,
        string $body
    ): void {
        $rating = max(1, min(5, $rating));
        $title = mb_substr(trim($title), 0, 120);
        $body = mb_substr(trim($body), 0, 10000);

        if ($title === '' || $body === '') {
            throw new RuntimeException(
                'Review title and body are required.'
            );
        }

        $verified = mp_stmt_row(
            $this->db,
            'SELECT oi.id
             FROM ws_market_order_items oi
             JOIN ws_market_orders o
                 ON o.id = oi.order_id
             WHERE o.buyer_id = ?
               AND oi.listing_id = ?
               AND oi.delivery_status = "delivered"
             ORDER BY oi.delivered_at DESC
             LIMIT 1',
            'si',
            [$buyerId, $listingId]
        );

        if (!$verified) {
            throw new RuntimeException(
                'Only verified purchasers can review this listing.'
            );
        }

        $orderItemId = (int)$verified['id'];

        $stmt = $this->db->prepare(
            'INSERT INTO ws_market_reviews
             (
                 listing_id,
                 buyer_id,
                 order_item_id,
                 rating,
                 title,
                 body,
                 status,
                 created_at,
                 updated_at
             )
             VALUES
             (?, ?, ?, ?, ?, ?, "published", NOW(), NOW())
             ON DUPLICATE KEY UPDATE
                 order_item_id = VALUES(order_item_id),
                 rating = VALUES(rating),
                 title = VALUES(title),
                 body = VALUES(body),
                 status = "published",
                 updated_at = NOW()'
        );

        $stmt->bind_param(
            'isiiss',
            $listingId,
            $buyerId,
            $orderItemId,
            $rating,
            $title,
            $body
        );

        $stmt->execute();
        $stmt->close();

        $this->audit(
            $buyerId,
            'review.save',
            'listing',
            (string)$listingId,
            [
                'rating' => $rating,
                'order_item_id' => $orderItemId,
            ]
        );
    }

    public function sellerRespondReview(
        string $sellerId,
        int $reviewId,
        string $response
    ): void {
        $response = mb_substr(
            trim($response),
            0,
            10000
        );

        if ($response === '') {
            throw new RuntimeException(
                'Seller response cannot be empty.'
            );
        }

        if (mp_stmt_exec(
            $this->db,
            'UPDATE ws_market_reviews r
             JOIN ws_market_listings l
                 ON l.id = r.listing_id
             SET
                 r.seller_response = ?,
                 r.responded_at = NOW(),
                 r.updated_at = NOW()
             WHERE r.id = ?
               AND l.seller_id = ?',
            'sis',
            [$response, $reviewId, $sellerId]
        ) < 1) {
            throw new RuntimeException(
                'Marketplace review was not found.'
            );
        }

        $this->audit(
            $sellerId,
            'review.respond',
            'review',
            (string)$reviewId,
            []
        );
    }

    public function sellerSales(
        string $sellerId
    ): array {
        return mp_stmt_rows(
            $this->db,
            'SELECT
                 sl.*,
                 oi.title,
                 o.order_uuid,
                 o.buyer_id,
                 o.recipient_id,
                 o.delivered_at
             FROM ws_market_seller_ledger sl
             JOIN ws_market_order_items oi
                 ON oi.id = sl.order_item_id
             JOIN ws_market_orders o
                 ON o.id = oi.order_id
             WHERE sl.seller_id = ?
             ORDER BY sl.created_at DESC, sl.id DESC
             LIMIT 500',
            's',
            [$sellerId]
        );
    }

    public function adminDashboard(): array
    {
        return [
            'sellers' => mp_stmt_rows(
                $this->db,
                'SELECT
                     s.*,
                     CONCAT(u.FirstName, " ", u.LastName)
                         AS avatar_name
                 FROM ws_market_sellers s
                 LEFT JOIN UserAccounts u
                     ON u.PrincipalID = s.seller_id
                 ORDER BY
                     FIELD(
                         s.status,
                         "pending",
                         "approved",
                         "suspended",
                         "rejected"
                     ),
                     s.updated_at DESC
                 LIMIT 200'
            ),
            'pending_listings' => mp_stmt_rows(
                $this->db,
                'SELECT
                     l.*,
                     s.store_name,
                     CONCAT(u.FirstName, " ", u.LastName)
                         AS seller_name
                 FROM ws_market_listings l
                 JOIN ws_market_sellers s
                     ON s.seller_id = l.seller_id
                 LEFT JOIN UserAccounts u
                     ON u.PrincipalID = l.seller_id
                 WHERE l.status = "pending"
                 ORDER BY l.updated_at
                 LIMIT 200'
            ),
            'orders' => mp_stmt_rows(
                $this->db,
                'SELECT *
                 FROM ws_market_orders
                 WHERE status IN
                 (
                     "payment_pending",
                     "approved",
                     "delivering",
                     "delivery_failed"
                 )
                 ORDER BY created_at DESC
                 LIMIT 200'
            ),
        ];
    }

    public function setSellerStatus(
        string $adminId,
        string $sellerId,
        string $status
    ): void {
        if (!in_array(
            $status,
            [
                'approved',
                'suspended',
                'rejected',
                'pending',
            ],
            true
        )) {
            throw new RuntimeException(
                'Invalid seller status.'
            );
        }

        mp_stmt_exec(
            $this->db,
            'UPDATE ws_market_sellers
             SET
                 status = ?,
                 approved_at =
                     IF(? = "approved", NOW(), approved_at),
                 approved_by =
                     IF(? = "approved", ?, approved_by),
                 updated_at = NOW()
             WHERE seller_id = ?',
            'sssss',
            [
                $status,
                $status,
                $status,
                $adminId,
                $sellerId,
            ]
        );

        $this->audit(
            $adminId,
            'seller.status',
            'seller',
            $sellerId,
            [
                'status' => $status,
            ]
        );
    }

    public function retryOrder(
        string $adminId,
        int $orderId
    ): void {
        $this->audit(
            $adminId,
            'order.retry',
            'order',
            (string)$orderId,
            []
        );

        $this->deliverOrder($orderId);
    }

    public function uploadImages(
        string $sellerId,
        int $listingId,
        array $files
    ): void {
        $listing = $this->listingForSeller(
            $sellerId,
            $listingId
        );

        if (!$listing) {
            throw new RuntimeException(
                'Marketplace listing was not found.'
            );
        }

        $currentCount = $this->imageCount(
            $listingId
        );

        $maximum = defined(
            'MP_MAX_IMAGES_PER_LISTING'
        )
            ? max(
                1,
                min(
                    20,
                    (int)MP_MAX_IMAGES_PER_LISTING
                )
            )
            : 8;

        $normalized = $this->normalizeUploadArray(
            $files
        );

        if ($normalized === []) {
            return;
        }

        if ($currentCount + count($normalized) >
            $maximum) {
            throw new RuntimeException(
                'A listing can contain at most ' .
                $maximum .
                ' images.'
            );
        }

        $storageRoot = $this->imageStorageRoot();
        $finfo = new finfo(FILEINFO_MIME_TYPE);

        $allowed = [
            'image/jpeg' => 'jpg',
            'image/png' => 'png',
            'image/webp' => 'webp',
            'image/gif' => 'gif',
        ];

        $maximumBytes = defined(
            'MP_MAX_IMAGE_BYTES'
        )
            ? max(
                1024,
                (int)MP_MAX_IMAGE_BYTES
            )
            : 8 * 1024 * 1024;

        foreach ($normalized as $upload) {
            if ((int)$upload['error'] !==
                UPLOAD_ERR_OK) {
                throw new RuntimeException(
                    'A Marketplace image upload failed with code ' .
                    (int)$upload['error'] .
                    '.'
                );
            }

            $size = (int)$upload['size'];

            if ($size < 1 || $size > $maximumBytes) {
                throw new RuntimeException(
                    'Marketplace image size is outside the configured limit.'
                );
            }

            $tmp = (string)$upload['tmp_name'];

            if (!is_uploaded_file($tmp)) {
                throw new RuntimeException(
                    'Marketplace image upload was not an HTTP upload.'
                );
            }

            $mime = (string)$finfo->file($tmp);

            if (!isset($allowed[$mime])) {
                throw new RuntimeException(
                    'Marketplace images must be JPEG, PNG, WebP or GIF.'
                );
            }

            $dimensions = getimagesize($tmp);

            if (!is_array($dimensions) ||
                empty($dimensions[0]) ||
                empty($dimensions[1])) {
                throw new RuntimeException(
                    'Marketplace image could not be decoded.'
                );
            }

            $storageName =
                bin2hex(random_bytes(24)) .
                '.' .
                $allowed[$mime];

            $destination =
                $storageRoot .
                DIRECTORY_SEPARATOR .
                $storageName;

            if (!move_uploaded_file(
                $tmp,
                $destination
            )) {
                throw new RuntimeException(
                    'Marketplace image could not be stored.'
                );
            }

            try {
                $stmt = $this->db->prepare(
                    'INSERT INTO ws_market_listing_images
                     (
                         listing_id,
                         storage_name,
                         mime_type,
                         byte_size,
                         width_px,
                         height_px,
                         sort_order,
                         alt_text,
                         created_at
                     )
                     VALUES (?, ?, ?, ?, ?, ?, ?, ?, NOW())'
                );

                $width = (int)$dimensions[0];
                $height = (int)$dimensions[1];
                $sortOrder =
                    $currentCount++;
                $altText =
                    (string)$listing['title'];

                $stmt->bind_param(
                    'issiiiis',
                    $listingId,
                    $storageName,
                    $mime,
                    $size,
                    $width,
                    $height,
                    $sortOrder,
                    $altText
                );

                $stmt->execute();
                $stmt->close();
            } catch (Throwable $e) {
                @unlink($destination);
                throw $e;
            }
        }

        $this->audit(
            $sellerId,
            'listing.images_upload',
            'listing',
            (string)$listingId,
            [
                'uploaded' => count($normalized),
            ]
        );
    }

    public function deleteImage(
        string $sellerId,
        int $imageId
    ): void {
        $image = mp_stmt_row(
            $this->db,
            'SELECT i.*
             FROM ws_market_listing_images i
             JOIN ws_market_listings l
                 ON l.id = i.listing_id
             WHERE i.id = ?
               AND l.seller_id = ?
             LIMIT 1',
            'is',
            [$imageId, $sellerId]
        );

        if (!$image) {
            throw new RuntimeException(
                'Marketplace image was not found.'
            );
        }

        mp_stmt_exec(
            $this->db,
            'DELETE FROM ws_market_listing_images
             WHERE id = ?',
            'i',
            [$imageId]
        );

        $path =
            $this->imageStorageRoot() .
            DIRECTORY_SEPARATOR .
            basename(
                (string)$image['storage_name']
            );

        if (is_file($path)) {
            @unlink($path);
        }

        $this->audit(
            $sellerId,
            'listing.image_delete',
            'image',
            (string)$imageId,
            [
                'listing_id' =>
                    (int)$image['listing_id'],
            ]
        );
    }

    public function imageRecord(
        int $imageId
    ): ?array {
        return mp_stmt_row(
            $this->db,
            'SELECT *
             FROM ws_market_listing_images
             WHERE id = ?
             LIMIT 1',
            'i',
            [$imageId]
        );
    }

    public function imagePath(array $image): string
    {
        return $this->imageStorageRoot() .
            DIRECTORY_SEPARATOR .
            basename(
                (string)$image['storage_name']
            );
    }

    private function imageCount(int $listingId): int
    {
        $row = mp_stmt_row(
            $this->db,
            'SELECT COUNT(*) AS count_value
             FROM ws_market_listing_images
             WHERE listing_id = ?',
            'i',
            [$listingId]
        );

        return (int)($row['count_value'] ?? 0);
    }

    private function imageStorageRoot(): string
    {
        if (!defined('MP_IMAGE_STORAGE_ROOT')) {
            throw new RuntimeException(
                'MP_IMAGE_STORAGE_ROOT is not configured.'
            );
        }

        $root = rtrim(
            (string)MP_IMAGE_STORAGE_ROOT,
            "\\/"
        );

        if ($root === '') {
            throw new RuntimeException(
                'MP_IMAGE_STORAGE_ROOT is empty.'
            );
        }

        if (!is_dir($root) &&
            !mkdir($root, 0750, true) &&
            !is_dir($root)) {
            throw new RuntimeException(
                'Marketplace image storage directory could not be created.'
            );
        }

        if (!is_writable($root)) {
            throw new RuntimeException(
                'Marketplace image storage directory is not writable.'
            );
        }

        return $root;
    }

    private function normalizeUploadArray(
        array $files
    ): array {
        if (!isset($files['name'])) {
            return [];
        }

        if (!is_array($files['name'])) {
            return [[
                'name' => $files['name'] ?? '',
                'type' => $files['type'] ?? '',
                'tmp_name' => $files['tmp_name'] ?? '',
                'error' => $files['error'] ?? UPLOAD_ERR_NO_FILE,
                'size' => $files['size'] ?? 0,
            ]];
        }

        $result = [];

        foreach ($files['name'] as $index => $name) {
            $error = $files['error'][$index] ??
                UPLOAD_ERR_NO_FILE;

            if ($error === UPLOAD_ERR_NO_FILE) {
                continue;
            }

            $result[] = [
                'name' => $name,
                'type' => $files['type'][$index] ?? '',
                'tmp_name' =>
                    $files['tmp_name'][$index] ?? '',
                'error' => $error,
                'size' => $files['size'][$index] ?? 0,
            ];
        }

        return $result;
    }

    private function uniqueListingSlug(
        string $base
    ): string {
        $slug = mb_substr($base, 0, 130);
        $candidate = $slug;
        $counter = 2;

        while (mp_stmt_row(
            $this->db,
            'SELECT id
             FROM ws_market_listings
             WHERE slug = ?',
            's',
            [$candidate]
        )) {
            $suffix = '-' . $counter++;
            $candidate =
                mb_substr(
                    $slug,
                    0,
                    150 - strlen($suffix)
                ) .
                $suffix;
        }

        return $candidate;
    }

    private function uniqueStoreSlug(
        string $base
    ): string {
        $slug = mb_substr($base, 0, 120);
        $candidate = $slug;
        $counter = 2;

        while (mp_stmt_row(
            $this->db,
            'SELECT seller_id
             FROM ws_market_sellers
             WHERE store_slug = ?',
            's',
            [$candidate]
        )) {
            $suffix = '-' . $counter++;
            $candidate =
                mb_substr(
                    $slug,
                    0,
                    140 - strlen($suffix)
                ) .
                $suffix;
        }

        return $candidate;
    }

    public function audit(
        ?string $actorId,
        string $action,
        string $entityType,
        string $entityId,
        array $details
    ): void {
        $json = json_encode(
            $details,
            JSON_UNESCAPED_SLASHES |
            JSON_UNESCAPED_UNICODE |
            JSON_THROW_ON_ERROR
        );

        $stmt = $this->db->prepare(
            'INSERT INTO ws_market_audit
             (
                 actor_id,
                 action_name,
                 entity_type,
                 entity_id,
                 details_json,
                 created_at
             )
             VALUES (NULLIF(?, ""), ?, ?, ?, ?, NOW())'
        );

        $actor = $actorId ?? '';

        $stmt->bind_param(
            'sssss',
            $actor,
            $action,
            $entityType,
            $entityId,
            $json
        );

        $stmt->execute();
        $stmt->close();
    }
}

function mp_service(mysqli $db): MarketplaceService
{
    return new MarketplaceService(
        $db,
        new MarketplaceOpenSimClient()
    );
}
