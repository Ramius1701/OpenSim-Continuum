<?php
declare(strict_types=1);

/**
 * OpenSim Marketplace v2.1.0 host adapter example.
 *
 * Copy this file to include/marketplace_host.php and adapt it to the website
 * hosting the Marketplace. The Marketplace core does not assume a particular
 * CMS, login page, session layout, database helper, or administrator model.
 *
 * This example implements a common OpenSimulator PHP-site arrangement:
 * - mysqli database access;
 * - an OpenSim UserAccounts table;
 * - UUID values stored in PHP session data;
 * - UserLevel-based administration.
 *
 * Replace the example values before deployment. Do not commit real secrets.
 */

if (!defined('MP_HOST_DB_HOST')) {
    define('MP_HOST_DB_HOST', '127.0.0.1');
}
if (!defined('MP_HOST_DB_PORT')) {
    define('MP_HOST_DB_PORT', 3306);
}
if (!defined('MP_HOST_DB_NAME')) {
    define('MP_HOST_DB_NAME', 'CHANGE_DATABASE_NAME');
}
if (!defined('MP_HOST_DB_USER')) {
    define('MP_HOST_DB_USER', 'CHANGE_DATABASE_USER');
}
if (!defined('MP_HOST_DB_PASSWORD')) {
    define('MP_HOST_DB_PASSWORD', 'CHANGE_DATABASE_PASSWORD');
}
if (!defined('MP_HOST_LOGIN_URL_TEMPLATE')) {
    define('MP_HOST_LOGIN_URL_TEMPLATE', '/login.php?redirect={return_url}');
}
if (!defined('MP_HOST_ADMIN_USERLEVEL_MIN')) {
    define('MP_HOST_ADMIN_USERLEVEL_MIN', 200);
}

function mp_host_db(): mysqli
{
    mysqli_report(MYSQLI_REPORT_ERROR | MYSQLI_REPORT_STRICT);

    $db = new mysqli(
        (string)MP_HOST_DB_HOST,
        (string)MP_HOST_DB_USER,
        (string)MP_HOST_DB_PASSWORD,
        (string)MP_HOST_DB_NAME,
        (int)MP_HOST_DB_PORT
    );
    $db->set_charset('utf8mb4');

    return $db;
}

function mp_host_current_user_id(): string
{
    $values = [
        $_SESSION['user']['PrincipalID'] ?? null,
        $_SESSION['user']['principal_id'] ?? null,
        $_SESSION['user']['UUID'] ?? null,
        $_SESSION['user']['uuid'] ?? null,
        $_SESSION['PrincipalID'] ?? null,
        $_SESSION['principal_id'] ?? null,
        $_SESSION['user_uuid'] ?? null,
        $_SESSION['uuid'] ?? null,
    ];

    foreach ($values as $value) {
        $candidate = strtolower(trim((string)$value));
        if (preg_match(
            '/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i',
            $candidate
        ) === 1 && $candidate !== '00000000-0000-0000-0000-000000000000') {
            return $candidate;
        }
    }

    return '';
}

function mp_host_current_user(mysqli $db): ?array
{
    $id = mp_host_current_user_id();
    if ($id === '') {
        return null;
    }

    /*
     * This query targets the standard OpenSimulator UserAccounts schema.
     * Replace this function when the host website uses another account source.
     */
    $stmt = $db->prepare(
        'SELECT PrincipalID, FirstName, LastName, UserLevel
         FROM UserAccounts
         WHERE PrincipalID = ?
         LIMIT 1'
    );
    $stmt->bind_param('s', $id);
    $stmt->execute();
    $result = $stmt->get_result();
    $user = $result ? $result->fetch_assoc() : null;
    $stmt->close();

    return is_array($user) ? $user : null;
}

function mp_host_login_url(string $returnUrl): string
{
    return str_replace(
        '{return_url}',
        rawurlencode($returnUrl),
        (string)MP_HOST_LOGIN_URL_TEMPLATE
    );
}

function mp_host_is_admin(array $user): bool
{
    return (int)($user['UserLevel'] ?? 0) >=
        (int)MP_HOST_ADMIN_USERLEVEL_MIN;
}
