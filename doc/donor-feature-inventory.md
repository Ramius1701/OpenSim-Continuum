# Donor feature audit and integration disposition

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
Audit base: `247b9182c1ca0f11743de06a2808f003bc8e2a90` (`baseline/opensim-dev-2026-08-01`)  
Scope: checkpoint 1 records the clean-baseline audit. The integration status below records the subsequent implementation candidate on `codex/complete-opensim-feature-set`.

## WhiteCore implementation complete — 2026-10-24

The remaining selected WebUI workflows are now implemented through native
services, explicit archival and private-data contracts, and native host control
rooms/consoles. The prior 11 blocked routes have working workflows; configuration
and runtime acceptance remain opt-in and pending. Purchase pages show currency
credits with no cash-payment claim, as explicitly chosen by the user. The final
implementation table, limits and passing validation are at the top of
`whitecore-continuum-improvement-audit.md`. No next donor was started.

## Historical selected-source closure — 2026-10-24

The recorded WhiteCore feature catalog now has final existing-equivalent, native
implementation or explicit-exclusion dispositions. Auction grid-portal integration
and durable event reminders are implemented; native SimulatorFeatures hover/copy
gaps were corrected. Full Release and focused acceptance checks pass. The final
source ledger is at the top of `whitecore-continuum-improvement-audit.md`.

This closes the selected-source reconciliation lane. It does not declare full
WebUI delivery, all SL feature parity, runtime certification or the 11 blocked
legacy paths complete. Those requirements remain tracked. No next donor was
started in this pass.

## Historical WhiteCore boundary — 2026-10-24

WhiteCore remains active; it is not certified source-complete. See the current
matrix at the top of `whitecore-continuum-improvement-audit.md` for delivered native
families, remaining service comparisons, the selected regional auction versus
missing grid-wide integration, and every blocked portal route. WebUI product
modernization and donor-source reconciliation must have separate visible status;
a legacy filename or a donor stub is not another completed/unfinished port by
itself. Older checkpoint tables below remain historical evidence.

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
| WhiteCore WebUI and OpenSim-Grid-Interface | Needed selected ports and native workflow adapters implemented on 2026-10-24, including the former 11 blocked routes, auctions, reminders and viewer fixes. Native host editors/consoles retain their own authority; closure is archival and purchase pages show currency credits without cash claims. Final implementations and passing checks are in `whitecore-continuum-improvement-audit.md`; deployed/provider acceptance remains in the handoff. |
| opensim-lickx archive | Implementation reconciliation complete, including IMAP object mail. All reviewed core/addon candidates have a port, existing-equivalent or explicit-exclusion disposition; see section 14. Runtime acceptance remains queued in the test handoff. |
| Mobius | Pinned feature-branch reconciliation complete. Existing/newer equivalents retained; alternatives without demonstrated benefit excluded. No additional source port selected. Scope and final evidence are in section 13; runtime acceptance remains separate. |
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

#### Inactive-donor continuation — 2026-10-21

Mobius is now the selected donor following completion of lickx implementation.
Use the Git objects already preserved under `mobius-current/master` at
`7b69b4d545b03cc909afced09b1a5431f4412349`; do not modify the metadata-less
`S:\Github\mobius-master` archive. The pinned history ends in README changes
dated September 2024; the latest substantive inspected changes are from July
2020. This is a frozen-source review, not a claim about live repository status.
Tranquillity, upstream and other donor reviews remain paused.

Operator precedence rule (2026-10-21): current Continuum/upstream and newer
Tranquillity implementations take precedence over historical Mobius code.
Reading the preserved Tranquillity checkpoint for this comparison is lineage
verification, not resumption of its separate port queue. Mobius-only file or
setting names are not evidence of a functional gap. Select older behavior only
after demonstrating that the newer implementations lack the required behavior;
preserve newer interfaces, fixes and constant values.

Additional operator selection rule: a change must provide a concrete benefit
to Continuum. Historical feature parity or a different configuration/command
name alone does not justify a port. Record the benefit and the newer-code gap
before selecting source changes.

Newer comparison checkpoint: preserved Tranquillity
`1d276209d4159e4cb70b416c9aa32994ea19f776` (September 2026), read without
modifying that checkout. No live upstream-status claim is made.

Opening evidence: `CHANGES.md`, the 56 commits attributed to Stolen Ruby, and
the concrete diffs listed below. This author-filtered list is a starting index,
not a complete inventory of all custom work or merged upstream changes.

| Slice / original commit | Current Continuum comparison and disposition |
|---|---|
| Display Names `20f50f7502`, HG batching `924deef165` and subsequent fixes | Existing Mobius-to-Tranquillity lineage; retain the selected implementation. Reconcile remaining original follow-ups against `parity-display-names.md` before closing this family. |
| Abuse Reports `8687793883` | Already integrated with additional providers/moderation. Retain original attribution and the existing `parity-abuse-reports.md` runtime gates. |
| Estate LandStat event queue, parcel/URL/memory fields `84f90c236a`; engine fallback `db667866f2` | Current `LLClientView.SendLandStatReply` already emits event-queue fields including ParcelName/PublicURLs; EstateManagementModule collects URL/script statistics. Verify memory accounting and fallback semantics against the complete donor diff before closing; do not duplicate the protocol path. |
| In-world terrain commands `2f3718f28a` | TerrainModule already registers fill/elevate/lower/load-texture commands through IRegionConsole. Compare permissions, argument validation and handler lifecycle before closing this family. |
| Map startup and in-world regeneration `7214561d14` | Candidate gap: no `OnlyGenerateMapTilesIfMissing` setting or IRegionConsole map command found in Scene/WorldMapModule. Existing server `generate map` is present. Adapt any selected startup suppression to current asynchronous registration/load ordering; donor lookup dereferences a possibly missing grid record and uses zero scope. Do not copy it verbatim. |
| Parcel details `3d524e2865`, `ade4f70d67` | Landing point/routing/object return behaviors already exist, but donor selectors 128/129/130 differ from current 9/11/67. In particular donor `PARCEL_DETAILS_OBJECT_RETURN=130` conflicts with current setter constant 67. Compatibility remains open; do not replace current constant values. |
| `osTriggerSoundAtPos` `aedb95b0d7` | API, interface and runtime stub already exist. Verify implementation semantics before closing. |
| TOS `34a1601a2a`, RSA login `6f52286f34` | Current LLLoginService explicitly carries TOS and RSA paths. Compare follow-ups and existing authentication gates; no duplicate login service. |
| Hardware/IP bans `2cb7636fb1`, console/config follow-ups `dc10eb5493`, `7ea452ea12` | Open comparison: service authority, authentication, persistence, console permissions and available providers must be established before selecting code. |
| Port range / MATCHING `1fccfa46df` | MATCHING parsing already exists in RegionInfo. Range allocation and failure behavior still need comparison. |
| Region restart `684182e830`, LSLSyntax `5acc4e32f7`, ViewerAsset `54453548df`, external avatar picker `d440af67d2` | Open comparison against current native capabilities, restart sequencing and network policy. Donor removal of native implementations is not automatically appropriate. |
| MySQL groups migration `5b956863fc` | Open schema comparison; apply only if the same defect remains in current migrations. |
| Branding, solution renames, old VS tooling and removed web submodules | Historical/deployment material; no automatic replacement of current build tooling, native services or branding. Independently evaluate any substantive addon behavior. |

Licensing: the pinned root LICENSE includes a customization caveat as well as
BSD terms. The inspected WorldMapModule has a complete BSD header. Check each
selected file and dependency individually; do not generalize one header to the
entire donor. The metadata-less archive provenance restriction above continues
to apply to material that cannot be tied to a licensed Git revision.

Status: initial review in progress; no Mobius implementation-completion claim.
Next work is to finish the finite custom-change inventory and resolve the map,
parcel and remaining service/capability rows. Queue manual checks in the existing
handoff as candidates are selected.

Newer-code follow-up dispositions:

- **Estate statistics: superseded; no port.** Current Continuum and the pinned
  Tranquillity LLClientView emit the event-queue parcel/URL fields. Continuum's
  EstateManagementModule consumes execution time, memory and URL statistics
  through the current reporting path. Retain that path rather than reintroduce
  the donor `GetObjectScriptsBytesUsed` API.
- **XEngine fallback: obsolete; no port.** `db667866f2` changes the old XEngine
  and its test mock. Current YEngine exposes `GetScriptsMemory`, as does newer
  Tranquillity. Do not restore an obsolete engine interface to carry its fix.
- **MySQL groups engine: already fixed; no port.** `5b956863fc` changes the
  initial groups table from MyISAM to InnoDB. Both current Continuum and the
  pinned Tranquillity migration already specify InnoDB. No schema change or
  additional migration is justified by this donor patch.
- **Parcel selectors: retain current interface; no constant replacement.**
  Current `llGetParcelDetails` reads landing point, routing and object return
  through 9/11/67. The old 128/129/130 values are not a reason to replace working
  current semantics. An additional compatibility surface would require a
  demonstrated requirement; it is not selected by this audit.
- **Map settings/console: unselected historical difference.** Exact donor
  settings/command names were not found in either current Continuum or the
  pinned Tranquillity search. This establishes a textual difference only.
  Compare the existing map lifecycle and operator controls before deciding
  whether any behavior is needed; no older map code is authorized merely by
  absence of its name.

Further behavior comparison (supersedes the name-only map search above):

- **Map startup:** newer Tranquillity already carries the behavior under
  `OnlyGenerateIfMissing`, not Mobius's `OnlyGenerateMapTilesIfMissing`.
  Its Scene checks the existing grid record before rendering. Continuum has
  newer background generation with startup delay, occupancy deferral and
  configurable refresh, plus the server `generate map` command. Retain these
  current controls. No old synchronous startup implementation is selected.
  Skipping regeneration for an existing tile remains an unselected operational
  enhancement, requiring an actual need and adaptation to asynchronous startup,
  scoped lookup and missing grid records before it can benefit Continuum.
- **Syntax and assets: superseded; no port.** Both current Continuum and newer
  Tranquillity implement LSLSyntax through SimulatorFeaturesModule and
  ViewerAsset through GetAssetsModule. Mobius's separate modules and removal
  of native implementations offer no demonstrated benefit over these paths.
- **Avatar picker: retain native capability; no old proxy selected.** Both
  implementations use AvatarPickerSearchModule/IPeople. Continuum additionally
  handles shared-region removal and close explicitly. The old external proxy
  is not needed to preserve the working native search capability.
- **Restart notifications: retain current implementation; no old API port.**
  Current RestartModule sends the native viewer countdown and cancellation,
  retaining its alert-array interface. Newer Tranquillity uses a seconds-based
  interface. Do not change current callers merely to reproduce an older donor
  signature, sound asset or schedule implementation. Existing worktree changes
  in RestartModule were not modified by this review.
- **Listener ranges: unselected deployment feature.** Mobius's range code
  scans active TCP listeners before binding, excludes the upper bound and can
  leave no listener when the range is exhausted. The scan cannot reserve a
  port. Both newer configurations retain an explicit HTTP listener port;
  Continuum also already accepts MATCHING for region ports. No demonstrated
  benefit justifies importing the older allocation logic.
- **Access control: do not replace current service boundaries.** Continuum
  already reads configured MAC/ID0 restrictions at login and gatekeeper and
  exposes the newer optional IBansService gatekeeper integration, also present
  in Tranquillity. Mobius's separate MySQL-only ban tables/console are a distinct
  management feature, not proof that current checks are absent. That provider's
  inspected SQL uses bound values; it is not rejected as SQL injection. No
  parallel service/schema is selected without a demonstrated management need,
  clear ownership of local/HG enforcement and supported-provider requirements.

No source changes were selected in this continuation: the inspected older
replacements do not establish a benefit over the retained implementations.
The final review below closes the remaining feature-branch dispositions.

#### Final reconciliation — 2026-10-21

**Complete for the pinned Mobius feature branch; no additional source port
selected.** Earlier open/initial findings above are superseded by these final
decisions. This closes the review queue, not deployment certification.

Coverage: every non-merge first-parent commit from Display Names introduction
`20f50f7502` through pinned tip `7b69b4d545` was indexed without an author
filter, including README/changelog commits under alternate author spellings.
The six first-parent merges (`706eaf86bf`, `1f7df92fc3`, `de8da8c5fb`,
`83cea8165f`, `920914ec3c`, `da7ceb1ce7`) are excluded as bulk ports of older
upstream/branch aggregates under newer-code precedence. This is not a
line-by-line certification of every ancestral OpenSim commit; pre-Mobius base
history is not a new backlog of old upstream patches.

| Final slice / commit | Decision and evidence |
|---|---|
| HG timeout `76275fe5f0`, catch/debug cleanup `da7a71b8ae`, `1e80350819` | Retain current connector: five-second default, configurable 1–30 seconds, bounded IDs/response bytes and requested-ID validation. No old request/catch implementation needed. |
| Missing GridUser name refresh `e4f0d9ad7e` | Retain current account/UUI-based path. UserManagement resolves authoritative local accounts first, skips null GridUser entries and refreshes cached foreign users by HomeURL without requiring a fresh GridUser row. Do not restore the old update_name overload or fabricate rows for name lookup. Runtime fallback remains in the handoff. |
| TOS `34a1601a2a` | Already retained in Continuum: handler passes agree_to_tos, login compares/persists TOSDate, and MySQL/SQLite/PostgreSQL account migrations carry the field. No duplicate port. Absence from newer Tranquillity does not justify removing a retained feature. |
| RSA `6f52286f34`, schema correction `55a67ede2b` | Already retained in handlers, authentication provider/connectors/interfaces and login flow. All three auth schemas contain rsaOnly/rsaKey defaults. The old correction relaxes MySQL nullability; no demonstrated benefit justifies changing the current NOT NULL/default contract. Keep runtime authentication/migration gates. |
| Resident welcome trimming `0200155663` | Exclude cosmetic replacement. Current Continuum and pinned Tranquillity use firstName plus lastName; no functional requirement justifies hiding the account surname. |
| Restart correction `db7387ae31` | Superseded by retained current alert-array scheduler/help/native notification. No old seconds-loop replacement. |
| LandStat debug removal `9454e0ccf6` | Already absent from current reply; no port. |
| Terrain console `2f3718f28a` | Existing commands retained. Current RegionConsole dispatcher requires estate owner/manager or god; full server-console forwarding is god-only. No duplicate handlers. |
| Positional sound `aedb95b0d7` | Existing API resolves the sound asset, handles absent module/invalid UUID and passes the supplied position to TriggerSound. No older replacement needed. |
| NoobDash/OSAWS/jOpenSim submodule history | Exclude removed deployment wiring. Pinned tip has no .gitmodules; missing external working trees are not licensed source supplied by this checkpoint. Retain native services/WebUI. |
| Version/readme/splash/changelog, solution rename, .gitignore, old VS tooling, example endpoints | Exclude historical/deployment changes. Retain feature attribution without importing branding, obsolete tooling or grid endpoints. |

The root license customization caveat prevents assuming every historical asset
has the same license. No new source/assets were copied. The metadata-less donor
archive remains untouched and is not a port source.

No beneficial missing change was established. Map suppression, parallel access
management, old external proxies and listener ranges are **not selected**,
rather than deferred implementation obligations. Reopen for a demonstrated
Continuum need. No live configuration, schema or deployment changed. Retained
feature runtime gates remain in the existing test handoff.

### 14. opensim-lickx archival candidates

- **Donor and checkpoint:** user-preserved last available copy at `S:\Github\opensim-lickx`, archival commit `6614599b7506b63861763e6cae5eefde861f8749`; the project is no longer on GitHub and the archive has no configured remote or earlier Git history.
- **Feature and intended behaviour:** an optional `Lickx_Api` exposing viewer-name lookup to scripts; an OpenSimMutelist module; a DTL/NSL-derived MoneyServer/currency distribution; and historical PHP/MySQL OpenSimSearch crawler/helper files.
- **Current Dev/Continuum equivalent:** Continuum already has current script APIs and viewer circuit metadata, native mute-list support, separately preserved MoneyServer Compatibility plus ContinuumEconomy, and a hardened optional OpenSimSearch viewer connector. The archive does not establish that any of these should replace the selected implementations.
- **Genuinely missing behaviour:** the archival `lxGetAgentViewer()` compatibility API has now been ported under its donor API name. Any missing mute, economy, or search behaviour must be demonstrated by code and runtime comparison rather than inferred from file presence.
- **Affected files and services:** script API registration/runtime; mute-list region module; MoneyServer, MySQL wrapper, region currency connector and helper URI; search crawler database and PHP endpoints.
- **Addon versus core:** the three-file script API is a narrow core compatibility patch because YEngine discovers APIs and runtime stubs from its core assemblies. Currency and crawler code remain outside core.
- **Compatibility:** archive documentation targets OpenSim 0.9.x, .NET 8 and MySQL for currency. SQLite/PostgreSQL, current Windows service operation, Robust, Hypergrid trust, current viewer and multi-region behaviour are unproven.
- **Licensing and provenance:** the OpenSim tree declares BSD-3-Clause. `opensim.currency-lickx` separately declares MIT and credits DTL/NSL. Preserve both lineages. Embedded certificate files, PHP helpers and every other addon asset require separate provenance/security review; archival commit identity is not original upstream provenance.
- **Tests required:** compare APIs and schemas with current Continuum; build each candidate separately; test script permissions and viewer privacy, mute persistence/crossings, MoneyServer migration and all supported database boundaries, helper authentication, search privacy/paging/deletion, Windows paths, Robust restart and Hypergrid inputs.
- **Recommendation:** retain the narrow Lickx API port and native Continuum mute-list implementation; use currency only as a MoneyServer parity reference; quarantine bundled certificates and historical PHP search/economy helpers unless independently secured and justified.

#### Frozen-donor continuation — 2026-10-21

Work on active Tranquillity/upstream donors is paused while this donor is being
reconciled. Do not move to another inactive donor until the open lickx rows have
an explicit implementation or exclusion disposition. Live certification is
tracked separately in the existing test handoff.

The archive remains unchanged at `6614599b7506b63861763e6cae5eefde861f8749`.
Its `OpenSim/` tree contains 1,543 files. Only three core paths are additions
against the original Continuum baseline: the Lickx API, interface and stub.
However, 152 shared core files have different blob IDs; file-presence checks
alone miss those changes. Matching historical upstream blobs over January–March
2026 found 1,486 exact matches at `4f8147696a6330bd0e48541f1b6f3ede2c2f8647`
(29 January). That gives a manageable 57-file comparison including the three
API additions. This is comparison evidence, not a claim that the archival
donor has recoverable original commit ancestry.

| Donor slice | Current disposition |
|---|---|
| `Lickx_Api`, `ILickx_Api`, `Lickx_Stub` | Already ported under donor names; retain. Broader lifecycle/privacy checks remain in the handoff. |
| OAR `--tenant` parser, object filtering and help | Ported this batch. Unlike the donor's silent fallback, malformed/zero UUIDs fail before archive content is written. An explicit estate-owner UUID still filters to that owner's objects. Existing permission filters apply as that tenant. This remains a region archive, including terrain, parcels and settings, not a tenant-isolated backup. |
| IAR foreign `CreatorData` retention and `--nocreators` | Ported this batch. Existing `--creators` remains accepted; explicit `--nocreators` wins. Donor `--home` fallback behavior is retained. |
| `llGetEnv`: `region_rating`, `grid`, `mailname`, `objectmail_hostname` | Ported this batch; mailbox aliases return empty when email is unavailable. |
| `PAYMENT_INFO_ON_FILE` / `PAYMENT_INFO_USED` | Ported with donor values 1 / 2. `CLICK_ACTION_IGNORE` was already present. |
| `PRIM_NORMAL` negative X/Y offsets | Ported donor range `[-1, 1]`; regression verifies values reaching the material provider. |
| MySQL profile migration v6 | Ported donor Unicode conversion, adapted from `utf8` to `utf8mb4` and extended to `usersettings`. Exact-version MySQL/MariaDB clean/upgrade tests remain pending; no live database was migrated. |
| Generated map tile directory | Equivalent `MapTilesDirectory` support is already implemented in Warp3D and WorldMap. Do not add a competing output directory implementation. |
| Chat configuration lookup | Donor correction to `[Chat]` is already present. |
| Email IMAP object transport and `[Email]` configuration | Compatible adaptation implemented, disabled by default. Uses bundled MailKit with bounded background APPEND/polling, exact single-recipient routing, bounded partial MIME reads, backpressure, UIDVALIDITY checks and UIDPLUS per-message acknowledgement only after local queue admission. Retains local delivery, SMTP and `[SMTP]` configuration; optional `[Email]` takes precedence. Replaces the donor's synchronous substring search and whole-inbox expunge. Fake-store, actual-module and loopback IMAP protocol regressions cover routing, limits, retries and lifecycle. Queues/deduplication are memory-only; live TLS/server and two-simulator acceptance remain in the handoff. |
| Group attachment active-group update / title refresh | Reconciled in both native and XML-RPC modules. Apply attachment group changes only when the authoritative active membership agrees, and only to the resident's live root attachments. Do not restart deliberately stopped scripts. Refresh the active group after a title-role change; do not activate an unrelated group or a missing membership. |
| HG friend notifications | Ported UUID-only recipient semantics and per-friend service resolution, adapted to batch by resolved endpoint. Deduplicate recipients, reject malformed identities/endpoints and limit returned online IDs to those requested. Do not assume the first friend's endpoint applies to all friends sharing a home-grid string. |
| HG creator identity/cache repair | Local creator metadata now requires an authoritative account with matching name and UUID in both local-URL/empty-host paths. Retain Continuum's existing shorter 1,800-second account/60-second negative-cache policy rather than the donor's 1–2-hour/10-minute values. |
| HG staged attachment loading/name refresh | Keep current ownership checks, gather-before-attach path and `ScenePresence` restart-generation debounce. Donor per-object script creation bypasses the selected root-agent restart sequencing; fake group-title name refresh sleeps five seconds and conflicts with authoritative DisplayName updates. Those replacements are excluded. Ported the narrow missing-asset-URI completion guard so a visitor allowed without attachments is not left waiting for attachment data. Do not use donor `LoggedIn()` calls merely to cache a new HG friend: that would report a login that did not occur. |
| Presence/GridUser `UseCache` controls | Ported opt-out, default true. Caches are per service instance rather than donor/global static state, and GridUser reads the setting outside its one-time console-registration block. No-cache paths bypass both cache reads and writes. |
| PGSQL ILIKE changes | Ported grid-user, group, region and account case-insensitive queries. Group and grid-user values are bound parameters; account queries retain DisplayName and ScopeID filtering while dropping the hardcoded `en_US.utf8` collation that is not portable to Windows clusters. Unicode case folding follows the selected database locale; this is not a collation-independent Unicode-search promise. |
| Profile lifecycle/null fixes | Already present: user management resolves during RegionLoaded, missing dependencies disable profiles, client removal uses TryGetScenePresence, pick limits guard null results, and asynchronous request locks exit in finally blocks. Retain current split local/HG request queues. |
| Archive default-user | Ported the two missing parcel fallback assignments to `m_defaultUser`, matching object fallback and the explicit import option. |
| Remaining scripting behavior | Existing `llRequestAgentData` covers payment data; `llGetRegionTimeOfDay` is already exposed with current EEP semantics; `[Chat]` lookup is already corrected. Retain standard `llGetTimeOfDay` rather than replacing its legacy cycle with parcel EEP/86,400-second behavior. Retain offline-local inventory delivery via authoritative accounts; do not authorize foreign offline recipients solely from a cached GridUser record. Exclude the donor's invented `slurl.opensim.local` offer URL default in favor of current inventory offer formatting. |
| Forced land defaults, terrain UUIDs, grid-god defaults, IP-only home-grid blocking, viewer-regex bans and `take_copy_restricted` | Excluded as deployment policy replacements: retain configured parcel/estate rules and standard copy/transfer permissions rather than adding an owner/friend-only override to otherwise copyable content. |
| Presence public `login` re-enablement, unbounded map queues and relaxed outbound filters | Excluded; retain current authentication, queue limits and outbound network policy. |
| Integer parser, movement/attachment physics, deletion provenance and SIGTERM | Retain current implementations. Donor `int.Parse` replacement loses numeric-prefix behavior; movement thresholds and attachment-frame changes are unverified physics experiments, not demonstrated fixes; root-only CHANGED_POSITION drops child-script events; overwriting LastOwnerID before deletion changes provenance. Unguarded PosixSignalRegistration in simulator/Robust construction is not Windows-compatible; portable process supervision remains separate operational work. |
| OpenSimMutelist/PHP mute helper | Native grid-wide mute services are already selected; historical direct-database PHP helper remains excluded as documented in `parity-social-services.md`. |
| MoneyServer/currency and PHP economy helper | Reconciled to separately selected MoneyServer Compatibility: ClientLogin/Logout, balance/history, cancel/transfer/force/charge/banker/script operations and quote/buy are present. Simulator callbacks retain OnMoneyTransfered/UpdateBalance/UserAlert only; public balance/mint/transfer callbacks, self-asserted WebLogin/history sessions and prototype no-debit land preparation remain deliberately unregistered. Native `/currency.php` and `/landtool.php` service adapters cover viewer helper flows. Retain existing atomic MySQL transactions, bounded requests and session/source validation. Bundled `server_cert.p12`/`SineWaveCert.pfx`, database configuration, Gloebit/Podex PHP payment addons and grid endpoints are excluded deployment assets. Protocol parity is not legacy-database runtime certification. |
| Gloebit | All 11 donor C# paths exist: seven match after line-ending normalization; four differ for current balance-error handling, explicit datastore connections, HTTP/version negotiation placement and initialization failure handling. Retain the current optional addon and its LGPL notices; do not merge it into the BSD core or ContinuumEconomy. No new Gloebit code is selected and no external payment calls were made. |
| OpenSimSearch/PHP crawler and helper assets/configuration | Places/popular/land/events/classifieds/details/map adapters are already present with lifecycle and transport bounds. ContinuumSearch replaces register/parser/query PHP plus cron/purge/schema files with authenticated bounded snapshot ingestion and a provider-neutral directory service. Do not add duplicate direct-SQL helpers or import databaseinfo credentials. Extra mutelist SQL/readme, search cron/schema/readme/purge, helper index/robots and example economy configuration are deployment/history assets, not missing server features. Preserve existing source/license inventory and pending viewer/provider gates in `parity-search.md`. |

Automated evidence for this batch is in `tests/Continuum.Lickx.Tests`, which
generates actual OAR streams and checks unfiltered/tenant/estate-owner selection,
permission filtering and bad filters; round-trips foreign IAR creator data;
checks environment keys, payment constants and material-provider offsets.
The reconciliation pass additionally tests per-instance cache isolation and
cache opt-out through actual service constructors, local creator UUID validation,
both group title backends and HG UUID/endpoint grouping. Actual PostgreSQL
providers passed quoted-input, directory-privacy and scoped-name tests in the
dedicated `continuum_lickx_20261021_160426` database on loopback port 9543.
That database contains disposable fixtures only and is retained for inspection.
Full Release build passed with zero warnings/errors. No deployed grid binaries
or existing grid schemas were changed.

**Reconciliation status: implementation complete (2026-10-21).** IMAP was the
last selected implementation gap. The previously open candidate families now have
explicit ports, existing equivalents or exclusions above. This is not a claim
of full lickx runtime parity: IMAP implementation is complete but awaits live
mail-server acceptance, the excluded experimental/policy changes are not implemented, and all queued
database/viewer acceptance remains in the test handoff. Do not silently turn
these dispositions into a production-certification claim or move to another
donor without the operator's direction.

### 15. SL-compatible parcel prim accounting

- **Classification / behaviour:** upstream-quality bug fix. `llGetParcelPrimCount` now handles every category for same-owner simulator-wide land and counts non-mesh temporary objects for parcel or simulator scope; parcel-detail capacity and usage report simulator-wide values.
- **Donor and commit:** Gunthar `3b2321ff69` (with parcel-detail correction `48c71206b8`).
- **Current Dev equivalent / missing behaviour:** Dev only supported total simulator count, returned zero for other simulator categories and temporary objects, and returned single-parcel capacity/usage.
- **Affected files and services:** `LSL_Api.cs`; simulator script execution and parcel accounting only. This is a narrow core compatibility patch, with no Robust or database dependency and no Hypergrid persistence effect.
- **Compatibility / requirements:** MySQL, PostgreSQL, SQLite and Windows-neutral; requires an SL-compatible viewer only to observe parcel UI values. Foreign objects and visitors remain subject to the destination region's counts.
- **Licensing and provenance:** OpenSim-derived Gunthar source, retained under the repository BSD lineage with exact commits recorded above.
- **Tests required / recommendation:** build plus scripted parcel fixtures covering owner/group/other/selected/temp, multiple same-owner parcels, mesh exclusions and attachments. Retain for production testing.

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

## 2026-10-22 WhiteCore WebUI continuation checkpoint

WhiteCore remains the active donor; earlier direct-module-port exclusions do not
exclude the integrated native-service WebUI implementation. News, recovery,
region-owner events and paid classified management are implemented in source.
The classified batch includes durable fee/publication recovery and viewer/provider
corrections that preserve paid metadata and ownership. Sixteen legacy page paths
remain blocked; grid-wide auction WebUI and other parity gaps remain open.
Use `parity-webui.md` for the current workflow inventory and limitations, and the
existing handoff for runtime tests. No donor switch or live deployment occurred.


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
