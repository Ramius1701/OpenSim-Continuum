<?php
declare(strict_types=1);

require_once dirname(__DIR__) . '/bootstrap.php';
require_once dirname(__DIR__) . '/layout.php';

$db = mp_db();
$service = mp_service($db);
$user = mp_require_user($db);
$sellerId = strtolower((string)$user['PrincipalID']);
$seller = $service->seller($sellerId);

if (!$seller || $seller['status'] !== 'approved') {
    http_response_code(403);
    exit('Approved Marketplace merchant status is required.');
}

$listingId = max(0, (int)($_GET['id'] ?? $_POST['listing_id'] ?? 0));
$listing = $listingId > 0
    ? $service->listingForSeller($sellerId, $listingId)
    : null;

if ($listingId > 0 && !$listing) {
    http_response_code(404);
    exit('Marketplace listing not found.');
}

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    try {
        mp_require_csrf();
        $action = (string)($_POST['action'] ?? 'save');

        if ($action === 'delete_image') {
            $service->deleteImage(
                $sellerId,
                (int)($_POST['image_id'] ?? 0)
            );

            mp_flash('success', 'Marketplace image removed.');
            mp_redirect('/marketplace/manage/listing_edit.php?id=' . $listingId);
        }

        if ($action === 'save') {
            $savedId = $service->saveListing(
                $user,
                $listingId > 0 ? $listingId : null,
                $_POST
            );

            if (!empty($_FILES['images'])) {
                $service->uploadImages(
                    $sellerId,
                    $savedId,
                    $_FILES['images']
                );
            }

            mp_flash(
                'success',
                'Listing draft saved and product folder revalidated.'
            );

            mp_redirect('/marketplace/manage/listing_edit.php?id=' . $savedId);
        }
    } catch (Throwable $e) {
        mp_flash('error', $e->getMessage());
        mp_redirect(
            '/marketplace/manage/listing_edit.php' .
            ($listingId > 0 ? '?id=' . $listingId : '')
        );
    }
}

$inventory = $service->merchantInventory($sellerId);
$categories = $service->categories();
$images = $listingId > 0
    ? $service->listingImages($listingId)
    : [];

mp_page_top($listing ? 'Edit Listing' : 'Create Listing', $user);
?>
<h1><?= $listing ? 'Edit Listing' : 'Create Marketplace Listing' ?></h1>

<div class="mp-note">
    A listing source is one top-level folder in
    <strong>OpenSim Marketplace / Merchant Outbox</strong>.
    Every inventory item in that product tree must currently be Copy + Transfer so the Marketplace can create and preserve an immutable published snapshot.
    Buyer permissions are still derived from each item's next-owner permissions.
</div>

<form method="post"
      enctype="multipart/form-data"
      class="mp-card mp-form"
      style="margin-top:1rem">
    <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
    <input type="hidden" name="action" value="save">
    <input type="hidden" name="listing_id" value="<?= $listingId ?>">

    <label>
        Merchant Outbox product folder
        <select name="source_folder_id" required>
            <option value="">Select a validated product folder</option>
            <?php foreach (($inventory['products'] ?? []) as $product): ?>
                <?php
                $ready = !empty($product['copy']) &&
                    !empty($product['transfer']) &&
                    !empty($product['fingerprint']);
                ?>
                <option value="<?= mp_h($product['folder_id'] ?? '') ?>"
                    <?= ($listing['source_folder_id'] ?? '') === ($product['folder_id'] ?? '') ? 'selected' : '' ?>
                    <?= $ready ? '' : 'disabled' ?>>
                    <?= mp_h($product['name'] ?? 'Unnamed Product') ?>
                    <?= $ready ? '' : ' — not delivery-ready' ?>
                </option>
            <?php endforeach; ?>
        </select>
    </label>

    <label>
        Product title
        <input name="title"
               maxlength="120"
               required
               value="<?= mp_h($listing['title'] ?? '') ?>">
    </label>

    <label>
        Short description
        <textarea name="short_description"
                  maxlength="500"
                  required><?= mp_h($listing['short_description'] ?? '') ?></textarea>
    </label>

    <label>
        Full description
        <textarea name="description"
                  maxlength="50000"
                  required><?= mp_h($listing['description'] ?? '') ?></textarea>
    </label>

    <label>
        Search keywords
        <input name="keywords"
               maxlength="500"
               value="<?= mp_h($listing['keywords'] ?? '') ?>"
               placeholder="comma separated terms residents may search">
    </label>

    <div class="mp-row">
        <label style="flex:1">
            Category
            <select name="category_id" required>
                <option value="">Select category</option>
                <?php foreach ($categories as $category): ?>
                    <option value="<?= (int)$category['id'] ?>"
                        <?= (int)($listing['category_id'] ?? 0) === (int)$category['id'] ? 'selected' : '' ?>>
                        <?= mp_h($category['name']) ?>
                    </option>
                <?php endforeach; ?>
            </select>
        </label>

        <label style="flex:1">
            Price
            <input type="number"
                   min="0"
                   name="price"
                   value="<?= (int)($listing['price'] ?? 0) ?>">
        </label>

        <label style="flex:1">
            Maturity
            <select name="maturity">
                <?php foreach (['general'=>'General','moderate'=>'Moderate','adult'=>'Adult'] as $value=>$label): ?>
                    <option value="<?= mp_h($value) ?>"
                        <?= ($listing['maturity'] ?? 'general') === $value ? 'selected' : '' ?>>
                        <?= mp_h($label) ?>
                    </option>
                <?php endforeach; ?>
            </select>
        </label>

        <label style="flex:1">
            Quantity limit
            <input type="number"
                   min="1"
                   name="quantity_limit"
                   value="<?= mp_h($listing['quantity_limit'] ?? '') ?>"
                   placeholder="Blank = unlimited">
        </label>
    </div>

    <label>
        <span>
            <input type="checkbox"
                   name="redelivery_enabled"
                   value="1"
                   <?= !isset($listing['redelivery_enabled']) || !empty($listing['redelivery_enabled']) ? 'checked' : '' ?>>
            Allow buyer self-redelivery from Order History
        </span>
    </label>

    <label>
        Product images
        <input type="file"
               name="images[]"
               accept="image/jpeg,image/png,image/webp,image/gif"
               multiple>
    </label>

    <p class="mp-muted">
        Up to <?= (int)(defined('MP_MAX_IMAGES_PER_LISTING') ? MP_MAX_IMAGES_PER_LISTING : 8) ?> images.
        Images are validated by MIME and decoded dimensions, then stored outside the web document root.
    </p>

    <button class="mp-button mp-button-primary">Save Draft</button>
</form>

<?php if ($listingId > 0): ?>
    <section class="mp-card" style="margin-top:1rem">
        <h2>Listing Images</h2>

        <div class="mp-image-strip">
            <?php foreach ($images as $image): ?>
                <div class="mp-image-tile">
                    <img src="/marketplace/image.php?id=<?= (int)$image['id'] ?>"
                         alt="<?= mp_h($image['alt_text']) ?>">
                    <form method="post">
                        <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                        <input type="hidden" name="action" value="delete_image">
                        <input type="hidden" name="listing_id" value="<?= $listingId ?>">
                        <input type="hidden" name="image_id" value="<?= (int)$image['id'] ?>">
                        <button class="mp-button mp-button-danger">Delete</button>
                    </form>
                </div>
            <?php endforeach; ?>
        </div>

        <?php if (!$images): ?>
            <p class="mp-muted">No images uploaded yet. At least one image is required before submission.</p>
        <?php endif; ?>
    </section>
<?php endif; ?>

<?php
mp_page_bottom();
$db->close();
