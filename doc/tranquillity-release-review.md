# Tranquillity release review

Donor: release/v1.1.2 at `628bdc7b09c701657693dd2d8cdc2be328f66dcb`.
Develop reference: `1f93669fc8ae8f77e666656d3338acd5fe855b6c`.
Comparison baseline: official OpenSim `9d00a55552` and published Continuum
`393885988b`. The numbered entries below preserve historical checkpoints;
the current boundary is recorded here.

## Current source boundary — 2026-11-19

The selected Tranquillity stable-release port is implemented, including AIS and
server-side appearance baking. The numbered batches below are historical;
earlier statements that upload binding, library links or baking are missing no
longer describe current source. Live acceptance is separate and remains open in
[the test handoff](donor-feature-test-handoff.md). Publication uses an independent healthy clone; no deployment or live
acceptance is implied.

The fetched stable master is `9ce6f33d3b3b617479389a896ba1f41d85736eb0`.
Its five commits beyond `628bdc7b09` contain release merging/documentation and
packaging changes, with no additional runtime code to port. Official OpenSim
master still resolves to `9d00a5555225b17d3d1063cb0f3496fb1b6217f9`, already in
the published Continuum baseline. Tranquillity develop is separately recorded at
`80a642a681759de7ccb798bb06466a50b099b9dd`, 258 commits beyond the release pin;
that evolving development stream is not this release's finish line.

AIS is wired through `[AIS] ExperimentalEnabled=true`, default false. It includes
resident/library reads, metadata and permission updates, category/link creation,
outfit replacement, purge/delete, validated library link copy and confirmed
native upload transactions. Direct arbitrary asset-ID rebinding is not selected;
Firestorm's normal item saves and native OpenSim use upload transactions. Moves
already exist natively; the donor does not implement parent moves in PATCH.
Provider, viewer fallback and multi-process concurrency checks remain acceptance
items. The process-local replay cache is not a database transaction.

Server-side baking is implemented through native assets, avatar storage, login
replies, the public texture connector, region protocol flag, appearance packets,
COF handshake and confirmed appearance-save trigger. A standalone adapter hosts
the same public connector. It stays off by default. See
[configuration and recovery](server-side-baking.md).

| Donor evidence | Continuum decision |
| --- | --- |
| `e9ba7f6eed` SQLite region extras and estate region listing | Ported the region extras API and migration at Continuum RegionStore v44. Estate region listing already exists. |
| `b805e944b9` PostgreSQL authentication and inventory | Fixed authentication field updates, binding both parameters and restricting column names to the auth schema. Inventory moves already quote mixed-case identifiers; no duplicate port. |
| `8fa0ba65b4` Mesh instance labels | Honor the nonblank string label sent by Firestorm as `instance_list[].mesh_name`; keep the existing fallback names. |
| `3e959cc810`, `612c8793ed` Reflection probes | Preserve 0–100 ambiance through serialized extra parameters; return flags as integers for prim and avatar readback. |
| `c8bdf045bf` Region-store fixes | Read a NULL SQLite task inventory description as empty. PostgreSQL's current migrations already use valid quoting and Continuum uses binary linkset storage; do not import the donor's old text column. |
| `3aaf0aeecf` Free land for sale | Current stock/Continuum path already classifies using the sale flag and buyer eligibility. |

Pending comparison: Experience persistence and consent deltas; viewer
appearance baking and AIS inventory; script and physics compatibility fixes.
Treat Phlox/SLua, the bot framework, framework upgrades and replacement
libraries as separate architecture decisions. Do not import those as
dependencies of unrelated fixes. Live acceptance is in the
[donor test handoff](donor-feature-test-handoff.md).

## Second batch: Experience persistence and compatibility review

- Ported the region-store portion of `148896caec`: task inventory keeps its
  Experience ID across database reopen, with zero as the default for old rows.
  RegionStore versions: SQLite 45, MySQL 69, PostgreSQL 59.
- `f8ea768647` is already covered by all three estate providers. No duplicate
  estate migration. Agent inventory, object XML, and foreign import handling
  (`ca0a691c81`, `cab4048246`) are still open and need joint review. Consent
  revocation (`575e197636`) is also open.
- Ported the infinite-Z vector check from `918650a7d3`. The other changes
  in that bundle have independent runtime effects and remain under review.
- `704127830c` adds two distinct capabilities absent from Continuum: Firestorm
  AIS v3 and server-side appearance baking. Existing inventory and rebake paths
  remain in place. AIS and baking need separate ports and viewer acceptance;
  neither was integrated in this batch.

Validation: combined Release build passed (two existing Gloebit warnings).
The focused harness passed real SQLite region and script inventory reopen,
reflection probe serialization and infinite-Z rejection. Other providers and
live viewer behavior remain in the test handoff.

## Third batch: consent and simulator lifecycle

- Adapted `575e197636` for the current Experience module. Explicit block or
  forget withdraws the specified Experience even if no parcel can be resolved.
  Only grants marked JoinAnExperience are cleared; unrelated Experiences and
  ordinary script permissions are left alone. Release captured controls when
  present and detach the affected Experience's attachments. Retain existing
  land-policy reevaluation for parcel changes.
- Ported the avatar shutdown and mass fixes from `918650a7d3`: snapshot the
  client during shutdown and keep the last valid actor mass through temporary
  physics removal. No default physics material or engine change.
- Agent inventory and archive transfer still need the foreign-import policy
  reviewed as a unit. AIS and baking are separate ports; the release uses a
  distinct inventory capability and a separate baking service/compositor.

Third batch validation: Release build passed with two existing Gloebit
warnings; focused harness passed SQLite reopen, probe serialization,
infinite-Z rejection and avatar mass retention. Firestorm withdrawal and
simulator shutdown race acceptance remain in the handoff.

## Fourth batch: vehicle, movement and sculpt compatibility

- Adapted `37c1781019`: nonpositive move time leaves an active movement target
  alone, copied prims own their vehicle data, and the base physics actor clears
  stale vehicle flags before applying new ones.
- Adapted `04693ba26c`: standard Meshmerizer masks the sculpt flag bits before
  choosing a topology, and the shared mesh key includes the complete sculpt
  type byte. Keep ubODE's existing topology decoding.
- Compared the agent-inventory and object XML changes in `ca0a691c81` and
  `148896caec`. A complete port also needs the inventory RPC/store fields and
  foreign asset/state handling in `cab4048246`. No cross-grid serialization
  change is included in this batch.

Fourth batch validation: combined Release build passed with two existing
Gloebit warnings; focused checks passed sculpt cache separation, zero-tau
movement, isolated vehicle copy settings, and vehicle flag replacement.

## Fifth batch: resident inventory Experience links

- Adapted `ca0a691c81` for the resident inventory model, SQLite/MySQL/PostgreSQL
  stores, internal XInventory RPC, script copies and task/agent inventory
  conversion. Legacy rows default to an unlinked script.
- The foreign inventory connector suppresses Experience metadata both ways.
  Hypergrid service writes do not establish an Experience link. A foreign
  metadata update to an unchanged asset preserves a previously stored local
  link; an asset replacement clears it.
- A legacy YEngine state with no ExperienceKey now preserves the inventory
  association instead of resetting it. Object XML, OAR/IAR and Hypergrid
  attachment/asset state remain open under `148896caec` and `cab4048246`.

Validation: Release and the final focused InventoryService rebuild passed.
The harness reopened actual SQLite resident inventory and checked local
persistence, foreign add/update handling and asset replacement. Live ROBUST
and Firestorm, plus MySQL and PostgreSQL migrations, remain in the handoff.

## Sixth batch: object XML and foreign Experience links

- Adapted the object XML portion of `148896caec`: task inventory writes and
  reads the Experience association used by local object assets and OARs.
  Older XML without the field still loads.
- Adapted `cab4048246`: foreign object assets are sanitized before this
  connector caches/stores them. Foreign attachments clear task links and the
  attachment association before applying script state. The sanitizer also
  clears Experience grants so a serialized state cannot restore them.
- This does not retroactively rewrite already-cached foreign assets, change
  standalone IAR script items, or certify every Hypergrid transfer route.
  AIS v3 and server-side baking remain separate open ports.

Validation: combined Release build passed with two existing Gloebit warnings.
The focused harness passed real task inventory XML round-trip, foreign object
and saved state link/grant removal, and rejection of invalid foreign state.

## Seventh batch: AIS v3 read foundation

- Ported the donor LLSD inventory envelopes and adapted item, category,
  children, categories, links and Current Outfit read routes against
  Continuum's existing inventory service. Enforce ownership on results even
  when a provider looks up by ID alone. Bound expansion and link target lookups.
- The capability is intentionally not registered. Firestorm uses the same
  capability for create, update, copy, purge, delete and outfit link changes.
  Shipping read-only registration would reroute those operations and break
  existing inventory behavior. Library AIS and server-side baking are open.

Validation: full Release build and final CAPS rebuild passed. The SQLite-backed
harness verified an owned item read, foreign-owner refusal, method and path
checks, and the children response's three embedded collections. No Firestorm
session used the capability.

## Eighth batch: AIS metadata writes

Adapted the name and description subset of Tranquillity's AisMutation.
Item PATCH can rename or change the description; category PATCH can rename
ordinary folders and outfits. The stored record supplies identity, asset,
permissions, and Experience association. Foreign-owned records, root/system
folder renames and fields outside this subset are refused. Requests are
limited to 16 KiB of LLSD XML with DTDs disabled. Replies report the parent's
stored version when the inventory provider returns it.

The focused CAPS Release build and SQLite-backed harness passed, including
owned item and folder updates, owner isolation, preservation of protected
fields, and rejection of unsupported fields. This is not the full Firestorm
write contract. Create, copy, purge, delete, outfit link replacement, remaining
item fields, library AIS and live verification are open. Before registering
the capability, also verify that the provider increments versions as needed
and that metadata writes cannot overwrite concurrent item changes.

## Ninth batch: AIS folder and link creation, item removal

Adapted Tranquillity POST category and DELETE item routes. Create preflights
the entire request, accepts ordinary and outfit folders and links to owned
items, and reports the rows actually created if a provider refuses a later
write. Delete verifies ownership and checks the row is gone before reporting
success. Focused Release and SQLite regression checks passed. Firestorm's
COPY route is for library categories; library copy, purge, folder deletion,
outfit link replacement, and full capability registration remain open.

## Tenth batch: AIS folder removal and purge

Adapted Tranquillity's category DELETE and children purge routes while
retaining Continuum's Trash/Lost And Found policy. Bound the subtree walk,
refuse foreign records, protected folders, and cycles before deleting, and
report removals verified after the provider operation. A purge preserves
the addressed folder. Corrected the stock service's Lost And Found ancestor
check. Focused CAPS and InventoryService builds and SQLite checks passed for
Trash purge, child deletion under Trash and Lost And Found, and refusal of
foreign, root, and outside-Trash deletions. AIS remains unadvertised; outfit
replacement, library copy/support, concurrency acceptance and baking remain.

## Eleventh batch: AIS outfit link replacement

Adapted Tranquillity AisSlam for Current Outfit and saved Outfit folders.
Validate the complete bounded body and every owned target before writing.
Accept owned outfit-folder targets and owned non-link item targets. Preserve
non-link inventory rows. Verify new links exist before removing old links;
on a create failure attempt to undo new writes and report observed deltas.
An empty array explicitly clears links; an empty map is invalid.
The focused CAPS build and SQLite harness passed, including injected partial
creation failure. Multi-writer races, folder versions, and transaction
replay still need resolution before capability registration. Library
support and server-side baking remain open.

## Twelfth batch: AIS library reads and category copy

Adapted Tranquillity LibraryAPIv3 item and category reads and the COPY route
using Continuum's existing library. Only the resident's own destination is
accepted. Honor Firestorm's shallow-copy query marker; bound recursive copy
and return confirmed additions on partial errors. Preserve asset, creator and
permission fields under the existing library-copy policy; reset group ownership
and Experience links. Library link rows are skipped rather than copied with
dangling targets. Focused CAPS Release and SQLite checks passed.
Capability registration, replay/concurrency and server-side baking remain open.

## Thirteenth batch: AIS write coordination and retries

Resident writes and library copies now share a bounded, process-local write
coordinator. It serializes AIS mutations for a resident and keeps replies
for 120 seconds when Firestorm provides a transaction ID. An exact retry
returns the existing response; a changed method, URL, destination, or body
under the same ID is refused. Failed or ambiguous writes are also remembered
to avoid repeating a possibly partial mutation. This is not a database
transaction and does not coordinate legacy writes, multiple simulators, or
restarts. The focused CAPS build and SQLite harness passed, including eight
concurrent retries of one outfit replacement and library COPY replay.
Provider folder versions, remaining viewer routes, registration, and baking
are still open.

## Fourteenth pass: registered HTTP paths and version validation

Adapted donor AIS capability lifecycle through Continuum's existing Caps API.
Variable-path registration is optional and leaves previous exact-path callers
unchanged. Logout deregisters handlers; region removal and module close also
remove active AIS URLs. Registrations use weak references to avoid retaining
logged-out Caps objects. Neither capability is advertised with the option off.

Real HTTP tests exposed that OSHttpRequest.RawUrl omits query parameters.
Both AIS handlers now use Url.PathAndQuery for transaction IDs, depth and
child selection. The harness verifies stored folder versions on HTTP creates,
retries and outfit writes. Library copy reports final versions for copied
folders, not their initial creation versions.

PATCH accepts whole-item viewer metadata without replacing stored ownership,
asset identity, current/base permissions or Experience linkage. Next/everyone/
group permissions are bounded by the base mask and preserve the existing
export bit. Permission/sale edits set the stock item change flags.

The test-grid option remains experimental because the source gaps listed at
the top are still unresolved. Do not close the donor or proceed as if AIS and
server-side baking are done.

Validation for this checkpoint: full Release solution build passed with the
two existing Gloebit obsolete-API warnings. The final CAPS rebuild passed with
zero warnings/errors. The focused SQLite harness passed registered HTTP GET,
POST, PATCH, PUT, DELETE and library COPY, eight concurrent HTTP retries,
DTD/body bounds, default-off behavior, cross-owner refusal, stored parent/child
versions, logout and region teardown. Earlier direct-handler tests alone did
not establish query-string behavior; the HTTP tests now cover it explicitly.

This checkpoint wires an experimental test path; it does not complete AIS.
The next required implementation is confirmed native upload transaction
handling, followed by the remaining request/data-model differences noted at
the top. Do not start server-side baking by treating these gaps as resolved.

## Fifteenth pass: confirmed native AIS upload results

Connected item PATCH hash_id to existing agent upload transactions instead of
interpreting the transaction UUID as an asset UUID. The module resolves the
current root agent and the optional confirmed-result interface. Unknown uploads,
other sessions, incompatible asset types and native validation failures cannot
consume or replace the item. The legacy upload interface remains unchanged.

The native path validates wearable texture permissions, stores the completed
asset, updates the inventory row and verifies the saved asset ID. Notification
failure after persistence does not misreport a saved upload as failed. Provider
failures are refused; asset storage and inventory writes are not one database
transaction and can leave an unreferenced asset on a failed inventory write.

When Firestorm omits query tid, hash_id supplies the process-local replay key.
An exact retry returns the confirmed reply after uploader consumption; a changed
request under that key is refused. A known pending upload is not cached as a
final failure because it has not mutated inventory. Restart and multi-process
replay remain outside this guarantee.

Validation: the SQLite harness exercises the actual native uploader completion
path with simulated transfer state and asset service: pending leaves the item
unchanged, completion saves asset and item, wrong-session and unowned wearable
texture attempts are refused, and exact hash retries succeed after consumption.
This does not simulate Firestorm transport or establish live viewer acceptance.
Full Release solution build passed with the two existing Gloebit obsolete-API
warnings and zero errors. The final harness passed all SQLite, native upload,
registered HTTP, replay, ownership and foreign Experience checks.

AIS remains default-off and experimental. Direct asset rebinding, library links
and remaining donor request differences still require comparison
against native/master behavior; server-side baking is also open.

Move-scope clarification: donor AisMutation.ApplyToItem explicitly does not apply
parent_id, and native/master inventory already supplies moves through its
existing paths. An AIS move port is not an established missing donor feature.
Continuum rejects changed parents rather than claiming a completed move; verify
Firestorm fallback before deciding whether an additional AIS move route benefits
Continuum. Earlier checkpoints listing moves as required implementation were
unproven scope, not evidence of a missing native feature.

## Sixteenth pass: library links and direct asset-ID reconciliation

At the pinned release, AisCopy copies all item rows but preserves link target IDs
unchanged. Continuum's earlier bounded adapter skipped links. It now preflights
item/folder link targets against the owned library, remaps targets included in
the copy to their new IDs, and retains verified external library references.
Shallow copies retain references to targets excluded by depth. Dangling targets
and item links targeting another link are refused before creating any rows.

Creation stores all folders, then real items, then links, so a failed real target
write cannot leave a newly created link to an unsaved copy. Readback verifies
owner, folder, asset and type. The delta separates links from items and reports
final stored folder versions. Partial additions remain explicit and replayed;
this remains additive, not a provider transaction. Experience/group ownership
reset and native library permission preservation remain in force.

Direct asset-ID decision: donor AisMutation applies a supplied asset_id without
resolving an upload transaction. Continuum's native Scene.UpdateInventoryItem
does not replace an asset from the incoming metadata row; it delegates a nonzero
transaction to the asset module. Local Firestorm LLViewerInventoryItem::
updateServer and update_inventory_item(item) erase asset_id/shadow_id and send
hash_id for an upload. Wearable save in llagentwearables.cpp uses that item path.
Those edits are served by the confirmed native upload bridge. Do not import
unvalidated arbitrary asset rebinding as a prerequisite for AIS completion.
The generic LLSD update helper can carry other fields; any concrete additional
asset workflow needs viewer and native permission evidence before a port.
Unchanged asset_id remains accepted; a changed one returns 400.

The local donor checkout HEAD is newer than the release pin. A git diff against
628bdc7b09 confirms AisCopy.cs and AisMutation.cs are identical at the selected
release; this pass does not advance the donor baseline.

Validation: focused CAPS Release build, full Release solution build (zero
warnings/errors on this incremental pass) and complete SQLite/native/HTTP harness
passed. New tests cover item and folder links, cross-folder remapping, external
library references, shallow copies, owner/Experience rules, proper link envelopes,
dangling/link-chain preflight, injected target-write refusal and failed replay.
Real Firestorm resolution of external library links and provider concurrency are
live acceptance items, not established by the in-process harness.

## Seventeenth pass: appearance pipeline and stable-source closeout

Ported the compositor and its embedded viewer layer/mask resources as a separate
.NET 8 assembly. Keep Continuum's native logging, plugin discovery, client APIs,
asset service and avatar service. The build restores pinned Skia/CoreJ2K packages,
copies native runtimes, and resolves the matching native Skia binary from the
shared bin layout. Source and viewer data notices accompany the port.

The region module advertises UpdateAvatarAppearance only when opted in, checks
COF versions, resolves owned wearable links and queues native appearance saves.
Confirmed saves trigger baking after wearable asset resolution. Root-agent/login
and console paths are implemented. Bakes for one resident are serialized locally;
region removal and module close revoke capabilities and event subscriptions.
Requests have bounded, DTD-disabled LLSD parsing. The UDP handshake preserves
existing flags while optionally setting the server-bake bit; actual baked agents
carry AppearanceData with their COF version.

Bakes use the avatar table's Bake-prefixed index, with no new database schema.
Native appearance saves preserve module rows; per-agent locks serialize service
reads/writes locally. Stored channel hashes support reuse. New assets are saved
before index confirmation; a refused, unpersisted or throwing index write restores
the previous appearance and retains old assets. Partial provider writes can leave
an incomplete index or unreferenced asset and are not a distributed transaction.
Old assets are superseded only after a confirmed index write.

Ported the native avatar-record fixes needed by the same pipeline: write Physics
and Universal slots, retain the resized array on readback, and sort layered
wearables by both indices instead of database row order. Also completed the two
selected material-table compatibility corrections from 918650a7d3 (Rubber bounce
and legacy Light friction/bounce). These change defaults for objects using those
materials; explicit physics overrides retain their existing paths.

Applied newer compatible develop corrections df2c1575d2, b52dca8257 and
1d9a655c29 for login baking: read the COF before
the first appearance, with a five-second limit and stored-record fallback. Late
reads cannot apply an outfit after the bake has proceeded, and cancellation stops
additional link lookups once an outstanding provider call returns. This selected
fix does not import the rest of develop or change the frozen release target.

### The owner's upstream submissions

- [OpenSim #75](https://github.com/opensim/opensim/pull/75): native LLSD login
  replies now include both region dimensions.
- [OpenSim #76](https://github.com/opensim/opensim/pull/76): added the missing sale
  flag, group-power, resale, deed-group, abandon and group-owner checks. Preserve
  Continuum's existing OnLandBuyFinalized event, exact committed-debit requirement
  and auction guards instead of adding the donor's duplicate finalization event.
- [OpenSim #77](https://github.com/opensim/opensim/pull/77): role powers combine,
  owners and role assignment are protected, joins require enrollment/invitations,
  title selection cannot grant unheld powers, and chat/notices respect powers.
  Preserve Continuum's group bans and reserved-charge cancellation. Role edits
  invalidate caches and refresh memberships. No upstream PR was edited or merged.

### Scope dispositions that are not unfinished ports

- Existing master/Continuum Display Names, aliases, Experiences and stock inventory
  behavior are retained rather than ported again.
- cab4048246 deliberately leaves operator-loaded region/inventory archives and
  same-grid transfers unchanged. Standalone IAR behavior and rewriting historical
  cached assets are not missing donor ports. Warm-cache/HG acceptance is recorded;
  no live cache or stored asset migration was performed.
- Direct unvalidated asset rebinding and a duplicate AIS move implementation are
  not selected. The native transaction/move behavior remains authoritative.
- Replacing YEngine with Phlox/SLua, replacing physics, bot frameworks, wholesale
  framework/logging/dependency changes and development-only feature families are
  outside this selected release port. Alternative subsystems are not prerequisites
  for supplying the missing behavior through the native baseline.
- Firestorm, provider, crossing, restart and captured-outfit acceptance remain
  tests in the handoff. Default-off baking/AIS are not production-certified by a
  source closeout. Further active-donor maintenance must use a separate dated delta.

### Final validation

- Full Release solution build: passed, zero errors. Three warnings: the two
  existing Gloebit obsolete-property warnings and CA2255 for the intentional
  native-library module initializer required by the plugin/shared-bin layout.
- Compositor suite: 104 passed, 5 skipped (private captured-outfit fixtures).
- Native baking/services and submitted group/land/login fixes: 140 passed,
  14 skipped (private captured-outfit fixtures). This includes refusing,
  false-acknowledging and throwing index providers; wearable range/order;
  COF read ownership/cancellation; exact debit/finalization and auction guards.
- Complete Tranquillity SQLite/native/registered HTTP harness: passed, including
  default-off baking, root-presence refusal, LLSD bounds/DTD refusal, public
  texture matching/path checks and region teardown alongside existing AIS checks.
- Auction portal and native lifecycle/real SQLite escrow harness: passed.
- git diff --check: passed. These checks precede the publication commit; no
  deployment was performed.

Commands: dotnet build OpenSim.sln -c Release --no-restore --nologo -m:1 -v:q
-p:UseSharedCompilation=false -nodeReuse:false; dotnet test for
Continuum.Baking.Tests and Continuum.ServerSideBaking.Tests; dotnet run for
Continuum.Tranquillity.Tests and Continuum.Auctions.Tests, all in Release.

The captured-reference skips are visible skips, not tests counted as passed by
returning without assertions. No private grid assets were fetched. Provider/live
acceptance remains in the handoff. Source closeout does not mean deployment or
complete Second Life parity across every alternative subsystem.

## Publication — 2026-11-19

The user authorized commit and push after the source closeout. The checked source
snapshot was transferred from OpenSim-Continuum-tranquillity into an independent
clone at S:/Github/OpenSim-Continuum-publish-tranquillity, based on the unchanged
remote development head 393885988b. Only the 512-file Tranquillity change manifest
was copied; build/runtime outputs and the other dirty worktrees were excluded.
The independent clone passed git fsck --full --strict. The original corrupt pack
was not repaired or reused. Publication targets development by fast-forward,
with no force push. Live acceptance and deployment remain separate.
