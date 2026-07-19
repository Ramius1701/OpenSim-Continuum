<?php
declare(strict_types=1);

function mp_page_top(
    string $title,
    ?array $user = null
): void {
    global $siteRoot;

    $pageTitle =
        $title . ' - OpenSim Marketplace';

    require $siteRoot . '/include/header.php';

    echo '<link rel="stylesheet" href="/marketplace/marketplace.css">';

    $flash = mp_take_flash();
    $cartCount = count(mp_cart_ids());

    echo '<div class="mp-shell">';
    echo '<header class="mp-head">';
    echo '<div>';
    echo '<a class="mp-brand" href="/marketplace/">OpenSim Marketplace</a>';
    echo '<div class="mp-kicker">Creator goods delivered directly to your OpenSim inventory.</div>';
    echo '</div>';
    echo '<nav class="mp-nav">';
    echo '<a href="/marketplace/">Browse</a>';
    echo '<a href="/marketplace/cart.php">Cart (' . $cartCount . ')</a>';

    if ($user) {
        echo '<a href="/marketplace/orders.php">My Orders</a>';
        echo '<a href="/marketplace/manage/">Sell</a>';

        if (mp_is_admin($user)) {
            echo '<a href="/admin/marketplace/">Admin</a>';
        }
    }

    echo '</nav>';
    echo '</header>';

    if ($flash) {
        $type = preg_replace(
            '/[^a-z]/',
            '',
            strtolower(
                (string)($flash['type'] ?? 'info')
            )
        );

        echo '<div class="mp-flash mp-' .
            mp_h($type ?: 'info') .
            '">' .
            mp_h(
                (string)($flash['message'] ?? '')
            ) .
            '</div>';
    }
}

function mp_page_bottom(): void
{
    global $siteRoot;

    echo '</div>';
    require $siteRoot . '/include/footer.php';
}

function mp_status_badge(string $status): string
{
    $class = preg_replace(
        '/[^a-z_]/',
        '',
        strtolower($status)
    );

    return '<span class="mp-badge mp-status-' .
        mp_h($class ?: 'unknown') .
        '">' .
        mp_h(
            ucwords(
                str_replace('_', ' ', $status)
            )
        ) .
        '</span>';
}

function mp_rating(float $rating): string
{
    if ($rating <= 0) {
        return 'No reviews yet';
    }

    return number_format($rating, 1) . ' / 5';
}
