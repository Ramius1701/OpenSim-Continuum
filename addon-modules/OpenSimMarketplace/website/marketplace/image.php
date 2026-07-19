<?php
declare(strict_types=1);

require_once __DIR__ . '/bootstrap.php';

$db = mp_db();
$service = mp_service($db);

$imageId = max(0, (int)($_GET['id'] ?? 0));
$image = $service->imageRecord($imageId);

if (!$image) {
    http_response_code(404);
    exit;
}

$path = $service->imagePath($image);

if (!is_file($path)) {
    http_response_code(404);
    exit;
}

$mime = (string)$image['mime_type'];
$allowed = [
    'image/jpeg',
    'image/png',
    'image/webp',
    'image/gif',
];

if (!in_array($mime, $allowed, true)) {
    http_response_code(415);
    exit;
}

header('Content-Type: ' . $mime);
header('Content-Length: ' . (string)filesize($path));
header('Cache-Control: public, max-age=86400');
header('X-Content-Type-Options: nosniff');
header('Content-Disposition: inline; filename="marketplace-image-' . $imageId . '"');

readfile($path);
$db->close();
