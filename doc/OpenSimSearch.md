# OpenSimSearch

Continuum includes both the OpenSim-side `OpenSimSearch` compatibility module
and the native `ContinuumSearch.Service`. The module translates viewer
directory requests for places, popular places, land, events, classifieds,
event/classified details, and map items to the established OpenSimSearch
XML-RPC protocol. The service supplies that protocol from an authenticated,
bounded DataSnapshot crawler and a provider-neutral index.

## Deployment boundary

The simulator module requires an endpoint configured by `[Search] SearchURL`.
For a self-contained Continuum deployment, point it at
`ContinuumSearch.Service`; a compatible external OpenSimSearch endpoint remains
optional. The historical donor's unlicensed PHP crawler and MySQL-only schema
are not bundled. Continuum's replacement owns its schema, supports SQLite,
MySQL/MariaDB and PostgreSQL, validates registration secrets and snapshot URLs,
bounds input and results, and performs crawling without cron. The integrated
WebUI consumes the same service rather than maintaining a second index.

## Simulator configuration

```ini
[Modules]
    SearchModule = "OpenSimSearch"

[Search]
    Module = "OpenSimSearch"
    SearchURL = "https://search.example.invalid/query"
    RequestTimeoutMs = 5000
    MaxConcurrentRequests = 8
```

`RequestTimeoutMs` is clamped to 1,000–30,000 milliseconds and defaults to five
seconds. `SearchURL` must be an absolute HTTP(S) URL; HTTP produces an explicit
transport warning. A failed, malformed, or unavailable endpoint returns a fixed
failure message to the viewer instead of throwing from the client request path.
Backend calls run away from the simulator client-event thread.
`MaxConcurrentRequests` limits active and queued work to 1-64 requests (default
8); excess viewer queries receive a short busy response instead of consuming an
unbounded number of workers while the backend is slow or unavailable.
Result processing is capped at the viewer's 100 entries plus paging sentinel,
and region-list reads are synchronized with region removal. Use HTTPS and do not
put database credentials in simulator configuration.

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
- concurrency saturation without simulator client-event stalls;
- Hypergrid inclusion/exclusion policy and foreign destination validation.

The SQLite parser, replacement, places, popular, parcel, land, events and event
details acceptance contracts pass locally. Provider-specific deployment and
real-viewer behavior remain runtime gates; compilation or an isolated
self-test does not certify a production search deployment.
