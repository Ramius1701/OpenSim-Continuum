# OpenSimSearch

Continuum includes the OpenSim-side `OpenSimSearch` compatibility module. It
translates viewer directory requests for places, popular places, land, events,
classifieds, event/classified details, and map items to the established
OpenSimSearch XML-RPC protocol.

## Deployment boundary

The tracked addon is a search client, not a complete search service. It requires
an independently deployed endpoint configured by `[Search] SearchURL`.
Continuum does not include the historical donor's PHP crawler and MySQL schema:
that repository has no declared software license and its service accepts
unauthenticated simulator registration, polls public DataSnapshot endpoints,
depends on cron, and supports only MySQL. It is therefore unsuitable for direct
production inclusion.

The future authenticated index, moderation, and administration service belongs
to the deferred OpenSim-Grid-Interface phase. Until that service is available,
operators may use a compatible external OpenSimSearch provider at their own
trust boundary.

## Simulator configuration

```ini
[Modules]
    SearchModule = "OpenSimSearch"

[Search]
    Module = "OpenSimSearch"
    SearchURL = "https://search.example.invalid/query"
    RequestTimeoutMs = 5000
```

`RequestTimeoutMs` is clamped to 1,000–30,000 milliseconds and defaults to five
seconds. A failed, malformed, or unavailable endpoint returns an empty/failure
result to the viewer instead of throwing from the client request path. Use HTTPS
and do not put database credentials in simulator configuration.

Do not enable both `BasicSearchModule` and `OpenSimSearch` for the same region.
Leave the default Basic module selected when no compatible external endpoint is
available.

## Runtime gate

Test all of the following against the exact deployed backend:

- places, popular places, land sales and land rentals;
- events search, paging, maturity filters and event details;
- classifieds search, paging and classified details;
- map item results and global coordinates;
- Unicode, apostrophes, XML metacharacters and empty searches;
- removal of parcels, regions, events and classifieds from the index;
- hidden/private parcel and resident privacy rules;
- two simulators publishing changes and observing the same results;
- backend outage, timeout, malformed XML-RPC and oversized result behavior;
- Hypergrid inclusion/exclusion policy and foreign destination validation.

Compilation alone does not certify an external search backend.
