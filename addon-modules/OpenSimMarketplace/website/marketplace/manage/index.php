<?php
declare(strict_types=1);

require_once dirname(__DIR__) . '/bootstrap.php';
require_once dirname(__DIR__) . '/layout.php';

$db = mp_db();
$service = mp_service($db);
$user = mp_require_user($db);
$sellerId = strtolower((string)$user['PrincipalID']);
$seller = $service->seller($sellerId);

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    try {
        mp_require_csrf();
        $action = (string)($_POST['action'] ?? '');

        if ($action === 'apply') {
            $service->applySeller(
                $user,
                (string)($_POST['store_name'] ?? ''),
                (string)($_POST['bio'] ?? '')
            );

            mp_flash('success', 'Marketplace merchant application saved.');
        } elseif ($action === 'init_outbox') {
            $service->initializeMerchantOutbox($sellerId);

            mp_flash(
                'success',
                'Merchant Outbox created or verified in your inventory.'
            );
        } elseif ($action === 'submit') {
            $service->submitListing(
                $sellerId,
                (int)($_POST['listing_id'] ?? 0)
            );

            mp_flash('success', 'Listing submitted for Marketplace review.');
        } elseif ($action === 'archive') {
            $service->archiveListing(
                $sellerId,
                (int)($_POST['listing_id'] ?? 0)
            );

            mp_flash('success', 'Marketplace listing archived.');
        } elseif ($action === 'test') {
            $service->testDelivery(
                $sellerId,
                (int)($_POST['listing_id'] ?? 0)
            );

            mp_flash(
                'success',
                'Test delivery sent to your OpenSim Marketplace / Received Items folder.'
            );
        }

        mp_redirect('/marketplace/manage/');
    } catch (Throwable $e) {
        mp_flash('error', $e->getMessage());
        mp_redirect('/marketplace/manage/');
    }
}

$seller = $service->seller($sellerId);
$inventory = null;
$listings = [];

if ($seller && $seller['status'] === 'approved') {
    try {
        $inventory = $service->merchantInventory($sellerId);
    } catch (Throwable $e) {
        mp_flash('error', $e->getMessage());
    }

    $listings = $service->sellerListings($sellerId);
}

mp_page_top('Merchant Center', $user);
?>
<h1>Marketplace Merchant Center</h1>

<?php if (!$seller): ?>
    <form method="post" class="mp-card mp-form">
        <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
        <input type="hidden" name="action" value="apply">

        <h2>Apply to sell on OpenSim Marketplace</h2>

        <label>
            Store name
            <input name="store_name"
                   maxlength="120"
                   value="<?= mp_h(mp_user_name($user) . "'s Store") ?>">
        </label>

        <label>
            Store description
            <textarea name="bio" maxlength="10000"></textarea>
        </label>

        <button class="mp-button mp-button-primary">Submit Merchant Application</button>
    </form>
<?php else: ?>
    <section class="mp-card">
        <div class="mp-row mp-between">
            <div>
                <h2><?= mp_h($seller['store_name']) ?></h2>
                <div class="mp-muted"><?= mp_h($seller['store_slug']) ?></div>
            </div>
            <?= mp_status_badge((string)$seller['status']) ?>
        </div>
        <div style="white-space:pre-wrap"><?= mp_h($seller['bio']) ?></div>
    </section>

    <?php if ($seller['status'] !== 'approved'): ?>
        <div class="mp-note" style="margin-top:1rem">
            Merchant inventory and listing tools become available after Marketplace staff approval.
        </div>
    <?php else: ?>
        <div class="mp-row" style="margin:1rem 0">
            <form method="post">
                <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                <input type="hidden" name="action" value="init_outbox">
                <button class="mp-button">Create / Verify Merchant Outbox</button>
            </form>

            <a class="mp-button mp-button-primary" href="/marketplace/manage/listing_edit.php">
                Create Listing
            </a>

            <a class="mp-button" href="/marketplace/manage/sales.php">
                Sales & Earnings
            </a>

            <a class="mp-button"
               href="/marketplace/seller.php?store=<?= rawurlencode($seller['store_slug']) ?>">
                View Store
            </a>
        </div>

        <section class="mp-card">
            <h2>Merchant Outbox</h2>
            <p>
                In your viewer inventory, use
                <strong>OpenSim Marketplace / Merchant Outbox</strong>.
                Each top-level product folder is one Marketplace product source.
            </p>

            <?php if ($inventory): ?>
                <p class="mp-muted">
                    Outbox UUID:
                    <span class="mp-source"><?= mp_h($inventory['outbox_folder_id']) ?></span>
                </p>

                <div class="mp-table-wrap">
                    <table class="mp-table">
                        <thead>
                        <tr>
                            <th>Product folder</th>
                            <th>Inventory</th>
                            <th>Permissions</th>
                            <th>Validation</th>
                        </tr>
                        </thead>
                        <tbody>
                        <?php foreach (($inventory['products'] ?? []) as $product): ?>
                            <tr>
                                <td>
                                    <strong><?= mp_h($product['name'] ?? '') ?></strong>
                                    <div class="mp-source"><?= mp_h($product['folder_id'] ?? '') ?></div>
                                </td>
                                <td>
                                    <?= (int)($product['item_count'] ?? 0) ?> item(s)<br>
                                    <?= (int)($product['folder_count'] ?? 0) ?> folder(s)
                                </td>
                                <td>
                                    Copy <?= !empty($product['copy']) ? 'Yes' : 'No' ?> ·
                                    Transfer <?= !empty($product['transfer']) ? 'Yes' : 'No' ?> ·
                                    Modify <?= !empty($product['modify']) ? 'Yes' : 'No' ?>
                                </td>
                                <td><?= mp_h($product['message'] ?? '') ?></td>
                            </tr>
                        <?php endforeach; ?>
                        </tbody>
                    </table>
                </div>
            <?php endif; ?>
        </section>

        <h2>Your Listings</h2>

        <div class="mp-table-wrap mp-card">
            <table class="mp-table">
                <thead>
                <tr>
                    <th>Listing</th>
                    <th>Status</th>
                    <th>Price</th>
                    <th>Inventory Version</th>
                    <th>Sold / Reserved</th>
                    <th>Actions</th>
                </tr>
                </thead>
                <tbody>
                <?php if (!$listings): ?>
                    <tr><td colspan="6">No listings yet.</td></tr>
                <?php endif; ?>

                <?php foreach ($listings as $listing): ?>
                    <tr>
                        <td>
                            <strong><?= mp_h($listing['title']) ?></strong>
                            <div class="mp-source"><?= mp_h($listing['source_folder_id']) ?></div>
                            <?php if (!empty($listing['rejection_reason'])): ?>
                                <div class="mp-error"><?= mp_h($listing['rejection_reason']) ?></div>
                            <?php endif; ?>
                        </td>
                        <td><?= mp_status_badge((string)$listing['status']) ?></td>
                        <td><?= mp_money((int)$listing['price']) ?></td>
                        <td>
                            <?= !empty($listing['version_uuid']) ? mp_h($listing['version_uuid']) : 'Not published' ?>
                        </td>
                        <td>
                            <?= (int)$listing['sold_count'] ?> /
                            <?= (int)$listing['reserved_count'] ?>
                        </td>
                        <td>
                            <div class="mp-actions">
                                <?php if ($listing['status'] !== 'archived'): ?>
                                    <a class="mp-button"
                                       href="/marketplace/manage/listing_edit.php?id=<?= (int)$listing['id'] ?>">
                                        Edit
                                    </a>
                                <?php endif; ?>

                                <?php if (in_array($listing['status'], ['draft','rejected'], true)): ?>
                                    <form method="post">
                                        <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                                        <input type="hidden" name="action" value="submit">
                                        <input type="hidden" name="listing_id" value="<?= (int)$listing['id'] ?>">
                                        <button class="mp-button">Submit</button>
                                    </form>
                                <?php endif; ?>

                                <?php if ($listing['status'] === 'published'): ?>
                                    <form method="post">
                                        <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                                        <input type="hidden" name="action" value="test">
                                        <input type="hidden" name="listing_id" value="<?= (int)$listing['id'] ?>">
                                        <button class="mp-button">Test Delivery</button>
                                    </form>
                                <?php endif; ?>

                                <?php if ($listing['status'] !== 'archived'): ?>
                                    <form method="post">
                                        <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                                        <input type="hidden" name="action" value="archive">
                                        <input type="hidden" name="listing_id" value="<?= (int)$listing['id'] ?>">
                                        <button class="mp-button mp-button-danger">Archive</button>
                                    </form>
                                <?php endif; ?>
                            </div>
                        </td>
                    </tr>
                <?php endforeach; ?>
                </tbody>
            </table>
        </div>
    <?php endif; ?>
<?php endif; ?>

<?php
mp_page_bottom();
$db->close();
