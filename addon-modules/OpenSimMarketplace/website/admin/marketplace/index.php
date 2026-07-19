<?php
declare(strict_types=1);

require_once dirname(__DIR__, 2) . '/marketplace/bootstrap.php';
require_once dirname(__DIR__, 2) . '/marketplace/layout.php';

$db = mp_db();
$service = mp_service($db);
$adminUser = mp_require_admin($db);
$adminId = strtolower((string)$adminUser['PrincipalID']);

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    try {
        mp_require_csrf();
        $action = (string)($_POST['action'] ?? '');

        if ($action === 'seller_status') {
            $service->setSellerStatus(
                $adminId,
                strtolower((string)($_POST['seller_id'] ?? '')),
                (string)($_POST['status'] ?? '')
            );

            mp_flash('success', 'Marketplace seller status updated.');
        } elseif ($action === 'publish') {
            $result = $service->publishListing(
                $adminId,
                (int)($_POST['listing_id'] ?? 0)
            );

            mp_flash(
                'success',
                'Listing published with immutable inventory version ' .
                (string)$result['version_uuid'] .
                '.'
            );
        } elseif ($action === 'reject') {
            $service->rejectListing(
                $adminId,
                (int)($_POST['listing_id'] ?? 0),
                (string)($_POST['reason'] ?? '')
            );

            mp_flash('success', 'Marketplace listing rejected with seller-visible reason.');
        } elseif ($action === 'approve_payment') {
            $service->approvePayment(
                $adminId,
                (int)($_POST['order_id'] ?? 0),
                (string)($_POST['reference'] ?? '')
            );

            mp_flash(
                'success',
                'Payment approved. Marketplace delivery was attempted immediately.'
            );
        } elseif ($action === 'retry_order') {
            $service->retryOrder(
                $adminId,
                (int)($_POST['order_id'] ?? 0)
            );

            mp_flash('success', 'Marketplace order delivery retried.');
        } elseif ($action === 'cancel_order') {
            $service->cancelOrder(
                $adminId,
                (int)($_POST['order_id'] ?? 0)
            );

            mp_flash('success', 'Marketplace order cancelled and undelivered stock reservations released.');
        }

        mp_redirect('/admin/marketplace/');
    } catch (Throwable $e) {
        mp_flash('error', $e->getMessage());
        mp_redirect('/admin/marketplace/');
    }
}

$dashboard = $service->adminDashboard();

$stats = [
    'published' => (int)(mp_stmt_row(
        $db,
        'SELECT COUNT(*) AS value FROM ws_market_listings WHERE status = "published"'
    )['value'] ?? 0),
    'pending' => (int)(mp_stmt_row(
        $db,
        'SELECT COUNT(*) AS value FROM ws_market_listings WHERE status = "pending"'
    )['value'] ?? 0),
    'delivered' => (int)(mp_stmt_row(
        $db,
        'SELECT COUNT(*) AS value FROM ws_market_orders WHERE status = "delivered"'
    )['value'] ?? 0),
    'sales' => (int)(mp_stmt_row(
        $db,
        'SELECT COALESCE(SUM(total_amount),0) AS value FROM ws_market_orders WHERE status = "delivered"'
    )['value'] ?? 0),
];

mp_page_top('Marketplace Administration', $adminUser);
?>
<h1>Marketplace Administration</h1>

<div class="mp-stat-grid">
    <div class="mp-stat"><span>Published listings</span><strong><?= $stats['published'] ?></strong></div>
    <div class="mp-stat"><span>Pending listings</span><strong><?= $stats['pending'] ?></strong></div>
    <div class="mp-stat"><span>Delivered orders</span><strong><?= $stats['delivered'] ?></strong></div>
    <div class="mp-stat"><span>Delivered order value</span><strong><?= mp_money($stats['sales']) ?></strong></div>
</div>

<h2>Merchant Applications & Status</h2>
<div class="mp-table-wrap mp-card">
    <table class="mp-table">
        <thead>
        <tr>
            <th>Merchant</th>
            <th>Store</th>
            <th>Status</th>
            <th>Commission</th>
            <th>Action</th>
        </tr>
        </thead>
        <tbody>
        <?php foreach ($dashboard['sellers'] as $seller): ?>
            <tr>
                <td>
                    <?= mp_h($seller['avatar_name'] ?: $seller['seller_id']) ?>
                    <div class="mp-source"><?= mp_h($seller['seller_id']) ?></div>
                </td>
                <td>
                    <strong><?= mp_h($seller['store_name']) ?></strong>
                    <div><?= mp_h($seller['bio']) ?></div>
                </td>
                <td><?= mp_status_badge((string)$seller['status']) ?></td>
                <td><?= number_format((int)$seller['commission_basis_points'] / 100, 2) ?>%</td>
                <td>
                    <form method="post" class="mp-row">
                        <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                        <input type="hidden" name="action" value="seller_status">
                        <input type="hidden" name="seller_id" value="<?= mp_h($seller['seller_id']) ?>">
                        <select name="status">
                            <?php foreach (['pending','approved','suspended','rejected'] as $status): ?>
                                <option value="<?= mp_h($status) ?>" <?= $seller['status'] === $status ? 'selected' : '' ?>>
                                    <?= mp_h(ucfirst($status)) ?>
                                </option>
                            <?php endforeach; ?>
                        </select>
                        <button class="mp-button">Save</button>
                    </form>
                </td>
            </tr>
        <?php endforeach; ?>
        </tbody>
    </table>
</div>

<h2>Listings Awaiting Publication</h2>
<?php if (!$dashboard['pending_listings']): ?>
    <div class="mp-card"><p>No pending listings.</p></div>
<?php endif; ?>

<?php foreach ($dashboard['pending_listings'] as $listing): ?>
    <?php
    $images = $service->listingImages((int)$listing['id']);
    ?>
    <section class="mp-card" style="margin-bottom:1rem">
        <div class="mp-row mp-between">
            <div>
                <h3><?= mp_h($listing['title']) ?></h3>
                <div class="mp-muted">
                    <?= mp_h($listing['seller_name'] ?: $listing['seller_id']) ?> ·
                    <?= mp_h($listing['store_name']) ?>
                </div>
            </div>
            <strong><?= mp_money((int)$listing['price']) ?></strong>
        </div>

        <p><?= mp_h($listing['short_description']) ?></p>
        <div style="white-space:pre-wrap"><?= mp_h($listing['description']) ?></div>

        <p>
            Maturity: <strong><?= mp_h(ucfirst($listing['maturity'])) ?></strong><br>
            Source folder:
            <span class="mp-source"><?= mp_h($listing['source_folder_id']) ?></span><br>
            Quantity:
            <?= $listing['quantity_limit'] === null ? 'Unlimited' : (int)$listing['quantity_limit'] ?>
        </p>

        <div class="mp-image-strip">
            <?php foreach ($images as $image): ?>
                <div class="mp-image-tile">
                    <img src="/marketplace/image.php?id=<?= (int)$image['id'] ?>"
                         alt="<?= mp_h($image['alt_text']) ?>">
                </div>
            <?php endforeach; ?>
        </div>

        <div class="mp-actions" style="margin-top:1rem">
            <form method="post">
                <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                <input type="hidden" name="action" value="publish">
                <input type="hidden" name="listing_id" value="<?= (int)$listing['id'] ?>">
                <button class="mp-button mp-button-primary">
                    Validate, Snapshot & Publish
                </button>
            </form>

            <form method="post" class="mp-row" style="flex:1">
                <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                <input type="hidden" name="action" value="reject">
                <input type="hidden" name="listing_id" value="<?= (int)$listing['id'] ?>">
                <input name="reason"
                       maxlength="1000"
                       required
                       placeholder="Seller-visible rejection reason"
                       style="flex:1">
                <button class="mp-button mp-button-danger">Reject</button>
            </form>
        </div>
    </section>
<?php endforeach; ?>

<h2>Orders Requiring Attention</h2>
<div class="mp-table-wrap mp-card">
    <table class="mp-table">
        <thead>
        <tr>
            <th>Order</th>
            <th>Buyer / Recipient</th>
            <th>Status</th>
            <th>Total</th>
            <th>Created</th>
            <th>Controls</th>
        </tr>
        </thead>
        <tbody>
        <?php if (!$dashboard['orders']): ?>
            <tr><td colspan="6">No Marketplace orders require staff attention.</td></tr>
        <?php endif; ?>

        <?php foreach ($dashboard['orders'] as $order): ?>
            <tr>
                <td class="mp-source"><?= mp_h($order['order_uuid']) ?></td>
                <td>
                    Buyer <span class="mp-source"><?= mp_h($order['buyer_id']) ?></span><br>
                    Recipient <span class="mp-source"><?= mp_h($order['recipient_id']) ?></span>
                </td>
                <td><?= mp_status_badge((string)$order['status']) ?></td>
                <td><?= mp_money((int)$order['total_amount']) ?></td>
                <td><?= mp_h($order['created_at']) ?></td>
                <td>
                    <div class="mp-actions">
                        <?php if ($order['status'] === 'payment_pending'): ?>
                            <form method="post" class="mp-row">
                                <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                                <input type="hidden" name="action" value="approve_payment">
                                <input type="hidden" name="order_id" value="<?= (int)$order['id'] ?>">
                                <input name="reference"
                                       maxlength="190"
                                       placeholder="Payment reference">
                                <button class="mp-button mp-button-primary">Approve Payment</button>
                            </form>
                        <?php endif; ?>

                        <?php if (in_array($order['status'], ['approved','delivering','delivery_failed'], true)): ?>
                            <form method="post">
                                <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                                <input type="hidden" name="action" value="retry_order">
                                <input type="hidden" name="order_id" value="<?= (int)$order['id'] ?>">
                                <button class="mp-button">Retry Delivery</button>
                            </form>
                        <?php endif; ?>

                        <?php if (in_array($order['status'], ['payment_pending','approved','delivery_failed'], true)): ?>
                            <form method="post">
                                <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                                <input type="hidden" name="action" value="cancel_order">
                                <input type="hidden" name="order_id" value="<?= (int)$order['id'] ?>">
                                <button class="mp-button mp-button-danger">Cancel</button>
                            </form>
                        <?php endif; ?>
                    </div>
                </td>
            </tr>
        <?php endforeach; ?>
        </tbody>
    </table>
</div>

<?php
mp_page_bottom();
$db->close();
