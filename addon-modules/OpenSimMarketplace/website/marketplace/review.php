<?php
declare(strict_types=1);

require_once __DIR__ . '/bootstrap.php';
require_once __DIR__ . '/layout.php';

$db = mp_db();
$service = mp_service($db);
$user = mp_require_user($db);
$buyerId = strtolower((string)$user['PrincipalID']);
$listingId = max(0, (int)($_GET['listing_id'] ?? $_POST['listing_id'] ?? 0));

$listing = mp_stmt_row(
    $db,
    'SELECT id, title, slug
     FROM ws_market_listings
     WHERE id = ?
     LIMIT 1',
    'i',
    [$listingId]
);

if (!$listing) {
    http_response_code(404);
    exit('Marketplace listing not found.');
}

$existing = mp_stmt_row(
    $db,
    'SELECT *
     FROM ws_market_reviews
     WHERE listing_id = ?
       AND buyer_id = ?
     LIMIT 1',
    'is',
    [$listingId, $buyerId]
);

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    try {
        mp_require_csrf();

        $service->saveReview(
            $buyerId,
            $listingId,
            (int)($_POST['rating'] ?? 5),
            (string)($_POST['title'] ?? ''),
            (string)($_POST['body'] ?? '')
        );

        mp_flash('success', 'Your verified-purchase review was saved.');
        mp_redirect('/marketplace/item.php?slug=' . rawurlencode($listing['slug']));
    } catch (Throwable $e) {
        mp_flash('error', $e->getMessage());
        mp_redirect('/marketplace/review.php?listing_id=' . $listingId);
    }
}

mp_page_top('Review ' . $listing['title'], $user);
?>
<h1>Review <?= mp_h($listing['title']) ?></h1>

<form method="post" class="mp-card mp-form">
    <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
    <input type="hidden" name="listing_id" value="<?= $listingId ?>">

    <div class="mp-note">
        Marketplace reviews are available only to accounts with a delivered order item for this listing.
    </div>

    <label>
        Rating
        <select name="rating">
            <?php for ($rating = 5; $rating >= 1; $rating--): ?>
                <option value="<?= $rating ?>"
                    <?= (int)($existing['rating'] ?? 5) === $rating ? 'selected' : '' ?>>
                    <?= $rating ?> / 5
                </option>
            <?php endfor; ?>
        </select>
    </label>

    <label>
        Review title
        <input name="title"
               maxlength="120"
               required
               value="<?= mp_h($existing['title'] ?? '') ?>">
    </label>

    <label>
        Review
        <textarea name="body"
                  maxlength="10000"
                  required><?= mp_h($existing['body'] ?? '') ?></textarea>
    </label>

    <button class="mp-button mp-button-primary">Save Review</button>
</form>
<?php
mp_page_bottom();
$db->close();
