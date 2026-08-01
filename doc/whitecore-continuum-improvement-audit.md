# WhiteCore-to-Continuum improvement audit

## Scope and evidence

This audit reviews `WhiteCoreSim/WhiteCore-Dev` snapshot
`f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` (2026-05-18) against the
Continuum integration branch based on OpenSim Dev
`247b9182c1ca0f11743de06a2808f003bc8e2a90`.

WhiteCore is treated as a first-class behavioural and viewer-protocol donor,
not as a wholesale source-tree donor. Its BSD-3-Clause project license is
compatible with OpenSim's BSD licensing, but copied code must still retain
applicable notices and file-level provenance. WhiteCore's service registry,
CAPS service, persistence abstractions, scene interfaces, and WebUI are too
different for unreviewed cherry-picks.

The goals are:

- closer Second Life viewer behaviour and data contracts;
- fewer login, cache, inventory, CAPS, and region-transition surprises;
- lower perceived latency and less redundant viewer/server work;
- production-safe MySQL, Robust, Windows, and Hypergrid operation;
- optional modules for behaviours that should not be forced into core.

## Executive disposition

| Priority | Candidate | Classification | Disposition |
|---|---|---|---|
| P0 | Display Name login initialization and lookup completeness | narrow core compatibility patch | Adopted from behavioural comparison; runtime retest required. |
| P0 | Abuse Report moderation lifecycle | Robust service extension | Implement retrieval, notes, status, screenshot access, and authenticated administration next. |
| P0 | Experience viewer protocol and region/parcel management | Robust service extension | Use WhiteCore for CAPS publication/schema evidence, but complete functionality from Tranquillity/Gunthar/Mobius and current viewer contracts. |
| P0 | Script/notecard task-inventory upload reliability | upstream-quality bug-fix investigation | Current Dev already has the same two-stage CAPS model; reproduce the reported failure and compare response/lifecycle details before changing code. |
| P1 | CAPS negotiation completeness | narrow core compatibility patch | WhiteCore's full registered-CAPS response informed the Experience-only compatibility patch. Do not return every unrelated CAP without tests. |
| P1 | Abuse notification and operational workflow | optional addon module | Add configurable estate/grid email or webhook notification after secure moderation APIs exist. |
| P1 | Parcel auction protocol | optional addon module | Potential SL-style land feature; requires economy, escrow, expiry, cancellation, and HG boundary design. |
| P1 | Profile/search identity consistency | upstream-quality bug fix | Audit every result and profile response for persisted Display Names, legacy usernames, HG identity, and cache expiry. |
| P1 | SimulatorFeatures/AgentPreferences parity | already present in OpenSim Dev | Compare response fields and add contract tests; no WhiteCore port currently justified. |
| P1 | GroupMemberData/GroupAPI | already present in OpenSim Dev | Continuum's current `GroupMemberData` is stronger; use WhiteCore only as a response-schema fixture. |
| P2 | Visitor logging | optional addon module | Useful opt-in audit module, but redesign for structured asynchronous logging and privacy controls. |
| P2 | Combat system | experimental feature | Optional only; WhiteCore behaviour is not a drop-in implementation of current SL damage/Experiences rules. |
| P2 | On-demand regions | experimental feature | Operational feature, not SL parity; redesign around modern process supervision and Robust registration. |
| P2 | Object cache persistence | obsolete or unsuitable | WhiteCore implementation disables itself and uses per-user files; modern viewer caches and current interest management need profiling first. |
| P2 | Banned-viewer enforcement | obsolete or unsuitable | User-agent blocking is easy to evade and creates interoperability/support risk. Prefer capability and protocol validation. |
| P2 | WebUI and Web APIs | optional separate application | Deferred portal phase. Requirements are useful; direct integration into Robust is unsuitable. |

## Detailed candidates

### 1. Display Names: complete viewer initialization

- **Behaviour:** persist a mutable display name grid-wide; supply it at login;
  resolve it by UUID or username; send `DisplayNameUpdate` and
  `SetDisplayNameReply`; preserve the immutable legacy account name.
- **WhiteCore evidence:**
  `WhiteCore/Services/GenericServices/CapsService/CAPModules/Avatar/DisplayNames.cs`
  (representative commit `a987590bf02d077e26a7396be50f5554eb7aa16c`),
  `LLLoginService.cs`, and `LLLoginResponse.cs`.
- **Current equivalent:** Continuum persists `DisplayName` and `NameChanged` in
  the user account service, publishes Get/Set CAPS, updates search and LSL
  identity functions, and refreshes region user caches.
- **Missing behaviour found:** the login response omitted `display_name`, and
  `GetDisplayNames` only accepted `ids`, not `username`. Both paths are now
  implemented on the integration branch. WhiteCore also offers configurable
  update days and banned substrings; Continuum currently enforces SL's
  seven-day cooldown and basic length/control-character validation.
- **Affected services/files:** LLLoginService, UserAccountService, region user
  management cache, Linden CAPS/event queue, avatar picker/search, LSL identity
  functions.
- **Addon/core:** narrow core compatibility; policy configuration can remain
  optional.
- **Compatibility:** MySQL-backed user account extras are already used;
  Windows-neutral; local users only may mutate names. HG visitors retain their
  authoritative remote identity and must never write local account data.
- **Viewer requirements:** viewers supporting `GetDisplayNames`,
  `SetDisplayName`, and display-name event-queue messages.
- **Tests:** XML-RPC and LLSD login; clean viewer cache; full Robust/simulator
  restart; nameplate, Nearby, search, profile, chat, IM, groups, object creator,
  LSL lookup, reset-to-default, weekly throttle, concurrent regions, and HG
  visitor cases.
- **Recommendation:** retain P0. Add configurable prohibited-name policy only
  after normalization, Unicode, audit logging, and administrator-bypass rules
  are specified; do not copy WhiteCore's substring filter verbatim.

### 2. Abuse Reports: moderation, not submission only

- **Behaviour:** receive viewer reports and optional screenshots, store them
  grid-wide, list/filter reports, retrieve a report, add moderator notes, mark
  it complete, and notify operators.
- **WhiteCore evidence:**
  `Modules/Avatar/AbuseReports/AbuseReportsModule.cs` (representative commit
  `25277cb3fab2735d619bbfb66372b5fccc67692e`),
  `Services/DataService/Connectors/Local/LocalAbuseReportsConnector.cs`,
  `Services/SQLServices/AbuseReportsService`, and
  `Services/API/WebAPI/AbusereportsAPI.cs` (representative commit
  `304313b0a1b5ebb882bb8405b5773cbf12bab1c6`).
- **Current equivalent:** Continuum accepts viewer CAPS reports, supports a
  bounded screenshot upload, sends them to a Robust service, and stores them in
  MySQL. There is not yet a supported retrieval/moderation surface.
- **Genuinely missing:** authenticated list/get/update operations, state and
  notes, screenshot retrieval authorization, retention, notification, and an
  operator UI/CLI.
- **Affected services/files:** Linden CAPS, AbuseReports service interface,
  Robust handlers/connectors, MySQL migrations/data plugin, asset or screenshot
  storage, console commands, and later the grid interface.
- **Addon/core:** Robust service extension plus an optional administration
  client; submission CAPS remains core viewer compatibility.
- **Compatibility:** design MySQL indexes for status/date/region/reporter;
  avoid WhiteCore's password-in-request pattern; use Robust service
  authentication and role authorization. Use safe Windows paths only if files
  are retained outside the asset service. HG reports must record both UUI and
  local session context without trusting foreign-supplied names.
- **Viewer requirements:** ordinary SL-compatible abuse-report CAPS; moderation
  does not require viewer changes.
- **Tests:** forged reporter/region fields, screenshot limits and content type,
  duplicate submission, pagination, concurrent updates, authorization, notes,
  completion/reopen, retention, email outage, SQL migration/rollback, HG UUI,
  and log redaction.
- **Recommendation:** highest unimplemented WhiteCore-derived improvement.
  Build the service API and console retrieval before the later WebUI.

### 3. Experiences: complete SL workflow

- **Behaviour:** advertise Experience CAPS, create/update/query Experiences,
  manage contributors/admins and agent permissions, expose parcel and region
  lists, persist estate/parcel policy, and enforce script permission semantics.
- **WhiteCore evidence:**
  `Services/GenericServices/CapsService/CAPModules/Experiences/*` and its CAPS
  service seed response. Commit `55962f37` moved these handlers out of its
  not-implemented registration set.
- **Current equivalent:** Continuum has the Tranquillity/Mobius service, MySQL
  schema, Robust connector, region connector, CAPS handlers, estate lists, LSL
  functions, and script permission integration.
- **Implemented checkpoint:** the Experience capability family is published;
  `RegionExperiences` supports estate-authorized viewer GET/POST management;
  estate trusted, allowed and blocked policy persists in MySQL; parcel allowed
  and blocked entries are returned to the viewer as well as accepted; and
  blocked estate/parcel policy overrides script permission grants.
- **Still unproven at runtime:** tab visibility after deployment;
  Experience create/edit/search usability; contributor/admin paths; grid-wide
  vs estate vs parcel policy; restart and multi-region enforcement. WhiteCore's
  inspected handlers often return empty maps, so they are not a functional
  backend donor.
- **Affected services/files:** ExperienceModule, land and estate modules,
  event queue/CAPS, script engines, attachment and inventory serialization,
  Experience Robust service and MySQL stores.
- **Addon/core:** Robust service extension with narrow core integration.
- **Compatibility:** use the existing MySQL revisioned store; all region
  mutation must be estate-authorized; Windows-neutral. HG must not assume a
  foreign Experience UUID is locally trusted or executable.
- **Viewer requirements:** modern Firestorm/SL-compatible viewer. Tabs appear
  only when `RegionExperiences` is present in the seed CAPS response.
- **Tests:** CAPS request/response capture, tab presence, create/update/search,
  parcel and estate lists, script permission allow/deny/revoke, attachment
  crossing, restart, two-region propagation, HG entry/exit, malformed LLSD,
  unauthorized mutation, and database rollback.
- **Recommendation:** use WhiteCore for protocol/schema comparison and its
  complete-map CAPS insight; use Tranquillity/Gunthar/Mobius plus viewer source
  for actual backend behaviour. Do not port WhiteCore's empty handlers.

### 4. Script and notecard task-inventory upload

- **Behaviour:** opening and saving a script/notecard in a prim creates a
  one-time uploader capability, receives the asset, updates task inventory,
  compiles scripts, reports errors, preserves running state and Experience ID,
  and removes the uploader.
- **WhiteCore evidence:**
  `Modules/Avatar/Inventory/LLClientInventory.cs` (representative commit
  `15b7118f2cd58f2057f8ee38d145f94a50f32339`) and its
  `TaskInventoryScriptUpdater`/`ItemUpdater` classes.
- **Current equivalent:** OpenSim Dev already registers `UpdateScriptTask` and
  legacy `UpdateScriptTaskInventory`, creates expiring upload capabilities,
  carries `experience_key`, updates the task asset, and returns compile data.
- **Genuinely missing:** none established by static comparison. The reported
  paste/save problem needs a captured viewer request, uploader response, second
  POST, asset update, and compiler log. Clipboard paste itself is viewer-side;
  save failure is not.
- **Addon/core:** upstream-quality core bug fix only if reproduced.
- **Compatibility:** MySQL asset/inventory path, Windows-neutral; HG ownership
  and permissions must remain authoritative locally.
- **Tests:** new script then paste/save; existing script; notecard; syntax
  error; running checkbox; Experience script; no-modify prim; deleted prim
  during upload; expired/replayed uploader; 0-byte/oversized asset; YEngine and
  XEngine; viewer reconnect.
- **Recommendation:** do not replace current code with the older WhiteCore
  implementation. Add request-lifecycle diagnostics and a regression test once
  the failure is reproduced.

### 5. CAPS seed negotiation

- **Behaviour:** viewers receive stable URLs for supported capabilities without
  losing UI solely because a viewer variant requests an incomplete capability
  family.
- **WhiteCore evidence:** `PerRegionClientCapsService.CapsRequest()` returns the
  complete `RegisteredCAPS` map. OpenSim filters its response to requested CAPS.
- **Current equivalent/missing:** requested filtering is appropriate and more
  conservative, but Experience UI depends on the full related family. A narrow
  Experience-family publication patch has been integrated with diagnostics.
- **Addon/core:** narrow compatibility patch.
- **Compatibility:** no database impact; Windows/HG neutral, but external CAPS
  URLs and foreign-region seeds must not be rewritten.
- **Tests:** Firestorm release/beta/nightly, official viewer, requested and
  omitted cap names, child agents, crossings, external CAPS, malformed seed
  body, and handler deregistration.
- **Recommendation:** retain family-level compatibility only. Do not adopt
  WhiteCore's unconditional all-CAPS response globally without profiling and
  viewer/security tests.

### 6. Groups and agent preferences

- **Behaviour:** compact group-member responses and persisted viewer language,
  maturity and visibility preferences.
- **WhiteCore evidence:** `CAPModules/Groups/GroupMemberData.cs`,
  `GroupAPIv1.cs`, and `CAPModules/Avatar/AgentPreferences.cs`.
- **Current equivalent:** OpenSim Dev already provides these service paths.
  Continuum's `GroupMemberData` calculates default powers, compresses repeated
  values, validates the requester and transit state, and returns a stable empty
  response on failure; this is stronger than the WhiteCore snapshot.
- **Addon/core:** already present.
- **Compatibility:** existing Robust/MySQL AgentPreferences service; Groups
  remains grid-wide. HG group authority must remain with the configured groups
  service.
- **Tests:** large groups, Unicode titles, owner/default powers, offline status,
  role changes, language persistence, privacy visibility, restart, and HG
  groups.
- **Recommendation:** write contract/performance tests; no WhiteCore code port.

### 7. Abuse notification

- **Behaviour:** optionally notify an estate owner or grid moderation address
  when a report arrives.
- **WhiteCore evidence:** AbuseReportsModule uses estate settings and the email
  module to send report details.
- **Current equivalent/missing:** submission is stored but no supported
  notification workflow is present.
- **Addon/core:** optional service extension.
- **Compatibility/security:** queue notification after durable storage; redact
  sensitive fields; never block report submission on SMTP/webhook failure;
  rate-limit and aggregate bursts. Windows-neutral and HG-aware.
- **Tests:** mail outage, retry, duplicate suppression, injection/escaping,
  secrets in logs, and disabled configuration.
- **Recommendation:** implement after authenticated moderation retrieval.

### 8. Parcel auctions

- **Behaviour:** start a parcel auction, accept bids, close/cancel it, transfer
  land to the winner, and expose viewer auction metadata.
- **WhiteCore evidence:** `Modules/World/Auction/AuctionModule.cs`, recent
  auction work including `75503385`, parcel `AuctionInfo`, map integration, and
  currency transaction type support.
- **Current equivalent/missing:** OpenSim has parcel sale but no complete
  supported grid auction lifecycle.
- **Addon/core:** optional addon module with narrow parcel protocol hooks.
- **Compatibility:** requires durable MySQL state, atomic money escrow/refund,
  restart-safe timers, idempotent close, and Windows-neutral scheduling. Disable
  across HG unless a trusted shared economy and land authority exist.
- **Viewer requirements:** viewer auction controls may be partial or hidden;
  operator/web workflows may still be needed.
- **Tests:** concurrent bids, insufficient funds, cancellation, region/Robust
  crash, owner change, abandoned parcel, tie/late bid, refund, fraud, and audit
  trail.
- **Recommendation:** valuable P1 optional feature, but redesign rather than
  cherry-pick.

### 9. Profile/search consistency

- **Behaviour:** every identity surface returns display name, legacy username,
  UUID/UUI, and online/location data consistently and with privacy applied.
- **WhiteCore evidence:** display names appear in login, CAPS, profile helpers,
  search and WebUI user management.
- **Current equivalent:** Continuum has avatar picker Display Names and improved
  LSL identity functions. Search already finds the persisted display name.
- **Missing/unproven:** profiles, groups, IM/chat history, creator/owner labels,
  offline cache expiry, and HG collisions have not passed an end-to-end matrix.
- **Addon/core:** upstream-quality consistency fixes.
- **Tests:** name change/reset, duplicate display names, legacy names, Unicode,
  case-folding, offline users, object ownership, groups, profiles, HG visitors,
  and cache expiry.
- **Recommendation:** P1 contract audit; avoid a second identity database.

### 10. Grid search and directory completeness

- **Classification:** optional addon module improvements, with narrow core
  compatibility fixes only where the viewer protocol requires them.
- **Behaviour:** provide an SL-like grid directory covering people, places,
  land sales, popular places, events and classifieds, plus event/classified
  details and compatible map-item results. Results must page predictably,
  respect maturity and publication/privacy settings, use correct landing
  points, and remain current when regions or parcels change.
- **WhiteCore evidence:**
  `WhiteCore/Modules/Avatar/Search/SearchModule.cs` implements the complete
  legacy viewer directory request family and map-item bridge. Its
  `LocalDirectoryServiceConnector` maintains grid-side `search_parcel`,
  `event_information`, `event_notifications`, and `user_classifieds` data,
  including parcel landing point/look-at data and scope filtering. The reviewed
  donor remains snapshot `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa`.
- **Current Continuum equivalent:** the optional
  `addon-modules/OpenSimSearch` module implements places, popular places, land,
  events, classifieds, event/classified details and map items through the
  established OpenSimSearch XML-RPC service. Core Dev supplies avatar-picker
  people search and the basic/map search modules. Display-name matching has
  already been added to the current user-account search path.
- **Genuinely missing or unproven:** a single production test matrix across all
  search categories; reliable incremental parcel removal/update after region
  changes; event notification behaviour; maturity/category/price/area filters;
  stable paging and deterministic ordering; privacy/`AllowPublish` handling;
  display-name presentation without losing the legacy username; variable-size
  region coordinates; stale-region cleanup; query limits and abuse resistance;
  Hypergrid identity collision handling; and indexed MySQL query plans at grid
  scale.
- **WhiteCore lessons worth adopting:** preserve the entire viewer request
  family as one compatibility contract; keep grid-search data in a service-side
  index rather than scanning live regions; store parcel landing and look-at
  coordinates; apply scope and publication filters; return empty valid replies
  for zero results; and split legacy UDP replies into viewer-safe batches.
- **WhiteCore code not suitable to port:** people search performs per-result
  profile, group and online-status calls and is explicitly marked for
  optimization; this creates an N+1 service-call path. Its generic connector,
  reflection/remoting framework and database abstraction do not fit current
  OpenSim Dev. Search strings and legacy XML-RPC inputs also require stricter
  validation, limits and escaping than the donor demonstrates.
- **Affected components:** OpenSimSearch region module and web service/database,
  user-account and profile services, grid/region/parcel publication, map-item
  responses, Display Names, event/classified profile data, and viewer UDP/CAPS
  search entry points.
- **Addon/core decision:** retain OpenSimSearch as the grid-wide optional addon.
  Do not create a WhiteCore directory service inside core. Core changes are
  acceptable only for protocol correctness, identity consistency, or a clean
  service interface that also benefits other directory providers.
- **Robust compatibility:** search must run as a separately authenticated
  grid-wide service with region publication credentials and bounded read APIs.
  It must not require direct region-database access from Robust.
- **MySQL compatibility:** certify schema migrations, UTF-8/Unicode matching,
  indexes for text/category/maturity/scope/price/area/time queries, deterministic
  paging, stale-row cleanup, and query plans with production-sized fixtures.
- **Windows compatibility:** certify the service and module under the supported
  .NET Windows build, including URL/config parsing, time zones for events, and
  no case-sensitive path assumptions.
- **Hypergrid implications:** local results must carry unambiguous UUID/UUI and
  home-grid identity. Remote visitors must not overwrite or collide with local
  people records, and private online/location data must not cross grid trust
  boundaries. Foreign parcel/event indexing is out of scope until an explicit
  federation policy exists.
- **Viewer requirements:** test Firestorm and another current OpenSim-capable
  viewer using legacy directory panels, web-search entry points where enabled,
  map event/land markers, profiles and avatar picker. Display names supplement
  rather than replace the stable legacy account name.
- **Licensing/provenance:** WhiteCore is behavioural and protocol evidence under
  its BSD-style contributor notice. Any adapted logic requires file-level
  provenance and notice review. Prefer independently implemented changes in the
  existing OpenSimSearch codebase.
- **Required tests:** people by legacy and display name; duplicate display names;
  places by text/category/maturity; land by sale type/price/area; popular ranking;
  upcoming/in-progress events and event details/notifications; classifieds and
  details; paging beyond one packet; zero/large result sets; Unicode and hostile
  input; parcel add/update/delete and region offline/re-register; permissions;
  MySQL restart/migration; multi-region concurrency; Hypergrid visitors; and
  sustained latency/load tests.
- **Recommendation:** P1 production hardening after the Experience management
  checkpoint. First add black-box compatibility tests around the current
  OpenSimSearch service, then fix demonstrated gaps. Do not replace it wholesale
  with WhiteCore search.

### 11. Built-in grid economy

- **Classification:** Robust service extension with narrow core compatibility
  integration. This supersedes the earlier assumption that WhiteCore was only a
  behavioural economy reference.
- **Behaviour:** provide one authoritative grid ledger for balances, transfers,
  object/script payments, land and object purchases, uploads, group creation,
  classifieds and parcel-directory fees; push viewer balance updates; retain an
  auditable transaction history; and support controlled grants, purchases,
  stipends and group accounting.
- **WhiteCore evidence:** `WhiteCore/Modules/Currency` contains the service,
  database connector, viewer RPC handlers, configuration, scheduled payments
  and remote/local connector boundary. `GroupMoneyModule` supplies viewer group
  accounting. Currency migration 6 defines keyed user and group balances,
  transaction histories and currency-purchase records. The reviewed donor is
  snapshot `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa`.
- **Gunthar evidence:** the Gunthar-derived RegionCurrency work describes an
  enhanced `BetaGridLikeMoneyModule` as a local persistent ledger with a
  first-use balance and viewer updates, avoiding a separate server. The
  RegionCurrency portal then calls the active `IMoneyModule`. This is a useful
  OpenSim integration attempt, but its region-local tab-separated ledger is not
  an authoritative multi-region production datastore. RegionCurrency is a
  wallet/admin presentation layer, not the grid ledger itself.
- **Current Continuum equivalent:** the recovered DTL/NSL-style MoneyServer,
  region currency module and MySQL money wrapper provide a dedicated process,
  XML-RPC viewer/region integration and MySQL-backed balances/transactions.
  Production repairs have improved concurrency, console behavior, banker
  controls and diagnostics, but the subsystem remains a separately evolved
  addon code island with duplicated service hosting and legacy RPC assumptions.
- **Genuinely missing or unproven:** atomic double-entry transfer semantics;
  idempotency for every charge and purchase path; crash-safe land/object sale
  completion and refund; consistent group balances/accounting; classified and
  directory renewal; scheduled stipend exactly-once behavior; administrative
  adjustment audit; clean Robust authentication; service failover/recovery;
  migration from the existing MoneyServer schema without balance drift; and a
  complete viewer transaction-description matrix.
- **Architecture decision:** evolve MoneyServer into the authoritative
  ContinuumEconomy service, built from shared service/data assemblies with
  authenticated connectors. The dedicated service process remains the primary
  production host and the region `IMoneyModule` remains a thin adapter. Shared
  assemblies may permit optional Robust hosting later, but there will never be
  two ledger implementations. RegionCurrency remains an optional portal over
  the service. Do not use a per-region file ledger.
- **WhiteCore lessons worth adopting:** unified user/group accounting; explicit
  transaction IDs and history; server-side purchase limits; configurable fees;
  scheduled-payment records; remote/local service separation; and one shared
  policy source for viewer economy data and charges.
- **WhiteCore code not suitable to port directly:** its registry, generic data,
  reflection/remoting and scheduler architectures are WhiteCore-specific. Some
  transfer paths update balances in multiple steps and must not be assumed
  atomic. Real-currency purchase code requires a new security/payment review;
  it must not be copied as production financial code.
- **MoneyServer reuse decision:** retain proven viewer protocol behavior,
  transaction types, existing MySQL migration knowledge and compatibility
  endpoints. Extract and rewrite the ledger boundary behind tests instead of
  discarding deployed balances or copying WhiteCore wholesale.
- **Robust compatibility:** private mutation endpoints require service
  authentication, replay protection and request idempotency. Public viewer quote
  or helper endpoints must expose no administrative operation. No production
  database credentials belong in region configuration.
- **MySQL compatibility:** certify transactional row locking, non-negative and
  overflow constraints, unique transaction IDs, indexed account/history
  queries, deadlock retry, schema upgrade/rollback and reconciliation against a
  snapshot of the existing MoneyServer tables.
- **Windows compatibility:** support the standard .NET Windows build and service
  supervision, invariant numeric/date serialization, TLS certificates, graceful
  shutdown and restart during in-flight requests.
- **Hypergrid implications:** currency is local-grid authority. A foreign avatar
  may use a deliberately provisioned local account, but balances and debit
  authority must never be trusted from a foreign grid. Cross-grid exchange is a
  separate, disabled design requiring explicit settlement and fraud controls.
- **Viewer requirements:** validate balance display, pay resident/object,
  scripted payment, object and land purchase, upload/group/classified fees,
  insufficient-funds messages, transaction descriptions, currency quote/buy
  panels and group accounting in current Firestorm and another compatible
  viewer.
- **Licensing/provenance:** preserve OpenSim/DTL-NSL and WhiteCore/Aurora notices
  at file level. Record independently reimplemented WhiteCore semantics and the
  Gunthar-derived RegionCurrency lineage. Gloebit is explicitly excluded and
  must not be modified or used as the backend.
- **Required tests:** concurrent same-account transfers; duplicate/replayed
  requests; debit/credit atomicity; insufficient funds; integer boundaries;
  object/script/group/land/classified/upload charges; sale delivery failure and
  refund; service/region/database crash at each transaction phase; stipend
  restart; group accounting privacy; admin grant audit; TLS/auth failures;
  multi-region balance updates; MySQL clean/upgrade migration; reconciliation;
  Hypergrid visitor isolation; and sustained load/latency tests.
- **Recommendation:** promote this to P0 after the Experience runtime checkpoint.
  First freeze an acceptance contract around the deployed MoneyServer protocol
  and schema, then implement a shared Robust-hostable economy service behind the
  existing region adapter. Do not deploy Gunthar's file ledger or directly port
  WhiteCore's financial transaction implementation.

## Lower-priority and rejected direct ports

| Candidate | WhiteCore behaviour | Continuum decision |
|---|---|---|
| SimulatorFeatures | Advertises server/viewer feature flags. | Already present and more current in Dev. Compare fields only. |
| AgentPreferences | CAPS-backed language and preference persistence. | Already present with local/remote services and MySQL/SQLite/PGSQL stores. Test configuration and persistence. |
| Physics materials | Viewer/object physics data and material handling. | Dev has current physics materials and PBR work. Use only for regression cases. |
| Asset/inventory CAPS | Upload, inventory descendants, assets, mesh and baked textures. | WhiteCore snapshot is older; Dev has current handlers, range support and expiring upload caps. No direct port. |
| Object cache | Per-agent/per-region cache serialized to local files. | Implementation explicitly disables itself after initialization and risks file churn, stale state and privacy leakage. Reject. |
| Visitor logger | Login/logout duration written to a file. | Redesign as disabled-by-default structured asynchronous addon with retention/privacy controls. |
| Combat | Configurable combat state, damage and teleport restrictions. | Experimental addon reference. Reconcile with current damage APIs, Experiences and parcel/estate policy. |
| On-demand regions | Starts/stops regions based on activity. | Operational experiment. Requires external supervision, registration and failure recovery; do not put process lifecycle into ordinary region code. |
| Banned viewers | Blocks configured viewer identifiers. | Reject as security control; identifiers are spoofable. Validate protocol/capabilities and use account/estate policy instead. |
| WorldView/WorldShader/Warp3D | Alternative map/world rendering. | Compare output and performance only after the Gunthar map renderer candidate is tested. Avoid duplicate render stacks. |
| OpenRegionSettings | Publishes OpenSim-specific region options. | Dev already has OpenRegionSettings support. Keep as OpenSim extension, not SL parity. |
| WebUI/Web API | Grid-wide public/admin portal and operational APIs. | Keep requirements for the later OpenSim-Grid-Interface/WhiteCore WebUI phase. Do not embed it in Robust now. |
| Installer/build scripts | Legacy .NET Framework/Mono setup. | Obsolete for current .NET/Windows build flow; retain only configuration ideas. |

## Performance and smoothness conclusions

WhiteCore does not provide a generally faster modern core that can be dropped
into OpenSim Dev. Most performance-sensitive implementations in the snapshot
predate current .NET OpenSim work. The useful performance lessons are narrower:

1. Publish coherent capability families so viewers do not retry, hide panels,
   or fall back to older protocols.
2. Return compact group data and stable empty/error responses; current Dev is
   already ahead of WhiteCore here.
3. Use one-time expiring upload CAPS and remove them after use; current Dev is
   already ahead and should be diagnosed, not replaced.
4. Keep identity in one authoritative account service and refresh bounded
   caches, avoiding profile/account divergence.
5. Keep report submission asynchronous from notifications and moderation UI.
6. Add measurements before adopting caches. WhiteCore's disabled file object
   cache is evidence that an unmeasured cache can worsen reliability.
7. Prefer contract tests and request tracing around login, CAPS, assets,
   inventory and crossings before low-level optimization.

## Recommended implementation sequence

1. Runtime-test the integrated Display Name login/CAPS changes with a clean
   viewer cache and full Robust/simulator restart.
2. Complete authenticated Abuse Report list/get/update console and Robust APIs,
   then notification, then the later portal UI.
3. Deploy the completed Experience seed, region/parcel mutation and blocked
   policy checkpoint, then capture viewer traffic and run the full Experience
   CRUD/permission/restart test matrix.
4. Reproduce the prim script paste/save issue with CAPS lifecycle diagnostics;
   port nothing from WhiteCore unless a specific semantic difference is proven.
5. Run identity/profile/group contract tests and correct only demonstrated
   schema or cache inconsistencies.
6. Design parcel auctions and structured visitor logging as independent,
   disabled-by-default addons after P0 runtime acceptance.

No WhiteCore WebUI implementation code is included by this audit.
