# Integrated WebUI completeness audit

<!-- continuum-current-status:begin -->
> Project checkpoint — 2026-10-24: lickx, Mobius and the selected WhiteCore
> ports/native workflow adapters are implemented in source. The former 11 blocked
> WebUI routes now have native workflows. Live/provider acceptance remains open.
> See the [current donor audit](whitecore-continuum-improvement-audit.md),
> [WebUI backlog](parity-webui.md) and [test handoff](donor-feature-test-handoff.md).
> Earlier dated checkpoints below retain their historical scope; they do not
> supersede this status or certify deployment.
<!-- continuum-current-status:end -->

Date: 2026-10-24

## Product and precedence

Continuum targets a reusable complete-grid distribution, not one operator's
configuration. Preserve WhiteCore-derived presentation and useful workflows;
use current OpenSim services and newer compatible implementations underneath.
Firestorm source is viewer-protocol evidence. Second Life user expectations
are acceptance targets, not evidence that an OpenSim backend already exists.
A retained template or successful build does not establish workflow parity.

Current checkpoint: the needed selected WhiteCore WebUI workflows are implemented
in source. The former 11 blocked paths now have native reset/configuration,
private contact, archival closure, account-settings, partnership, currency-credit
and region/console workflows. Earlier route counts and gap classifications below
are historical. A route's presence alone does not establish feature completion;
the implementation/authority table and final passing checks are recorded at the
top of `whitecore-continuum-improvement-audit.md`.

## Completion boundary

Region configuration remains with the native RegionWeb host editor, while native
REST console credentials permit operator provisioning/restarts. This is a usable
native workflow with independent host authentication, not a newly invented central
host supervisor or shared SSO session. Native grid identity configuration saves
require ROBUST restart. Account closure archives; it does not erase related data.
Purchase pages show completed virtual currency credits, explicitly selected by
the user because no real-money provider is connected. Wider product evolution
and live acceptance remain separate, documented work; selected donor workflows
are no longer deferred merely by classifying their gaps.

## Historical opening gaps

The existing connector explicitly blocks 23 legacy page paths. Its mutation
switch supplies registration, password/email/profile updates and selected admin
account/report operations, not every donor operation. Regional auctions have a
session-bound regional page but no integrated WebUI route, catalog or grid-wide
bid/management adapter. The previous blanket statement that WebUI integration
was complete with only deployment tests pending is withdrawn.

Pages must be checked for navigation, authorization, input validation, complete
read/write behavior, persistence, provider parity, outage/restart handling and
user-visible results. A safe denial is preferable to false success but remains
a missing workflow where the feature is selected. Destructive factory reset,
remote console and obsolete external purchase paths need an explicit product
and authority disposition; they are not automatically new implementation tasks.

## Pinned donor controller inventory

Source: WhiteCore `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa`, all tracked
`.cs` controllers under `WhiteCore/Modules/Web/html`. Rows below map filenames
to candidate `.html` paths only. Aliases, fragments and renamed pages require
manual tracing; absence at the guessed path is not a confirmed missing feature.
"Adapter path present" means a literal route reference, not certified parity.
This inventory is a bounded starting index, not a declaration of completion.

| Donor controller page | Initial Continuum disposition |
|---|---|
| `admin/abuse_manager.html` | Adapter path present; verify complete read/write workflow |
| `admin/abuse_report.html` | Adapter path present; verify complete read/write workflow |
| `admin/estate_edit.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/estate_manager.html` | Adapter path present; verify complete read/write workflow |
| `admin/factory_reset.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/gridsettings_manager.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/news_add.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/news_edit.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/news_manager.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/page_manager.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/purchases.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/region_edit.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/region_manager.html` | Adapter path present; verify complete read/write workflow |
| `admin/settings_manager.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/sim_console.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `admin/statistics.html` | Adapter path present; verify complete read/write workflow |
| `admin/transactions.html` | Adapter path present; verify complete read/write workflow |
| `admin/user_edit.html` | Adapter path present; verify complete read/write workflow |
| `admin/user_manager.html` | Adapter path present; verify complete read/write workflow |
| `admin/user_password.html` | Adapter path present; verify complete read/write workflow |
| `admin/user_register.html` | Adapter path present; verify complete read/write workflow |
| `admin/welcomescreen_manager.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `classifieds/add_classified.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `classifieds/classifieds.html` | Adapter path present; verify complete read/write workflow |
| `classifieds/marketplace.html` | Template present; controller/route coverage unverified |
| `css/index.html` | No matching template path; check aliases/replacement |
| `events/add_event.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `events/events.html` | Adapter path present; verify complete read/write workflow |
| `forgot_pass.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `help.html` | Template present; controller/route coverage unverified |
| `home.html` | Adapter path present; verify complete read/write workflow |
| `http_404.html` | Template present; controller/route coverage unverified |
| `http_505.html` | No matching template path; check aliases/replacement |
| `index.html` | Adapter path present; verify complete read/write workflow |
| `irc_chat.html` | Template present; controller/route coverage unverified |
| `javascripts/jquery.jclock.html` | No matching template path; check aliases/replacement |
| `login.html` | Template present; controller/route coverage unverified |
| `logout.html` | Template present; controller/route coverage unverified |
| `maintenance.html` | Template present; controller/route coverage unverified |
| `mapapi.html` | No matching template path; check aliases/replacement |
| `news/news.html` | No matching template path; check aliases/replacement |
| `noregistrations.html` | Template present; controller/route coverage unverified |
| `online_users.html` | Adapter path present; verify complete read/write workflow |
| `region_info.html` | No matching template path; check aliases/replacement |
| `region_list.html` | Adapter path present; verify complete read/write workflow |
| `region_search.html` | Adapter path present; verify complete read/write workflow |
| `regionprofile/index.html` | No matching template path; check aliases/replacement |
| `regionprofile/owner.html` | No matching template path; check aliases/replacement |
| `regionprofile/parcels.html` | No matching template path; check aliases/replacement |
| `register.html` | Adapter path present; verify complete read/write workflow |
| `skeleton.html` | No matching template path; check aliases/replacement |
| `user/buyland.html` | Template present; controller/route coverage unverified |
| `user/classifieds.html` | Adapter path present; verify complete read/write workflow |
| `user/classifieds_edit.html` | No matching template path; check aliases/replacement |
| `user/contact.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `user/deleteaccount.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `user/email.html` | Adapter path present; verify complete read/write workflow |
| `user/estate_edit.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `user/estate_manager.html` | Adapter path present; verify complete read/write workflow |
| `user/event_edit.html` | No matching template path; check aliases/replacement |
| `user/events.html` | Adapter path present; verify complete read/write workflow |
| `user/friends.html` | Adapter path present; verify complete read/write workflow |
| `user/groupland.html` | Template present; controller/route coverage unverified |
| `user/home.html` | No matching template path; check aliases/replacement |
| `user/landfees.html` | Template present; controller/route coverage unverified |
| `user/mainland.html` | Template present; controller/route coverage unverified |
| `user/partnership.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `user/password.html` | Adapter path present; verify complete read/write workflow |
| `user/profile.html` | Adapter path present; verify complete read/write workflow |
| `user/purchases.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `user/region_edit.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `user/region_manager.html` | Adapter path present; verify complete read/write workflow |
| `user/transactions.html` | Adapter path present; verify complete read/write workflow |
| `user/update_user.html` | Blocked explicitly; workflow incomplete or deliberately excluded |
| `userhome.html` | Adapter path present; verify complete read/write workflow |
| `userindex.html` | Template present; controller/route coverage unverified |
| `webprofile/groups.html` | No matching template path; check aliases/replacement |
| `webprofile/index.html` | No matching template path; check aliases/replacement |
| `webprofile/picks.html` | No matching template path; check aliases/replacement |
| `webprofile/regions.html` | No matching template path; check aliases/replacement |
| `welcomescreen/gridstatus.html` | Adapter path present; verify complete read/write workflow |
| `welcomescreen/index.html` | Adapter path present; verify complete read/write workflow |
| `welcomescreen/info_box.html` | Template present; controller/route coverage unverified |
| `welcomescreen/news.html` | Template present; controller/route coverage unverified |
| `welcomescreen/region_box.html` | Template present; controller/route coverage unverified |
| `world.html` | Adapter path present; verify complete read/write workflow |
| `world_map.html` | No matching template path; check aliases/replacement |

## Historical opening reconciliation plan

1. Trace all blocked paths and record implemented equivalent, genuinely missing,
   obsolete, or deliberately excluded behavior, with evidence.
2. Check routed pages for write paths and false-success/placeholder responses.
3. Reconcile grid-wide auction discovery, bids and seller/admin management into
   the existing WebUI using authenticated native service boundaries. Do not
   mistake the regional auction page for this grid-wide workflow.
4. Inspect navigation, templates, fragments, localization and operator configuration
   beyond controller filenames; compare Grid-Interface only for established gaps.
5. Keep manual browser/viewer/provider requirements in donor-feature-test-handoff.md.

No live deployment or source functionality changed in this opening audit.

## Implementation checkpoint — grid news, 2026-10-22

Three formerly blocked workflows are implemented: admin/news_manager.html,
admin/news_add.html and admin/news_edit.html. The initial inventory above is
historical; **20 paths remain explicitly blocked**. Overall WebUI completion and
integrated grid-wide auctions remain open.

Administrators can create/edit drafts, publish, unpublish and delete articles.
Public feeds show published articles only, 20 per page. Deletion is POST-only;
the existing authenticated/same-origin checks and independent adapter admin
check protect mutations. Text is HTML-escaped, stale revisions are rejected,
and mutation logs identify the actor and article without logging article bodies.

ContentStorePath (default ContinuumWebUI/content.json) is a provider-independent
portal store with atomic replacement and an exclusive writer lease. Back up the
file. Accounts/regions/estates/ledger remain service owned. Storage is bounded
to 500 articles/8 MiB; corruption/unwritable storage fails rather than resetting
content or reporting success. Multiple portal processes need the same persistent
path for shared news; separate local paths produce separate feeds.

Responsive news cards/editors, keyboard focus, reduced-motion support, mobile
actions and revised landing onboarding are an initial modernization batch,
not complete portal graphics/accessibility/browser certification.

After asset edits, run addon-modules/ContinuumWebUI/Build-WebUIAssets.ps1 before
building: the portal serves embedded WhiteCoreWebUI.zip, not loose HTML. The
script preserves inherited assets and fixes entry ordering/timestamps. Source
files are included by prebuild's existing nonrecursive C# match.

tests/Continuum.WebUI.Tests verifies HTTP anonymous admin-route denial,
resident/admin mutation boundaries, draft privacy, escaping, actual embedded
template rendering, CRUD, stale edits, persistence, writer contention and
corruption. Live browser/visual acceptance remains in the test handoff.

## Resident password recovery — 2026-10-22

forgot_pass.html is now implemented, including the login link, email request,
single-use reset and disabled-service presentation. The existing password-change
page now uses labeled responsive forms and redirects to login after invalidating
credential-bound portal sessions. This checkpoint does not implement event or
classified creation/editing; directory inspection found query RPCs and an
internal event-save helper, not an authorized resident-write API. Those workflows
need native owner validation and any required idempotent listing-fee handling,
not a form that reports success without persisting through the services.

Operator setup is in both Robust examples: PasswordRecoveryEnabled defaults
false. Configure PasswordRecoveryURL to the public HTTPS portal root (e.g.
https://grid.example/webui/), a persistent PasswordRecoveryStorePath and the
PasswordRecoverySMTP* / PasswordRecoveryFrom fields. SMTP requires StartTls or
SslOnConnect, validates certificates and has a bounded delivery deadline.
No real mail account was configured and no emails were sent during development.

The request handler returns the same neutral response for matching and unknown
accounts. A bounded single-worker queue keeps account lookup/mail timing out of
that response. Limits: 32 queued/in-flight requests, five requests per client IP
per 15 minutes, three live issued records per account per 30 minutes and 4,096
active records total. Existing links are not invalidated by a new unauthenticated
request. Queue/mail failures do not change a password or reveal account details.

Only SHA-256 token hashes, identity/credential bindings and expiry/used metadata
are persisted. Tokens are 256-bit random values with 30-minute expiry. Email
links carry the secret in a URL fragment; the browser places it in the POST
form and clears the fragment. It never enters the normal server URL/query log.
Email/credential changes invalidate prior links. Durable consumption precedes
the password-service call: on a failed or ambiguous write the link stays spent,
and the user must request another. No password, recovery link or mailbox address
is logged by this implementation. Protect the recovery file like auth metadata;
restore it with account/auth data and keep it outside public asset directories.

While recovery is enabled, signed portal cookies bind the opaque auth token to
the current password hash/salt. Password changes invalidate existing WebUI
sessions across portal instances; enabling the feature requires existing portal
users to log in again. A login racing a reset is rejected rather than signing an
old-password token against new credentials. The configured authentication service
must support GetAuthInfo and SetPassword. Recovery-disabled login retains the
legacy token path. Existing in-world/presence sessions are not terminated by this
portal feature; it does not add a global token-revocation service.
All public portal instances must use the same recovery/session policy; a legacy
frontend cannot provide the new credential-bound session revocation guarantee.

Recovery-enabled account operations require HTTPS. Isolated direct loopback
HTTP is allowed for development; arbitrary Host/forwarded headers do not make
an external request trusted. TLS-offloading proxies must be explicitly listed in
TrustedProxyAddresses and supply X-Forwarded-Proto=https. Only trusted proxies
affect client-IP rate limits; X-Forwarded-For is traversed from the trusted right
edge so a caller cannot select an arbitrary rate bucket. Secure portal cookies
are issued for native HTTPS and explicitly trusted TLS termination.

Automated tests cover durable/expired/one-use/concurrent tokens, changed account
email/credentials, neutral dispatch, failed/ambiguous writes, fragment link
construction, actual form rendering, HTTP rate/origin/transport checks, proxy
trust, login/reset races and old-cookie rejection. News tests remain passing.
Real SMTP/TLS/browser/provider acceptance remains in the existing handoff.


## 2026-10-22 resident event management

The portal now supports event creation, editing and POST-only deletion, backed
by a private `continuum_event_write` Search RPC. Enable only by configuring
matching Search `WebUIWriteSecret` and WebUI `SearchWriteSecret` values (unique,
at least 32 characters); keep the service on a private network or HTTPS. The
key is never rendered into HTML. Writes are disabled by default. No deployment
or live database/configuration changes were made during this implementation.

Authority comes from the authenticated portal account, verified against the
native grid service for region ownership and additionally checked against the
search index. Clients cannot choose the creator. Current support is limited to
regions owned by that resident; parcel-owner and group permission support is
still pending and this does not claim full event parity. Event posting is free;
new events have no admission fee and no money is collected. Existing cover
metadata is preserved on edits. Broader configurable listing fees are not added.

Search allocates event IDs in the database. A durable request ID and payload
fingerprint prevent duplicate creation on identical retries. Ownership and
revision checks are part of conditional UPDATE/DELETE statements. Conflicting
forms fail rather than silently overwriting another edit. The schema adds a
revision column and request receipts at service initialization. Retain the
receipt table with the events database when backing up/restoring. Receipts are
retained, including after deletion, so an old retry cannot recreate a deleted
listing. MySQL/PostgreSQL execution is not yet certified for the new writes.

The forms use explicit UTC times. Category IDs (18–20, 22–29), maturity flags
(0/1/2) and global positions match local Firestorm source:
`indra/newview/skins/default/xui/en/panel_fs_search_legacy_events.xml`,
`indra/llmessage/lleventflags.h` and
`indra/newview/lleventnotifier.cpp`. The service converts region-local landing
coordinates to global coordinates using the indexed region handle. Event hop
links use native region origins and sizes instead of truncating every location
to a 256m region. A maturity increase beyond the indexed region is rejected.

SQLite acceptance covers private API authentication, disabled-by-default writes,
create/retry/update/delete, actor ownership, stale revisions, field validation,
region maturity and stored global coordinates. Embedded-form checks cover
configuration gating, secret isolation, rendering and native ownership rejection.
Browser/Firestorm and provider acceptance is recorded in the existing handoff.
Before the classified batch below, paid classifieds required a dedicated authenticated web charging contract
and durable charge/publication recovery; existing in-world charge authorization
requires viewer sessions and must not be weakened to accept portal cookies.
The existing SQLite profile `UpdateClassifiedRecord` also recomputes creation
and expiration dates on every update (`OpenSim/Data/SQLite/SQLiteUserProfilesData.cs`).
A portal editor cannot assume editing text preserves a paid listing's lifetime;
the provider lifecycle and fee behavior must be reconciled before exposing it.


## 2026-10-22 paid classified management

The portal now creates, edits and deletes resident classifieds. New listings
require explicit confirmation of a one-time payment and last seven days after
publication. They do not auto-renew. Editing title, description, category or
snapshot preserves the original price, dates, maturity and destination; deleting
a listing does not refund its fee. Expired listings are excluded from public
Search results. The native profile providers also preserve an existing listing's
creator, price and lifetime so a later viewer edit cannot bypass the portal rules.

The authenticated portal sends only its current resident as actor to the private
`continuum_classified_manage` Search endpoint. A service key protects every
operation, including private details/pending lists. Search must use the same
authoritative profile database as the profile service (as already required for
classified search). It manages classified writes and payment receipts in that
backend; the WebUI never connects to provider databases. The native schema is
retained. No new classifieds table is created by normal service startup.

`continuum_classified_orders` stores immutable publication payloads and receipt
IDs before payment. Confirmed capture precedes publication. Insertion and the
published marker commit together. If capture succeeds but its reply or the
publication write is lost, the pending listing remains visible only to its
owner; its Resume action reuses the same ledger purchase ID. Replays cannot
charge twice or recreate a previously deleted published listing. Insufficient
funds is a terminal rejected receipt, not a free listing; after adding funds,
the resident starts a new form. Ambiguous errors retain pending intent.

This is a recovery workflow across two databases, not a distributed transaction:
a fee can be captured while publication remains pending. Restore/back up the
Search/profile database and economy ledger consistently; retain order receipts.
Pending publication requires an explicit resident Resume or operator repair if
storage/configuration cannot recover. No background refund or renewal service
is introduced. Editing/deletion locks the current row and checks an opaque
content token so stale forms fail without replacing newer data.

Opt-in configuration:

- WebUI `SearchWriteSecret` matches Search `WebUIWriteSecret` (also used by events).
  Enabling it requires HTTPS for portal account operations/sessions independently
  of password recovery; direct loopback development remains allowed. TLS proxies
  must be explicitly trusted before forwarded headers affect this policy.
- Search `ClassifiedEconomyURL` plus `ClassifiedEconomySecret` enable paid creation.
  The URL requires HTTPS except for loopback HTTP. The money secret is distinct
  from the portal write key and is never passed to the browser.
- Economy `WebUIClassifiedSecret` matches that money key and must differ from
  `RegionSharedSecret`. The dedicated handler can only capture classified fees
  (type 1103) to the configured system account; it cannot select arbitrary
  recipients/types or bypass the existing viewer-session handlers.
- Search `MinimumClassifiedPrice` defaults to 50 and controls new invoices.
  Existing pending receipts retain their quoted amount if that minimum changes;
  the dedicated money authority checks a positive, immutable charge for the fixed
  purpose/recipient. Paid WebUI creation currently supports
  ContinuumEconomy; other economy backends, free grids and automatic renewal
  need separate adapters/policy and are not claimed as complete parity.

Ownership checks, row locking and parameterized writes support the existing
SQLite/MySQL/PostgreSQL backends. New execution was verified on disposable SQLite
fixtures only; MySQL/PostgreSQL write/runtime certification remains queued.
Source limits are 100 existing classifieds per account and ten pending intents
at preparation; these are admission checks, not a cross-service global quota.
Description/title/category/image/price/coordinates/maturity are checked server
side. Native grid validation bounds destination coordinates; indexed region
maturity constrains the listing. The portal displays untrusted text as text.

Firestorm checks: `indra/llmessage/llclassifiedflags.h` and `.cpp` establish PG
4, moderate 10, adult 64 and auto-renew 32. New listings emit 4/10/64 without 32.
`llpanelprofileclassifieds.cpp` supplies the default minimum price 50 and limit
100; the operator can configure the minimum independently. Global positions
use the indexed region handle and the native vector representation.

Automated evidence includes real SQLite ledger capture through the private HTTP
handler, a deliberately lost capture reply followed by Search-store restart and
single-charge publication, recipient/type restrictions, repeated and conflicting
purchase IDs, insufficient funds, disabled authority, immutable paid metadata,
owner/stale-edit/delete checks, concurrent submissions, expiry filtering and
actual embedded create/edit/pending rendering. News/recovery/event regressions
remain in the suite. No live deployment, schema/configuration changes or real
payments were performed. Viewer profile caches and browser/mobile behavior still
need runtime checks, including profile refresh/relog after WebUI changes.


### Native viewer/provider interoperability corrections in this batch

All three built-in profile providers now keep creator, creation/expiration dates
and listing price on updates to an existing classified. Ownership guards reject
foreign-creator replacements. SQLite uses a guarded upsert instead of destructive
INSERT OR REPLACE. PostgreSQL uses a primary-key upsert instead of the previous
UPDATE CTE that lacked a row predicate; it cannot update unrelated classifieds.
MySQL guards each conflict assignment and verifies the resulting owner.
New native-listing lifetime calculation is retained; this change prevents edits
from renewing/repricing existing listings, not a new recurring-billing policy.

Native price readers now use Int32 rather than Int16, so prices above 32,767 do
not fail profile reads. SQLite's classified delete parameter spelling is fixed.
The viewer delete handler verifies the classified against the current resident's
service-side listing before forwarding the deletion. Existing viewer charge
session checks remain intact. This does not redesign the legacy profile RPC
protocol or claim new transport authentication for that separate service.

The actual SQLite provider was exercised against a paid web-created listing:
a viewer-style edit preserved its paid dates/price, a foreign creator failed,
a 50,000-price listing read back correctly, deletion worked and unrelated data
remained. MySQL/PostgreSQL changes compile but still require their provider
matrix; new native-listing duration defaults are not unified with portal policy.


Additional completion checks: Search binds native PostgreSQL profile UUID fields
as UUID parameters and categories as text, matching the migrated profile schema.
Order receipts keep their separate text-key schema. SQLite runs use the same
native category representation; PostgreSQL execution remains uncertified.
Classified POSTs are limited to 30 per account per ten minutes on each portal
instance, including differently cased route aliases. An unavailable management
backend suppresses new-payment controls and displays an explicit status.

Authenticated HTTP-controller checks now verify that event/classified success
responses are returned as success, not error-prefixed messages. Cross-origin and
plaintext account POSTs are rejected, and account rate limits are exercised.
The legacy `userindex.html` route resolves to the current main portal shell so
its account links use the same implemented routes instead of a separate legacy
UserMenuItems model. New forms retain owner-only data and no service secrets.


## Portal administration reconciliation — 2026-10-24

Reconciled the pinned WhiteCore welcome, settings, page and grid-settings
controllers. Three previously blocked manager routes now have native Continuum
adapters: `admin/welcomescreen_manager.html`, `admin/settings_manager.html`, and
`admin/page_manager.html`. This is a bounded presentation implementation, not a
claim of full donor-manager parity:

- Welcome heading/introduction and an optional plaintext announcement persist;
  the announcement appears on both the portal home and viewer welcome screen.
- Portal settings provide resident support instructions and the home gallery
  visibility switch. Authenticated residents use the new `user/support.html`.
- Navigation can hide/show nine supported public links. Home, login/account and
  administration access cannot be removed through these controls. Hidden pages
  remain reachable; authorization is enforced by the existing native adapter.
- Fixed application routes replace arbitrary page locations and executable
  template/CSS overrides. Custom menu ordering/labels, map-center settings and
  browser-managed registration policy have not been implemented. Registration
  remains governed by `AllowRegistration`; public sign-up actions honor it.
- `admin/gridsettings_manager.html` remains blocked: grid identity, system
  accounts/estates and operational service settings require authoritative native
  support. Announcement text does not override measured grid availability.
- WhiteCore `user/contact.html` stores real-world postal address information.
  It remains blocked and is not represented as completed by the support page.

`PortalSettingsPath` defaults to `ContinuumWebUI/portal-settings.json`, separate
from news. Bounded JSON is validated on read/write; corrupt content fails closed.
Writes acquire a process lease, reload under the lease, require the editor's
current revision, flush and atomically replace the file. All manager POSTs check
administrator authority and the HTTP origin. Submitted text is escaped, with
no arbitrary markup, URL or credential fields. A global revision prevents
conflicting updates even when administrators edit different sections.

Release build and WebUI acceptance harness passed, including restart persistence,
stale/concurrent writers, protected navigation, resident and cross-site denial,
HTTP success handling, embedded forms, escaping and disabled controls. Embedded
assets were rebuilt. Runtime browser/Firestorm and backup checks are queued in
`doc/donor-feature-test-handoff.md`; no deployment or live config changes made.


## Native estate editing and region reconciliation — 2026-10-24

WhiteCore remains the active donor. Reconciled the pinned donor's resident and
administrator estate editors and region editors against current native service
contracts. `user/estate_edit.html` and `admin/estate_edit.html` now provide native
estate editing from their respective estate lists. **11 legacy paths remain
explicitly blocked**; source availability does not certify complete donor parity.

Implemented estate fields: name (1–64 characters), reference price per square
metre (0–1,000,000), public access, voice and direct teleport. The price is estate
metadata; saving does not sell land or debit an account. Resident editing requires
current native estate ownership; the administrator route checks configured admin
level. Both the page and mutation adapter enforce these rules. Native owner
identity comes from a fresh estate load, not submitted owner/actor fields. Lists
also recheck loaded ownership instead of trusting a potentially stale owner index.

The optional `IEstateSettingsUpdater` capability leaves existing service plugins
compatible and hides write controls when unsupported. The local EstateDataService
and SQLite/MySQL/PostgreSQL stores implement it. A single conditional statement
matches estate ID, old owner and all five old edited values, then changes only
those five columns. Text comparison is case-sensitive. The WebUI also checks an
opaque form token; the database comparison closes races after the form check.
No new schema, WebUI database queries or public estate-write endpoint is added.
These are trusted in-process service calls, with HTTP identity/origin enforcement
at the existing portal boundary. Remote/third-party connectors without the new
capability remain read-only.

Manager, access, group, ban and experience lists, membership, ownership, access
override and all other settings remain intact. Ownership transfer, creation,
deletion and membership changes are not implemented by these editors. The donor's
`TaxFree` name predates current access-override semantics. Local Firestorm's
`llestateinfomodel.cpp` uses `REGION_FLAGS_ALLOW_ACCESS_OVERRIDE` and sends
`override_public_access`; the current native estate handlers' inversion must be
reconciled separately before adding that control. It is preserved, not relabeled
as a tax switch. No simulator settings or viewer protocol are downgraded to donor
presets.

**Runtime limitation:** these saves persist native estate settings; running
simulators still require a native estate reload (or a controlled restart) before
they use the changes. RegionWeb Estate Admin can reload estate data for its
loaded regions through the Reload action on an allowed estate-scoped file.
The form and success response state the reload requirement. An unreloaded
simulator's later viewer save can overwrite the stored settings. Automatic reload
fan-out from the grid portal remains outstanding; the legacy shared-token
estate communications handler is not exposed through an arbitrary-URL browser
proxy. Operators should verify state after reload before changing policy in-world.

The region editors remain blocked. WhiteCore's controllers reconstruct simulator
configuration, including location, size, terrain, ports and presets, and depend on
WhiteCore region-management APIs. Native `IGridService.RegisterRegion` is region
registration, not a replacement for durable simulator lifecycle management.
Continuum already has authenticated per-host configuration editing, backups and
estate reload in RegionWeb Estate Admin. Optional operator-configured HTTPS links
from resident/admin region lists now reuse that control room. RegionWeb retains
its independent estate-owner login; portal cookies are not forwarded. Links come
only from the `[ContinuumWebUIRegionControlRooms]` region-UUID map, never submitted
URLs or directory hostnames; no backend requests are made to those links. Full
grid-wide editor integration and multi-host reload coordination are still missing.
Changing directory entries alone would leave configuration/runtime inconsistent.
These gaps remain selected work, not silently excluded features.

Two native SQLite/estate defects found during verification were corrected:
`GetRegions` now reads actual estate membership instead of returning an empty
list; `EstateSettings.AddBan` deduplicates explicit entries independently of
payment/age access policy. Previously an estate denying anonymous users could
lose explicit bans when a provider loaded them through `AddBan`.

Release build passed with zero warnings/errors. The WebUI acceptance matrix
covers actual SQLite migration/persistence and service delegation, stale and
concurrent writers, ownership races, unrelated policy/list preservation, region
lookup, explicit bans, owner/admin HTTP authority, origin checks, GET non-mutation,
unsupported services, navigation and escaped embedded forms. MySQL/PostgreSQL
runtime and real browser/Firestorm reload checks remain in the existing handoff.
No live deployment, grid configuration or production database changes were made.


## Auction integration and donor closure — 2026-10-24

`auctions.html` now lists configured regional auctions and provides native
viewer-authorized bids and seller controls through the regional bridge. Public
listing, identity-scoped history/pending data, pagination, explicit reservation
confirmation, endpoint/key validation, outage handling and mutation rate/origin
checks are implemented. Bidding requires the authenticated local resident's
viewer session in the selected region; offline/HG/group bidding is not implemented.
See the final WhiteCore audit for configuration, acceptance commands and exact
limits. This closes the recorded donor-source catalog while preserving all 11
blocked-route and larger portal/native-service requirements as product backlog.
