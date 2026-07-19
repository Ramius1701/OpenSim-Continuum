<?php
declare(strict_types=1);

require_once __DIR__ . '/bootstrap.php';
require_once __DIR__ . '/layout.php';

$db = mp_db();
$service = mp_service($db);
$user = mp_current_user($db);

$store = trim((string)($_GET['store'] ?? ''));
$seller = $service->storefront($store);

if (!$seller) {
    http_response_code(404);
    exit('Marketplace store not found.');
}

mp_page_top((string)$seller['store_name'], $user);
?>
<section class="mp-card">
    <h1><?= mp_h($seller['store_name']) ?></h1>
    <p class="mp-muted">Merchant: <?= mp_h($seller['avatar_name']) ?></p>
    <div style="white-space:pre-wrap"><?= mp_h($seller['bio']) ?></div>
</section>

<h2>Store listings</h2>
<div class="mp-grid">
<?php foreach ($seller['listings'] as $listing): ?>
    <article class="mp-card">
        <?php if (!empty($listing['primary_image_id'])): ?>
            <img class="mp-product-image"
                 src="/marketplace/image.php?id=<?= (int)$listing['primary_image_id'] ?>"
                 alt="<?= mp_h($listing['title']) ?>">
        <?php endif; ?>
        <h3><a href="/marketplace/item.php?slug=<?= rawurlencode($listing['slug']) ?>"><?= mp_h($listing['title']) ?></a></h3>
        <p><?= mp_h($listing['short_description']) ?></p>
        <p class="mp-price"><?= mp_money((int)$listing['price']) ?></p>
    </article>
<?php endforeach; ?>
</div>
<?php
mp_page_bottom();
$db->close();
