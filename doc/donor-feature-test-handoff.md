# Donor feature testing handoff

Date: 2026-08-01

This branch is based on OpenSim Dev `247b9182c1ca0f11743de06a2808f003bc8e2a90`. It contains the viable donor work selected from Gunthar, Mobius/Tranquillity, WhiteCore behavioural review, previous Continuum branches, and recovered addon archives. `S:\GitHub\Casperia` was not used.

## Test branches

| Branch | Purpose | Default posture |
|---|---|---|
| `codex/production-feature-integration` | Main functional test candidate | New externally visible and trust-sensitive modules are opt-in. |
| `codex/testing-ubode-tuning` | Main candidate plus Gunthar's complete ubODE tuning sequence | Experimental; test on a disposable region before any production rollout. |
| `audit/donor-feature-inventory` | Evidence, provenance, compatibility, and disposition records | Documentation only. |

Do not deploy either test branch over the running grid in place. Build and configure a separate simulator/Robust test instance, back up databases, and use a copy of production data when migration coverage is required.

## Integrated test inventory

| Feature | Classification | Primary acceptance test | Compatibility focus |
|---|---|---|---|
| YEngine orphaned resumed-event recovery | upstream-quality bug fix | Restart a region with persisted scripts, delete/recompile one during queued-event recovery, and verify the engine continues processing later events without a scheduler crash. | No DB/viewer/HG dependency. |
| Mutable Display Names | Robust service extension | Set a Unicode display name in a compatible viewer, relog, search for the resident, cross a region, and verify account-name fallback in a legacy viewer. Confirm change throttling. | MySQL migration, Robust connector, HG foreign-name trust/cache. |
| Experiences | Robust service extension | Create/associate an experience, grant and revoke permission, relog, restart region and Robust, exercise region trusted/allowed/blocked and parcel allowed/blocked controls, confirm blocked policy overrides grants, and test KVP persistence. | MySQL and Robust are the supported production target; migration 38 adds estate blocked-Experience persistence. HG experience trust must remain local. SQLite/PGSQL service providers are not certified. |
| Experience-Lite script surface | experimental feature | Enable `[ScriptExperiences]` only in an isolated trusted estate, whitelist explicit owner/object UUIDs, then test permission events, sit/teleport behavior, KVP quotas, restart persistence, and denial for untrusted scripts. | Disabled by default; never include debit permission in automatic grants. |
| Abuse Reports | Robust service extension | Submit reports with and without screenshot through viewer CAPS, verify persistence and retrieval, reject malformed/oversized requests, and confirm reports are not exposed across HG. | MySQL/Robust; administrative review UI remains external. |
| User aliases and OAR identity lookup | Robust service extension | Import OARs containing local, aliased, missing, duplicate, and foreign creators; verify no silent identity substitution and no HG impersonation. | MySQL service, Robust/local connectors, negative-result behavior. |
| Hypergrid stale identity repair | narrow core compatibility patch | Change a foreign grid's DNS/IP/service URLs, restart services, teleport again, and verify live circuits, cached names, HomeURI, and GatekeeperURI are repaired without changing identity. | Multi-grid security test required. |
| RSA login and TOS acceptance | Robust service extension | Generate supported PEM keys, test valid/invalid signatures, password fallback, key rotation, replay resistance, TOS-version changes, and mixed-version login clients. | MySQL migration version 8, Robust authentication configuration, secret-key protection. |
| Extended LSL/OSSL surface | narrow core compatibility and experimental features | Compile and run the supplied API groups for environment, pathfinding, Combat2, GLTF/materials, inventory/ownership, estate/parcel, attachment, sound, RSA and Experience calls; verify permissions, throttles and negative paths. | Viewer capability varies; untrusted-script and estate-boundary testing required. |
| Crossing, attachment and map recovery | upstream-quality fixes | Cross regions and Hypergrid boundaries with attachments, scripted sits and animations, force failure/retry paths, then verify attachment ownership/state and background map completion. | Robust/HG concurrency and shutdown/restart behavior. |
| RegionWeb | optional addon module | Explicitly enable on a test region, verify public/region routes, authentication and estate authorization, escaping, CSRF behavior, path traversal resistance, inventory privacy, and concurrent requests. | Disabled by default; Windows paths and HG-originated content. |
| GroupAutoInvite | optional addon module | Enable for one test group, verify once-per-login invitations, existing-member behavior, invalid group/service outage handling, and no invitation loops. | Groups connector and HG visitor policy. |
| RegionCurrency | optional addon module | Test balance display and portal routes against the selected currency backend, including authentication, unavailable service, malformed response and disabled startup. | Must not be enabled beside another viewer economy path without an explicit design. |
| OpenSimMarketplace | optional addon module | Configure a dedicated service region/account, publish immutable listing snapshots, deliver nested inventory, retry duplicate orders, reject forged/oversized requests, and audit the JSONL ledger. | Disabled by default; HTTPS/auth, MySQL website schema, inventory permissions, delivery idempotency. |
| Gloebit | optional addon module | Enable with test credentials, perform purchase/refund/failure flows, and verify simulator startup and ordinary inventory remain available during service outage. | External service terms/API; HG economy boundary. |
| HoloPhysicsGuard | optional addon module | Exercise configured physics limits with benign and abusive objects; verify enforcement, logging, false-positive rate, and clean disabled startup. | Region-only, Windows/native physics load. |
| MoneyServer and currency modules | optional addon/service | On a disposable database test balance, transfer, purchase, insufficient funds, duplicate request/idempotency, rollback, and service outage. | MySQL schema, Robust separation, HG currency policy. |
| OpenSimSearch | optional addon/service | Index and query parcels/people/events, update and remove records, validate authorization and escaping, and test stale-index recovery. | MySQL, Robust, HG privacy. |
| Tide and Weather | optional addon modules | Enable independently, run full cycles, restart, validate region environment changes and CPU/network cost, then disable without persistent side effects. | Viewer rendering differences; no service schema expected. |
| Warp3D alpha texture-card sprites | experimental feature | With sprite flags explicitly enabled, compare golden map tiles for opaque, masked, blended, transparent, missing, and corrupt textures; record generation time and memory. | Disabled by default; Windows rendering parity. |
| Windows first-run wizard | experimental tooling | Run on a clean Windows VM with a path containing spaces; test cancel/retry, occupied ports, repeated runs, SQLite standalone, and failure cleanup. | Donor branding/config assumptions remain unsuitable for production automation. |
| ubODE tuning | experimental feature branch | Use a fixed scene corpus covering avatar stairs/falls, collisions, stacked objects, vehicles, bounce, buoyancy, water entry and crossings; compare behavior, CPU, and determinism to the main branch. | Dedicated branch only; Windows/Linux native parity before promotion. |

## Safe configuration gates

- `[ScriptExperiences] Enabled = false` by default. Its KVP store is separate from the full Tranquillity Experience service and must not be treated as the authoritative grid Experience database.
- `[RegionWeb] Enabled = false` by default. Enabling it creates a public HTTP surface and content files.
- `[OpenSimMarketplace] Enabled = false` by default. Its HTTP endpoints, service account and delivery ledger require dedicated credentials and storage review.
- Warp3D flat-card sprite rendering remains disabled unless both renderer options are deliberately enabled.
- Every recovered addon must be tested both disabled and enabled. External-service credentials and production database strings must not be committed.
- Display Names, full Experiences, aliases, Abuse Reports, search, and money tests require clean-schema and upgraded-schema MySQL runs. Take a database snapshot before each migration test.

## Explicit non-ports and boundaries

- WhiteCore WebUI is not copied into OpenSim. It is a separate grid-wide web application with a divergent service/data architecture; use it only as a behavioural and administrative acceptance-test reference.
- Mobius establishes provenance for Display Names, Experiences, and Abuse Reports. Tranquillity supplies the traceable enhanced service implementation used here. Gunthar's Experience-Lite behavior is tested as an opt-in script layer and is not allowed to replace the authoritative Tranquillity service.

### Display Names multi-simulator gate

Display Names are not accepted on a single-region result. With at least two
separate simulator processes connected to the same Robust services, cache the
same resident on both, change the name on simulator A, and verify simulator B
returns the new Robust value through `GetDisplayNames`. Then cross A→B, relog
directly into B, restart B only, and restart the complete grid. In every case
the nameplate, nearby list, profile/search results, and display-name LSL calls
must agree. Repeat reset-to-legacy-name and test an adjacent-region observer.
Failure of any step blocks promotion.
- Tranquillity's later wholesale OAR identity rewrite was not selected because it would regress current OpenSim Dev's force-assets import path. The compatible alias lookup behavior was retained.
- Donor branding, curated destinations, auto-update/reset scripts, forced defaults, grid-specific endpoints, and incomplete or obsolete architectural rewrites are intentionally excluded.
- SQLite providers are required for standalone operation. MySQL/MariaDB and
  PostgreSQL providers are both required for grid/Robust operation. Experience,
  Abuse Report, and alias provider implementations now exist for all three, but
  clean/upgrade migration and runtime certification remain open. MoneyServer
  Compatibility remains MySQL-only. ContinuumEconomy has independent SQLite,
  MySQL/MariaDB and PostgreSQL providers, but still requires this handoff's
  provider-specific production-test certification before deployment.

## Release gate

A feature may move from test to deployment only after its row above passes, its disabled-mode startup passes, logs contain no secrets or repeated exceptions, MySQL upgrade and rollback are rehearsed where applicable, and the baseline comparison shows no regression in login, teleport, inventory, assets, scripts, region crossing, shutdown, or restart. Experimental features require separate approval and must not be enabled merely because their branch builds.
