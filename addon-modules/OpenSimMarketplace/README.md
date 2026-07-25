# OpenSimMarketplace v2.1.0

Direct Delivery marketplace add-on for current OpenSimulator development builds.

## Purpose

This module is intentionally an add-on module.

Marketplace is a grid/website feature that can use current OpenSim inventory and account
services without changing OpenSim core protocols or adding a new Robust core service.

The module provides protected server-side inventory operations. The bundled PHP portal
provides catalogue, seller, order, review, payment-state and administrative workflows.

## Architecture

```text
Merchant viewer inventory
        |
        v
OpenSim Marketplace
└── Merchant Outbox
    ├── Product Folder A
    ├── Product Folder B
    └── Product Folder C
        |
        | seller associates one top-level folder with a listing
        v
Marketplace admin publication
        |
        +-- revalidate source tree
        +-- reject inventory links
        +-- require current-owner Copy + Transfer
        +-- calculate source fingerprint
        |
        v
Marketplace service account
└── Marketplace Inventory
    └── <seller UUID>
        └── immutable deterministic listing-version snapshot
                |
                +-- snapshot fingerprint stored with listing version
                |
                v
Approved/free order
        |
        +-- deterministic delivery ID
        +-- snapshot UUID + fingerprint verification
        +-- next-owner permission propagation
        |
        v
Recipient inventory
└── OpenSim Marketplace
    └── Received Items
        └── Product Folder
```

There is no Magic Box, rezzed warehouse object, simulator object URL, product-name
lookup, or LSL delivery server.

## Service account

`ServiceAccountUUID` must identify a normal local grid account with a valid inventory.

The service account owns immutable published Marketplace snapshots.

Operational rules:

- use a dedicated Marketplace account;
- give it a strong random password;
- do not use the account as a normal resident/avatar;
- do not reorganize or edit its `Marketplace Inventory` tree in a viewer;
- exclude it from merchant approval;
- back up its inventory through the same procedures used for other service accounts.

The module blocks the service account from acting as a seller and from receiving
customer deliveries.

## Seller inventory

The module creates:

```text
OpenSim Marketplace
└── Merchant Outbox
```

Each top-level folder beneath `Merchant Outbox` is one product source.

Nested folders are supported.

Inventory links and link folders are rejected.

Every source item must currently have owner Copy and Transfer permissions. This is
required so the Marketplace can create a reliable service-account snapshot and perform
repeated original/redelivery operations.

The buyer's permissions are still derived from the inventory item's next-owner
permissions.

For example, a source item may be:

```text
merchant current permissions: Copy + Modify + Transfer
next owner permissions:       Modify + Transfer
```

The published snapshot remains delivery-capable while the recipient copy becomes
no-copy according to next-owner permissions.

## Immutable listing versions

Publication creates a new version UUID.

Snapshot folder/item UUIDs are deterministic from:

```text
OpenSimMarketplace.v2
operation purpose
version UUID
source inventory node UUID
```

The version record stores:

- source folder UUID;
- source SHA-256 tree fingerprint;
- snapshot folder UUID;
- snapshot SHA-256 tree fingerprint;
- source product name/description;
- item/folder counts;
- permission summary.

Every delivery supplies the expected snapshot fingerprint.

If the Marketplace service-account inventory tree no longer matches the published
version, delivery is refused.

This prevents a changed service snapshot from silently changing an already published
listing version or redelivery.

## Direct Delivery endpoints

All endpoints are POST-only JSON and protected by OpenSim
`BasicHttpAuthentication`.

```text
POST /opensim/marketplace/v2/inventory
POST /opensim/marketplace/v2/inspect
POST /opensim/marketplace/v2/snapshot
POST /opensim/marketplace/v2/deliver
```

### Inventory

Creates/verifies the Marketplace seller folders and returns the validated top-level
Merchant Outbox products.

### Inspect

Validates one top-level Merchant Outbox folder and returns its fingerprint, counts and
permission summary.

### Snapshot

Copies one validated seller product into the Marketplace service-account inventory using
deterministic node UUIDs and returns both source and stored-snapshot fingerprints.

### Deliver

Validates:

- seller UUID;
- service-account snapshot location;
- snapshot folder UUID;
- expected snapshot fingerprint;
- local recipient account;
- delivery ID binding.

It then copies the immutable snapshot into the recipient's Received Items folder and
applies next-owner permissions.

## Idempotent delivery

Delivery IDs are stable for original orders:

```text
market-<order UUID>-<order item ID>
```

The module writes:

```text
Data/OpenSimMarketplace/marketplace-deliveries-v2.jsonl
```

The durable JSON Lines receipt binds:

- delivery ID;
- seller UUID;
- snapshot folder UUID;
- snapshot fingerprint;
- recipient UUID;
- destination folder UUID;
- delivered folder/item counts.

If PHP retries an already completed delivery with the same binding, the module returns
the recorded receipt instead of creating another copy.

If inventory copy completes but the receipt write fails, retry uses the same deterministic
destination node UUIDs, verifies/reuses the copied nodes, and retries the receipt write.

## Permission handling

The recipient copy applies next-owner permission propagation modelled on the current
OpenSim `Scene.GiveInventoryItem` path.

The Marketplace module does not grant Copy, Modify or Transfer beyond the source item's
next-owner permission intent.

Inventory links are not supported in Marketplace products.

## Local-grid delivery

Marketplace v2.0 delivers only to local grid accounts.

Hypergrid inventory delivery is deliberately out of scope for this version because
foreign inventory/asset routing has different ownership and asset-transfer boundaries.

Gifts therefore require a local grid recipient. Checkout accepts an exact local avatar name or account UUID.

## Website workflow

### Residents

- browse/search catalogue;
- filter category and maturity;
- view images and seller storefront;
- cart up to the configured item limit;
- purchase for self or gift to a local account;
- free-order immediate delivery;
- order history;
- eligible self-redelivery;
- verified-purchase reviews.

### Merchants

- apply for seller status;
- create/verify Merchant Outbox;
- synchronize product folders;
- associate a product folder with a listing;
- title, short/full description, keywords and category;
- price;
- General/Moderate/Adult maturity;
- optional quantity limit;
- redelivery policy;
- up to eight securely stored product images by default;
- submit for publication;
- test delivery of a published version;
- sales/fee/net earnings statement;
- merchant responses to reviews.

### Marketplace staff

- approve/suspend/reject merchants;
- review pending listings and images;
- revalidate, snapshot and publish;
- reject with seller-visible reason;
- manually verify paid orders;
- retry delivery;
- cancel eligible undelivered orders.

## Images

Product images are stored outside the web document root in:

`MP_IMAGE_STORAGE_ROOT`

Uploads are checked with:

- `is_uploaded_file`;
- byte-size limit;
- `finfo` MIME detection;
- JPEG/PNG/WebP/GIF allowlist;
- `getimagesize` decode/dimension validation;
- random storage filename.

The browser receives images through:

```text
/marketplace/image.php?id=<database image id>
```

The handler uses stored database metadata and `basename()`; it does not accept a file
path or URL from the request.

## Payments

Marketplace v2.0 does not directly update an economy/balance table.

The order state machine and seller ledger are complete, but the default paid provider is:

```php
define('MP_PAYMENT_PROVIDER', 'manual');
```

Free orders are approved and delivered immediately.

Paid orders become `payment_pending`. Marketplace staff verify the actual payment and
approve the order, after which Direct Delivery is attempted immediately.

A payment provider adapter can later replace the manual provider without changing
inventory snapshots, orders, order items, deliveries, reviews or seller ledger.

No public payment webhook is shipped in v2.0. A provider endpoint will be added only with a real, tested economy adapter.

## Database

Import explicitly:

```text
website/database/20260715_opensim_marketplace_v2.sql
```

The migration refuses to run over the unpublished v1 warehouse-object prototype schema
when it detects the old `ws_market_listings.object_id` column. Back up and remove the
v1 `ws_market_*` prototype tables before importing v2.

PHP never creates or alters Marketplace tables at runtime.

Major tables:

- `ws_market_sellers`
- `ws_market_listings`
- `ws_market_listing_versions`
- `ws_market_listing_images`
- `ws_market_orders`
- `ws_market_order_items`
- `ws_market_deliveries`
- `ws_market_payments`
- `ws_market_seller_ledger`
- `ws_market_reviews`
- `ws_market_audit`

## Skidz Parts Exchange donor review

`Skidz Parts Exchange 1.0` was useful as a historical Marketplace feature checklist.

It demonstrated:

- seller inventory registration;
- listing association;
- test delivery;
- gifting;
- sales history;
- quantity;
- adult/maturity classification;
- product images;
- ratings/comments;
- wishlists;
- commissions;
- storefront workflow.

Its delivery/payment architecture is not used.

The historical Exchange:

- depended on in-world Magic Box HTTP URLs;
- matched inventory products by product name;
- posted avatar UUID and product name to an LSL object;
- could fall back to script email delivery;
- directly changed PHP account balance fields with old SQL code.

OpenSim Marketplace v2 retains the useful commerce workflow ideas and replaces the
Magic Box/payment core with protected OpenSim inventory service operations, immutable
listing snapshots, idempotent delivery receipts and an explicit payment-provider
boundary.

## Deferred Marketplace features

Not included in v2.0:

- wishlists;
- store managers;
- revenue distributions/split payouts;
- demo associations;
- related items;
- paid listing enhancements;
- bulk product update/redelivery;
- Hypergrid recipient delivery.

These are later Marketplace features, not prerequisites for the Direct Delivery
foundation.

## Runtime checkpoint

Before merge:

1. compile the module against the target OpenSimulator build;
2. create a dedicated local Marketplace service account;
3. configure one stable Marketplace service region process;
4. enable Basic authentication with a long random secret;
5. import the v2 schema;
6. create `include/marketplace_env.php`;
7. approve one test merchant;
8. initialize the merchant's Merchant Outbox;
9. create a nested test product with varied next-owner permissions;
10. verify no-copy or no-transfer current-owner source items are rejected;
11. publish and confirm source/snapshot fingerprints are stored;
12. verify the service-account snapshot exists;
13. place one free self-order;
14. confirm folder delivery under Received Items;
15. retry the same delivery ID and confirm no duplicate;
16. perform buyer self-redelivery and confirm a new redelivery copy;
17. place one gifted order to a local account;
18. verify quantity reservation/sold counts;
19. verify a delivered buyer can review and another account cannot;
20. create one paid manual order, approve it as admin, and confirm delivery;
21. alter a test snapshot directly in the service account and confirm fingerprint
    mismatch blocks delivery;
22. check `marketplace-deliveries-v2.jsonl`;
23. run a full OpenSim application build and check `git status --short`.

Do not merge until this focused checkpoint passes.
