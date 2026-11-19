# Donor feature testing handoff

<!-- continuum-current-status:begin -->
> Project checkpoint — 2026-11-19: selected Tranquillity stable-release ports,
> including native AIS and server-side baking, are implemented in this source
> snapshot. Release and automated suites passed. Your OpenSim
> submissions #75–77 are adapted while preserving native Continuum protections.
> See the [current Tranquillity ledger](tranquillity-release-review.md),
> [baking configuration](server-side-baking.md) and
> [test handoff](donor-feature-test-handoff.md). Live acceptance remains separate
> from publication; AIS/baking stay off by default. lickx, Mobius and the selected
> WhiteCore ports/native workflows retain their earlier source-complete status.
> Earlier dated checkpoints below are historical, not the current source backlog.
<!-- continuum-current-status:end -->

Date: 2026-08-01

This branch is based on OpenSim Dev `247b9182c1ca0f11743de06a2808f003bc8e2a90`. It contains the viable donor work selected from Gunthar, Mobius/Tranquillity, WhiteCore behavioural review, previous Continuum branches, and recovered addon archives. `S:\GitHub\Casperia` was not used.

## Latest operator results and queued checks — 2026-10-21

Use this handoff for new manual-test requirements; do not interrupt donor porting
with repeated requests to execute the existing matrix. Record implementation
and automated evidence separately in `integration-progress.md`.

Operator-confirmed on the recovered Continuum test grid:

- [x] Firestorm login, terrain/texture loading across West/Central/East,
  region crossings, inter-region teleports, and logout/relogin in West.
- [x] Currency purchase using Firestorm's Buy button. This confirms that flow,
  not every transfer, object-sale, fee or failure scenario in the economy matrix.

### Tranquillity script-completion port (3bba0e510a)

Automated regression command, after building the Release solution:

```powershell
dotnet run --project tests/Continuum.ScriptCompletions.Tests -c Release
```

Pending deployment/manual checks:

- [ ] Deploy the matching rebuilt Region.Framework, CoreModules and script
  engine assemblies together to the isolated test grid after backing up and
  stopping its processes. Do not mix old/new interface assemblies.
- [ ] In a prim, use `llHTTPRequest` against a controlled permitted endpoint;
  confirm one `http_response` with matching request key, status and body.
- [ ] Repeat a controlled failure and multiple queued requests; confirm no lost
  or duplicated completion events, including after script reset/removal.
- [ ] With XML-RPC explicitly enabled in an isolated configuration, exercise
  incoming `remote_data` delivery in each of two region/engine instances.
- [ ] Track outbound `llSendRemoteData` separately: its concrete completion cast
  was not changed by this donor commit and is not certified by these checks.

### Active frozen donor: opensim-lickx (6614599b7506)

Run after the full Release solution build:

```powershell
dotnet run --project tests/Continuum.Lickx.Tests -c Release
```

Queued checks for the 2026-10-21 port batch:

- [ ] On a disposable scene, `save oar --tenant=<uuid>` includes only that
  owner's eligible objects, including when the specified avatar is the estate
  owner. Repeat with `--perm=CT`, `--all`, no matching objects and asset inclusion.
  Terrain, parcels and region settings remain included; this is not a private
  tenant-only archive. Verify invalid/zero UUIDs do not produce a usable export.
- [ ] Save/load IARs containing foreign creators with default options,
  `--nocreators`, legacy `--creators` and `--home`; confirm creator identity is
  preserved or explicitly omitted as requested, without owner reassignment.
- [ ] Run profile migration v6 from clean and populated version-5 schemas on
  exact deployment MySQL/MariaDB versions. Preserve IDs/rows and round-trip
  accented, non-Latin and supplementary Unicode profile/pick/classified/note
  data. Verify a repeat startup does not rerun the migration.
- [ ] In-world `llGetEnv` keys return the selected region's rating/grid name
  and configured object-mail hostname; disabled email returns empty aliases.
- [ ] Compile scripts using both payment-info constants. Apply negative
  `PRIM_NORMAL` offsets, verify viewer appearance/readback and save/restart
  persistence, including bounds and ALL_SIDES.

All further lickx test requirements belong here while its source dispositions
are reconciled in section 14 of `donor-feature-inventory.md`.

Reconciliation follow-up checks (automated constructor/cache, creator, title and
notification-target cases already pass in `Continuum.Lickx.Tests`):

- [ ] With PresenceService/GridUserService `UseCache=false`, verify login,
  report, lookup, home/last-position updates and logout always observe backing
  storage; repeat with defaults and separate service/database instances.
- [ ] In both native and XML-RPC Groups, change active group and role title
  while wearing scripted attachments. Confirm attachment group/task-inventory
  group updates, correct visible title and that deliberately stopped scripts
  stay stopped. Rejected/nonactive/missing memberships must not switch groups.
- [ ] On two grids, verify friend online/offline notifications reach each
  resolved endpoint, use UUID-only recipients, suppress duplicates and ignore
  unrequested IDs or malformed/outage responses.
- [ ] Import creator metadata with a local grid URL and with an empty host;
  reject nonexistent/mismatched local UUIDs, preserve valid foreign creators,
  and retain account-cache invalidation after DisplayName changes.
- [ ] HG login without AssetServerURI must finish without attachment-data
  deadlock; repeat valid attachments, missing assets, leaving mid-load and
  rapid crossings through the retained debounced restart pipeline.
- [ ] Import an OAR with invalid/missing parcel owner and invalid group owner
  using `--default-user`; parcel/object ownership must agree with the option.
- [ ] Repeat the PostgreSQL query checks on deployment locale/version and
  existing schemas. Quotes and injection-shaped patterns stay data, hidden
  groups remain hidden, and account/display-name/region scope remains enforced.
  Non-Latin case folding depends on database locale and needs separate checks.

Optional provider test invocation: create a new disposable database named
`continuum_lickx_*` on the isolated loopback PostgreSQL port 9543, set
`CONTINUUM_LICKX_PG` to its connection string, then run the same harness. The
harness refuses other hosts/ports/database-name prefixes. Do not point it at
the running grid database. The 21 October run used
`continuum_lickx_20261021_160426`, retained solely for inspection.

### IMAP object mail implementation and queued acceptance

The compatible IMAP adaptation is implemented. The same lickx harness includes
fake-store, real EmailModule and loopback MailKit protocol checks for exact
routing, MIME/UTF-8 limits, bounded queues/pages, retries, sparse UIDs,
UIDVALIDITY changes, concurrent expunges, region removal, shutdown, APPEND and
selective UID EXPUNGE. These checks do not certify a live TLS mail service.

Configure a separate test instance using the commented settings in
`bin/OpenSim.ini.example`: `[Startup] emailmodule=DefaultEmailModule`, then
`[SMTP] enabled=true`, `enableEmailToExternalObjects=true`, `IMAPEnabled=true`,
the shared account credentials, existing folder and common `internal_object_host`.
All participating simulators must use the same account/folder/domain. SMTP
outbound is independent and can remain off with `enableEmailToSMTP=false`.
If `[Email]` exists it takes precedence over `[SMTP]`; keep settings together.
The server must support UIDPLUS. StartTls is the default; SslOnConnect normally
uses port 993. Certificate validation remains enabled. Explicit `None` and the
legacy `IMAP_SERVER_TLS` option are supported for controlled configurations.
No live credentials or mail account have been configured by this port.

Default bounds are 64 KiB raw MIME per message, 128 pending sends, 1,024 active
object subscriptions, 256 messages per page, eight sends/mailboxes per cycle,
five-second polling, ten-second cycle deadline, 15-second retry, one-hour send
lifetime and 30-minute idle subscription expiry. The local object queue holds
50 messages. Sequence/UID seeking handles sparse UIDs without SEARCH ALL;
mailbox changes during a page cause a retry.

Only a single exact `<object-UUID>@internal_object_host` To address is accepted.
Unknown, unregistered, oversized, multiple-recipient and HTML-only messages
remain on the server. Full local queues retain mail for retry. Accepted messages
are selectively deleted only after local queue admission; an acknowledgement
retry for the same UID does not redeliver within that process.

Operational limitation: outbound queues, local queues, cursors and retry
deduplication are memory-only. Restart can lose queued sends or locally admitted
unread mail; an ambiguous APPEND/network failure or crash can produce duplicates.
This is not a durable, exactly-once mail service.

- [ ] On two independent simulators sharing the test inbox, send/reply between
  registered objects; verify sender/subject filters, UTF-8, local direct delivery
  and the legacy object-address alias.
- [ ] Exercise StartTls and SslOnConnect with a valid certificate; verify invalid
  certificates, failed authentication, missing UIDPLUS and folder permissions
  fail without deleting mail or disabling local object delivery.
- [ ] Exercise outages, retry recovery, send expiry and full object queues;
  verify oversized/unrelated/multiple-recipient mail remains untouched and
  unrelated messages already marked Deleted survive acknowledgement.
- [ ] Exercise UIDVALIDITY reset, concurrent mailbox deletion/arrival, region
  removal/reload, object crossings and shutdown while work is active. Confirm
  unloaded or in-transit objects do not acknowledge messages.

### Mobius final reconciliation — retained-feature checks

Pinned Mobius feature-branch reconciliation is complete; no additional source
port was selected. These checks validate retained behavior, not a new code-port
backlog. Section 13 of the donor inventory records scope and exclusions.

- [ ] Include a foreign user with trusted cached UUI/HomeURL but no GridUser
  row in the Display Names multi-simulator gate; check refresh, home-grid
  outage/timeout fallback and rejection of unrequested returned IDs.
- [ ] In the existing RSA/TOS gate, test fresh/upgraded schemas on all three
  providers, missing keys, password/RSA-only policy and acceptance persistence.
  Finding schema fields is not migration certification.
- [ ] Verify native restart countdown/cancellation, authorized terrain console
  commands and denial for unprivileged residents. Verify positional sound and
  invalid-asset handling without script-engine failure.

No live tests were executed by this documentation-only reconciliation. Existing
authentication, Display Names and viewer gates below remain authoritative.

### Regional land auctions — 2026-10-22

Build the full Release solution, then run:

```powershell
dotnet run --project tests/Continuum.Auctions.Tests -c Release
```

Automated coverage includes exclusive journal access, bounded/copy-isolated
state, concurrent equal bids, outbid release, stale-parcel cancellation,
no-bid expiry, cancellation replay, lost reserve/capture replies and delivery
acknowledgement recovery. A disposable real SQLite ledger verifies available
balance holds, insufficient replacement funds, payment and conservation.

Isolated deployment configuration: enable `[LandAuctions] Enabled=true` only
with the matching rebuilt ContinuumEconomy connector, Framework, UDP client,
CoreModules, OptionalModules and script API assemblies. Choose a persistent
local JournalDirectory, default DurationHours=24, MinimumBid=1, ChatChannel=93.
Back up journal, parcel store and ledger together. Do not share a region's
journal between simulator processes or discard it while holds exist.

- [ ] In Firestorm, estate authority on an individually owned parcel can use
  native Start Auction; confirm LocalID/SnapshotID and AuctionID metadata.
  Forged agent/session packets and non-owner/non-manager starts are rejected.
- [ ] Type `/93 auctions`; verify page open/start/bid/cancel for permitted users,
  numeric local parcel IDs, escaped names, hidden form token and expired CAPS.
  Confirm HTTP GET never bids, invalid/missing token fails, requests are bounded,
  unprivileged users cannot start/cancel, and logout/crossing revokes access.
- [ ] Two local residents bid concurrently. Check high-bid ordering, full
  reservation, outbid release, insufficient funds, own-bid increase liquidity,
  seller/lower/late bids and denial of foreign-grid/group-owned participation.
- [ ] Verify the normal sale, subdivision/join, deed/reclaim/abandon/force-owner
  and script owner-change guards while AuctionID is set. Auction completion
  transfers land only and refreshes parcel metadata/overlay; cancellation and
  no-bid expiry clear the auction marker without transferring land or money.
- [ ] Repeat reserve/capture/cancel with dropped replies and service restart;
  restart simulator during authorization, old-hold release, capture, parcel
  persistence and completion acknowledgement. No duplicate debit or ownership
  transfer; unavailable storage retains recoverable state rather than losing it.
- [ ] Test exact deployed MySQL/MariaDB/PostgreSQL ledger providers as well as
  SQLite. Reconcile pending journal IDs with existing economy hold tools; never
  assume an old auction hold can be manually cancelled merely because of age.
- [ ] Restore coordinated backups, move a region with its journal, and test
  failure for missing/corrupt/replaced parcels and a second process opening the
  same journal. Paid-but-undelivered Captured records need explicit operator
  attention if automatic retry cannot repair the parcel. Journal loss and
  cross-service failure are not an exactly-once distributed transaction guarantee.

This initial feature provides region-scoped discovery and bidding, not a
grid-wide WebUI catalog. No live deployment/provider/viewer result is claimed.

### Integrated WebUI completeness — 2026-10-22

Use `parity-webui.md` to track the pinned 87-controller inventory. The prior
blanket completion statement is withdrawn: 23 explicit legacy page denials and
the missing grid-wide auction workflow require dispositions/implementation,
not merely browser certification. Source inspection is not a live test result.

- [ ] For each selected page, verify navigation, anonymous/resident/owner/admin
  access, full create/read/update/delete behavior, CSRF and validation errors,
  persisted results after restart, and failure reporting when services reject
  changes. Do not accept a retained template or success banner as backend proof.
- [ ] Verify grid-wide auction listing, bidding and seller/admin management
  through the integrated WebUI after its service adapter is implemented;
  the regional `/93 auctions` page does not establish this workflow.
- [ ] Repeat selected public/resident/admin workflows with supported providers,
  operator-configured mount/grid URLs and a clean installation. Check links,
  branding configuration, localization and unavailable optional services.

### Grid news WebUI implementation — 2026-10-22

Three news admin pages are implemented; 20 other legacy paths remain blocked.
Run the Release build and `dotnet run --project tests/Continuum.WebUI.Tests -c Release`.
Run `addon-modules/ContinuumWebUI/Build-WebUIAssets.ps1` before building asset
changes. Configure persistent writable ContentStorePath and back it up.

- [ ] Create a draft, publish, edit, unpublish and delete it; verify public News
  feed/admin navigation. Anonymous/resident requests cannot access admin pages
  or mutate news. GET with delete parameters cannot delete anything.
- [ ] Edit in two browsers; stale edits cannot overwrite newer revisions.
  Verify Robust restart, backup restore, busy/unwritable storage and errors.
- [ ] Test HTML/script-like text, quotes and Unicode, draft privacy, pagination
  and field/storage limits. Stored content must remain literal text.
- [ ] Check desktop/mobile/viewer-browser layouts, keyboard focus/labels,
  reduced motion and landing signup/destination/help links. Actual browser and
  screenshot verification have not yet been performed; no deployment is claimed.

### Resident password recovery — 2026-10-22

Implemented recovery removes one additional blocked path: 19 legacy WebUI paths
remain blocked. Run `dotnet run --project tests/Continuum.WebUI.Tests -c Release`
after building. No real account mail, live provider or browser test was run by
this implementation checkpoint. Git's corrupt baseline archive object has been
recovered and the full diff check passes.

Isolated setup: configure PasswordRecoveryEnabled=true, PasswordRecoveryURL as
the public HTTPS portal root, persistent PasswordRecoveryStorePath and the
PasswordRecoverySMTP* / PasswordRecoveryFrom settings in [ContinuumWebUI].
SMTP supports StartTls or SslOnConnect with normal certificate validation.
Explicitly list TLS-offloading proxy IPs in TrustedProxyAddresses; the proxy
must set X-Forwarded-Proto=https and a correct X-Forwarded-For chain. Do not
configure a wildcard trust of internet clients. Enable consistently across all
portal instances sharing account/auth services. Existing portal users must log
in again when credential-bound sessions become required. Recovery-disabled
login compatibility remains tested. Do not expose recovery.json as a web asset.

- [ ] Log in normally, use Forgot password, and verify unknown/mismatched and
  disabled-account requests return the same neutral response as valid requests.
  Only the matching active account receives a link; check mailbox/spam delivery.
- [ ] Follow the email link through the public portal mount. Its secret stays
  in the URL fragment, is removed from browser history after form loading and
  enters only the POST body. No query/log/referrer contains the token/password.
  Confirm success returns to login and a disabled recovery service shows help.
- [ ] Check 30-minute expiry, repeated/concurrent link use, changed email or
  password after issuance, wrong confirmations and existing password change.
  Verify prior portal cookies stop working after reset/change, including another
  browser/front-end instance. Existing in-world sessions are a separate contract
  and are not forcibly terminated by this WebUI change.
- [ ] Verify five attempts per IP per 15 minutes, per-account issue limits,
  spoofed forwarding headers, native HTTPS, trusted TLS proxies and direct
  loopback development. An untrusted Host or X-Forwarded-Proto header cannot
  make an external HTTP request secure. Native/proxied HTTPS cookies are Secure.
- [ ] Exercise SMTP outage, invalid TLS certificate, failed auth, timed-out mail,
  busy/corrupt/unwritable recovery storage and failed/ambiguous password writes.
  No false password-change success; consumed tokens stay spent. Requesting a
  new link remains subject to the documented cooldown and storage bounds.
- [ ] Repeat native auth/GetAuthInfo/SetPassword behavior on the exact deployed
  MySQL/MariaDB, PostgreSQL and SQLite providers, plus password-manager autofill,
  mobile/desktop/Firestorm embedded-browser layout and keyboard interaction.

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
| Experiences | Robust service extension | Create/associate an experience, grant and revoke permission, relog, restart region and Robust, exercise region trusted/allowed/blocked and parcel allowed/blocked controls, confirm blocked policy overrides grants, and test KVP persistence. | SQLite standalone plus MySQL/MariaDB and PostgreSQL grid/Robust paths all require runtime certification. HG experience trust must remain local. |
| Experience-Lite script surface | experimental feature | Enable `[ScriptExperiences]` only in an isolated trusted estate, whitelist explicit owner/object UUIDs, then test permission events, sit/teleport behavior, KVP quotas, restart persistence, and denial for untrusted scripts. | Disabled by default; never include debit permission in automatic grants. |
| Abuse Reports | Robust service extension | Submit reports with and without screenshot through viewer CAPS, verify persistence and retrieval, reject malformed/oversized requests, and confirm reports are not exposed across HG. | MySQL/Robust; administrative review UI remains external. |
| User aliases and OAR identity lookup | Robust service extension | Import OARs containing local, aliased, missing, duplicate, and foreign creators; verify no silent identity substitution and no HG impersonation. | MySQL service, Robust/local connectors, negative-result behavior. |
| Hypergrid stale identity repair | narrow core compatibility patch | Change a foreign grid's DNS/IP/service URLs, restart services, teleport again, and verify live circuits, cached names, HomeURI, and GatekeeperURI are repaired without changing identity. | Multi-grid security test required. |
| RSA login and TOS acceptance | Robust service extension | Generate supported PEM keys, test valid/invalid signatures, password fallback, key rotation, replay resistance, TOS-version changes, and mixed-version login clients. | MySQL migration version 8, Robust authentication configuration, secret-key protection. |
| Extended LSL/OSSL surface | narrow core compatibility and experimental features | Compile and run the supplied API groups for environment, pathfinding, Combat2, GLTF/materials, inventory/ownership, estate/parcel, attachment, sound, RSA and Experience calls; verify permissions, throttles and negative paths. | Viewer capability varies; untrusted-script and estate-boundary testing required. |
| Crossing, attachment and map recovery | upstream-quality fixes | Cross regions and Hypergrid boundaries with attachments, scripted sits and animations, force failure/retry paths, then verify attachment ownership/state and background map completion. | Robust/HG concurrency and shutdown/restart behavior. |
| RegionWeb | optional combined module | Explicitly enable on a test region; verify public/region routes, HTTPS-only Estate Admin and wallet routes, Secure cookies, authentication, estate authorization, balance/transfer behavior against one backend, CSRF, request limits, escaping, path traversal resistance, inventory privacy, configuration backup/reload and concurrency. | Canonical Gunthar-derived website and wallet presentation, disabled by default; not WhiteCore WebUI or a ledger. Test Windows paths, reverse-proxy TLS and HG-originated content. |
| GroupAutoInvite | optional addon module | Enable for one test group, verify once-per-login invitations, existing-member behavior, invalid group/service outage handling, and no invitation loops. | Groups connector and HG visitor policy. |
| RegionCurrency | obsolete or unsuitable (deprecated compatibility module) | Verify it disables itself when RegionWeb is enabled. For legacy-only regression, begin with purchases/transfers disabled and test against one compatible backend. | Do not select for new deployments. It exists only to avoid abruptly breaking configurations created during the earlier split. |
| OpenSimMarketplace | optional addon module | Configure a dedicated service region/account, publish immutable listing snapshots, deliver nested inventory, retry duplicate orders, reject forged/oversized requests, and audit the JSONL ledger. | Disabled by default; HTTPS/auth, MySQL website schema, inventory permissions, delivery idempotency. |
| Gloebit | optional addon module | Enable with test credentials, perform purchase/refund/failure flows, and verify simulator startup and ordinary inventory remain available during service outage. | External service terms/API; HG economy boundary. |
| HoloPhysicsGuard | optional addon module | Exercise configured physics limits with benign and abusive objects; verify enforcement, logging, false-positive rate, and clean disabled startup. | Region-only, Windows/native physics load. |
| MoneyServer and currency modules | optional addon/service | On a disposable database test balance, transfer, purchase, insufficient funds, duplicate request/idempotency, rollback, and service outage. | MySQL schema, Robust separation, HG currency policy. |
| OpenSimSearch | optional addon/service | Index and query parcels/people/events, update and remove records, validate authorization and escaping, and test stale-index recovery. | MySQL, Robust, HG privacy. |
| Tide and Weather | optional addon modules | Enable independently, run full cycles, restart, validate region environment changes and CPU/network cost, then disable without persistent side effects. | Viewer rendering differences; no service schema expected. |
| Warp3D alpha texture-card sprites | experimental feature | With sprite flags explicitly enabled, compare golden map tiles for opaque, masked, blended, transparent, missing, and corrupt textures; record generation time and memory. | Disabled by default; Windows rendering parity. |
| Windows first-run wizard | obsolete or unsuitable (quarantined experimental tooling) | The launcher must exit without modifying files. Any developer rehabilitation must use an isolated disposable VM and test secret removal, non-destructive repeatability, cancel/retry, occupied ports, SQLite standalone, paths with spaces and failure cleanup. | Not a supported setup path. Direct execution requires `-UnsafeExperimental`; donor branding, configuration overwrite behavior and plaintext credential lifecycle remain unsuitable for production automation. |
| ubODE tuning | experimental feature branch | Use a fixed scene corpus covering avatar stairs/falls, collisions, stacked objects, vehicles, bounce, buoyancy, water entry and crossings; compare behavior, CPU, and determinism to the main branch. | Dedicated branch only; Windows/Linux native parity before promotion. |

## Safe configuration gates

- `[ScriptExperiences] Enabled = false` by default. Its KVP store is separate from the full Tranquillity Experience service and must not be treated as the authoritative grid Experience database.
- The full viewer Experience service is enabled independently: grid simulators
  select `RemoteExperienceServicesConnector` and standalone simulators select
  `LocalExperienceServicesConnector` in `[Modules]`. Without that selection no
  `IExperienceService` exists, Experience CAPS are intentionally withheld, and
  Parcel/Region Experience tabs will not appear. The corrected example profiles
  include both the selection and matching `[ExperienceService]` section.
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

The receiving simulator now evicts its cached account record whenever the
avatar becomes a root agent and broadcasts an authoritative
`DisplayNameUpdate` to its connected viewers. This specifically covers the
previous failure where Nearby/search showed the persisted name but the
nameplate reverted after crossing or restart. Runtime testing must confirm both
surfaces agree; the update is an event-queue message, not a region chat notice.

### Experiences viewer-panel gate

Use the Second Life/Firestorm Experiences floater as the acceptance target. It
must show Search, Allowed, Blocked, Admin, Contributor, Owned, and Events. The
server mapping is `FindExperienceByName`, `GetExperiences`,
`GetAdminExperiences`, `GetCreatorExperiences`, and `AgentExperiences`; Events
is the viewer's local experience event log. Empty server lists must be LLSD
arrays, not `undef`, so a new account renders empty tabs without response-type
errors.

The Region/Estate Experience panel is a separate gate. The official viewer only
creates it when `RegionExperiences` is present in the seed response. Verify GET
and authorized POST for trusted, allowed, and blocked lists, persistence across
simulator and Robust restarts, and denial for ordinary residents. The Parcel
Experience panel uses parcel access-list flags 8 (allowed) and 16 (blocked);
verify owner/group authorization, persistence, and that blocked policy wins.
Run the complete matrix in Firestorm and one current official SL-compatible
viewer on two simulator processes. Crossing must retain the same Owned,
Allowed, and Blocked results because these are backed by the grid-wide Robust
service rather than simulator-local state.

### Abuse Reports persistence gate

Do not accept the viewer's submission acknowledgement as proof of storage.
Confirm the simulator logs that `LocalAbuseReportsServicesConnector` or
`RemoteAbuseReportsServicesConnector` registered, then retrieve the resulting
report by its SQL-assigned ID from the Standalone or ROBUST moderation console.
The connector is selected solely by `[Modules] AbuseReportsService`; the removed
legacy `[Messaging] AbuseReportsModule` gate was undocumented and prevented the
shipped profiles from registering storage. Test simulator-side LLSD limits and
the independent service-side summary, details, and screenshot limits. Repeat
without screenshots, with screenshots, after a ROBUST restart, and from two
separate simulator processes.
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


## WebUI resident events (2026-10-22 implementation)

Source/build changes only. Use an isolated test deployment and matching private
Search `WebUIWriteSecret` / portal `SearchWriteSecret`; do not expose those keys.

- [ ] With writes disabled, the editor explains unavailability and offers no form.
- [ ] Anonymous create/edit/POST routes reject access; cross-origin POSTs fail.
- [ ] A resident creates an event in their own indexed region with a future UTC
  start, description, category, duration and maturity. Public WebUI search and
  Firestorm show matching data, time conversion and landing location.
- [ ] Retry the identical submitted create form after a lost reply/restart: one
  event and the same ID. Reusing its request ID with different content fails.
- [ ] Another resident cannot create there, edit the event or delete it, including
  forged actor/creator IDs and direct private API calls without the service key.
- [ ] Edit in two tabs: the first succeeds and the stale second edit/delete fails.
- [ ] Delete by POST, verify disappearance and restart persistence. A GET cannot
  delete. Retrying the old create request cannot resurrect the deleted event.
- [ ] Reject past/over-one-year dates, invalid duration/category/maturity,
  out-of-region or non-finite coordinates and overlong/empty fields. Markup is
  displayed as text and does not execute.
- [ ] Adult/moderate events cannot exceed the region maturity. Verify Firestorm
  category/maturity filters. Test landing X/Y beyond 256 in a variable-size region.
- [ ] Confirm event posting is free and does not charge admission or listing fees.
  Editing a legacy event retains existing cover metadata.
- [ ] Run the Search acceptance suite against dedicated MySQL and PostgreSQL test
  databases, including schema upgrade and concurrent duplicate submissions.
- [ ] Review remaining parcel-owner/group event permissions before claiming full
  donor parity. Certify paid-classified writes separately using the matrix below.


## WebUI paid classifieds (2026-10-22 implementation)

Use an isolated test deployment. Point Search at the authoritative profile DB;
configure the private portal/Search write key and the separate Search/economy
classified key. Set the intended new-listing minimum in Search. Defaults leave paid
creation disabled. Do not change the live grid merely to execute this matrix.

- [ ] Disabled payments show no paid-creation form; existing read-only lists work.
  Missing/mismatched keys cannot debit any account or disclose private details.
- [ ] Create a seven-day classified after explicitly confirming its displayed
  price. Verify one debit to the resident and one credit to the system account;
  no remaining hold, duplicate receipt or public unpaid listing.
- [ ] Verify public WebUI/Search and Firestorm category, maturity (PG/moderate/
  adult), image, destination/global coordinates, price/ranking and descriptions.
  Test a landing point beyond 256m in a variable-size region.
- [ ] Repeat an identical submission, including concurrent tabs: one payment and
  one classified. Reusing a receipt with changed content or price must fail.
- [ ] Drop the reply after actual capture or stop Search before publication.
  Restart and Resume from My classifieds: same fee receipt, one publication,
  no second debit. Test database write failure after capture as well.
- [ ] Insufficient funds publishes nothing and leaves no live hold. After adding
  funds, use a new form; the rejected request must not become an unpaid listing.
- [ ] Different residents cannot read private details/pending intents, resume,
  edit or delete another listing; forged actor IDs do not change the acting user.
  Anonymous routes and cross-origin POSTs must fail.
- [ ] Edits preserve creation/expiration, price, maturity and location; no fee or
  expiry extension. A stale edit/delete fails. Forged price/date fields do nothing.
- [ ] Deletion is POST-only, has no refund and survives restart. Replaying its
  original creation request must not resurrect it or charge again.
- [ ] Expired listings disappear from public search. There is no auto-renewal;
  verify flag 32 is not emitted and no recurring debit occurs.
- [ ] Viewer profile caches refresh correctly, including after relog; document
  any delay in an already-open profile separately from Search result freshness.
- [ ] HTML/script-like text stays escaped in editor, listing and pending cards.
  Check mobile layout, keyboard navigation, labels and payment confirmation.
- [ ] Run clean/upgrade/write/concurrency and failure-recovery checks on dedicated
  MySQL and PostgreSQL databases, with the chosen native profile service.
- [ ] Rehearse coordinated Search/profile/ledger backup and restore. A captured
  but persistently unpublished listing must remain visible for reconciliation.
- [ ] Keep other economy adapters, free-listing grids, automatic renewal and
  broader SL parity explicitly uncertified until implemented and tested.
- [ ] Edit a paid WebUI listing in Firestorm: preserve original price and paid
  expiry. Try a forged creator/price/date packet and deletion of another user's
  listing. The viewer handler/provider must reject foreign ownership changes.
- [ ] Verify native provider reads for prices above 32,767 and native deletion
  on SQLite. On PostgreSQL, edit one of two different residents' classifieds:
  the second row's ID, owner and metadata must remain unchanged. Run equivalent
  lifetime/ownership checks on MySQL, including unchanged-content saves.
- [ ] With Search writes enabled and password recovery disabled, public HTTP
  account POSTs/sessions must still require HTTPS. Spoofed forwarded headers must
  not bypass that check; trusted TLS termination and direct loopback must work.

- [ ] Successful event/classified writes show success and return to their list;
  they must not appear as errors after the mutation committed. Check the legacy
  userindex.html entry path uses the same working portal navigation.
- [ ] After 30 classified POSTs per account in ten minutes on one frontend, further
  requests return 429. Alternate route casing and verify it cannot bypass limits.
- [ ] During a Search outage, the list shows management unavailability and hides
  new-payment controls. After recovery, the same pending intent is available.
- [ ] Exercise accented/non-Latin descriptions and titles on each production
  provider schema, including upgrade encodings, before enabling paid publication.

- [ ] Change the new-listing minimum while an old receipt is pending. Resume and
  publish that receipt at its original confirmed price, without another debit;
  a fresh request below the new minimum must be rejected before payment.


## Portal presentation administration — 2026-10-24

Source implementation/build and isolated persistence/HTTP/render checks pass.
Run these against an intentionally deployed test grid; do not overwrite a live
operator's content or operational configuration. Back up the configured news and
portal JSON stores together with the existing application backup set.

- [ ] Log in as an administrator. Administration exposes Welcome content,
  Portal settings and Navigation. Direct anonymous/resident requests are denied.
- [ ] Save a heading and introduction, including accented/non-Latin text. Reload
  the portal and restart Robust; values persist without changing grid identity.
- [ ] Enable an announcement. Verify the same escaped plaintext appears on the
  portal home and actual Firestorm login/welcome page, with line breaks intact.
  Disable it and verify neither surface shows it after refresh.
- [ ] Edit support instructions. A resident can read them through Account →
  Contact support. The page does not submit mail or collect postal addresses.
- [ ] Toggle the home gallery off/on. Its images and script disappear/reappear.
  Check responsive layout, long text, keyboard focus and field labels on desktop
  and mobile, plus the viewer's embedded browser.
- [ ] Hide a public navigation link, reload the full page and confirm the link is
  absent while its direct route still works. Restore the link. Account, login,
  home and administration must remain usable, with their original access rules.
- [ ] Open two editors (including different manager pages). Save one; the stale
  editor must request a reload rather than overwriting either section. Verify
  successful saves display success, not an error following a committed change.
- [ ] Submit a manager POST with a foreign origin and a resident cookie; reject
  both without modifying the JSON file. Confirm GET/query parameters cannot save.
- [ ] Set `AllowRegistration=false` in a dedicated test configuration. Neither
  public sign-up action is shown, and registration submission remains rejected.
- [ ] Restore the backed-up portal file while the service is stopped. Confirm
  presentation returns after restart. On a disposable copy, malformed/unreadable
  content must fail visibly without silently replacing it with defaults.
- [ ] Confirm an operator can reach all three managers through the configured
  public HTTPS/proxy URL from another computer; shared storage writers must
  support exclusive leases and atomic replacement on the chosen filesystem.

Remaining donor scope is explicit in `doc/parity-webui.md`: grid settings,
map-center customization, arbitrary menu/page editing and resident postal contact
editing are not certified by this batch. Continue WhiteCore reconciliation before
moving to another donor.


## Native estate editing — 2026-10-24

Source/native SQLite acceptance checks cover persistence, race protection,
authority, escaped forms and preservation of unrelated estate policy. Use a test
estate with a known backup. Browser/Firestorm and production provider checks:

- [ ] Owner's My estates and administrator's Grid estates lists expose working
  Edit links. Residents cannot read or mutate another owner's estate, including
  by forged IDs or a stale owner-index entry. Anonymous editor requests fail.
- [ ] Owner saves name, reference price, public access, voice and direct teleport.
  Administrator can edit the same fields on another owner's estate through the
  administrator route. Verify UTF-8, field bounds and escaped text on reload.
- [ ] Confirmation explicitly says running simulators must reload estate data.
  Use RegionWeb Estate Admin’s Reload action on an allowed estate-scoped file
  (or a controlled simulator restart), relog in
  Firestorm and inspect native estate values. Check voice availability separately
  from its configured estate permission. Saving must not charge money or sell land.
- [ ] Before/after database snapshots show unchanged owner, managers, access and
  group lists, bans, experience lists, environment policy, parcel access override
  and region membership; a second estate must remain unchanged.
- [ ] With anonymous/payment and age restrictions enabled, explicit bans survive
  provider reload, WebUI save and simulator restart. Adding the same explicit ban
  twice must not duplicate it; adding a different ban must not be suppressed by
  general age/payment restrictions.
- [ ] SQLite estate region counts match actual linked regions after restart.
  Verify region links remain correct and other estates' membership is unchanged.
- [ ] Two browser editors of one estate: save one, reject the stale second editor.
  Race two service writers and a viewer edit; conditional updates must reject
  changed edited values/owner, while unrelated list/policy changes are preserved.
- [ ] Transfer estate ownership using an existing supported native workflow while
  an old-owner form is open. The old-owner save must fail, including a race after
  the form's initial state check. WebUI fields cannot transfer estate ownership.
- [ ] Reject cross-origin POSTs and resident requests to administrator routes.
  GET/query parameters must not mutate state; unsupported services hide save
  controls. Success is shown normally, with the simulator-reload requirement.
- [ ] Reload simulators before subsequent viewer edits. Record the outstanding
  behavior where an unreloaded simulator can write older in-memory settings back
  over a WebUI save; automatic live propagation is not implemented in this batch.
- [ ] Run the persistence/concurrency/ownership/no-op-save checks on actual MySQL
  and PostgreSQL schemas. Include case-only name edits, owner UUID binding,
  Boolean columns, missing/deleted estates and changed-row connection options.
- [ ] Verify service outage and response-loss behavior: do not claim confirmation
  after an uncertain save; reload current native values before retrying. Confirm
  unrelated legacy viewer estate actions still work after upgrading the binaries.

Remaining region work: durable simulator location/name/size/terrain configuration
and authenticated lifecycle administration, plus live estate reload propagation.
WhiteCore presets must not overwrite newer Continuum defaults or merely change
GridService registration records. Ownership transfer, deletion, creation and
access-override editing remain separately tracked in `doc/parity-webui.md`.


- [ ] Configure `[ContinuumWebUIRegionControlRooms]` with a test region UUID and
  its public HTTPS RegionWeb `/admin` URL. Its region-list link opens the existing
  simulator control room without triggering the region-profile row action.
  RegionWeb requires its own estate-owner authentication; a grid admin account
  does not automatically acquire simulator-owner privileges. Remove the mapping
  and confirm the link disappears. Verify malformed IDs and non-HTTPS/credential/
  query-bearing URLs fail configuration validation. No portal cookie is sent as
  a query parameter or reused as a RegionWeb session.


## WhiteCore closure: auctions, reminders and feature contracts — 2026-10-24

Source reconciliation/selected-port lane is closed; the broader WebUI/native
product backlog and runtime certification remain open. See the final WhiteCore
audit matrix rather than inferring completion from blocked-page counts.

Automated commands (each native service fixture has independent process state):

```powershell
dotnet build OpenSim.sln -c Release --no-restore --nologo -m:1 -v:q
dotnet build tests/Continuum.WebUI.Tests/Continuum.WebUI.Tests.csproj -c Release --no-restore
dotnet tests/Continuum.WebUI.Tests/bin/Release/net8.0/Continuum.WebUI.Tests.dll
dotnet tests/Continuum.WebUI.Tests/bin/Release/net8.0/Continuum.WebUI.Tests.dll --auctions-only
dotnet tests/Continuum.WebUI.Tests/bin/Release/net8.0/Continuum.WebUI.Tests.dll --event-notifications-only
dotnet run --project tests/Continuum.Auctions.Tests/Continuum.Auctions.Tests.csproj -c Release --no-restore
```

Fresh Search self-test also passed against a new temporary SQLite filename
containing `test`; never point it at operator data. MySQL/PostgreSQL runtime is
not certified by these SQLite checks.

### Auction grid portal

- [ ] Configure distinct matching bridge keys in test Robust and the selected
  simulator hosts. Map exact region UUIDs to HTTPS `/continuum/auctions/<UUID>`
  endpoints. Blank keys disable the regional bridge. Direct loopback HTTP is
  permitted; public plaintext and spoofed forwarded TLS must be rejected.
- [ ] With public TLS termination, only explicitly configured simulator proxy
  addresses may assert forwarded HTTPS. Wrong/blank bridge keys fail without
  exposing journals, pending purchase IDs or participant identities.
- [ ] A logged-out visitor can browse the paginated catalog without bid/seller
  forms. No bridge keys or bidder/purchase IDs appear in HTML, scripts or URLs.
- [ ] An authenticated resident without a viewer in the target region can browse
  but cannot reserve funds. Log in/teleport there in Firestorm; bid controls become
  available. Child, foreign, disabled, disconnected and in-transit clients fail.
- [ ] Start an eligible individually owned parcel sale as its seller with estate
  authority. Group/foreign parcels fail; configured duration/minimum are fixed.
- [ ] Explicitly confirm a bid. Verify actual available balance/reservation and
  conserved escrow through an outbid, own-bid replacement, cancellation and expiry.
  Retain all existing regional auction/recovery acceptance requirements above.
- [ ] A forged actor, parcel, auction ID or endpoint field cannot act for another
  resident or contact an unconfigured server. A grid administrator's portal role
  does not bypass native seller/estate cancellation authority.
- [ ] Lose an authorization reply, reload, retry the same pending amount and
  verify the same purchase ID. Lose a successful reply and verify the catalog
  reflects the existing high bid without a second debit. No automatic fresh bid.
- [ ] Unknown capture/delivery outcomes remain recoverable states. Operator
  review is required for corrupt/replaced parcels or mismatched restored backups.
- [ ] Browse beyond four regions and twenty listings in one region. Older open
  auctions remain reachable. Owner/high-bidder history remains identity scoped.
- [ ] Region outage/denial hides financial controls and warns that existing holds
  may remain active. After recovery, pending state is still available.
- [ ] Auction POSTs require an authenticated portal session, HTTPS account
  transport, same origin and the per-account operation bound, including mixed
  route casing. GET/query parameters cannot bid, open or cancel.
- [ ] Test actual mobile/desktop/viewer layout, keyboard access, long/Unicode text,
  maturity expectations and operation across more than one simulator host.
- [ ] Repeat storage/ledger/restart recovery on dedicated MySQL and PostgreSQL
  grids, including removal/reload of a region's bridge handler. An occupied route
  must not be removed or hijacked by a failed second module startup.

### Event reminders and retained viewer contracts

- [ ] Set a dedicated matching EventNotificationSecret in Search, simulator
  `[Search]` and `[LoginService]`, with HTTPS EventNotificationURL except direct
  loopback. It must differ from the portal event/classified write key.
- [ ] In Firestorm, subscribe to an upcoming event. Restart simulator/Search and
  relog through XML-RPC and LLSD: the reminder returns with correct event ID,
  event_date_ut and ISO UTC event_date. Verify the viewer's local reminder timing.
- [ ] Remove a reminder, repeat removal, restart and confirm it stays removed.
  Repeat addition and confirm exactly one subscription. Another account must not
  receive or alter the subscriber's list through native viewer requests.
- [ ] Missing, deleted and started events are absent on login. Subscriber limit
  is 100; test duplicate submissions, concurrent add/remove and admission bounds
  on each provider. No new server IM/email delivery system is implied.
- [ ] Wrong/missing keys and public HTTP endpoints fail. Bound/timeout the reply;
  an unavailable directory must not fail grid login. Reconnect and retry native
  subscriptions explicitly after recovery; do not claim success during an outage.
- [ ] Exercise repeated native HTTP listener teardown/replacement separately:
  recycled in-process test listeners exhibited timeouts. The isolated fixture
  passes do not certify that lifecycle. Test service restart and multi-listener
  startup with the actual chosen deployment configuration.
- [ ] Firestorm enables preference hover height when SimulatorFeatures publishes
  AvatarHoverHeightEnabled. Apply it, relog and verify persisted hover preferences.
  GetFeatures snapshots must retain existing mesh/physics/Experience extensions
  without sharing mutable state with callers.
- [ ] Retest native task-script/notecard uploads (success and compiler failure),
  uploader cleanup/timeouts, GroupMemberData title/power shapes, account/display
  name consistency, HG identity/privacy and registered Experience seed responses.
  These retain newer native implementations, not older donor service internals.

The existing product backlog (11 legacy route dispositions, full portal
modernization and the missing native service workflows) remains authoritative.
No live deployment, paid bid, user message or operator schema modification was
performed as part of source acceptance.


## WhiteCore remaining workflow closeout — 2026-10-24

This section supersedes the earlier 11-blocked-route implementation gap list.
It is an acceptance queue, not a claim that the current test grid was deployed.
The user explicitly chose currency credits without a real-money provider for the
purchase pages; quoted cash prices must never be presented as settled receipts.

Final source automation: full Release and both harness builds passed with zero
warnings/errors. Main WebUI, isolated currency credits/auctions/reminders,
auction lifecycle/escrow, fresh Search self-test and browser console event-handler
checks all passed. Windows sandbox restrictions required authorized temporary-file
and loopback access for the disposable fixtures. No live deployment was tested.

Automated checks for the new source:

```powershell
dotnet build OpenSim.sln -c Release --no-restore --nologo -m:1 -v:q -p:UseSharedCompilation=false -nodeReuse:false
dotnet build tests/Continuum.WebUI.Tests/Continuum.WebUI.Tests.csproj -c Release --no-restore --nologo -v:q -p:UseSharedCompilation=false -nodeReuse:false
dotnet tests/Continuum.WebUI.Tests/bin/Release/net8.0/Continuum.WebUI.Tests.dll
dotnet tests/Continuum.WebUI.Tests/bin/Release/net8.0/Continuum.WebUI.Tests.dll --currency-credits-only
node tests/Continuum.WebUI.Tests/console-client-checks.cjs
```

Keep the auction and event-notification HTTP fixtures in their separate processes
as documented above. The console fixture queues an inert `show info` string in a
controlled console object; it does not execute commands against a running grid.
Credit tests use newly created disposable SQLite accounts, not live payments.

| Workflow | Required browser/native/provider acceptance |
|---|---|
| Portal reset | As admin, reset navigation and presentation independently; confirm stale revisions and unchecked confirmation cannot save. Check welcome, support and slideshow defaults, while news, native registration policy, account data and ledgers remain unchanged. Resident/anonymous/cross-origin requests must fail. |
| Grid identity/login | Keep GridConfigurationPath blank until selecting the actual loaded ROBUST INI containing GridInfoService, LoginService and ContinuumWebUI. Back up the original. Save native grid name/nick, login welcome and matching portal SystemName; inspect the exact same-directory backup and preservation of every unrelated key/comment. Verify concurrent external edits are rejected where detected. Arrange a native ROBUST restart, then verify grid-info, portal and viewer welcome. Included files, other simulator hosts and LoginService.MessageUrl can override these settings; verify their effective values. Protect the INI, directory, lock/temp files and backups from public serving and untrusted access. |
| Private contacts | Enable ResidentContactEnabled only with the operator's ContactPrivacyNotice. Test native SQLite, MySQL/MariaDB and PostgreSQL migrations and persistence, Unicode and maximum lengths, save/clear and consent. Contact JSON must be one native storage write. Neither public profiles nor user_data_request/user_data_update may expose/alter continuum.private tags, including case/whitespace variants; public app-data tag identifiers also reject non-printable/non-ASCII values to prevent database collation aliases. Verify resident isolation, disabled/foreign accounts, HTTPS/trusted proxy origin enforcement, service failures, backup retention and explicit clearing before account closure. |
| Account settings | Verify the combined settings page links to the working password/email/contact/closure pages and contains no change_user_information legacy action. |
| Account closure | AllowAccountClosure is opt-in. Populate AccountClosureProtectedIDs with every grid system/service account. Test current-password confirmation, wrong-password rate limit, no GET mutation, protected/privileged/HG account rejection, online viewer and estate-owner guards, native UserLevel=-2 archive, concurrent/replayed writes and preservation of all other account fields. Check new portal/viewer logins fail even with a negative configured MinLoginLevel, cookie/current web token is cleared, and other web sessions fail fresh account checks. Check in-flight viewer login races and cached remote service identities; this is not a global active-session kill or data-erasure contract. Retain inventory, land, profiles, balances and history; test operator reactivation separately. |
| Partnerships | AllowPartnerships is opt-in. Test actual provider migrations, persistent private proposals, one outgoing/20 incoming limits, seven-day expiry, exact replay binding to ID/recipient/message, sender withdrawal and recipient-only accept/decline, stale IDs and invalid/disabled/HG targets. Inject a failure in the second profile update and verify rollback of the first; race competing accept/dissolve operations on every provider. Both native profilePartner values must change together and unrelated profile fields must survive. Refresh Firestorm profiles and relog. No fee, email or IM is generated; residents respond through their authenticated portal page. Verify escaping and 30 operations/account/15-minute limit. |
| Currency credit pages | Resident identity must come from the authenticated session; admin account lookup must require current admin authority. Read only successful positive type-5010 credit adjustments; exclude failed adjustments, debits, transfers and other residents. Traverse more than 50 records with identical timestamps and verify no omissions/duplicates on SQLite, MySQL/MariaDB and PostgreSQL. Test invalid private key/cursor, unavailable backend versus an empty result, native restart persistence, 64-bit amounts and escaped descriptions. No cash amount, paid status, external receipt or payment-provider settlement may be invented. |
| Region management | Configure the fixed HTTPS RegionWeb control-room URLs for each host/region. Test both user/admin legacy routes and region-list navigation. Ownership must be checked against fresh native estate data, including ownership transfer with stale grid registration; neither posted URLs nor grid-level role may confer owner authority in RegionWeb. Sign into the independently authenticated native control room, select the correct region definition, save with its existing backups and exercise supported reload/restart behavior. Verify registry refresh after name/location/size/port edits. The portal does not proxy file writes or silently share host sessions. |
| Native consoles | Enable native REST consoles on selected simulator/ROBUST hosts; configure unique Network.ConsoleUser/ConsolePass and exact portal ConsoleAllowedOrigin, trusted TLS and any reverse-proxy prefix rewriting. Add only fixed HTTPS base URLs in ContinuumWebUIConsoles. From an admin browser, connect, read output, submit harmless diagnostic commands/prompt responses and disconnect. Verify native credential rejection, no resident page access, secret clearing, no browser storage, redirects refused, escaped output, bounded responses, interruption/unconfirmed command handling without automatic retry, navigation cleanup and 15-minute expiry. Confirm native polling handlers are removed on close and expiry. Do not execute restart/shutdown/destructive commands just to prove the UI can submit commands; rehearse intentional operational changes in the isolated deployment. |

The region routes intentionally use existing native host control rooms and the
console page uses the existing native RemoteConsole protocol. A new shared SSO
session, a central host supervisor and external real-money settlement are not
implemented by these adapters. They must not be advertised as such. None of the
work in this closeout was deployed to the surviving test grid.

## Separate content concept: Starfleet headquarters (2026-10-24)

This is independent of donor reconciliation. `docs/starfleet-hq/README.md` defines
a 114 × 80 m OpenSim lobby/welcome blockout with south entrance, turbolifts only,
and a continuous 31 × 80 m atrium (revision 2; other floors unassigned). Generated
geometry passes bounds, non-overlap, complete-area and doorway-to-corridor checks.
No prims, scripts, terrain or live region configuration have been changed.

Required in-world acceptance before treating the building as complete:

- Confirm placement, parcel boundaries and chosen finished-floor elevation.
- Walk from the south entrance to every room with representative avatars; check
  third-person camera clearance, 3 m/4 m door openings and clear corridor widths.
- Check collision and physics of modular floors, walls, doorways and ceilings;
  verify no overlapping shared walls or gaps in the shell.
- Fit and test all four corner turbolift banks, destination landing clearance,
  floor selection and return travel after region restart. No stairs are planned.
- Check welcome reception flow and all six atrium-side openings to both wings.
- Confirm future floor elevations and atrium vertical extent before adding slabs.
- Test welcome-area furniture circulation, viewer rendering and script load
  on the intended region before duplicating upper floors or exporting an OAR.

These are pending build acceptance checks, not evidence of a live completed build.

Subsequent mesh blockout: `docs/starfleet-hq/blender/README.md` records the shaped
first-floor shell generated with Blender 3.6.23: 7,482 m², 105 floor tiles and
152 shell/core panels (historical mesh v1). Current mesh v2 replaces this with
smooth curved wings and ribbon windows: 7,984.840 m², 20 mesh objects, 8,128
triangles. Blender Collada reimport, manifold and nondegenerate-face checks pass;
no measured land-impact claim is made. Verify window transparency/materials and
curve smoothing in the actual viewer. The prior rectangular room schedule is historical for this
shaped-shell phase. Before upload acceptance, author section-specific LOD/physics,
verify Collada scale/origin and material handling in the actual viewer, and test
the south opening, hollow lift cores and floor seams with avatars. No furnishings,
doors, working lift systems, upper floors or multi-storey atrium height are complete.

### Region-scale hybrid master supersedes the shell concept

The user released the old footprint, selected studies 1 and 2, and authorized a
complete Blender master with textures. The approved starting programme is four
wing levels and unequal twelve/eight-level towers, welcome/public spaces below,
meeting/training rooms in the wings and office/administration tower rooms.
`docs/starfleet-hq/hybrid-master/README.md` records the current first architectural
draft and remaining work. Earlier shell-only statements above are historical.

The refined 2026-10-24 draft has 129 programmed zones, basic furnishings, 157
unscripted door leaves, four shaft/cabin stacks, upper galleries and a skybridge.
It has 21 saved/packed texture maps, 470 visual mesh nodes and 124,968 triangles.
Blender checks passed UV presence, six-or-fewer materials per mesh, region bounds,
manifold/nondegenerate geometry, shaft-centre clearance, Collada reimport node
count and resolved base-colour image references. These are authoring checks only.

Pending acceptance: review silhouette and every room route with representative
avatars; verify floor/wing/tower/bridge landings and all lift clearances; finish
matching upload physics and lower LODs; configure and inspect diffuse/normal/
roughness or PBR materials, transparency and light surfaces in the actual viewer;
script/test doors and lifts including restart recovery; measure uploaded object
cost, culling, rendering and simulator performance. The standalone collision
reference export is not a certified matched uploader physics file. Nothing in
this batch was uploaded, rezzed or deployed to a live region.

### Standalone Horizon Gate replaces the rejected hybrid

The user rejected the combined version and authorized Horizon Gate alone.
`docs/starfleet-hq/horizon-gate/README.md` is the current architectural record.
The standalone Blender 3.6.23 draft has 12/8-level leaning pylons, a bridge at
Z=56 m, a low welcome hall and a 31 × 80 m atrium. It includes interior partitions,
ceilings, basic furnishings, two true shaft stacks/cabins, 139 unscripted leaves,
118 zones, 322 visual meshes, 80,188 triangles and 22 saved/packed texture maps.

Its own checks passed UV presence, six-or-fewer materials per mesh, manifold and
nondegenerate geometry, region bounds, door/shaft/bridge centreline clearance,
Collada mesh-node reimport and resolved base-colour references. These do not
certify full avatar/camera clearance, every room route, final art or live acceptance.
The hybrid remains an independent historical checkpoint.

Pending standalone tests: inspect the selected silhouette and room/window fitout;
walk all pylon rooms, welcome areas and bridge approaches with representative
avatars; test door/lift scripts and restart recovery; finish/test matched upload
physics and lower LODs; configure viewer alpha/normal/roughness or PBR materials;
verify upload origins, landscaping placement, actual cost and rendering/simulator
performance. The collision reference export is not a matched uploader physics
set. No live upload, rez, terrain change or deployment occurred.

## Horizon Gate editable exterior — 2026-10-24
Separate content-design work, with no donor or live grid status change. The user prefers option 2 and requires closer reference detail, no interior partitions/furniture, and removable components. Active files: docs/starfleet-hq/horizon-gate-shell/README.md and validation.json. Earlier fitted models are historical.

Pending live/manual checks: visual fidelity against the selected concept; every exterior entry, bridge socket, terrace edge and future lift void; all assembled seams (authoring ray checks are sampled); upload grouping, LODs and matched physics; viewer glass, maps and lighting; working door/turbolift scripts; measured land impact and region performance. No upload or live acceptance is claimed.

## Presidio Command alternative study — 2026-10-24
The user requested a new reference-led direction based on Trek footage/pictures/games. docs/starfleet-hq/presidio-study contains an actual Blender mesh study and four renders; it is not selected and does not supersede Horizon Gate. Topology/UV checks pass (799 architectural meshes, 78,212 triangles); no envelope ray or live checks were run. Pending if chosen: resolve all hall/wing/command connections and apertures, floor plans and turbolift cores; verify every seam and avatar route; create upload physics, LODs and grouping; verify textures/materials and measure land impact. No interior partitions/furniture, deployment or live acceptance is claimed.

## Vector Command alternative — 2026-10-24
User requested another Blender concept after Presidio. docs/starfleet-hq/vector-study/review.html contains actual renders; this is an unselected alternative. Mesh manifold/face-area/UV checks pass: 856 architectural meshes / 38,620 triangles. No interior partitions, furniture or stairs. Resolve connections, roof supports, apertures, floor layouts and turbolift cores if selected; then verify every assembled seam/avatar route, upload grouping, LODs, physics, viewer materials/scripts and measured land impact. No envelope ray or live checks, deployment or acceptance is claimed.

## Aster Command user-reference response — 2026-10-24
The user supplied two screenshots of their own prim-built campus and challenged the preceding plain mesh studies. docs/starfleet-hq/aster-study contains an actual Blender sculpted-shell alternative and user-reference comparison. Unselected exterior study only. Manifold/face-area/UV checks are recorded in manifest.json. Pending: every shell/glass/crown junction, tower/podium roof/floor intersections, entrance apertures, internal layouts and lift voids; assembled seam/avatar testing; upload grouping, LODs/physics, viewer materials/scripts and land-impact measurement. No interior partitions/furniture/stairs, live deployment or acceptance is claimed.

## Direct user-tower reconstruction — 2026-10-24
User rejected Aster and authorized trying a direct reconstruction of their central prim-built tower. docs/starfleet-hq/reference-tower/review.html compares an unaltered-source viewport crop with actual mesh renders. 759 architectural meshes / 98,048 triangles; manifold/area/UV checks pass; floor protrusions corrected after render inspection. Dimensions, ten levels, unseen surfaces and camera angle are inferred, not measured or accepted. Pending: visual fidelity review, all crown/shoulder/curtain junctions and seam tests, real entrance aperture behind separate leaves, lifts and circulation, upload grouping/physics/LODs, viewer materials/scripts and measured land impact. No partitions/furniture/stairs, deployment or live acceptance.

## Horizon Gate refinement and live user-reference clarification — 2026-10-25
User clarified: all their snapshots show buildings in a current live OpenSim grid, including a design dating to 2016. Those establish the in-world quality standard to exceed. Generated Horizon and nineteen additional sheets are visual proofs of concept; embedded stairs/fitout/dimensions do not override the shell scope. Replication and plainer alternatives did not meet the brief.

New isolated Horizon refinement: docs/starfleet-hq/horizon-gate-refined/review.html, generated by generate_horizon_refined.ps1. 826 separate meshes / 177,828 evaluated triangles; manifold/face-area/UV/material checks pass; 22 packed images; 826 matching reimported Collada nodes. New rounded façade frames, sail crowns, bridge portals/haunches, terraces and canopy retain editability and empty interiors. These checks do not inherit the former shell's envelope sampling. Pending: all new junctions, full circulation/entry/bridge clearances, crown transitions/roof gardens, reference fidelity, botanical/material finish, upload grouping, LOD/physics, working scripts and measured live performance. The user's live buildings do not establish acceptance of this new mesh. No grid deployment occurred.

## Horizon concept multi-view correction — 2026-10-25
The user rejected refined mesh fidelity and requested multiple concept views beyond the front. Mesh detail is on hold. docs/starfleet-hq/horizon-gate/multiview.html shows a built-in imagegen interpretation of the original target: six views, including proposed unseen geometry. The roof overview is pictorial, not a measured orthographic plan. Reconcile floor counts, setbacks, crown depths, roof volumes and bridge connections across views before modeling; no geometric consistency or live acceptance is established by this image. Earlier mesh authoring passes remain distinct from rejected visual acceptance.

## Horizon reconstruction from multi-view concepts — 2026-10-25
User explicitly authorized building from the new conceptual views with all visible detail and no arbitrary prim budget. This supersedes the preceding mesh hold. Fresh master and six actual views: docs/starfleet-hq/horizon-gate-multiview-build/review.html; generator generate_horizon_multiview.ps1. Earlier rejected models are preserved. The build reconciles 12/8 occupied levels, bridge 57.6 m, curved hall, continuous crown shells, ceramic side blades, front/rear terraces, roof oculus and modeled architectural/planting details. No partitions, furniture or stairs; floor lift reservations follow the leaning towers.

Validation.json records evaluated topology/UV/material checks, sampled tower/crown/welcome roof/front/bridge coverage and clean Collada node identity. Sampling is not exhaustive seam or avatar certification. Pending: exact reference fidelity and window rhythm, crown/bridge/roof junctions, all entrance/north/railing/lift clearances and routes, material/planting finish, working doors/turbolifts, practical upload grouping, matched physics/LODs, viewer maps/alpha and measured live grid performance. No deployment or live acceptance is claimed. The user's current live builds remain the standard to exceed.

## Welcome scale and terrace access correction — 2026-10-25
User identified the welcome hall as oversized and the balconies as inaccessible. Current central hall: 114 m wide / 43 m max depth (~3,874.72 m² central floor), reduced from 170 × 81 m, with small rounded tower links. This supersedes the earlier 31 × 80 m reservation. Previous master, renders and sources preserved in horizon-gate-multiview-build/checkpoints/before-welcome-access.

Fourteen front/rear terrace openings now cut all facade and wall-side guard layers, with flush thresholds and separate leaves stored open. Doorway schedule: terrace-access.json. Checks PASS: 168 clearance rays over 1.1 m width / 2.4 m height, 630 level-floor probes, fourteen routes; parked leaves included in geometry. Updated total: 1,417 meshes / 628,468 triangles, 21 packed maps, 10,802 sampled assembled surface rays, matching 1,417 Collada nodes. No sample misses or route errors. This is geometric sampling, not a live avatar or physics test. Remaining: automatic doors/lifts, north/connector circulation and all broader avatar routes, visual fidelity, upload grouping/LOD/physics/material and grid performance checks. No live deployment occurred.

## Manual in-world visual preview package — 2026-10-25
User asked to see Horizon in OpenSim, then selected prepare files for manual upload. Firestorm was already logged in; active user input blocked tool clicks. No upload, rez, fee, message or existing in-world object modification was performed. User will handle the upload.

prepare_horizon_upload_preview.py creates a separate 209-piece copy with no piece above 55 m, preserving the full high-detail geometry and original master. A duplicate coplanar-face issue at split boundaries was found and corrected; final surface-area preservation and clean 209-node reimport pass. Package Horizon_Gate_OpenSim_PREVIEW.zip and upload-preview/START-HERE.md specify scale 1, Include textures, Use LoD above for initial appearance, temporary Bounding Box physics and whole-object Phantom after rez. Full-detail LODs are not optimized, and phantom preview is not walkable floor physics. Live material interpretation, upload cost/resource use, efficient LODs, matched collision and avatar acceptance remain unverified.

### Manual preview physics correction (2026-10-25)

The user’s first upload attempt failed with Missing physics_convex block.
Use Horizon_Gate_OpenSim_PREVIEW_v2.zip and the revised upload-preview/START-HERE.md:
load the matching PHYS DAE in Physics → From file, Analyze, then calculate fees.
All 209 closed convex proxies pass positive-volume and matching clean-reimport
checks. This is still phantom display physics, not walkable collision. No assistant
upload occurred; successful user retry and live appearance remain unverified.

### Interior rejection and repair candidate — 2026-10-25

User reports misplaced interior pieces, welcome roof through occupied floors,
misaligned doors/jambs and balcony defects. Current released master and v2 upload
are REJECTED for interior use; the physics correction does not resolve assembly.
`interior-audit.json` confirms welcome roof/fascia intersect E_L02 and west
connector roof intersects W_L02; both entries had fixed center posts.
`horizon-gate-interior-candidate.blend` is a separate, unexported repair candidate.
Occupied-envelope clipping removes those three detected roof/floor intersections;
entry center posts and transom obstruction removed; door leaf dimensions adjusted
for jamb/header clearance. `interior-candidate-audit.json` records no remaining
intersections in that narrow check. Balcony guards are clipped at occupied tower
volumes, but doors, balconies, stray pieces and full avatar routes still require
systematic interior visual/geometric review. No complete interior acceptance,
new upload package or live validation is claimed. Do not recommend retrying v2
as a solution to the interior defects. Generator includes the candidate repairs;
the old master/exports are retained as rejected historical artifacts.

2026-10-25: User requested more realistic modern/futuristic concepts with all views.
Three generated six-view sheets are in docs/starfleet-hq/modern-concepts/review.html:
Crescent Court, Meridian Terrace and Compass Hall. Exact built-in image_gen prompts
are saved in prompts.json. These are concept artwork with cross-view discrepancies,
not Blender geometry or validated construction drawings. No design selected; no
new mesh built. Previous Horizon interior rejection remains in effect.

### Crescent Court selected — 2026-10-25

User authorized starting Crescent Court. Development checkpoint 01 is separate
from rejected Horizon: docs/starfleet-hq/crescent-court/review.html and
crescent-court-development.blend, generated by build_crescent_court.py.
Five wing floors at 4.8 m spacing, eight tower occupied levels, double-height
welcome hall with roof top 9.25 m below tower floor 9.6 m; top-floor terrace
setbacks. Shared opening/frame/leaf datums; separate editable parts, 21 packed
texture maps. Actual six-view, interior, terrace and lower-floor renders exist.
Five doorway routes pass 45 clearance rays and 45 flush-floor support probes;
no positive-overlap roof/floor triangle intersections. Low facade bands across
doors and missing landing floors were found and corrected before this checkpoint.
This is DEVELOPMENT, not concept fidelity, complete interior or live acceptance.
Tower/wing interface review, lift access/guards/cabins, detailed architecture,
materials/planting, scripts, LODs and walkable physics remain. No Collada upload
package issued, no live action. Do not describe this initial shell as finished.

Crescent Court draft 02 (2026-10-25): authorized exterior refinement implemented
in build_crescent_court.py plus refine_crescent_exterior.py. Current review and
blend updated; draft 01 preserved in crescent-court/checkpoints/draft-01.
Broad ceramic ribbons/reveals, swept wing roofs, tower piers/side cladding,
observation cap/soffit and welcome facade/signage added. Final 657 tagged meshes,
21 packed maps; five doors retain 45 passing clearance rays and 45 floor probes,
no detected positive-overlap roof/floor intersections. Actual renders inspected.
Still development, not exact artwork match or upload ready; glazing/landscape,
interfaces, lifts, LODs, physics and live acceptance remain. No live action.

### Crescent Court hybrid reset — 2026-10-25

User rejected visual fidelity of draft 02 and authorized trying again with native
prim floors/walls and mesh curved exteriors. Current visual study is separate:
docs/starfleet-hq/crescent-court/hybrid-study/review.html, generated by
build_crescent_hybrid_study.py. It is NOT another accepted assembly or upload.
Front artwork guides proportions, overhead guides footprint, side views guide
south-rising wing roof profiles. Broader wings, narrower/taller tower and taller
welcome hall are modeled fresh. Six actual Blender views and a diagnostic floor
construction view are supplied alongside the reference. Match is still unapproved.
145 native box-prim PROXIES plus native-prims.csv are supplied for later native
reconstruction; no OpenSim prims are created by these Blender boxes. 19 mesh
floor-edge pieces exclude the rectangular prim cores. Native cores cover 3392
of 4065.80 square metres per wing floor (about 83%); 21 texture maps packed.
Per-floor containment and area conservation pass in floor-checks.json. This
narrow check does not validate connections or routes. Doors, terrace access,
hall/wing/tower interfaces, native prim rez/import, lifts, LODs, walkable collision
and live appearance remain unresolved. No previous 45-ray route pass is carried
forward to this new study. Drafts 01/02 are historical; no live action occurred.
### Modular all-mesh Crescent Court — 2026-10-26

User explicitly requires all-mesh pieces assembled in-world, full detail and all
six reference views. Native prim grids are superseded. Current source is
build_crescent_modular_reference.py; review/blend are in crescent-court/modular-reference/.
Manual roof-view Bezier trace replaces constant-width wings, tower moves north,
and rear/side cladding is reconciled. Six reference/model pairs and a trace overlay
are included; view-reconciliation.md records each view and unresolved differences.
Exact likeness is not established. 909 separate mesh modules, no native prims.
Large parts use shared 30 m cut planes without decimation. assembly.csv records
module positions, dimensions, source identity and materials. Closed-manifold and
floor-area preservation checks PASS: 36379.818965 m2 before, 36379.817974 m2 after.
These are geometry checks only. All renders inspected; visible facade finish,
entrance/terrace openings and interior junctions still require work. Detail,
lifts, LODs, collision, individual exports and assembly workflow/live acceptance
remain. No old route pass is carried forward; no live action or upload claimed.
### Entrance and tower reconstruction — 2026-10-26

User asked to continue correcting the entrance and tower shapes, not just trim.
The same modular-reference review/blend is updated, with six overall views and
new entrance-detail.png / tower-detail.png. Previous files and generator are
preserved in checkpoints/before-entrance-tower. Source helpers:
detail_crescent_entrance_tower.py and check_crescent_entrance_tower.py, invoked by
build_crescent_modular_reference.py before spatial division and rendering.

Arrival pavilion now has a genuinely bowed floor/glazing/roof outline, ceramic
roof ring and glazed skylight, curve-based mullions/transoms, shared entrance
jamb/header/leaf datums, layered rounded canopy, cove, delta and closed-solid
lettering. Separate leaves are stored open and unscripted. Apron overlap removed;
pavilion parts clip at wing/tower occupied envelopes to avoid upper-floor intrusion.
Welcome roof rim top is 11.95 m. A detected rim/tower-floor contact was corrected
with 0.04 m hidden interface clearance. Tower ceramic pier reveals, vertical
mullions, dark floor spandrels, separate observation canopy/soffit/cove/posts and
command marker are added; upper side blades stop below the observation floor.

Final narrow checks PASS: 18 clearance rays and 18 floor probes across arrival
and tower portal; no selected roof/floor triangle intersections. All 982 mesh
modules pass closed-manifold checks and floor top area is preserved:
36474.3500107 m2 before / 36474.3490406 m2 after. A nonmanifold lettering edge was
found and corrected by closed voxel remeshing. Floor checks also pass. This is
not complete interior, visual fidelity, avatar, physics or live acceptance.
Terraces/gardens, other openings, all interior routes, lifts, LODs, collision,
module exports and in-world assembly remain. No live action or upload occurred.
### Prim rezzer prototype and reference inconsistency — 2026-10-26

User asked about a native-prim rezzer and later mesh conversion, then authorized
trying it. User subsequently noticed the six concept images do not match. This
is acknowledged: wing outlines, tower footprint/front/rear details, welcome shape
and roof profiles conflict. Do not present them as consistent surveyed drawings
or promise simultaneous exact reproduction. Reconcile a single design first.

Local package docs/starfleet-hq/Crescent_Court_Prim_Rezzer_PROTOTYPE.zip contains
CC_Controller.lsl, CC_Seed.lsl, CC_TEST.txt (15 prims), CC_BUILD.txt (411 prims),
parts records and START-HERE.md, plus existing 21 maps for manual material setup.
Scope is OVAL TOWER + ENTRANCES only, no wings or completed full-building claim.
Generator generate_crescent_prim_rezzer.py uses Blender's bundled Python to write
and validate the records. Data shape/dimension checks pass; LSL compilation and
live rez are PENDING, not certified. Maximum component dimension 48 m. No OSSL
required. Owner must supply a CC_SEED inventory object and CC_BUILD notecard.
Build/Stop/Move/Remove use a private per-controller channel and session, sequential
part acknowledgements, no automatic duplicate rez on timeout. Do not reset the
controller while preview remains: it would lose session Move/Remove control.
No live viewer actions, exports, mesh conversion, rezzing or uploads performed.
Conversion later is optional per-section Collada plus cleanup/LOD/physics, not an
automatic fidelity/performance improvement. Prim originals should be retained.
### Complete native-prim building rezzer — 2026-10-30

User explicitly rejected the tower-only prototype scope and authorized the entire
Crescent Court BUILDING, for now, in one rezzer. Complete package is
 docs/starfleet-hq/Crescent_Court_Prim_Rezzer_BUILDING.zip
with prim-rezzer-building/review.html and START-HERE.md. Generator is
 generate_crescent_prim_building.py; source prototype and old mesh studies preserved.
872 editable native prim records include both five-level curved wings, eight-level
oval tower, welcome hall, front/rear entrances, windows, roofs, trim, two top-wing
terraces and central lift cabin/shaft guards/gates. No office partitions/furniture.
Wing floors use three large pieces per level. First five tower/wing floor datums
align: 0/4.8/9.6/14.4/19.2 m; upper tower floors 28.8/38.4/45.6 m. Upper spacing
is a reconciled design decision, not exact artwork reconstruction. Max native
prim dimension 106 m before quarter-profile cut. Local default NonPhysicalPrimMax
is 256; seed checks returned size/hollow and stops on clamp. Target grid unknown.
Controller/Seed/Lift scripts all COMPILE PASS using offline YEngine compiler
(source tool docs/starfleet-hq/tools/RezzerScriptCheck). No scene/runtime invoked.
The preview is reconstructed directly from prim records by render_crescent_prim_building.py,
not an in-world screenshot. 15 routes / 135 clearance rays / 135 support probes
PASS in that geometric reconstruction. Native mesher/collision and script runtime,
lift/rider/gates, move/remove/keep, textures, cost/performance remain live gates.
CC_BUILD is the FULL definition; optional CC_TEST is not the requested deliverable.
Owner creates CC_SEED with CC_Seed and CC_Lift scripts, puts seed + CC_BUILD card
in controller with CC_Controller. Optional CC_MATERIALS card maps uploaded texture
UUIDs line by line. 21 maps included, no automatic asset upload or invented UUIDs.
Keep removes rezzer scripts while retaining cabin/gate scripts; other idle lift
copies are removed after placement. Move restores recorded offsets; do it before
manual edits. Do not reset the controller before Keep/Remove. No live operation,
rez, upload, fee or asset conversion performed. Exact reference likeness is not
claimed; inconsistent concept views and remaining visual acceptance stay explicit.
### Rezzer zero-acknowledgement timeout repair — 2026-10-30

User reported the live controller timed out with Created 0 prims. This is a
failed live attempt, not acceptance; the count measures acknowledgements and
cannot establish whether no seed appeared. Original generic message cannot
identify the root cause. Updated prim-rezzer-building CC_Seed initializes from
on_rez's argument without reset; controller requires a ready handshake, retries
messages up to 45 ticks, reports no object_rez vs silent seed vs configuration
failure, and sends only the current material mapping. No duplicate rezzing.
All three revised scripts COMPILE PASS offline; no live execution by agent.
START-HERE now explains replacing both scripts AND the stored CC_SEED inventory
object, removing only the failed attempt before resetting, and obtaining the
specific new diagnostic if needed. Previous delivered scripts preserved under
docs/starfleet-hq/checkpoints/prim-rezzer-before-handshake-fix. Building geometry
and CC_BUILD unchanged. Full ZIP updated; live root cause/rez acceptance pending.

### Notecard paging after 553 acknowledged parts — 2026-10-30

User reported Bad CC_BUILD record at line 554, Created 553 prims. This confirms
user-reported placement progress, not complete geometry/runtime acceptance.
Source record is valid (11 fields). In LF text, zero-based line 554 spans
65,458–65,594 bytes: likely viewer truncation at 65,536, but saved live card has
not been inspected. Previous packaging required pasting ~105 KB into one card.
Now use CC_BUILD_01/02/03 with 300/300/272 records, each below 48 KB. Updated
controller requires all three, verifies each card's EOF count, reports card and
one-based line/field count on malformed input. Generator writes these pages and
asserts 872-part contract; exact ordered reconstruction checked. All scripts
COMPILE PASS offline. Source geometry unchanged. User must Remove using the old
controller BEFORE replacing/resetting it, then install controller/cards and
rebuild; no live resume promise or automated grid action. Previous source kept
in docs/starfleet-hq/checkpoints/prim-rezzer-before-split-cards. Full package and
small Notecard_Update ZIP updated; live full-building acceptance remains pending.

### Waypoint Command: model-first replacement concept — 2026-10-30

User rejected Crescent Court's live appearance and explicitly requested a concept
the agent can actually produce. New independent exterior concept is Waypoint
Command: docs/starfleet-hq/waypoint-command/review.html and waypoint-command.blend,
source build_waypoint_concept.py. All six views (arrival/front/rear/west/east/top)
are Blender renders from the SAME saved scene, not generated concept pictures.
Footprint envelope 98.8 x72m; above-datum height32.75m; six tower floors and three
wing floors on4.8m datums. Separate curved floors, rounded tower corner shells,
blue glazing, ceramic bands, metal frames, low south pavilion, rear entrance,
roof terrace canopies and courtyard. 1,577 architectural Blender objects are NOT
an OpenSim prim count or land-impact measurement. Three existing BaseColor maps
packed; model-space box projection needs UV baking for export. Geometry source
and .blend exist; renders visually inspected across all views. No new live grid
operation. Exterior concept ONLY: interiors, lift shaft floor apertures, working
lifts, UV/export/LOD/physics and live acceptance unfinished. Do not promise these
are solved or package as a ready building. Prior rejected concepts/rezzer remain
historical, untouched by the new concept. No claim it reproduces Crescent Court.

### Waypoint visible material revision — 2026-10-30

User said the concept had no textures. Prior scene only used three BaseColor
maps with subtle variation; most surfaces were plain colours. Revised actual
scene/source now packs ten PNG maps (four BaseColor plus normal/roughness pairs
for ceramic, metal and terrazzo), supplies copies under waypoint-command/textures,
and connects normal/roughness nodes correctly as Non-Color data. Explicit
Surface_metres UVs replace object box projection. Visible ceramic/paving panel
joints and metal tint are shader nodes, not painted image overlays. All six
views regenerated plus entrance-detail/facade-detail; closeups visually inspected.
Previous model/source/initial renders retained in checkpoints/before-material-detail.
Review and MATERIALS.md explain that procedural joints need export baking.
Footprint/1577 architectural objects unchanged; interiors/lift openings, working
lifts, baked export textures, LOD/physics and OpenSim live acceptance unfinished.
No live grid operations or uploads occurred.

### Tranquillity: storage, mesh uploads and reflection probes

- [ ] On a disposable SQLite region upgraded from RegionStore v43, write/update
  module extras for two regions, restart, and remove one key without changing
  the other region. Load prim inventory with both empty and NULL descriptions.
- [ ] On disposable PostgreSQL, update a known authentication field for one
  test account; confirm the other account is unchanged and a missing account
  or unknown field returns false.
- [ ] In Firestorm, upload a named multipart mesh, verify each label in the
  linked object and after relog; check a legacy upload without instance names.
- [ ] Set a reflection probe above 1 ambiance; save/restart and verify ambiance
  and clip distance are retained and LSL flags read back as an integer.

### Tranquillity: script Experience links in region storage

- [ ] On disposable SQLite, MySQL/MariaDB and PostgreSQL, upgrade a region
  database with existing prim inventory. Verify old items remain unassociated,
  a linked script retains its Experience on restart, and clearing its link
  persists. Verify stored linkage does not replace normal consent checks.
- [ ] Separately validate object archives, resident inventory, Hypergrid import
  and active consent revocation when their respective source changes land.

### Tranquillity: Experience withdrawal and simulator lifecycle

- [ ] With an active Experience holding controls and an Experience attachment,
  block or forget it in Firestorm. Verify controls are released, the denial
  event fires, and the affected attachment detaches. Repeat with parcel data
  unavailable and verify unrelated grants remain active.
- [ ] Confirm llGetMass during a sit/stand or local teleport still returns the
  last valid avatar mass while the physics actor is temporarily absent.
- [ ] Close a disposable simulator while a resident logs out. Confirm shutdown
  completes and remaining clients receive the normal disconnect notice.

### Tranquillity: vehicle movement and sculpt compatibility

- [ ] In a disposable region, copy a scripted vehicle and change the copy's
  settings. Confirm the original retains its flags and parameters. Reapply
  settings after a physics rebuild and verify stale flags are removed.
- [ ] Start llMoveToTarget and call it again with zero or negative tau; verify
  the original motion continues and llStopMoveToTarget still stops it.
- [ ] Rez sculpt prims sharing a texture with different topology, mirror and
  invert settings. Verify their physics shapes match each variant after cache
  warm-up and region restart.

### Tranquillity: Experience links in resident inventory

- [ ] With separate simulator and ROBUST processes, compile a local script into
  an Experience, move it into resident inventory, copy it, then put both
  copies into objects after relog. Confirm the association survives and that
  ordinary resident consent is still needed.
- [ ] Upgrade each supported inventory database with legacy items; confirm
  unlinked items stay unlinked. Confirm that foreign inventory reads do not
  import an association and writes cannot set one.

### Tranquillity: object archive and Hypergrid Experience links

- [ ] Take and rez a local object containing an Experience-linked script. Save
  and reload an OAR and test an IAR containing that object asset. Verify the
  link survives and still requires resident consent.
- [ ] Import a new foreign object asset and cross a Hypergrid boundary with
  linked attachments and saved script state. Confirm links and grants are
  stripped, unrelated state still loads, and the stored copy stays unlinked
  after local take/re-rez. Check a normal local crossing preserves the link.
- [ ] Repeat with previously cached foreign assets and record any remaining
  route that requires a separate cleanup or import guard.

### Tranquillity: AIS v3 acceptance (after full capability is wired)

- [ ] In Firestorm verify owned item/folder fetch, links, nested children and
  Current Outfit resolution; check large folders and ordinary legacy inventory
  when AIS is disabled.
- [ ] Before enabling AIS, verify write, copy, purge, delete and outfit changes,
  folder versions, library access and restart persistence in a disposable grid.

- [ ] For AIS metadata PATCH, verify item rename/description and ordinary
  folder rename persist after relog, with stable item asset, permissions and
  Experience link. Check returned folder versions and concurrent viewer edits
  before enabling the capability.

- [ ] Verify AIS folder/link creation and item deletion in Firestorm after
  registration. Check partial-create replies, retry behavior, provider
  deletion policy, folder versions, and that deleting a link keeps its target.

- [ ] In a disposable grid, verify AIS Trash and Lost And Found purge and
  folder removal, protected folder refusal, partial deletion replies, and
  provider delete-policy enforcement. Include concurrent inventory changes
  before enabling the capability.

- [ ] With Firestorm, replace Current Outfit and saved Outfit links, including
  a base-outfit folder link and an explicit empty array. Verify malformed
  requests and failed writes preserve the existing outfit, and concurrent
  changes do not cause duplicate or missing wearables before enabling AIS.

- [ ] In Firestorm, browse library item and folder data, copy one category
  shallow and recursive, and verify copied assets, permissions, destination
  ownership and folder versions after relog. Verify retry behavior and library
  categories containing links before advertising the capability.

- [ ] Before advertising AIS, verify Firestorm reuses the transaction ID and
  exact request on retry; confirm the same IDs and no duplicate rows. Exercise
  parallel writes against one outfit and compare returned folder versions with
  the resident inventory after relog. Check region changes and cross-process
  writes separately from the process-local replay guarantee.

### Registered AIS test-grid checkpoint — 2026-11-19

- [ ] Use only a disposable grid to opt in with `[AIS] ExperimentalEnabled=true`.
  Verify both capabilities appear in the seed response and disappear after
  logout, region removal and restart; default-off grids retain stock inventory.
- [ ] Exercise GET, metadata PATCH, category POST, outfit PUT, item/category
  DELETE and library COPY through Firestorm. Verify versions against stored
  inventory after relog; repeat requests with the same transaction ID and body.
- [ ] Check stock move fallback separately; the donor does not apply parent_id.
  AIS remains experimental pending the viewer/provider checks in this handoff.

### Native AIS upload acceptance — 2026-11-19

- [ ] On a disposable grid with experimental AIS enabled, edit and save a
  notecard and wearable through Firestorm. Confirm the returned asset ID is the
  stored asset, the data loads after relog, and item metadata and folder versions
  agree with native inventory. Transfer completion is simulated in the automated
  fixture; real viewer upload transport still needs this check.
- [ ] Retry the same PATCH hash_id without query tid after upload completion;
  verify one saved item and the same asset ID. Retry while transfer is pending
  and confirm 503 leaves inventory unchanged, then completion succeeds. Changed
  metadata under a consumed hash must be refused.
- [ ] Confirm a second viewer session cannot consume the first session's upload,
  missing transactions do not create uploads, and unowned wearable textures are
  rejected by the native validation path. Exercise asset/inventory provider
  outages and verify no success reply claims an unsaved inventory asset. An
  inventory failure after asset storage can leave an unreferenced asset.
- [ ] Check upload retry across region changes, simulator processes and restart
  separately: replay is bounded and process-local, not a persistent transaction.

### AIS library links and asset scope — 2026-11-19

- [ ] In Firestorm copy a library category containing item links, folder links
  and cross-folder targets. Confirm links point to the new resident copies when
  included, keep valid external library targets otherwise, and still resolve
  after relog. Include shallow copies excluding a target subfolder.
- [ ] Inject a copy provider failure and inspect the explicit partial reply and
  inventory. No new link should precede its copied target; exact retry must not
  create another partial tree within the process-local replay window. Confirm
  returned versions against inventory on each enabled provider.
- [ ] Save wearables and notecards using the native upload transaction path.
  Ordinary Firestorm item update helpers remove asset_id/shadow_id in favor of
  hash_id. Changed direct asset_id is refused (unchanged accepted), consistent
  with retaining native transaction validation. If a real viewer workflow needs
  direct rebinding, capture its request and establish native permission behavior
  before selecting an additional port.

### Tranquillity stable-source closeout acceptance — 2026-11-19

The selected stable source port is implemented; these checks are live acceptance,
not instructions to rebuild stock OpenSim behavior. Do not use older numbered
batch notes as the current source backlog. Configuration: server-side-baking.md.

- [ ] Enable baking on a disposable region with its public appearance service
  and login URL configured. Use Firestorm and a compatible LL viewer; verify
  first login, relog, outfit changes, removal/layering of clothing, Physics and
  Universal/BOM wearables, hover appearance and visibility to another resident.
  Compare stored COF versions and the actual appearance packets and texture URLs.
- [ ] Test normal, cold and warm bakes, root/child crossings and teleports between
  baking-enabled and client-baking regions. Log out during a pending save/bake.
  Verify no stale outfit wins at login; delay the inventory service beyond five
  seconds and verify fallback and that late reads never apply to the presence.
- [ ] Verify the public texture connector behind the grid's actual proxy/TLS
  configuration. Wrong agents, channels and expected assets must not return other
  content. Test the standalone adapter separately, including multi-region removal
  and process restart. Keep bin/runtimes with the distribution.
- [ ] Exercise MySQL/MariaDB and PostgreSQL alongside SQLite/native row fixtures:
  avatar save, layered-order readback, Universal slots, bake reuse and index
  preservation. Inject asset/index failures; previous assets must remain usable
  and a refused index must not become a successful appearance. Inspect partial
  provider writes and unreferenced assets; local locks are not a cross-process
  transaction. Test two simulators updating one resident independently.
- [ ] Generate the private real-outfit reference captures using the recipes in
  tests/Continuum.Baking.Tests/Golden and rerun the skipped reference tests.
  Include real Universal/BOM content, which the stock reference sets do not cover.
  No capture recipe was executed against a real grid during the port.
- [ ] Validate native group powers across several simultaneous roles, titles,
  owners, invited/open/banned/HG joins, notices and group chat. Confirm a refused
  fee-bearing join cancels the reservation and role edits refresh the viewer.
- [ ] Exercise cancelled land sales containing objects, free/paid/failed-payment
  sales, authorized buyers, group purchase/deed powers, resale-disabled estates,
  abandonment and auctions. Confirm exact committed payment and auction safeguards
  survive the adapted upstream submission; do not double-subscribe transfer events.
- [ ] Verify LLSD and XML-RPC login into rectangular/variable-sized regions and
  confirm their region dimensions match before the first crossing.
- [ ] Check Rubber and legacy Light default physics behavior with the enabled
  native physics engine and confirm explicit physics overrides still apply.
- [ ] Repeat the earlier AIS and foreign Experience cold/warm-cache/HG checks.
  Operator-loaded archives keep the existing trust model; this port does not
  retroactively rewrite cached objects or stored assets.

The final Release build and automated suites are source evidence only. No live
configuration/database, deployment, user upload, payment or viewer session was
changed by this closeout.
