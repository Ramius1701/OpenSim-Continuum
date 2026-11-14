# WhiteCore-to-Continuum improvement audit

<!-- continuum-current-status:begin -->
> Project checkpoint — 2026-10-24: lickx, Mobius and the selected WhiteCore
> ports/native workflow adapters are implemented in source. The former 11 blocked
> WebUI routes now have native workflows. Live/provider acceptance remains open.
> See the [current donor audit](whitecore-continuum-improvement-audit.md),
> [WebUI backlog](parity-webui.md) and [test handoff](donor-feature-test-handoff.md).
> Earlier dated checkpoints below retain their historical scope; they do not
> supersede this status or certify deployment.
<!-- continuum-current-status:end -->

## WhiteCore implementation closeout — 2026-10-24

**The needed selected WhiteCore ports and native WebUI workflow adaptations are
implemented in source.** This supersedes the earlier catalog-only closure, which
left selected workflows unfinished. No next donor was started. The donor HEAD was
reverified as `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa`; newer native services remain
authoritative. Completion is supported by implemented behavior and acceptance
checks, not by removing a blocked-page list. Deployment acceptance remains open.

| Formerly blocked workflow | Delivered implementation and authority |
|---|---|
| `admin/factory_reset.html` | Administrator-only, confirmed navigation/presentation reset through the existing revision/lease/atomic portal store. Independent sections preserve each other; news and native service policy are untouched. |
| `admin/gridsettings_manager.html` | Opt-in native ROBUST INI editor for GridInfoService grid name/nick, LoginService WelcomeMessage and matching portal SystemName. Fixed operator-selected path, bounded fields, hash-based stale checks, writer lease, flushed temporary replacement and exact same-directory backup. ROBUST restart is explicitly required. Actual system accounts and estates use their existing native administration; donor-only label aliases are not substituted for account authority. |
| `user/contact.html` | Opt-in private postal address/city/code/country save and clear, explicit operator privacy notice and consent, HTTPS account transport, authenticated local-actor binding, bounded escaped fields and a single native profile application-data JSON write. Native SQLite/MySQL/PostgreSQL providers retain storage ownership. The private namespace is refused through public application-data methods. |
| `user/deleteaccount.html` | Opt-in, password-confirmed account closure using a conditional native `UserLevel=0` to `-2` update. Privileged, protected, online-viewer and estate-owner guards; current web token/cookie cleared and fresh account checks reject other portal sessions. The account, identity and related data are retained. |
| `user/update_user.html` | Working account-settings navigation to native password/email/contact/closure operations replaces the broken legacy action. |
| `user/partnership.html` | Opt-in durable private proposals, recipient accept/decline, sender withdrawal, seven-day expiry, one outgoing/20 incoming limits and exact ID/recipient/message replay binding. Native serializable transactions update both profile partners together; failure rolls back both. Either partner can dissolve the reciprocal pair. No fee, email or IM is generated. |
| `admin/purchases.html`, `user/purchases.html` | Per the user's explicit choice, authenticated currency-credit history, with administrator lookup and a server-derived resident actor. A private native economy read returns successful positive type-5010 credit adjustments, 50 per page with stable timestamp/operation-ID cursors. No real-money provider, cash amount or settlement receipt is invented. |
| `admin/region_edit.html`, `user/region_edit.html` | Native region-management workflow: recheck current estate ownership, show native identity/location/size/host, and open the operator-configured HTTPS RegionWeb control room for actual backed-up file editing/reloads. Native estate editing is linked directly. Administrators can also use the native host console for provisioning/restarts; OpenSim's existing create region, restart and remove-region commands remain authoritative. Posted URLs never select a host. |
| `admin/sim_console.html` | A working administrator browser client for the existing OpenSim/ROBUST REST console replaces the donor stub. Fixed configured HTTPS hosts, independent native credentials, exact-origin CORS, bounded XML/output, no credential/session browser storage, explicit command submission and unconfirmed outcomes without automatic retry. Disconnect/navigation/time-limit cleanup is implemented. |

**Correction to the earlier deletion audit:** the WhiteCore page calls
`DeleteUser(..., archiveInformation: true, wipeFromDatabase: false)`. Its
LocalUserAccountConnector archives by setting UserLevel to -2. That exact path
was not a permanent deletion implementation. Continuum now supplies an explicit
closure workflow with the same archival meaning; permanent cross-service data
erasure is not falsely claimed or required to reproduce that donor behavior.

### Native contracts, configuration and limitations

- Optional local service/provider capabilities preserve third-party interface
  compatibility. The WebUI does not query database tables directly.
- Native profile migrations add durable proposals (SQLite v6; MySQL/PostgreSQL
  v8). MySQL/PostgreSQL v7 widens application DataVal to TEXT for the bounded
  private contact JSON; SQLite already has text affinity. These are source
  migrations, exercised against disposable SQLite only, not live schemas.
- Public profile application-data tag identifiers are restricted to printable
  ASCII and cannot address the `continuum.private.` namespace, including trimmed
  or case variants. This prevents MySQL collation aliases from bypassing private
  contact protection. No contact fields are added to public profile responses.
- `ResidentContactEnabled`, `AllowAccountClosure` and `AllowPartnerships` default
  false. Contact collection requires `ContactPrivacyNotice`; configure every
  service/system UUID in `AccountClosureProtectedIDs` before enabling closure.
- `GridConfigurationPath` defaults blank. It must select the loaded native INI
  containing the three supported sections. Included files, remote hosts or
  LoginService.MessageUrl may override values. Saving does not update running
  service fields. Keep secret-bearing originals/backups private.
- Region administration delegates to the existing native per-host editor;
  sessions and grid-admin privileges are not forwarded as estate-owner authority.
  This is not a new central host supervisor or a shared simulator SSO session.
- `[ContinuumWebUIConsoles]` is empty by default. Configure native REST consoles
  and TLS, credentials and exact ConsoleAllowedOrigin on the chosen hosts.
  Native console authority is independent of portal roles. The client expires
  after 15 minutes; it does not replay commands after ambiguous responses.
- Native console fixes remove the exact registered poll capability on close and
  publish command help without temporarily removing live help/quit entries,
  avoiding concurrent handshake races.
- Native login rejects negative user levels even when MinLoginLevel is configured
  negative. The closure fixture exercises the actual LLLoginService with an
  archived account and a negative minimum, rather than merely testing a helper.
- Native account closure prevents new logins; it is not a global active-session
  kill, refund or data-erasure service. In-flight login/cache races remain a live
  regression gate, alongside coordinated backup/recovery and provider execution.
- Earlier selected auctions/reminders/SimulatorFeatures ports and explicit
  optional-feature exclusions remain unchanged. No external money integration,
  unselected combat/tooling/visitor/cache/viewer-ban/on-demand module was added.

### Validation of the final source snapshot

Full Release solution build and both acceptance harness builds passed with
**zero warnings and zero errors**. All of the following passed after rebuilding:

- Main WebUI suite: events, portal/news, estates, private contacts, native closure,
  partnership persistence/consent/rollback, native grid configuration, region
  authority, actual RemoteConsole protocol/cleanup/concurrent help, recovery and
  real SQLite classified fees/restart recovery.
- Isolated currency-credit HTTP suite: actual native SQLite ledger/RPC, filtering,
  actor scoping, same-timestamp pagination and escaped embedded pages.
- Browser console event-handler fixture: native request fields, credential
  clearing, confirmation, no automatic command retry and handshake cleanup.
- Isolated auction WebUI/HTTP and event-reminder HTTP/SQLite/login suites.
- Auction lifecycle/real SQLite escrow and fresh disposable Search self-test.

Sandbox restrictions initially prevented temporary-file replacement and loopback
connections; authorized disposable fixtures passed outside those restrictions.
Native HTTP listener fixtures retain separate processes as documented earlier.
Live browser/mobile, Firestorm, MySQL/MariaDB/PostgreSQL migrations and workflows,
TLS/CORS/proxy configuration, restart and backup recovery are still acceptance
work in `donor-feature-test-handoff.md`. Nothing was deployed and no real user
messages, payments, bids or console commands were executed.

## Historical catalog-only closure — 2026-10-24


**The recorded WhiteCore feature catalog is reconciled against current Continuum.**
Pinned donor HEAD was reverified as
`f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa`. This closes the inactive-donor source
comparison/selected-port lane, not the larger WebUI product backlog or runtime
certification. No new donor was started in this pass. Older "active", "implement
next" and "not yet compared" statements below are historical checkpoints.

Closure applies to the feature catalog already selected in this audit, the
optional-feature decisions explicitly made by the operator, and the 87-controller
WebUI disposition inventory. It does not claim that every WhiteCore source file
has a line-for-line OpenSim equivalent, that all SL features are implemented, or
that the remaining native-service gaps disappeared. The pinned Modules/CAPS tree
inventory contains 320 C# files (including 106 Web and 19 service CAPS files);
filenames and templates were not counted as working implementations.

| Catalog family | Final source disposition | Concrete evidence / result |
|---|---|---|
| Display Names | Retain newer native implementation | DisplayNameModule and LLLoginResponse already cover lookup/login; no older donor replacement |
| Abuse moderation and notification | Retain native service and existing notification | GetReports/UpdateReport and CAPS estate SendSystemEmail already implemented |
| Experience service and CAPS seed publication | Retain selected newer service lineage | ExperienceModule registers native endpoints; BunchOfCaps already adds registered Experience CAPS without indiscriminately returning unrelated CAPS |
| Task-script/notecard uploads and inventory CAPS | Existing native equivalent; no speculative transplant | Current BunchOfCaps has staged uploader URLs, temporary-handler cleanup and completion/compiler-result handling. Retain current ownership/service boundaries; viewer/restart tests remain in handoff |
| Agent preferences | Existing native equivalent | Access, default permission masks, hover height and language/public-language fields already persist through native AgentPreferencesService. Do not copy donor god-level elevation or agent-storage architecture |
| SimulatorFeatures | Narrow compatibility fixes implemented | Advertise supported AvatarHoverHeightEnabled for Firestorm. GetFeatures now uses the existing deep-copy path; the previous OSDMap constructor expression returned an empty map |
| Groups / GroupMemberData / GroupAPI | Retain newer native implementation | Native membership authority, title/power compression and hexadecimal power representation take precedence over donor numeric/registry shapes. No duplicate Group API is introduced |
| Profile/search identity and directory contracts | Retain native services; direct places equivalent confirmed | OpenSearch.OnDirPlacesQuery/DirPlacesQuery, native avatar picker/account identity and UserProfileModule remain authoritative; preserve HG/profile privacy and paid-classified metadata repairs |
| Event reminder subscriptions | Missing behavior implemented natively | Durable authenticated Search subscriptions, viewer add/remove hooks and XML-RPC/LLSD login restoration using Firestorm event_date_ut plus ISO UTC event_date |
| Grid economy | Retain implemented ContinuumEconomy | Existing authoritative ledger/reservations/capture/history and viewer integration replace donor-specific currency services; no older ledger or defaults substituted |
| Selected land auctions | Regional lifecycle retained; grid-wide integration implemented | Configured native regional bridge plus paginated WebUI catalog, bids and seller controls use the same journal, viewer authority and escrow callbacks |
| Built-in grid WebUI | Selected adapters implemented; remaining requirements explicitly transferred to product backlog | News/recovery/event/classified/portal/estate source is retained. The 11 blocked legacy paths have individual dispositions below; they are not 11 unexamined donor ports |
| Visitor logger, object cache replacement, viewer bans, legacy tooling, combat, on-demand regions | Excluded under the operator's explicit selection | Only auctions were selected from this optional/rejected list; no alternatives were silently reintroduced |

### Final implementation boundary

**Auctions:** `[ContinuumWebUIAuctions]` maps region UUIDs to fixed regional URLs;
`AuctionBridgeSecret` must match `[LandAuctions] WebUIBridgeSecret`. Endpoints are
off when the key is blank, require native HTTPS, explicitly trusted TLS proxy or
direct loopback HTTP, and atomically own/remove their handler. Frontend endpoint
URLs require HTTPS except loopback, forbid credentials/query/fragment, do not
follow redirects and bound response sizes. No URLs or actors are trusted from
resident form fields. Four configured regions are queried per page; each regional
catalog and its continuation page contain at most 20 listings. Up to 256 region
mappings are supported. Pagination keeps older open auctions reachable.

The simulator rechecks connected local resident authority before mutations,
retaining the existing money module's viewer-session authorization. Owners with
native estate authority can start/cancel eligible individually owned parcels;
other connected local residents may bid. Grid-level admin status does not grant
permission to cancel someone else's sale. Public results exclude bidder and
purchase IDs; owner/high-bidder history and pending amounts are identity scoped.
Bids require explicit full-reservation confirmation, preserve pending transaction
IDs, and report unconfirmed outcomes rather than create another payment attempt.
Account transport is HTTPS protected when auction endpoints are enabled; POST
origin and per-account operation limits apply. Outages hide financial controls.

**Limitations retained:** the portal does not authorize offline bids or HG/group
bidders. Residents must use their viewer to visit the target region. This is the
current native economy authority boundary, not complete Second Life website
parity. Auction settlement remains the existing recoverable capture/delivery
saga, with coordinated journal/parcel/ledger backups and operator handling of
review states. No live bids or money transfers were performed by this work.

**Event reminders:** optional `EventNotificationSecret` is configured separately
in ContinuumSearch, simulator `[Search]` and login `[LoginService]`. It is distinct
from the portal write key. `EventNotificationURL` requires HTTPS except loopback;
transport has deadlines, redirect refusal and a bounded response. The Search
service maintains `continuum_event_notifications`, limits each subscriber to 100,
uses serializable transactions for admission, makes add/remove idempotent and
filters removed/started events. The simulator derives the subscriber from the
validated live local client; the login service reads only the authenticated
account's list. Search outages yield an empty reminder list without failing
login. Firestorm's local notifier handles timing; there is no new server IM/email
dispatch system. All keys are blank by default. No messages were sent to users.

### Validation and remaining delivery work

- Full Release solution build: **0 warnings, 0 errors**.
- Existing WebUI acceptance suite: pass (news, recovery, events, classifieds,
  portal presentation and estates).
- Isolated auction HTTP/WebUI fixture: pass (actual bridge handler, transport/key,
  actor/session boundary, forms, escaping, origin, rate and outage behavior).
- Isolated event reminder fixture: pass (actual Search HTTP + SQLite persistence,
  subscriber isolation/restart/removal, both login formats and feature publication).
- Auction lifecycle + real SQLite escrow + grid portal lifecycle checks: pass.
- Fresh temporary SQLite Search self-test: pass, including authenticated reminders.
- Text diff check: pass. Embedded WebUI archive rebuilt.

Native service HTTP fixtures run in separate processes, matching the separate
simulator/Search service roles. Recycled in-process listener fixtures showed
transport timeouts; repeated listener teardown/replacement is not certified by
these tests and remains a runtime regression check. Tests and production
certification are not conflated.

MySQL/PostgreSQL execution, deployed configuration, live Firestorm, browser/mobile
layout, simulator restarts and coordinated backup recovery remain in
`doc/donor-feature-test-handoff.md`. No deployment, live configuration or
production schema change was performed. The newly authored behavior uses native
Continuum services; no WhiteCore implementation was copied wholesale.

**Carry-forward product work:** preserve all previously requested grid-wide WebUI
modernization and native-service gaps: the 11 legacy path dispositions, centralized
multi-host simulator management/reload coordination, actual account deletion,
mutual-consent partnerships, contact-data policy/storage, real-currency purchase
history and remaining portal customization. These are not marked implemented,
not silently excluded, and not reasons to restart the completed source comparison.

## Historical boundary before final implementation — 2026-10-24

The operator has directed sustained donor reconciliation, one inactive donor at a
time. WhiteCore is still active. The repeated WebUI implementation checkpoints
have obscured that boundary: they are not separate requests for the operator to
restart the work. Continue independently and consolidate results across feature
families. Keep manual tests in the existing handoff.

**Do not use the number of blocked legacy HTML routes as the donor completion
criterion.** The 11 remaining paths include duplicate operations, donor stubs,
service-backed gaps and larger Continuum product requirements. The full-tree
WhiteCore audit has not been certified complete. Preserve the broader WebUI
requirements, but track their implementation independently of donor-source review.

| Work family | Current source evidence | Remaining work / boundary |
|---|---|---|
| Display Names | Native account persistence, DisplayNameModule and LLLoginResponse display_name fields exist | Retain newer implementation; runtime propagation/HG checks stay in the handoff |
| Abuse moderation/notification | AbuseReportsService.GetReports/UpdateReport; CAPS estate-email SendSystemEmail path | Existing native implementation; no replacement donor service |
| Experience service/CAPS | Current native service and ExperienceModule GetExperienceInfo/GetMetadata/ExperiencePreferences | Preserve selected newer lineage; do not reopen as a WhiteCore service transplant |
| Upload/CAPS contracts | BunchOfCaps registers current task/notecard upload paths and two-stage upload handling | Finish bounded response/lifecycle comparison; a runtime test is not automatically a missing source port |
| Groups/preferences/features | Current GroupsModule, AgentPreferencesModule and SimulatorFeaturesModule | Finish response-contract comparison; no wholesale donor replacement justified |
| Profiles/search | Native profile service and bundled ContinuumSearch exist, including event/classified writes | Finish remaining identity/query/subscription comparisons; packet dispatch alone does not implement event reminders |
| Economy | Existing ContinuumEconomy ledger, reservations/capture/history and configured purchase handling | Preserve existing native economy; real-money purchase history is distinct from virtual-currency transaction history |
| Selected auctions | Native viewer start path, regional LandAuctionModule and recoverable escrow/settlement are implemented | Integrated grid-wide catalog/bid/management service boundary remains open; regional source is not that workflow |
| WebUI product work | Native adapters and recent news/recovery/event/classified/portal/estate workflows | Continue as a separately visible implementation backlog; modernization is not another donor to audit |
| Optional rejected families | Operator selected only auctions from that list | Visitor logger, object-cache replacement, viewer bans, legacy tooling, combat and on-demand regions remain excluded |

### All remaining blocked WebUI paths: source-backed disposition

| Path | What the pinned donor actually does | Current disposition |
|---|---|---|
| `admin/factory_reset.html` | Resets WebUI menus/settings through PagesMigrator/SettingsMigrator | Portal defaults/reset gap; it is not a whole-grid factory reset |
| `admin/gridsettings_manager.html` | Changes grid identity, system accounts/estates and related settings | Native authority/integration gap; portal branding is not an equivalent |
| `admin/region_edit.html`, `user/region_edit.html` | Reconstruct region configuration using WhiteCore simulator-management APIs | Existing RegionWeb provides per-host configuration/backups/reload, now linked from region lists; central multi-host editing remains a separate gap |
| `admin/sim_console.html` | Contains local RunCommand and reports that the sim console is not implemented | Donor stub; do not import as a working remote-console feature. Native remote operator access remains a product integration requirement |
| `admin/purchases.html`, `user/purchases.html` | Reads AgentPurchase real-currency purchase history, including RealAmount and purchase/update dates | Distinct economy-history adapter gap; generic transaction pages are not equivalent |
| `user/contact.html` | Reads/writes RLAddress/RLCity/RLZip/RLCountry through WhiteCore agent storage | Native resident contact-data model/workflow gap; the new support page does not complete it |
| `user/deleteaccount.html` | Uses donor DeleteUser after password verification and confirmation | Current IUserAccountService has no equivalent cross-service deletion contract; disabling a user is not deletion |
| `user/partnership.html` | Reads current partner and sends a proposal message; reciprocal profile writes are commented out | Donor does not supply a complete partnership lifecycle. A mutual-consent native service and atomic paired updates would be new implementation work |
| `user/update_user.html` | Combines password/email changes and donor deletion; template points at a legacy change_user_information action | Password/email already have native pages. Reconcile duplicate navigation separately from the missing deletion contract |

Do not silently drop these product requirements, label a stub as implemented, or
start each as a new donor port. The remaining source comparisons and selected
auction integration must be reported together. Live browser/viewer/database
certification remains distinct from source implementation and donor disposition.

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

## Historical opening executive disposition

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
| P2 | WebUI and Web APIs | optional Robust connector | Implemented as ContinuumWebUI with retained WhiteCore presentation assets and native OpenSim service adapters; direct WhiteCore persistence internals remain unsuitable. |

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
- **Runtime propagation correction:** when an avatar becomes root after a
  crossing or relog, the receiving simulator evicts its account cache, reloads
  the persisted grid value and sends `DisplayNameUpdate` to local observers.
  This targets the nameplate-only stale state seen while Nearby and search were
  already correct.
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

For the subsequent portal phase, that statement is a staging boundary rather
than a rejection of the donor implementation. The required target is the actual
integrated WhiteCore WebUI: its page/controller `.cs` files under
`WhiteCore/Modules/Web/html`, its deployable `.html` templates and static assets
under `WhiteCoreSim/bin/html`, and its `WebInterface` routing/page framework.
OpenSim/Robust service adapters may replace incompatible WhiteCore backend
architecture, but a newly designed replacement website is explicitly out of
scope unless the user separately requests one.

OpenSim-Grid-Interface at local checkpoint
`ecb377d42b5a1f0ec7a969a65f48596c8e5dbe87` is the designated secondary donor
for gaps in that WebUI. It contributes missing functionality into the WhiteCore
portal rather than becoming a competing replacement shell. Every portal page and
endpoint requires a WhiteCore-present / WhiteCore-incomplete / Grid-Interface
gap-fill / genuinely-missing disposition before implementation.

## Inactive-donor continuation — 2026-10-22

WhiteCore is the active donor after lickx and Mobius reconciliation. The pinned
local donor remains `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` (2026-05-18).
Current Continuum/upstream and newer Tranquillity code take precedence. A port
must establish a concrete benefit; older feature names alone are insufficient.
This continuation supersedes stale "implement next" recommendations above,
without claiming the complete WhiteCore tree has been reconciled.

Opening source-verified dispositions:

| Candidate | Current decision / evidence |
|---|---|
| Display Name login | Already present: LLLoginResponse supplies display_name in both response paths. Retain current implementation and runtime gates. |
| Abuse moderation | Already present: AbuseReportsService exposes GetReports and UpdateReport. Existing report/WebUI/provider acceptance remains; do not build another moderation service. |
| Estate abuse notification | Already present: CAPS AbuseReportsModule checks AbuseEmailToEstateOwner/AbuseEmail and calls SendSystemEmail. Retain the current implementation and delivery-failure gates. |
| Experience capability publication | Already present: ExperienceModule registers GetExperienceInfo, GetMetadata and ExperiencePreferences; BunchOfCaps includes the corresponding seed names. Retain selected service lineage; do not substitute WhiteCore service abstractions. |
| Parcel auction | Not selected as a direct port. Pinned AuctionModule.ViewerStartAuction returns a blank response with request parsing commented out. Bids are appended to AuctionInfo, and AuctionEnd chooses a winner and sends an offline message; that is not evidence of a complete atomic payment/escrow/ownership lifecycle. No safe, complete auction feature is established by importing this module. |
| Visitor logger | Not selected as a direct port. Pinned VisitorLogger performs synchronous file writes, concatenates the log path, uses TimeSpan.Minutes rather than total elapsed minutes, and has empty RemoveRegion/Close handlers despite event subscriptions. No demonstrated operational need justifies adding a parallel logger with those behaviors. |

No source, live configuration or deployed binaries changed in this pass. No
live tests were run. Continue the remaining profile/search/group/preference,
CAPS/upload, economy/WebUI and optional-module comparisons before declaring
WhiteCore complete. Manual requirements stay in donor-feature-test-handoff.md.

### Auctions selected by operator — 2026-10-22

The operator explicitly requires auctions; the earlier direct-port exclusion
does not exclude the feature. Only auctions from the optional/excluded feature
list are selected. Visitor logging, object-cache replacement, viewer bans,
legacy tooling, combat and on-demand regions are not added by this work.

Continuum now has an opt-in regional LandAuctionModule and durable
LandAuctionBook, using existing ContinuumEconomy purchase reservations rather
than WhiteCore's incomplete settlement. WhiteCore remains behavioral evidence;
no auction source/assets were copied. Firestorm source at
`S:\Github\phoenix-firestorm\indra\newview\llfloaterauction.cpp`,
`onClickStartAuction`, establishes the actual UDP ViewerStartAuction request
with agent/session IDs, parcel LocalID and SnapshotID. Continuum validates both
identity fields before dispatch. The existing parcel AuctionID viewer metadata
is used; older donor CAPS handlers are not substituted.

Estate owners/managers may start auctions on their own individually owned land.
Local-grid residents bid through a session-bound regional browser page, opened
by `/93 auctions`; the page also permits start/cancel without god-mode UI.
Only the seller with estate authority may cancel. The minimum and duration are
fixed from configuration when the auction starts. Group/foreign bidder support,
a grid-wide WebUI auction catalog and the external Second Life auction website
are outside this initial regional implementation.

Each bid reserves the full amount before replacing the high bid; old holds are
released afterwards. Thus increasing one's own bid requires enough available
funds for the full new reservation while the previous one remains held.
Concurrent equal bids admit one winner. Seller bidding, stale ownership/shape,
late/lower bids and ordinary sales/divide/join/deed/reclaim/abandon/force-owner
operations on marked auction land are rejected. Script parcel-owner changes
are blocked while marked. Auctions sell land only, not parcel objects.

Atomic journal replacement records reservation/capture intent before external
operations. Ledger purchase IDs are replayed after lost replies. A process
lease prevents two instances from opening the same regional journal. Up to four
active records are recovered per timer pass. End-of-auction capture pays the
seller before a separately persisted, idempotently retried land transfer.
This is a recovery saga, not a distributed atomic transaction: an unavailable
parcel store can leave a paid auction in Captured until delivery recovers;
externally replaced/corrupt parcels or journal loss require operator review.
Unknown authorizations retain their pending ID; retry the same bid, cancel or
allow expiry cancellation. Unresolved ledger outcomes retain holds/parcel locks
rather than silently reallocating land or charging a fresh transaction.
Do not manually resolve live auction holds without reconciling their journal.

Persistence is a per-region JSON journal (10,000-record lifetime bound, including
completed history) plus existing parcel storage and provider-backed economy.
Back them up together and preserve the journal across restarts/moving a region.
SQLite escrow is covered by automated tests; exact MySQL/MariaDB/PostgreSQL and
live Firestorm/browser/crossing checks remain in the handoff. The feature is
disabled by default, requires IAuctionMoneyModule (currently ContinuumEconomy),
and has not been deployed or certified for production.

## 2026-10-22 WebUI resident workflows

The current native WebUI bridge now implements news management, password
recovery, region-owner events and paid classified create/edit/delete. Classified
publication uses durable private-service payment receipts and explicit pending
recovery. Native profile edits preserve paid lifetime/price and reject foreign
creator overwrites; the PostgreSQL updater is now restricted by its primary-key
upsert. Sixteen legacy page paths and grid-wide auction WebUI remain open.
The current detailed disposition is `parity-webui.md`, with browser/viewer/provider
acceptance in `donor-feature-test-handoff.md`. No live deployment was performed.


### 2026-10-24 portal presentation checkpoint

WhiteCore remains the active donor. Welcome content/announcement, portal support
instructions/gallery visibility and fixed-route public navigation visibility now
have persistent admin workflows. Three manager paths are unblocked; **13 legacy
paths remain blocked**. This does not complete grid configuration, real-world
resident address editing, arbitrary page customization or full WebUI parity.
See `doc/parity-webui.md` for the precise implemented scope and remaining gaps;
browser/viewer acceptance remains in `doc/donor-feature-test-handoff.md`.


### 2026-10-24 native estate checkpoint

Resident/admin estate editing now supports name, reference price, public access,
voice and direct teleport through native service/provider conditional updates.
SQLite estate membership lookup and explicit-ban insertion were corrected during
persistence testing. **11 legacy WebUI routes remain blocked.** Simulator reload
is still required; native region lifecycle management and live estate propagation
remain outstanding. Ownership transfer and access-override editing are not part
of this bounded editor. See `doc/parity-webui.md` and the existing runtime handoff.
