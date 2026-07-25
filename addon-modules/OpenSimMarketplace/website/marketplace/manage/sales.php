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

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    try {
        mp_require_csrf();

        if ((string)($_POST['action'] ?? '') === 'respond_review') {
            $service->sellerRespondReview(
                $sellerId,
                (int)($_POST['review_id'] ?? 0),
                (string)($_POST['response'] ?? '')
            );

            mp_flash('success', 'Merchant review response saved.');
        }

        mp_redirect('/marketplace/manage/sales.php');
    } catch (Throwable $e) {
        mp_flash('error', $e->getMessage());
        mp_redirect('/marketplace/manage/sales.php');
    }
}

$sales = $service->sellerSales($sellerId);
$summary = mp_stmt_row(
    $db,
    'SELECT
         COALESCE(SUM(gross_amount), 0) AS gross,
         COALESCE(SUM(fee_amount), 0) AS fees,
         COALESCE(SUM(net_amount), 0) AS net,
         COALESCE(SUM(IF(settlement_status = "unsettled", net_amount, 0)), 0) AS unsettled
     FROM ws_market_seller_ledger
     WHERE seller_id = ?',
    's',
    [$sellerId]
) ?? [];

$reviews = mp_stmt_rows(
    $db,
    'SELECT
         r.*,
         l.title AS listing_title,
         CONCAT(u.FirstName, " ", u.LastName) AS buyer_name
     FROM ws_market_reviews r
     JOIN ws_market_listings l ON l.id = r.listing_id
     LEFT JOIN UserAccounts u ON u.PrincipalID = r.buyer_id
     WHERE l.seller_id = ?
       AND r.status = "published"
     ORDER BY r.created_at DESC
     LIMIT 200',
    's',
    [$sellerId]
);

mp_page_top('Sales & Earnings', $user);
?>
<h1>Sales & Earnings</h1>

<div class="mp-stat-grid">
    <div class="mp-stat"><span>Gross sales</span><strong><?= mp_money((int)($summary['gross'] ?? 0)) ?></strong></div>
    <div class="mp-stat"><span>Marketplace fees</span><strong><?= mp_money((int)($summary['fees'] ?? 0)) ?></strong></div>
    <div class="mp-stat"><span>Seller net</span><strong><?= mp_money((int)($summary['net'] ?? 0)) ?></strong></div>
    <div class="mp-stat"><span>Unsettled ledger</span><strong><?= mp_money((int)($summary['unsettled'] ?? 0)) ?></strong></div>
</div>

<div class="mp-note" style="margin:1rem 0">
    The seller ledger records marketplace earnings after successful inventory delivery.
    Payment settlement is separate and remains manual until a grid economy provider adapter is enabled.
</div>

<div class="mp-table-wrap mp-card">
    <table class="mp-table">
        <thead>
        <tr>
            <th>Date</th>
            <th>Order</th>
            <th>Product</th>
            <th>Recipient</th>
            <th>Gross</th>
            <th>Fee</th>
            <th>Net</th>
            <th>Settlement</th>
        </tr>
        </thead>
        <tbody>
        <?php foreach ($sales as $sale): ?>
            <tr>
                <td><?= mp_h($sale['created_at']) ?></td>
                <td class="mp-source"><?= mp_h($sale['order_uuid']) ?></td>
                <td><?= mp_h($sale['title']) ?></td>
                <td class="mp-source"><?= mp_h($sale['recipient_id']) ?></td>
                <td><?= mp_money((int)$sale['gross_amount']) ?></td>
                <td><?= mp_money((int)$sale['fee_amount']) ?></td>
                <td><?= mp_money((int)$sale['net_amount']) ?></td>
                <td><?= mp_status_badge((string)$sale['settlement_status']) ?></td>
            </tr>
        <?php endforeach; ?>
        </tbody>
    </table>
</div>

<h2>Product Reviews</h2>

<?php foreach ($reviews as $review): ?>
    <section class="mp-card" style="margin-bottom:1rem">
        <div class="mp-row mp-between">
            <strong><?= mp_h($review['listing_title']) ?> — <?= mp_h($review['title']) ?></strong>
            <span><?= (int)$review['rating'] ?> / 5</span>
        </div>
        <p class="mp-muted"><?= mp_h($review['buyer_name'] ?: 'Grid Resident') ?></p>
        <div style="white-space:pre-wrap"><?= mp_h($review['body']) ?></div>

        <?php if (!empty($review['seller_response'])): ?>
            <div class="mp-note" style="margin-top:.75rem">
                <strong>Your response</strong>
                <div style="white-space:pre-wrap"><?= mp_h($review['seller_response']) ?></div>
            </div>
        <?php else: ?>
            <form method="post" class="mp-form" style="margin-top:.75rem">
                <input type="hidden" name="csrf" value="<?= mp_h(mp_csrf_token()) ?>">
                <input type="hidden" name="action" value="respond_review">
                <input type="hidden" name="review_id" value="<?= (int)$review['id'] ?>">
                <label>
                    Merchant response
                    <textarea name="response" maxlength="10000" required></textarea>
                </label>
                <button class="mp-button">Post Merchant Response</button>
            </form>
        <?php endif; ?>
    </section>
<?php endforeach; ?>

<?php
mp_page_bottom();
$db->close();
