# Search donor-parity audit

## Lineage and authority

- Official OpenSim Dev supplies `BasicSearchModule`, map search and the current
  avatar-picker CAPS. These remain the baseline when no external directory
  service is selected.
- The recovered lickx archive at `6614599` contains the historical
  OpenSimSearch simulator module plus PHP/MySQL crawler, registration and query
  helpers.
- Tranquillity `develop` at
  `6180f4027e7e055360124112408286217137bf8e` carries the same OpenSimSearch
  protocol into its current addon tree and has a newer internal search data
  model. It confirms protocol continuity but not production readiness of the
  historical PHP service.
- WhiteCore `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` is behavioral evidence for
  a grid directory service, resident search, event notification, places and
  region-scoped map results, plus later integrated WebUI search.
- Continuum's tracked OpenSimSearch addon is a hardened viewer-side client. It
  is not a grid index or crawler.

## Current comparison

| Search surface | Current status | Decision |
|---|---|---|
| Avatar picker / people | Official OpenSim CAPS present | Retain official implementation; reconcile aliases and Display Names through the authenticated identity service rather than OpenSimSearch PHP. |
| Map region-name search | Official OpenSim module present | Already present; do not duplicate. |
| Places and popular places | Continuum OpenSimSearch client present | Protocol-compatible with lickx/Tranquillity; requires a trusted external backend. |
| Land sales and rentals | Client present | Retain with exact flag, price, area, maturity and paging tests. |
| Events and event details | Client present | Retain; the production service must own lifecycle, moderation, timezone and expiry. |
| Classifieds and details | Client present | Retain; verify maturity, paging, removal and fee-state consistency. |
| Map event/classified items | Client present | Retain and compare result coordinates/maturity with WhiteCore. |
| Direct places query | WhiteCore behavior not separately implemented by the addon | Determine viewer reachability on current Firestorm; port only if the existing official/addon paths do not answer it. |
| Event notifications | WhiteCore supports add/remove subscriptions | Genuinely missing from this addon lineage; service/data ownership and viewer delivery must be designed before porting. |
| Grid directory/index service | Missing from Continuum | Required for a self-contained grid search product; implement during OpenSim-Grid-Interface service phase. |
| Integrated WebUI search | Deferred | Port WhiteCore's actual `region_search`/`user_search` UI behavior and fill gaps with OpenSim-Grid-Interface. Do not create a second index. |

## Donor disposition

### Retain as an optional addon module

The current `OpenSimSearch` region module retains the established XML-RPC
method surface for places, popular places, land, events, classifieds, details
and map items. Continuum's timeout, HTTPS warning, concurrency bound, result
bound, lifecycle cleanup and off-client-thread execution are compatibility
hardening. They require tests but are preferable to the synchronous donor.

### Do not deploy from the recovered helpers

The lickx/Tranquillity PHP service is obsolete or unsuitable as a production
backend because it relies on unauthenticated simulator registration, public
DataSnapshot polling, cron, historical PHP assumptions and MySQL-only tables.
The recovered copy also lacks sufficiently clear licensing for redistribution
as a new Continuum service. It remains protocol and data-shape evidence only.

### Adapt from WhiteCore behavior

WhiteCore's `DirectoryService` model demonstrates a single grid authority for:

- people, places, popular places, land, events and classifieds;
- event and classified detail;
- region-scoped map results;
- event notification subscriptions; and
- WebUI queries using the same directory data.

WhiteCore's service registry and database layer are architecturally divergent.
Port behavior and HTML/UI assets later with provenance; do not copy the entire
service framework.

## Required production service boundary

The later OpenSim-Grid-Interface search service should:

- accept authenticated simulator publication rather than arbitrary public
  registration;
- consume explicit change events or bounded authenticated snapshots;
- support SQLite for standalone and MySQL/MariaDB plus PostgreSQL for grid mode;
- provide deterministic paging, maturity filtering and deletion/tombstones;
- enforce parcel visibility, resident privacy and moderation;
- expose one versioned API to the viewer adapter and integrated WebUI;
- avoid direct WebUI access to search tables; and
- define whether foreign Hypergrid destinations are excluded, linked or
  separately labelled.

## Compatibility and provenance

- **Robust:** grid indexing is a grid service; simulator addons are clients.
- **Databases:** the legacy donor is MySQL-only and cannot satisfy Continuum's
  required database support. New persistence must have provider parity.
- **Windows:** test service paths, Unicode, scheduling, restarts and proxy/TLS
  behavior on the supported Windows deployment.
- **Viewer:** test current Firestorm legacy directory and current search panels;
  no custom viewer should be necessary.
- **Hypergrid:** never leak private residents/parcels or trust foreign search
  results as local destinations without validation.
- **Licensing:** preserve donor notices for the simulator code. Treat the
  historical PHP/schema as audit evidence unless its redistribution license is
  established. WhiteCore-derived code/assets require its BSD notices.

## Required test gate

- Compare BasicSearch and OpenSimSearch selection; prove they cannot both own
  the same client callbacks.
- Test people/aliases/Display Names, places, popular, land sale/rent, events,
  classifieds, details and every supported map-item type.
- Verify paging, packet boundaries, maturity, price, area, timezone, traffic and
  global-coordinate calculations.
- Exercise Unicode, apostrophes, XML metacharacters, empty and oversized terms,
  malformed responses, timeouts, backend outage and concurrency saturation.
- Publish from two simulators, update and delete records, restart every process,
  and prove stale entries disappear deterministically.
- Verify hidden/private parcels and residents, banned content and moderator
  removal cannot reappear from an older snapshot.
- Test Hypergrid inclusion policy and reject invalid or untrusted destinations.
- During the WebUI phase, prove viewer and website results come from the same
  authority and enforce equivalent visibility rules.

The simulator addon is ready for controlled compatibility testing. A complete
self-hosted search product remains pending the authenticated directory service
and faithful WhiteCore/OpenSim-Grid-Interface WebUI integration.
