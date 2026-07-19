<?php
declare(strict_types=1);

require_once __DIR__ . '/bootstrap.php';
require_once __DIR__ . '/layout.php';

$db = mp_db();
$service = mp_service($db);
$user = mp_current_user($db);

$slug = trim((string)($_GET['slug'] ?? ''));
$listing = $service->publicListingBySlug($slug);

if (!$listing) {
    http_response_code(404);
    exit('Marketplace listing not found.');
}

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    try {
        mp_require_csrf();
        $action = (string)($_POST['action'] ?? '');

        if ($action === 'add_cart') {
            $ids = mp_cart_ids();

            if (!in_array((int)$listing['id'], $ids, true)) {
                $ids[] = (int)$listing['id'];
            }

            $maximum = defined('MP_MAX_CART_ITEMS')
                ? (int)MP_MAX_CART_ITEMS
                : 10;

            if (count($ids) > $maximum) {
                throw new RuntimeException(
                    'The Marketplace cart is limited to ' .
                    $maximum .
                    ' products.'
                );
            }

            mp_cart_save($ids);
            mp_flash('success', 'Product added to your Marketplace cart.');
            mp_redirect('/marketplace/cart.php');
        }
    } catch (Throwable $e) {
        mp_flash('error', $e->getMessage());
        mp_redirect('/marketplace/item.php?slug=' . rawurlencode($slug));
    }
}

$images = $service->listingImages((int)$listing['id']);
$reviews = $service->reviews((int)$listing['id']);

mp_page_top((string)$listing['title'], $user);
?>
<div class="mp-hero">
    <div class="mp-gallery">
        <?php foreach ($images as $image): ?>
            <img src="/marketplace/image.php?id=<?= (int)$image['id'] ?>"
                 alt="<?= mp_h($image['alt_text'] ?: $listing['title']) ?>">
        <?php endforeach; ?>
    </div>

    <section>
        <div class="mp-row mp-between">
            <span class="mp-badge"><?= mp_h(ucfirst($listing['maturity'])) ?></span>
            <span><?= mp_h($listing['category_name']) ?></span>
        </div>

        <h1><?= mp_h($listing['title']) ?></h1>
        <p><?= mp_h($listing['short_description']) ?></p>
        <p class="mp-price"><?= mp_money((int)$listing['price']) ?></p>

        <?php if ($listing['quantity_limit'] !== null): ?>
            <p class="mp-muted">
                <?= max(0, (int)$listing['quantity_limit'] - (int)$listing['sold_count'] - (int)$listing['reserved_count']) ?>
                available
            </p>
        <?php endif; ?>

        <p>
            Sold by
            <a href="/marketplace/seller.php?store=<?= rawurlencode($listing['store_slug']) ?>">
                <?= mp_h($listing['store_name']) ?>
            </a>
        </p>

        <p>
            <?= mp_h(mp_rating((float)$listing['average_rating'])) ?>
            from <?= (int)$listing['review_count'] ?> verified-purchase review(s)
        </p>

        <form method="post">
            <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
            <input type="hidden" name="action" value="add_cart">
            <button class="mp-button mp-button-primary">Add to Cart</button>
        </form>

        <div class="mp-note" style="margin-top:1rem">
            This product is delivered as an inventory folder to
            <strong>OpenSim Marketplace / Received Items</strong>.
            <?= !empty($listing['redelivery_enabled']) ? 'Buyer redelivery is enabled.' : 'Buyer redelivery is disabled for this listing.' ?>
        </div>
    </section>
</div>

<section class="mp-card" style="margin-top:1.5rem">
    <h2>Product details</h2>
    <div style="white-space:pre-wrap"><?= mp_h($listing['description']) ?></div>
    <p class="mp-muted">
        Product version <?= mp_h($listing['version_uuid']) ?> ·
        <?= (int)$listing['item_count'] ?> inventory item(s) ·
        <?= (int)$listing['folder_count'] ?> folder(s)
    </p>
</section>

<section class="mp-card" style="margin-top:1.5rem">
    <h2>Verified-purchase reviews</h2>

    <?php if (!$reviews): ?>
        <p class="mp-muted">No reviews yet.</p>
    <?php endif; ?>

    <?php foreach ($reviews as $review): ?>
        <article class="mp-review">
            <div class="mp-row mp-between">
                <strong><?= mp_h($review['title']) ?></strong>
                <span><?= (int)$review['rating'] ?> / 5</span>
            </div>
            <p class="mp-muted">
                <?= mp_h($review['buyer_name'] ?: 'Grid Resident') ?> ·
                <?= mp_h($review['created_at']) ?>
            </p>
            <div style="white-space:pre-wrap"><?= mp_h($review['body']) ?></div>

            <?php if (!empty($review['seller_response'])): ?>
                <div class="mp-note" style="margin-top:.75rem">
                    <strong>Merchant response</strong>
                    <div style="white-space:pre-wrap"><?= mp_h($review['seller_response']) ?></div>
                </div>
            <?php endif; ?>
        </article>
    <?php endforeach; ?>
</section>
<?php
mp_page_bottom();
$db->close();
