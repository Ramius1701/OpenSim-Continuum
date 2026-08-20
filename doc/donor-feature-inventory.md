# Donor feature audit and integration disposition

Date: 2026-08-01  
Audit base: `247b9182c1ca0f11743de06a2808f003bc8e2a90` (`baseline/opensim-dev-2026-08-01`)  
Scope: checkpoint 1 records the clean-baseline audit. The integration status below records the subsequent implementation candidate on `codex/complete-opensim-feature-set`.

## Integration checkpoint

The initial recommendations in this document are retained as the evidence available before implementation. They are not the current delivery status. The OpenSim-side candidate now includes the accepted production integrations, disabled-by-default addons, and separately isolated experiments described below.

| Candidate family | Current disposition |
|---|---|
| Display Names | Integrated from the Mobius lineage with Tranquillity enhancements; MySQL, CAPS, search, cache and Hypergrid runtime tests remain required. |
| User aliases/OAR identities | Integrated as a Robust service extension with narrow archive compatibility hooks; trust-boundary and migration tests remain required. |
| Experiences | Tranquillity full service is authoritative; Gunthar script-facing behavior was reconciled without retaining duplicate service implementations. |
| Abuse Reports | Integrated from the Mobius lineage as viewer, simulator and Robust functionality with SQLite, MySQL/MariaDB and PostgreSQL providers; migration and runtime certification remain required. |
| Hypergrid identity/crossing repairs | Integrated as narrow compatibility fixes; multi-grid topology and hostile-input tests remain required. |
| YEngine recovery and script compatibility | Integrated, including the later GLTF/material, identity, transfer, terrain, parcel, environment, damage, sit, attachment and pathfinding surface. Duplicate donor aggregates are compile-excluded in favor of the reconciled implementations. |
| RegionWeb | Integrated as a disabled-by-default per-region addon; it is not WhiteCore WebUI and not a grid-wide portal. |
| Recovered operational addons | Gloebit, HoloPhysicsGuard, MoneyServer, Search, Tide and Weather are packaged as optional components. GroupAutoInvite, RegionCurrency and OpenSimMarketplace are also included and disabled by default where applicable. |
| Warp3D sprites | Integrated behind opt-in renderer settings for visual/performance testing. |
| ubODE tuning | Kept on `codex/testing-ubode-tuning`; it is not part of the default production candidate. |
| WhiteCore WebUI and OpenSim-Grid-Interface | Deliberately deferred until the OpenSim simulator/service/addon candidate completes testing. No portal code is included here. |
| opensim-lickx archive | Added as an audit donor/reference for its optional Lickx script API, OpenSimMutelist, DTL/NSL-derived currency tree and historical OpenSimSearch helpers. No code selected yet. |
| Branded profiles, forced defaults, curated grid endpoints and destructive updater behavior | Excluded as obsolete or unsuitable for a production OpenSim distribution. |

The latest complete Release solution build completed successfully with zero warnings and zero errors. The untouched baseline's four known CS9193 warnings were corrected without changing the quaternion calculations. Compilation does not replace the runtime, viewer, Robust, database, Hypergrid, security, migration and performance acceptance work in `doc/donor-feature-test-handoff.md`.

## Repository verification

| Check | Result |
|---|---|
| Baseline versus live upstream | PASS. `baseline/opensim-dev-2026-08-01` and live `upstream/master` both resolve to `247b9182c1ca0f11743de06a2808f003bc8e2a90`. |
| Baseline tags | PASS. The annotated tags `opensim-dev-baseline-2026-08-01` and `opensim-dev-baseline-2026-08-01-build-clean` both peel to `247b9182c1ca0f11743de06a2808f003bc8e2a90`, locally and on origin. |
| Preserved stabilization work | PASS. Live `origin/fix/continuum-runtime-stabilization` resolves to `065769b51ce356819aad23ec7d8d93621aa20831`. |
| Main checkout tracked state | PASS. Index and tracked worktree are clean. |
| Runtimeconfig artifacts | PASS. The only untracked paths are the five generated `bin/*.runtimeconfig.json` files listed below; `git ls-files` confirms none is tracked. |
| Audit isolation | PASS. This document is authored in a separate worktree on `audit/donor-feature-inventory`, created directly from the baseline commit. |

Generated, untracked build artifacts in the main checkout:

- `bin/OpenSim.ConsoleClient.runtimeconfig.json`
- `bin/OpenSim.Tools.Configger.runtimeconfig.json`
- `bin/OpenSim.runtimeconfig.json`
- `bin/Robust.runtimeconfig.json`
- `bin/pCampBot.runtimeconfig.json`

The earlier official baseline build passed with four known CS9193 warnings. This checkpoint does not rebuild because it changes documentation only.

## Donor interpretation and audit rules

- GuntharDeNiro/opensim is the closest active OpenSim-derived donor. RegionWeb is a per-region website module, not a grid portal.
- WhiteCore WebUI is a separate grid-wide public/administrative interface. WhiteCore is a behavioural reference because its service, data, and module architecture is substantially divergent.
- Mobius is the original donor lineage for Display Names, Experiences, and Abuse Reports. Its GitHub repository is currently reachable and has been captured as audit ref `mobius-current/master` at `7b69b4d545b03cc909afced09b1a5431f4412349`. Traceable master-history commits include Display Names `20f50f7502` plus Hypergrid follow-up `924deef165`, and Abuse Reports `8687793883`. The separately recovered archive remains useful evidence for material not present on master.
- Tranquillity incorporated some Mobius work before adding its own enhancements. These are lineage stages, not automatically separate patches: preserve Mobius origin attribution, audit the Tranquillity delta, and never double-port behavior already carried forward in Tranquillity.
- Tranquillity enhances the Mobius Display Names and Experiences work and is the leading traceable service-level donor for those implementations.
- Gunthar implements a separate Experience-Lite series. It must be reconciled against the fuller Mobius/Tranquillity service implementation rather than treated as a replacement.
- `S:\Github\opensim-lickx` is the user-preserved last available copy of a project no longer hosted on GitHub. Its evidence checkpoint is `6614599b7506b63861763e6cae5eefde861f8749` on `master`, dated 2026-08-07. It has no configured Git remote and consists of one archival commit, so original upstream commit provenance is unresolved. Preserve that archive unchanged and treat it as evidence and a candidate source, never as authority over current OpenSim Dev or an automatic wholesale port.
- Previous Continuum branches and recovered archives are evidence and staging references, not proof that a change belongs in official core. This checkpoint does not use the local Casperia checkout.
- Gunthar commit identities and history in this checkpoint were verified directly from a temporary clone of `https://github.com/GuntharDeNiro/opensim.git` (`origin/master` observed at `6c7021cc36fd6890db27200cd65fd4bb37bd60fd`).
- A candidate is not eligible to port until its original upstream license and complete provenance chain are confirmed. OpenSim-derived code is generally BSD-3-Clause, but bundled assets and third-party addons must be checked individually.

## Production integration order

The official baseline already builds successfully. Production work should keep each candidate isolated, tested, and independently reversible rather than combining unrelated donor histories.

| Lane | Items | Integration decision |
|---|---|---|
| Focused correctness | YEngine orphaned-event recovery | Integrated as a narrow guard with defensive exception formatting; retain a corrupt-state restart fixture in the production test gate. |
| Service enhancements | Mutable Display Names, user aliases, Experiences | Implement on separate branches in dependency/risk order; each requires schema, connector, CAPS, Robust, MySQL, and HG verification. |
| Optional modules | RegionWeb and recovered addons | Integrate one module per branch with disabled-by-default configuration and failure isolation. |
| Experimental behaviour | Warp3D sprite renderer, ubODE tuning | Keep out of production until deterministic visual/physics and performance tests pass. |

The preserved runtime-stabilization branch remains available for issue-by-issue review, but its existence is not evidence that unrelated changes should ship together.

## Prioritized inventory

Priority reflects expected value and auditability, not authorization to implement.

| Priority | Candidate | Classification | Donor commit/checkpoint | Recommendation |
|---:|---|---|---|---|
| Integrated/P0 test | YEngine orphaned resumed-event guard | upstream-quality bug fix | Gunthar `af16af08b66c049d81faef423950b5c98f34eb9`; Continuum `091217b99c82f591a1be10d376447739073eeca8` | The guard drops only the unrecoverable current event, clears transient resume state and keeps the scheduler alive; production certification still requires the corrupt-state/restart fixture. |
| Closed | Estate connector must not terminate host process | already present in OpenSim Dev | Tranquillity `d978ab12b4c0c05a9ff2016bdce7ed569e7e13f8`; Dev equivalent `a9be42a304d9113c889a59ef2041bdccfc37c6f3` | No port. Current Dev already returns null without terminating the process. |
| P2 | Warp3D alpha texture-card sprites | experimental feature | Gunthar initial subsystem `50ff704e14f2114ff2b5613be4aa1252fbad6694`, later black-card fix `c2c30d2dcfa95c93421265bcaa18fc12f88aeef7` | Audit the complete 400-line sprite subsystem as an optional renderer enhancement; the later fix cannot stand alone. |
| P1 | Mutable Display Names | Robust service extension | Tranquillity `0e0953667cdc71a9934bfcdef73a661befcd6619` plus MySQL case fix `45232b2f318e6f225675047fc92edfd20f54b51a` | Dev already serves default names; implement mutable-name persistence as a dedicated production branch. |
| P2 | User aliases and OAR identity resolution | Robust service extension | Tranquillity feature merge `0fa0abcbc7ef081117c40cb82da01d3f5203199e`; later OAR/cache fixes `cfd4742a29958781b7778c74fe731fc0be7c9bb6`, `39aaed76b74c640db62f73ca1f46d42bd299d15b` | Audit and implement the 25-file service as a standalone identity project. |
| P2 | Experiences reconciliation | Robust service extension | Mobius origin; Tranquillity `26d3971448107725ad30d0abf769175ccb7f2467`; Gunthar Experience-Lite series `a4fbbbd0b0a733c8a838af9d21d9bb43cbcec7ae` through `4f43137c8beac427ae93557f422d0022079e1ede` | Reconcile full service/CAPS behavior with Gunthar's script-facing additions; do not cherry-pick either implementation wholesale. |
| P1 | Hypergrid stale identity URL/IP repair | narrow core compatibility patch | Gunthar `b72694a29bf2aa7d119cfa4a7900b83b113a18af` (related stored URL repair `4c0d5e1e586ee33a919cece719a6d4562d8d1b4e`) | Reproduce against current HG before considering a minimal patch. |
| P1 | RegionWeb | optional addon module | Gunthar, represented by protected admin increment `b05cd2bb4bc0d325aa1a7c6771d8a933ec6d1405` | Keep optional and region-scoped; perform security review before packaging. |
| P2 | Recovered operational addons | optional addon module | Continuum build-clean checkpoints listed below | Keep out of core; audit each against its original repository and current APIs. |
| P2 | First-run Windows setup wizard | optional addon module | Gunthar `603aa2c983e2f4d52495947010ad19656d951893`, timing fix `db1509cdc43a97df1244e72999f76e7bf838bebe` | Generalize branding/config assumptions before considering a tools package. |
| P3 | ubODE social, contact, and water tuning | experimental feature | Gunthar series culminating in `95aec15852ce206b0acc0147a3487c6721f0c83a` | Do not port as a batch; require deterministic physics benchmarks and opt-in settings. |
| P3 | WhiteCore WebUI | obsolete or unsuitable (as a direct donor) | WhiteCore `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` snapshot | Use only for behavioural requirements; architecture is not a suitable OpenSim module transplant. |
| Provenance | Mobius Display Names implementation | original feature lineage | Mobius `20f50f7502`; HG follow-up `924deef165` | Compare directly while retaining Tranquillity's later enhancement history. |

## Candidate records

### 1. YEngine orphaned resumed-event guard

- **Feature and intended behaviour:** avoid a YEngine crash when a persisted/resumed script event refers to a script instance that is no longer valid.
- **Donor and commit:** GuntharDeNiro/opensim, `af16af08b66c049d81faef423950b5c98f34eb9`.
- **Current Dev equivalent / missing behaviour:** baseline Dev `ResumeEx()` explicitly throws when `stackFrames` is null, and `RunOne()` does not catch that call locally. Continuum commit `091217b99c82f591a1be10d376447739073eeca8` replaces that simulator-level failure with a bounded recovery: it logs the corrupt resume, clears the current event arguments/detect state and transient resume mode, then returns control to the scheduler. It also makes exception-stack formatting tolerate absent exception, stack and object-code metadata.
- **Affected files and services:** `XMRInstAbstract.cs`, `XMRScriptUThread.cs`; region-side YEngine only. The donor commit also contains an unrelated example LSL file which must be excluded.
- **Addon versus core:** narrow core bug fix.
- **Compatibility:** no database impact; Windows-neutral managed code; no intended Robust or Hypergrid impact.
- **Viewer requirements:** none.
- **Licensing/provenance:** OpenSim-derived donor; confirm BSD notices and authorship in the source commit before porting.
- **Tests required:** first create a fixture that reaches `eventCode != None` with `stackFrames == null`; then cover persisted-state restart, deleted/recompiled script race, event-queue concurrency, subsequent queued-event execution, and Debug/Release builds on Windows. Assert that cleanup does not strand suspend/detach state.
- **Recommendation:** integrated as an upstream-quality bug fix. Keep at P0 in the runtime test matrix until a corrupt persisted-state fixture proves that the bad event is discarded, later queued events execute, and suspend/detach state is not stranded.

### 2. Estate connector process-termination path

- **Feature and intended behaviour:** a failed estate lookup returns failure to its caller instead of calling `Environment.Exit()` from a library.
- **Donor and commit:** OpenSim-NGC/OpenSim-Tranquillity, `d978ab12b4c0c05a9ff2016bdce7ed569e7e13f8`.
- **Current Dev equivalent / missing behaviour:** already present. OpenSim Dev commit `a9be42a304d9113c889a59ef2041bdccfc37c6f3` removed the fatal behaviour on 2024-09-22; the baseline calls `MakeRequest()` and returns null for a null or empty reply. A later cleanup, `0d71b6d871e`, supplies the current `string.IsNullOrEmpty` form.
- **Affected files and services:** `OpenSim/Services/Connectors/Estate/EstateDataConnector.cs`; region and Robust estate lookup paths.
- **Addon versus core:** core bug fix.
- **Compatibility:** datastore-neutral in intent; explicitly test MySQL missing-estate and connector-error cases. Windows-neutral. No direct viewer or HG protocol change, though HG region startup may exercise the connector.
- **Licensing/provenance:** traceable OpenSim-derived Tranquillity commit; retain BSD provenance.
- **Tests required:** missing estate, database unavailable, malformed response, standalone/grid/Robust startup, MySQL and SQLite, Windows build/runtime.
- **Recommendation:** closed as already present in OpenSim Dev. Do not port the Tranquillity patch or its commented-out `Environment.Exit` lines.

### 3. Warp3D alpha texture-card sprite rendering

- **Feature and intended behaviour:** add a sprite pass for flat alpha texture cards in Warp3D map tiles, then prevent those cards from also rendering as black-backed volume geometry.
- **Donor and commit:** GuntharDeNiro/opensim initial sprite subsystem `50ff704e14f2114ff2b5613be4aa1252fbad6694`; follow-up black-card fix `c2c30d2dcfa95c93421265bcaa18fc12f88aeef7`.
- **Current Dev equivalent / missing behaviour:** current Dev has Warp3D volume rendering but none of the donor identifiers `m_drawFlatTextureCardSprites`, `IsUsableSpriteFace`, `GetSpriteTexture`, `AlphaCoverage`, or `OpaqueCoverage`. The follow-up fix adds only 29 lines but depends on the initial subsystem, which adds 403 lines and removes 4 in `Warp3DImageModule.cs` plus configuration changes. Therefore the cited black-card commit is not independently portable.
- **Affected files and services:** `Warp3DImageModule.cs`, `OpenSim.ini.example`, and `OpenSimDefaults.ini`; map-image generation only.
- **Addon versus core:** experimental core renderer enhancement, preferably behind opt-in configuration during evaluation; avoid importing donor-specific defaults without evidence.
- **Compatibility:** no Robust/MySQL/HG effect; managed Windows rendering dependencies must be exercised.
- **Viewer requirements:** none; output is a map image consumed by viewers/web maps.
- **Licensing/provenance:** OpenSim-derived donor; verify any rendering technique attribution.
- **Tests required:** golden images for opaque, blended, masked, and fully transparent texture cards; comparison with baseline volume output; sprite/texture decode budgets; missing assets; Linux/Windows parity; memory and map-generation timing.
- **Recommendation:** downgrade to P2 experimental. Audit the entire sprite subsystem and its performance/security limits; do not describe or port `c2c30d...` as a standalone upstream bug fix.

### 4. Mutable Display Names

- **Feature and intended behaviour:** let residents change viewer-visible names independently of immutable account names, persist the value, enforce a change interval, and distribute updates through CAPS and grid services.
- **Donor and commit:** original feature lineage is Mobius; Tranquillity enhancement/integration `0e0953667cdc71a9934bfcdef73a661befcd6619`; MySQL migration case correction `45232b2f318e6f225675047fc92edfd20f54b51a`.
- **Current Dev equivalent / missing behaviour:** Dev already registers `GetDisplayNames`, returns default legacy names, and exposes display-name-shaped fields through avatar search. What is genuinely missing is mutable `SetDisplayName`, persisted `DisplayName`/`NameChanged` account fields, and propagation of non-default names. Mobius is the original feature source; Tranquillity provides the enhanced, traceable integration used for the production port.
- **Affected files and services:** UserAccount data/service/connectors, CAPS, avatar picker/search, user-management and HG account cache; MySQL migration and configuration.
- **Addon versus core:** opt-in Robust service extension with narrowly required core interfaces/caps wiring.
- **Compatibility:** needs standalone and Robust modes; MySQL migration/rollback and mixed-case table tests; Windows-neutral managed code; HG trust, caching, spoofing, and fallback-name rules are central. The feature commit's migration incorrectly names `useraccounts` and requires follow-up `45232b2...` to use `UserAccounts`, demonstrating that the feature commit is not safe alone on case-sensitive MySQL deployments.
- **Viewer requirements:** viewers supporting standard Display Names caps; legacy viewers must retain account-name fallback.
- **Licensing/provenance:** preserve Mobius commits `20f50f7502` and `924deef165` as original traceable lineage and retain Tranquillity contributors for the enhanced integration. Both are OpenSim-derived; file-level attribution must be retained and verified.
- **Tests required:** CAPS conformance, cache expiry, rate limits, Unicode/normalization, MySQL upgrade, search, HG foreign-user fallback, old viewer regression.
- **Recommendation:** P1 production enhancement on a dedicated branch. Treat the approximately 495-added-line/17-file donor change as a design source, not a blind cherry-pick candidate.

### 5. User alias service and OAR identity resolution

- **Feature and intended behaviour:** map stable identities through configured aliases, optionally resolving OAR creators/owners without silently substituting a default user; negative caching prevents connector hammering.
- **Donor and commit:** Tranquillity feature merge `0fa0abcbc7ef081117c40cb82da01d3f5203199e`; later OAR behaviour `cfd4742a29958781b7778c74fe731fc0be7c9bb6`, negative caching `39aaed76b74c640db62f73ca1f46d42bd299d15b`, and config example `d3aac7d244456d74374bbb8446f8188be45ebe96`.
- **Current Dev equivalent / missing behaviour:** Dev has OAR identity/default-user handling but no identified alias service.
- **Affected files and services:** the feature merge changes 25 files with about 1,534 additions: MySQL data/migration, local and remote region connectors, Robust handlers/connectors, service interfaces and implementation, scene/application wiring, remote admin, archive import, configuration/build metadata, and tests.
- **Addon versus core:** Robust service extension plus narrow archive compatibility hooks.
- **Compatibility:** define standalone/Robust behaviour; MySQL schema depends on final design; Windows-neutral; HG aliases must never permit foreign identity impersonation.
- **Viewer requirements:** none directly.
- **Licensing/provenance:** trace all commits behind the alias connector, not only the config commit; BSD compatibility must be confirmed.
- **Tests required:** OAR import matrices, missing/duplicate alias, cache expiry, SSRF/authentication review, MySQL persistence, HG foreign/local collision cases.
- **Recommendation:** P2 standalone identity project. Audit jointly with Display Names at the trust boundary, but keep semantics and persistence distinct; do not use the later four-file OAR commit as if it contained the service itself.

### 6. Experiences

- **Feature and intended behaviour:** implement viewer Experience capabilities, permissions, estate/land integration, persistence, and LSL experience APIs.
- **Donor and commit:** original feature lineage is Mobius. Tranquillity feature integration `26d3971448107725ad30d0abf769175ccb7f2467`, persisted-state restart fix `0281a9f87b8f0f2c0f54876c3e29eeb0b626bb83`, and typo cleanup `d707ed64e056c77bbf5ae110c45bedf3098d5eb8`. Gunthar Experience-Lite additions run from auto-grants `a4fbbbd0b0a733c8a838af9d21d9bb43cbcec7ae` through build repair `4f43137c8beac427ae93557f422d0022079e1ede`.
- **Current Dev equivalent / missing behaviour:** Dev exposes some experience-related constants/stubs but lacks Tranquillity's `ExperienceService`, connectors, data implementation, CAPS module, and complete runtime behaviour. Gunthar adds script-facing Experience-Lite permissions, events, KVP persistence/statistics, information APIs, and scripted sit behavior, but does not contain the full service file set in its current tree.
- **Affected files and services:** 62 files, roughly 6,543 additions and 53 deletions: MySQL, estate/region stores, CAPS/event queue, scene/attachments/inventory, YEngine, LSL APIs, Robust handlers/connectors, configuration, and tests. The largest slices are LSL API integration (about 1,678 changed lines) and the new CAPS module (about 1,337 lines).
- **Addon versus core:** Robust service extension requiring core protocol, persistence, simulator, and script changes; cannot be a drop-in addon alone.
- **Compatibility:** standalone and Robust deployment are mandatory; MySQL migrations need upgrade/rollback validation; Windows-neutral; HG permission ownership and foreign experience IDs require an explicit policy.
- **Viewer requirements:** an Experience-capable viewer; non-supporting viewers and scripts must degrade safely.
- **Licensing/provenance:** traceable Tranquillity/OpenSim lineage, but review every new file and contribution in the feature merge.
- **Tests required:** service/API contracts, permission lifecycle, parcel/estate controls, script restart, KV storage, attachments, MySQL concurrency/migration, Robust outage, HG boundaries, viewer interoperability.
- **Recommendation:** P2 and highest complexity. Use Tranquillity as the full service/CAPS/data base, then reconcile Gunthar's Experience-Lite script behavior item by item. Stage independently testable service, CAPS, persistence, permissions/events, KVP, and scripting milestones.

### 7. Hypergrid stale identity repair

- **Feature and intended behaviour:** replace stale service URLs/IP-derived identity data in live circuits and the user-name cache when HG topology changes.
- **Donor and commit:** Gunthar `b72694a29bf2aa7d119cfa4a7900b83b113a18af`; related persisted URL repair `4c0d5e1e586ee33a919cece719a6d4562d8d1b4e`.
- **Current Dev equivalent / missing behaviour:** Dev has HG entity transfer and user management caches; the reported stale-address scenario needs a current multi-grid reproduction.
- **Affected files and services:** `HGEntityTransferModule.cs`, `UserManagementModule.cs`, and potentially user storage/service URL records.
- **Addon versus core:** narrow core compatibility patch.
- **Compatibility:** no presumed schema change, but test MySQL-backed grid services and Robust; Windows DNS/IP behavior; major HG security and identity implications.
- **Viewer requirements:** none beyond normal HG teleport/name display.
- **Licensing/provenance:** OpenSim-derived Gunthar commits verified directly in the donor repository; trace authorship and retain notices before porting.
- **Tests required:** DNS/IP change, NAT, cached foreign user, concurrent teleport, malicious URI, Robust restart, MySQL persistence, multi-grid regression.
- **Recommendation:** P1, security review and reproduction before patch extraction.

### 8. Gunthar RegionWeb

- **Feature and intended behaviour:** optional per-region public pages, map/carousel content, and protected estate administration. It is not a grid-wide WebUI.
- **Donor and commit:** Gunthar RegionWeb series through packaged checkpoint `b3511ea070501c32612e24949ded5612c437e8dc` (source blob `db48bab8ff7e115dcb97cd3e4eb54b44d1bd3468`); representative admin commit `b05cd2bb4bc0d325aa1a7c6771d8a933ec6d1405` and inventory carousel `2f9abda3877b406b2ca49b9fda55f85dc77e2bbf`.
- **Current Dev equivalent / missing behaviour:** no `RegionWeb` module in Dev. Existing HTTP/caps and map services do not provide this site.
- **Affected files and services:** `addon-modules/RegionWeb/RegionWebModule/RegionWebModule.cs`, region HTTP server, inventory/map and estate controls.
- **Addon versus core:** optional addon module; only narrowly justified host APIs should touch core.
- **Compatibility:** should run without Robust/database dependencies where possible; any account/admin data path needs MySQL and Robust tests; Windows path/case handling; HG content must be escaped and identity-aware.
- **Viewer requirements:** ordinary web browser; optionally used as viewer login splash.
- **Licensing/provenance:** audit source, templates, logos, images, fonts, and bundled JS separately; strip Vanilla-specific branding unless licensed and desired.
- **Tests required:** authentication/authorization, CSRF/XSS/path traversal, inventory privacy, HTTP concurrency, disabled-module startup, Windows paths, standalone/grid/HG deployment.
- **Recommendation:** P1/P2 optional package after a security and asset-license review.

### 9. Recovered operational addon set

- **Feature and intended behaviour:** optional Abuse Reports, Gloebit money, HoloPhysicsGuard, MoneyServer, OpenSimSearch, OpenSimTide, and OpenSimWeather capabilities recovered and previously reconciled in Continuum. Abuse Reports originates from the Mobius lineage.
- **Donor and commit:** Continuum checkpoints: Abuse Reports `900dcad6fce839a5fc493192aa83f3d651839f23`; Gloebit `86051a68327b9d4c0034c25a9445404f46d28afa`; HoloPhysicsGuard `d9152a92ad725e19daffffd338b948fcb60f9434`; MoneyServer `e86018e495677fad241e60d33130e45d3fa81ada`; Search `15afe9937a81d3631d2071bcfc5cc6362668959f`; Tide `346a96359eb65eab21f0792674b6cac3680f6061`; Weather `211f2a81a12ba084092a6c9d16d4769483781d0b`.
- **Current Dev equivalent / missing behaviour:** Dev includes basic search and a sample money module, but not these complete third-party behaviours. Each candidate needs a separate equivalence analysis.
- **Affected files and services:** optional region modules, external web/service endpoints, configuration, assets, and—in MoneyServer/Search/Abuse Reports—database and Robust-adjacent services.
- **Addon versus core:** addons; any proposed core shim must be narrow, generic, and independently justified.
- **Compatibility:** certify each separately for Robust/standalone, MySQL schema and migrations, Windows filesystem/process behavior, and HG data/privacy/economy boundaries.
- **Viewer requirements:** varies: standard search/money/abuse UI for several; environmental effects may require compatible viewers; external Gloebit service/account requirements must be documented.
- **Licensing/provenance:** Continuum commits are not sufficient provenance. Reconcile with the recorded original remotes and licenses; separately inventory media/assets and service SDK terms.
- **Tests required:** clean addon builds against baseline, disabled/enabled startup, service contract tests, MySQL fresh/upgrade installs, Windows runtime, Robust separation, HG abuse/economy/search exposure, failure isolation.
- **Recommendation:** P2; create one audit document and branch per addon before deciding whether any belongs in an official optional-module distribution.

### 10. First-run Windows setup wizard

- **Feature and intended behaviour:** guide a new standalone user through configuration, estate bootstrap, and process startup.
- **Donor and commit:** Gunthar `603aa2c983e2f4d52495947010ad19656d951893`, auto-start `ced4a248b8a78ec719ca1cbefb7444d4f6241830`, timing fix `db1509cdc43a97df1244e72999f76e7bf838bebe`.
- **Current Dev equivalent / missing behaviour:** Dev provides example INI files and startup programs, not this guided PowerShell/bootstrap flow.
- **Affected files and services:** PowerShell/batch tooling and config-profile documentation; launches OpenSim and initializes estate state.
- **Addon versus core:** optional tooling, not simulator core.
- **Compatibility:** Windows-specific by design; no direct MySQL/Robust support until generalized; HG/grid profiles must not be silently enabled.
- **Viewer requirements:** none.
- **Licensing/provenance:** OpenSim-derived repository scripts; remove donor branding and audit any embedded defaults/secrets.
- **Tests required:** clean Windows VM, paths with spaces, execution policy, ports in use, interrupted setup, SQLite default, opt-in MySQL/Robust, rerun/idempotency.
- **Recommendation:** P2 after replacing product-specific assumptions with explicit profiles.

### 11. ubODE social/contact/water tuning

- **Feature and intended behaviour:** change avatar contacts, object bounce, buoyancy, waterline stability, damping, and social collision feel.
- **Donor and commit:** Gunthar multi-commit experimental series from `c69a2941ef4ba5fb198f3c04bf7513a502b51cc4` through representative head `95aec15852ce206b0acc0147a3487c6721f0c83a`.
- **Current Dev equivalent / missing behaviour:** Dev has ubODE and existing material/buoyancy behavior; the donor changes alter tuning rather than filling one isolated missing API.
- **Affected files and services:** chiefly `OpenSim/Region/PhysicsModules/ubOde/ODEScene.cs` and defaults/config profiles.
- **Addon versus core:** experimental core feature; should be opt-in if pursued.
- **Compatibility:** no database/Robust effect; native physics determinism and Windows/Linux parity matter; HG travellers may observe different simulator physics.
- **Viewer requirements:** none, although viewer prediction may make movement changes visible.
- **Licensing/provenance:** OpenSim/ubODE lineage must be checked across the entire series.
- **Tests required:** deterministic scene corpus, performance/CPU, avatar stairs/falls/collisions, vehicles, stacked objects, water entry, material bounce, cross-platform native builds, legacy-setting parity.
- **Recommendation:** P3 research branch only; never batch-port into official defaults.

### 12. WhiteCore WebUI and feature behaviour

The broader simulator/service review is maintained in
[`whitecore-continuum-improvement-audit.md`](whitecore-continuum-improvement-audit.md).
WhiteCore is a first-class behavioural and viewer-protocol donor even though
its divergent architecture makes wholesale OpenSim ports unsuitable.

- **Feature and intended behaviour:** grid-wide public and administrative web interface; WhiteCore also demonstrates Display Names and Experiences behaviours.
- **Donor and commit:** WhiteCoreSim/WhiteCore-Dev snapshot `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa`.
- **Current Dev equivalent / missing behaviour:** official Dev has no equivalent bundled grid-wide WebUI. This is distinct from RegionWeb.
- **Affected files and services:** WhiteCore's generic services, CAPS modules, web templates, account/grid administration and data layer; mappings to OpenSim are non-local.
- **Addon versus core:** if ever pursued, a separately maintained external web application/service; not a core port.
- **Compatibility:** WhiteCore persistence/service assumptions cannot establish OpenSim Robust or MySQL compatibility; Windows and HG behaviour must be redesigned/tested in an OpenSim implementation.
- **Viewer requirements:** browser for WebUI; viewer protocol references only for behavioural comparison.
- **Licensing/provenance:** WhiteCore license and every web asset/dependency require review; architectural rewriting does not erase provenance obligations.
- **Tests required:** requirements-level acceptance tests, API/auth design, role authorization, CSRF/XSS, MySQL, Robust HA, Windows hosting, HG privacy.
- **Recommendation:** classify direct code port as obsolete or unsuitable; retain as behavioural reference only.

### 13. Mobius Display Names origin

- **Feature and intended behaviour:** original Display Names feature lineage, including caps and Hypergrid handlers later enhanced by Tranquillity.
- **Donor and commit:** Mobius-Team/Mobius `20f50f7502` with Hypergrid follow-up `924deef165`; current master evidence checkpoint `7b69b4d545b03cc909afced09b1a5431f4412349`. The recovered metadata-less archive remains secondary evidence.
- **Current Dev equivalent / missing behaviour:** Dev lacks complete Display Names; the archive contains `DisplayNamesModule`, `GetDisplayNames` handlers, and HG connectors.
- **Affected files and services:** CAPS, region module interfaces, HG handlers/connectors, user lookup.
- **Addon versus core:** historical reference for a Robust service extension, not an import source.
- **Compatibility:** the exact master revisions are now available, but their older OpenSim base still requires current MySQL/SQLite/PostgreSQL, Windows, Robust and Hypergrid comparison. HG protocol behaviour is direct comparative evidence, not an automatic port.
- **Viewer requirements:** Display Names-capable viewer.
- **Licensing/provenance:** blocked for code use until commit and license are tied to the archive.
- **Tests required:** protocol comparison against Tranquillity and current viewers; no code tests until provenance is resolved.
- **Recommendation:** retain as required provenance and behavioral comparison. Use Tranquillity's traceable enhancement commits for integration while preserving Mobius attribution.

### 14. opensim-lickx archival candidates

- **Donor and checkpoint:** user-preserved last available copy at `S:\Github\opensim-lickx`, archival commit `6614599b7506b63861763e6cae5eefde861f8749`; the project is no longer on GitHub and the archive has no configured remote or earlier Git history.
- **Feature and intended behaviour:** an optional `Lickx_Api` exposing viewer-name lookup to scripts; an OpenSimMutelist module; a DTL/NSL-derived MoneyServer/currency distribution; and historical PHP/MySQL OpenSimSearch crawler/helper files.
- **Current Dev/Continuum equivalent:** Continuum already has current script APIs and viewer circuit metadata, native mute-list support, separately preserved MoneyServer Compatibility plus ContinuumEconomy, and a hardened optional OpenSimSearch viewer connector. The archive does not establish that any of these should replace the selected implementations.
- **Genuinely missing behaviour:** `lxGetAgentViewer()` is a narrow optional script convenience not currently exposed under that donor API name. Any missing mute, economy, or search behaviour must be demonstrated by code and runtime comparison rather than inferred from file presence.
- **Affected files and services:** script API registration/runtime; mute-list region module; MoneyServer, MySQL wrapper, region currency connector and helper URI; search crawler database and PHP endpoints.
- **Addon versus core:** prefer optional addons. Do not add a donor-specific core script API until its registration, threat model and compatibility value are proven. Currency and crawler code must remain outside core.
- **Compatibility:** archive documentation targets OpenSim 0.9.x, .NET 8 and MySQL for currency. SQLite/PostgreSQL, current Windows service operation, Robust, Hypergrid trust, current viewer and multi-region behaviour are unproven.
- **Licensing and provenance:** the OpenSim tree declares BSD-3-Clause. `opensim.currency-lickx` separately declares MIT and credits DTL/NSL. Preserve both lineages. Embedded certificate files, PHP helpers and every other addon asset require separate provenance/security review; archival commit identity is not original upstream provenance.
- **Tests required:** compare APIs and schemas with current Continuum; build each candidate separately; test script permissions and viewer privacy, mute persistence/crossings, MoneyServer migration and all supported database boundaries, helper authentication, search privacy/paging/deletion, Windows paths, Robust restart and Hypergrid inputs.
- **Recommendation:** audit-only donor. Evaluate the narrow Lickx API and Mutelist first; use currency only as a MoneyServer parity reference; quarantine bundled certificates and historical PHP search/economy helpers unless independently secured and justified.

## Already-present and unsuitable observations

- Basic avatar/search infrastructure and a sample money module are already present in Dev; donor products must not be described as wholly missing merely because they offer richer behaviour.
- Several Tranquillity build/dependency commits and broad script-engine restructures are unsuitable for direct selection because current Dev has advanced independently; only independently reproducible fixes should proceed.
- Donor branding, curated destinations, forced configuration defaults, auto-stashing update scripts, and grid-specific endpoints are obsolete or unsuitable for official Dev.
- The previous Continuum region-scripting archive (`30211551ce76ebc2d7e0c0d048c04e5ef49a2ea1`) is a provenance lead for region crossing, attachments, and background map generation, not a single candidate. Those behaviours require issue-level decomposition against current Dev before classification.

## Next audit actions

1. Build the minimal corrupt-state/restart fixture for the integrated YEngine guard and verify safe continuation semantics.
2. Produce protocol/data-flow maps for Display Names, aliases, and Experiences, including trust boundaries and MySQL migrations.
3. Trace RegionWeb and each recovered addon to its original repository/license and enumerate assets separately from code.
4. Convert each viable item into its own audit checkpoint with exact diff boundaries and test plan. No implementation should be ported until that item's audit is accepted.
