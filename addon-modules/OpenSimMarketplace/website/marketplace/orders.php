<?php
declare(strict_types=1);

require_once __DIR__ . '/bootstrap.php';
require_once __DIR__ . '/layout.php';

$db = mp_db();
$service = mp_service($db);
$user = mp_require_user($db);
$buyerId = strtolower((string)$user['PrincipalID']);

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    try {
        mp_require_csrf();
        $action = (string)($_POST['action'] ?? '');

        if ($action === 'redeliver') {
            $service->redeliver(
                $buyerId,
                (int)($_POST['order_item_id'] ?? 0)
            );

            mp_flash(
                'success',
                'Marketplace item redelivered to OpenSim Marketplace / Received Items.'
            );
        }

        mp_redirect('/marketplace/orders.php');
    } catch (Throwable $e) {
        mp_flash('error', $e->getMessage());
        mp_redirect('/marketplace/orders.php');
    }
}

$orders = $service->buyerOrders($buyerId);
$focus = trim((string)($_GET['order'] ?? ''));

mp_page_top('My Orders', $user);
?>
<h1>My Marketplace Orders</h1>

<?php if (!$orders): ?>
    <div class="mp-card">
        <p>No Marketplace orders yet.</p>
    </div>
<?php endif; ?>

<?php foreach ($orders as $order): ?>
    <section class="mp-card"
             style="margin-bottom:1rem;<?= $focus === $order['order_uuid'] ? 'outline:2px solid currentColor' : '' ?>">
        <div class="mp-row mp-between">
            <div>
                <strong>Order <?= mp_h($order['order_uuid']) ?></strong>
                <div class="mp-muted"><?= mp_h($order['created_at']) ?></div>
            </div>
            <div>
                <?= mp_status_badge((string)$order['status']) ?>
                <span class="mp-price"><?= mp_money((int)$order['total_amount']) ?></span>
            </div>
        </div>

        <p>
            Delivered to UUID:
            <span class="mp-source"><?= mp_h($order['recipient_id']) ?></span>
        </p>

        <?php if (!empty($order['gift_message'])): ?>
            <div class="mp-note"><?= mp_h($order['gift_message']) ?></div>
        <?php endif; ?>

        <div class="mp-table-wrap">
            <table class="mp-table">
                <thead>
                <tr>
                    <th>Item</th>
                    <th>Price</th>
                    <th>Delivery</th>
                    <th>Actions</th>
                </tr>
                </thead>
                <tbody>
                <?php foreach ($order['items'] as $item): ?>
                    <tr>
                        <td>
                            <a href="/marketplace/item.php?slug=<?= rawurlencode($item['slug']) ?>">
                                <?= mp_h($item['title']) ?>
                            </a>
                        </td>
                        <td><?= mp_money((int)$item['unit_price']) ?></td>
                        <td><?= mp_status_badge((string)$item['delivery_status']) ?></td>
                        <td>
                            <div class="mp-actions">
                                <?php if ($item['delivery_status'] === 'delivered' && !empty($item['redelivery_enabled'])): ?>
                                    <form method="post">
                                        <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                                        <input type="hidden" name="action" value="redeliver">
                                        <input type="hidden" name="order_item_id" value="<?= (int)$item['id'] ?>">
                                        <button class="mp-button">Redeliver</button>
                                    </form>
                                <?php endif; ?>

                                <?php if ($item['delivery_status'] === 'delivered'): ?>
                                    <a class="mp-button" href="/marketplace/review.php?listing_id=<?= (int)$item['listing_id'] ?>">
                                        Review
                                    </a>
                                <?php endif; ?>
                            </div>
                        </td>
                    </tr>
                <?php endforeach; ?>
                </tbody>
            </table>
        </div>
    </section>
<?php endforeach; ?>

<?php
mp_page_bottom();
$db->close();
