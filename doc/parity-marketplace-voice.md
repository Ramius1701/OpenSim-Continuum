# Marketplace and voice donor-parity audit

## OpenSimMarketplace

### Provenance and architecture

The checked-in recovery note identifies TASIA Marketplace and Skidz Parts
Exchange 1.0 as historical references, but does not record exact repository
commits. TASIA supplied a small protected delivery endpoint rather than a full
marketplace. Skidz supplied a historical XStreet/SLE-style feature set using
Magic Boxes, PHP-era SQL and direct balance manipulation.

Continuum Marketplace v2 is explicitly a new architecture: immutable inventory
snapshots, deterministic inventory IDs, a JSONL receipt ledger, a PHP/MySQL
catalogue and a manual payment-provider boundary. It is not a cherry-pick or
faithful donor port. Existing hardening improves this candidate but does not
establish functional donor parity.

### Classification and decision

- **Classification:** experimental optional addon module.
- **Current Dev equivalent:** none; official inventory services provide the
  extension seam, not a commerce product.
- **Genuinely implemented behavior:** merchant outbox publication, service-
  account snapshots, local direct delivery, gifting, redelivery, catalogue,
  cart, seller workflow, reviews, statements and administration.
- **Still deferred:** wishlists, managers, split payouts, demos, related items,
  paid enhancements, bulk update/redelivery and Hypergrid delivery.
- **Addon versus core:** addon only. Marketplace must not alter core inventory
  or economy persistence.
- **Robust:** use authenticated inventory/account services; a stable service
  region is an operational dependency in the current design.
- **MySQL:** the website schema is MySQL-specific. The simulator delivery ledger
  is JSONL. This does not meet the wider Continuum database parity expectation
  for a complete grid service and must be documented as an addon limitation.
- **Windows:** test durable paths, atomic recovery, locks and service restart.
- **Hypergrid:** v2 is local-account delivery only; retain that fail-closed
  boundary.
- **Viewer:** no custom viewer; delivery appears in received inventory.
- **Licensing/provenance:** exact TASIA/Skidz revisions and licenses must be
  recorded before claiming their code or assets were ported. Current v2 code
  requires its own explicit license inventory.
- **Recommendation:** retain disabled as an experimental controlled-test
  candidate. Do not call it a donor-complete production Marketplace.

### Required audit and runtime gate

1. Inventory every recovered TASIA/Skidz source, schema, template and asset;
   record hashes, license and feature behavior without copying secrets.
2. Map every donor feature to present, intentionally replaced, deferred or
   unsuitable; add exact provenance to the Marketplace documentation.
3. Verify publication snapshots and tree fingerprints for nested folders,
   every permission combination, links, missing assets and concurrent edits.
4. Test original delivery, duplicate retry, conflicting delivery ID, gift,
   redelivery and test delivery without duplicate or permission escalation.
5. Inject shutdown, disk-full, truncated ledger tail, inventory outage and
   route conflicts at every delivery transition.
6. Test authentication, TLS/proxy handling, request bounds, forged recipient,
   service-account compromise boundaries, CSRF and stored/reflected content.
7. Reconcile orders, deliveries, payments and seller ledger after every failure.
   No website operation may write an economy balance table directly.
8. Keep paid orders manual until an idempotent authenticated economy capture
   and refund contract is separately approved.

## WebRTC/Janus voice

### Lineage

Tranquillity records the original addon integration in `674c3a0424`, followed by
namespace, viewer-trust and async/runtime changes. The code was licensed for
OpenSimulator under the project BSD-style license. The addon is now also
present in official OpenSim Dev.

Continuum differs from current official Dev in seven files, adding 321 lines
and removing 80. The reviewed commits add session authorization, bounded Janus
completion, concurrent room preservation, shutdown cancellation, provisioning
hardening and session routing. These are candidate upstream-quality fixes, not
new voice capabilities.

### Functional reality

The donor README explicitly lists the current limitations:

- no true spatial audio through the Janus AudioBridge path;
- missing other-avatar voice presence indicators;
- no mute integration; and
- no per-avatar volume control.

Therefore this addon does not yet mimic Second Life voice behavior. Security
and lifecycle fixes are necessary, but cannot close those protocol/media gaps.

### Classification and decision

- **Official addon plus Continuum hardening:** upstream-quality bug-fix
  candidates; compare each delta with current official and Tranquillity code.
- **Janus voice product:** experimental feature requiring an external service.
- **Vivox and FreeSwitch modules:** already-present optional alternatives; do
  not combine multiple active voice authorities for one region/session.
- **Robust:** grid spatial/non-spatial sessions may route through an
  authenticated private Robust service.
- **Databases:** none required by the addon; Janus operational persistence is
  external.
- **Windows:** OpenSim components run on Windows, while the documented Janus
  deployment may require Docker/WSL. Treat that as an explicit dependency.
- **Hypergrid:** visited grid controls local voice rooms; group and peer voice
  across grid boundaries require an explicit federation/privacy design.
- **Viewer:** requires a WebRTC-capable viewer. Legacy voice paths must remain
  separately configurable.
- **Licensing:** BSD-style OpenSimulator-use grant recorded by Tranquillity;
  retain addon and Janus dependency notices.
- **Recommendation:** retain disabled by default and controlled-test the
  hardening. Do not market it as SL-equivalent spatial voice.

### Required test gate

- Authenticate a resident into the correct parcel/region room and reject
  spoofed agent, session, parcel, region and foreign-grid claims.
- Test local spatial, grid spatial, group and peer session routing separately.
- Create/join/leave many rooms concurrently; restart Janus, Robust, simulator
  and the full grid while calls are active.
- Exercise long-poll timeout/cancellation, dropped replies, duplicate Janus
  transactions, delayed completion and shutdown.
- Verify room/member cleanup on logout, teleport, crossing, parcel change,
  region removal and viewer crash.
- Test API/admin token secrecy, TLS, proxy boundaries, request bounds and rate
  limiting.
- Measure audio latency, dropouts and capacity; explicitly record lack of true
  spatialization, mute, volume and indicators for the tested viewer.
- Test both Windows-hosted OpenSim and the exact supported Janus deployment.

Marketplace and Janus voice are experimental products. Their presence and clean
build do not qualify either for production or SL-parity claims.
