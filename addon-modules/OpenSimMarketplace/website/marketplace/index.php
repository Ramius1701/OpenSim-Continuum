<?php
declare(strict_types=1);

require_once __DIR__ . '/bootstrap.php';
require_once __DIR__ . '/layout.php';

$db = mp_db();
$service = mp_service($db);
$user = mp_current_user($db);

$query = trim((string)($_GET['q'] ?? ''));
$categoryId = max(0, (int)($_GET['category_id'] ?? 0));
$maturity = strtolower((string)($_GET['maturity'] ?? 'general'));

$listings = $service->publicListings(
    $query,
    $categoryId,
    $maturity
);

$categories = $service->categories();

mp_page_top('Browse', $user);
?>
<form method="get" class="mp-card mp-form">
    <div class="mp-row">
        <label style="flex:2">
            Search
            <input name="q"
                   value="<?= mp_h($query) ?>"
                   placeholder="Products, descriptions, keywords or stores">
        </label>

        <label style="flex:1">
            Category
            <select name="category_id">
                <option value="0">All categories</option>
                <?php foreach ($categories as $category): ?>
                    <option value="<?= (int)$category['id'] ?>"
                        <?= (int)$category['id'] === $categoryId ? 'selected' : '' ?>>
                        <?= mp_h($category['name']) ?>
                    </option>
                <?php endforeach; ?>
            </select>
        </label>

        <label style="flex:1">
            Show maturity through
            <select name="maturity">
                <?php foreach (['general'=>'General','moderate'=>'Moderate','adult'=>'Adult','all'=>'All'] as $value=>$label): ?>
                    <option value="<?= mp_h($value) ?>"
                        <?= $maturity === $value ? 'selected' : '' ?>>
                        <?= mp_h($label) ?>
                    </option>
                <?php endforeach; ?>
            </select>
        </label>
    </div>
    <div><button class="mp-button mp-button-primary">Search Marketplace</button></div>
</form>

<p class="mp-muted"><?= count($listings) ?> listing(s) found.</p>

<div class="mp-grid">
<?php foreach ($listings as $listing): ?>
    <article class="mp-card">
        <?php if (!empty($listing['primary_image_id'])): ?>
            <a href="/marketplace/item.php?slug=<?= rawurlencode($listing['slug']) ?>">
                <img class="mp-product-image"
                     src="/marketplace/image.php?id=<?= (int)$listing['primary_image_id'] ?>"
                     alt="<?= mp_h($listing['title']) ?>">
            </a>
        <?php endif; ?>

        <h2>
            <a href="/marketplace/item.php?slug=<?= rawurlencode($listing['slug']) ?>">
                <?= mp_h($listing['title']) ?>
            </a>
        </h2>

        <p><?= mp_h($listing['short_description']) ?></p>
        <p class="mp-price"><?= mp_money((int)$listing['price']) ?></p>
        <p class="mp-muted">
            <?= mp_h($listing['category_name']) ?> ·
            <?= mp_h(ucfirst($listing['maturity'])) ?> ·
            <?= mp_h(mp_rating((float)$listing['average_rating'])) ?>
            (<?= (int)$listing['review_count'] ?>)
        </p>
        <p>
            By <a href="/marketplace/seller.php?store=<?= rawurlencode($listing['store_slug']) ?>">
                <?= mp_h($listing['store_name']) ?>
            </a>
        </p>
    </article>
<?php endforeach; ?>
</div>
<?php
mp_page_bottom();
$db->close();
