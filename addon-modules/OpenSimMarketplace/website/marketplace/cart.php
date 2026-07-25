<?php
declare(strict_types=1);

require_once __DIR__ . '/bootstrap.php';
require_once __DIR__ . '/layout.php';

$db = mp_db();
$service = mp_service($db);
$user = mp_current_user($db);

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    try {
        mp_require_csrf();
        $action = (string)($_POST['action'] ?? '');

        if ($action === 'remove') {
            $removeId = (int)($_POST['listing_id'] ?? 0);

            mp_cart_save(
                array_values(
                    array_filter(
                        mp_cart_ids(),
                        static fn (int $id): bool =>
                            $id !== $removeId
                    )
                )
            );

            mp_flash('success', 'Product removed from your Marketplace cart.');
            mp_redirect('/marketplace/cart.php');
        }

        if ($action === 'checkout') {
            $buyer = mp_require_user($db);
            $recipientId = strtolower(
                trim(
                    (string)(
                        $_POST['recipient_id'] ?? ''
                    )
                )
            );

            $giftMessage = (string)(
                $_POST['gift_message'] ?? ''
            );

            $order = $service->createOrder(
                $buyer,
                mp_cart_ids(),
                $recipientId,
                $giftMessage
            );

            mp_cart_save([]);

            mp_flash(
                'success',
                $order['status'] === 'delivered'
                    ? 'Marketplace order delivered.'
                    : 'Marketplace order created. Paid orders remain pending until payment is verified.'
            );

            mp_redirect(
                '/marketplace/orders.php?order=' .
                rawurlencode(
                    (string)$order['order_uuid']
                )
            );
        }
    } catch (Throwable $e) {
        mp_flash('error', $e->getMessage());
        mp_redirect('/marketplace/cart.php');
    }
}

$ids = mp_cart_ids();
$listings = $service->cartListings($ids);

$availableIds = array_map(
    static fn (array $row): int =>
        (int)$row['id'],
    $listings
);

if ($availableIds !== $ids) {
    mp_cart_save($availableIds);
}

$total = array_sum(
    array_map(
        static fn (array $row): int =>
            (int)$row['price'],
        $listings
    )
);

mp_page_top('Cart', $user);
?>
<h1>Marketplace Cart</h1>

<?php if (!$listings): ?>
    <div class="mp-card">
        <p>Your cart is empty.</p>
        <a class="mp-button mp-button-primary" href="/marketplace/">Browse Marketplace</a>
    </div>
<?php else: ?>
    <div class="mp-table-wrap mp-card">
        <table class="mp-table">
            <thead>
            <tr>
                <th>Product</th>
                <th>Store</th>
                <th>Price</th>
                <th></th>
            </tr>
            </thead>
            <tbody>
            <?php foreach ($listings as $listing): ?>
                <tr>
                    <td>
                        <a href="/marketplace/item.php?slug=<?= rawurlencode($listing['slug']) ?>">
                            <?= mp_h($listing['title']) ?>
                        </a>
                    </td>
                    <td><?= mp_h($listing['store_name']) ?></td>
                    <td><?= mp_money((int)$listing['price']) ?></td>
                    <td>
                        <form method="post">
                            <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                            <input type="hidden" name="action" value="remove">
                            <input type="hidden" name="listing_id" value="<?= (int)$listing['id'] ?>">
                            <button class="mp-button">Remove</button>
                        </form>
                    </td>
                </tr>
            <?php endforeach; ?>
            </tbody>
            <tfoot>
            <tr>
                <th colspan="2">Total</th>
                <th><?= mp_money($total) ?></th>
                <th></th>
            </tr>
            </tfoot>
        </table>
    </div>

    <?php if (!$user): ?>
        <div class="mp-note" style="margin-top:1rem">
            Sign in to your grid account to complete checkout.
        </div>
    <?php else: ?>
        <form method="post" class="mp-card mp-form" style="margin-top:1rem">
            <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
            <input type="hidden" name="action" value="checkout">

            <h2>Delivery</h2>
            <p>
                By default, the order is delivered to your account
                <strong><?= mp_h(mp_user_name($user)) ?></strong>.
            </p>

            <label>
                Gift recipient name or UUID
                <input name="recipient_id"
                       placeholder="Leave blank for yourself; otherwise use an exact local avatar name or UUID">
            </label>

            <label>
                Gift message
                <textarea name="gift_message"
                          maxlength="500"
                          placeholder="Optional order note for a gifted purchase"></textarea>
            </label>

            <div class="mp-note">
                Gift delivery currently supports local grid accounts only. Exact avatar names and UUIDs are accepted.
                Hypergrid inventory delivery is not enabled in Marketplace v2.0.
            </div>

            <button class="mp-button mp-button-primary">
                <?= $total === 0 ? 'Place Free Order' : 'Create Order for ' . mp_h(mp_money($total)) ?>
            </button>
        </form>
    <?php endif; ?>
<?php endif; ?>

<?php
mp_page_bottom();
$db->close();
