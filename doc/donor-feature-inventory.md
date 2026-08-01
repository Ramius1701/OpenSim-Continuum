# Donor feature audit: checkpoint 1

Date: 2026-08-01  
Audit base: `247b9182c1ca0f11743de06a2808f003bc8e2a90` (`baseline/opensim-dev-2026-08-01`)  
Scope: evidence-led inventory only; no donor implementation has been cherry-picked or ported.

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
- Mobius is a historical feature reference; the recovered local archive has no Git metadata, so it cannot itself establish commit-level provenance.
- Tranquillity is the leading service-level reference for Display Names, aliases, and Experiences.
- Previous Continuum branches and recovered archives are evidence and staging references, not proof that a change belongs in official core. This checkpoint does not use the local Casperia checkout.
- Gunthar commit identities and history in this checkpoint were verified directly from a temporary clone of `https://github.com/GuntharDeNiro/opensim.git` (`origin/master` observed at `6c7021cc36fd6890db27200cd65fd4bb37bd60fd`).
- A candidate is not eligible to port until its original upstream license and complete provenance chain are confirmed. OpenSim-derived code is generally BSD-3-Clause, but bundled assets and third-party addons must be checked individually.

## Prioritized inventory

Priority reflects expected value and auditability, not authorization to implement.

| Priority | Candidate | Classification | Donor commit/checkpoint | Recommendation |
|---:|---|---|---|---|
| P0 | YEngine orphaned resumed-event guard | upstream-quality bug fix | Gunthar `af16af08b66c049d81faef423950b5c98f34eb9` | Isolate the two YEngine changes from the unrelated example script and review first. |
| P0 | Estate connector must not terminate host process | upstream-quality bug fix | Tranquillity `d978ab12b4c0c05a9ff2016bdce7ed569e7e13f8` | Verify current call semantics and port only if the fatal path still exists. |
| P0 | Warp3D alpha-card rendering | upstream-quality bug fix | Gunthar `c2c30d2dcfa95c93421265bcaa18fc12f88aeef7` | Compare with current renderer; validate transparent/alpha-masked map tiles. |
| P1 | Display Names | Robust service extension | Tranquillity `0e0953667cdc71a9934bfcdef73a661befcd6619` plus MySQL case fix `45232b2f318e6f225675047fc92edfd20f54b51a` | Design as an opt-in service/caps slice; do not transplant wholesale. |
| P1 | User aliases and OAR identity resolution | Robust service extension | Tranquillity `cfd4742a29958781b7778c74fe731fc0be7c9bb6`, negative cache `39aaed76b74c640db62f73ca1f46d42bd299d15b`, config example `d3aac7d244456d74374bbb8446f8188be45ebe96` | Audit API and threat model together with Display Names. |
| P1 | Experiences | Robust service extension | Tranquillity `26d3971448107725ad30d0abf769175ccb7f2467`; restart fix `0281a9f87b8f0f2c0f54876c3e29eeb0b626bb83` | Split protocol/service/data/script work into milestones; high test burden. |
| P1 | Hypergrid stale identity URL/IP repair | narrow core compatibility patch | Gunthar `b72694a29bf2aa7d119cfa4a7900b83b113a18af` (related stored URL repair `4c0d5e1e586ee33a919cece719a6d4562d8d1b4e`) | Reproduce against current HG before considering a minimal patch. |
| P1 | RegionWeb | optional addon module | Gunthar, represented by protected admin increment `b05cd2bb4bc0d325aa1a7c6771d8a933ec6d1405` | Keep optional and region-scoped; perform security review before packaging. |
| P2 | Recovered operational addons | optional addon module | Continuum build-clean checkpoints listed below | Keep out of core; audit each against its original repository and current APIs. |
| P2 | First-run Windows setup wizard | optional addon module | Gunthar `603aa2c983e2f4d52495947010ad19656d951893`, timing fix `db1509cdc43a97df1244e72999f76e7bf838bebe` | Generalize branding/config assumptions before considering a tools package. |
| P3 | ubODE social, contact, and water tuning | experimental feature | Gunthar series culminating in `95aec15852ce206b0acc0147a3487c6721f0c83a` | Do not port as a batch; require deterministic physics benchmarks and opt-in settings. |
| P3 | WhiteCore WebUI | obsolete or unsuitable (as a direct donor) | WhiteCore `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` snapshot | Use only for behavioural requirements; architecture is not a suitable OpenSim module transplant. |
| P3 | Mobius Display Names implementation | obsolete or unsuitable (as code provenance) | recovered `mobius-master` archive; commit unknown | Use for historical protocol comparison only; prefer Tranquillity's traceable history. |

## Candidate records

### 1. YEngine orphaned resumed-event guard

- **Feature and intended behaviour:** avoid a YEngine crash when a persisted/resumed script event refers to a script instance that is no longer valid.
- **Donor and commit:** GuntharDeNiro/opensim, `af16af08b66c049d81faef423950b5c98f34eb9`.
- **Current Dev equivalent / missing behaviour:** current Dev has YEngine resume/event machinery, but not this donor guard as an identifiable feature. A line-level semantic comparison and failing regression fixture are still required.
- **Affected files and services:** `XMRInstAbstract.cs`, `XMRScriptUThread.cs`; region-side YEngine only. The donor commit also contains an unrelated example LSL file which must be excluded.
- **Addon versus core:** narrow core bug fix.
- **Compatibility:** no database impact; Windows-neutral managed code; no intended Robust or Hypergrid impact.
- **Viewer requirements:** none.
- **Licensing/provenance:** OpenSim-derived donor; confirm BSD notices and authorship in the source commit before porting.
- **Tests required:** persisted-state restart, deleted/recompiled script race, event-queue concurrency, existing YEngine unit/integration suite, Debug and Release builds on Windows.
- **Recommendation:** P0, investigate first and upstream as a minimal regression-tested fix if reproduced.

### 2. Estate connector process-termination path

- **Feature and intended behaviour:** a failed estate lookup returns failure to its caller instead of calling `Environment.Exit()` from a library.
- **Donor and commit:** OpenSim-NGC/OpenSim-Tranquillity, `d978ab12b4c0c05a9ff2016bdce7ed569e7e13f8`.
- **Current Dev equivalent / missing behaviour:** `EstateDataConnector` exists in Dev; confirm whether the fatal path or an equivalent remains and whether callers correctly handle null/failure.
- **Affected files and services:** `OpenSim/Services/Connectors/Estate/EstateDataConnector.cs`; region and Robust estate lookup paths.
- **Addon versus core:** core bug fix.
- **Compatibility:** datastore-neutral in intent; explicitly test MySQL missing-estate and connector-error cases. Windows-neutral. No direct viewer or HG protocol change, though HG region startup may exercise the connector.
- **Licensing/provenance:** traceable OpenSim-derived Tranquillity commit; retain BSD provenance.
- **Tests required:** missing estate, database unavailable, malformed response, standalone/grid/Robust startup, MySQL and SQLite, Windows build/runtime.
- **Recommendation:** P0 if the path remains; otherwise classify as already present and document the equivalent upstream fix.

### 3. Warp3D transparent-card map rendering

- **Feature and intended behaviour:** render alpha texture cards correctly in Warp3D map tiles instead of producing black cards.
- **Donor and commit:** GuntharDeNiro/opensim `c2c30d2dcfa95c93421265bcaa18fc12f88aeef7`.
- **Current Dev equivalent / missing behaviour:** Warp3D map generation exists. Missing behaviour must be confirmed because the audit baseline postdates the donor and may contain a different fix.
- **Affected files and services:** `Warp3DImageModule.cs`, plus a donor default configuration change.
- **Addon versus core:** core renderer bug fix; avoid importing donor-specific defaults without justification.
- **Compatibility:** no Robust/MySQL/HG effect; managed Windows rendering dependencies must be exercised.
- **Viewer requirements:** none; output is a map image consumed by viewers/web maps.
- **Licensing/provenance:** OpenSim-derived donor; verify any rendering technique attribution.
- **Tests required:** golden images for opaque, blended, masked, and fully transparent textures; Linux/Windows parity; memory and map-generation timing.
- **Recommendation:** P0 visual regression audit, then minimal fix only.

### 4. Display Names

- **Feature and intended behaviour:** store and serve viewer-visible names independently of immutable account names through caps, account services, and grid service connectors.
- **Donor and commit:** Tranquillity `0e0953667cdc71a9934bfcdef73a661befcd6619`; MySQL migration case correction `45232b2f318e6f225675047fc92edfd20f54b51a`.
- **Current Dev equivalent / missing behaviour:** Dev has avatar search/user management but lacks `DisplayNameModule.cs` and a display-name service/data slice. Historical Mobius code has analogous handlers but is not preferred provenance.
- **Affected files and services:** UserAccount data/service/connectors, CAPS, avatar picker/search, user-management and HG account cache; MySQL migration and configuration.
- **Addon versus core:** opt-in Robust service extension with narrowly required core interfaces/caps wiring.
- **Compatibility:** needs standalone and Robust modes; MySQL migration/rollback and mixed-case table tests; Windows-neutral managed code; HG trust, caching, spoofing, and fallback-name rules are central.
- **Viewer requirements:** viewers supporting standard Display Names caps; legacy viewers must retain account-name fallback.
- **Licensing/provenance:** traceable Tranquillity BSD-derived commits; compare Mobius only as historical prior art.
- **Tests required:** CAPS conformance, cache expiry, rate limits, Unicode/normalization, MySQL upgrade, search, HG foreign-user fallback, old viewer regression.
- **Recommendation:** P1 design spike and protocol tests before code selection.

### 5. User alias service and OAR identity resolution

- **Feature and intended behaviour:** map stable identities through configured aliases, optionally resolving OAR creators/owners without silently substituting a default user; negative caching prevents connector hammering.
- **Donor and commit:** Tranquillity `cfd4742a29958781b7778c74fe731fc0be7c9bb6`, `39aaed76b74c640db62f73ca1f46d42bd299d15b`, and config example `d3aac7d244456d74374bbb8446f8188be45ebe96`.
- **Current Dev equivalent / missing behaviour:** Dev has OAR identity/default-user handling but no identified alias service.
- **Affected files and services:** archive read/import, user-account lookup/cache, connector configuration, and likely Robust endpoints after full history tracing.
- **Addon versus core:** Robust service extension plus narrow archive compatibility hooks.
- **Compatibility:** define standalone/Robust behaviour; MySQL schema depends on final design; Windows-neutral; HG aliases must never permit foreign identity impersonation.
- **Viewer requirements:** none directly.
- **Licensing/provenance:** trace all commits behind the alias connector, not only the config commit; BSD compatibility must be confirmed.
- **Tests required:** OAR import matrices, missing/duplicate alias, cache expiry, SSRF/authentication review, MySQL persistence, HG foreign/local collision cases.
- **Recommendation:** P1, audit jointly with Display Names but keep identity semantics distinct.

### 6. Experiences

- **Feature and intended behaviour:** implement viewer Experience capabilities, permissions, estate/land integration, persistence, and LSL experience APIs.
- **Donor and commit:** Tranquillity feature integration `26d3971448107725ad30d0abf769175ccb7f2467`; persisted-state restart fix `0281a9f87b8f0f2c0f54876c3e29eeb0b626bb83`; typo cleanup `d707ed64e056c77bbf5ae110c45bedf3098d5eb8`.
- **Current Dev equivalent / missing behaviour:** Dev exposes some experience-related constants/stubs but lacks the donor's `ExperienceService`, connectors, data implementation, CAPS module, and complete runtime behaviour.
- **Affected files and services:** broad cross-cut across MySQL, estate/region stores, CAPS/event queue, scene/attachments/inventory, YEngine, LSL APIs, Robust handlers/connectors, configuration, and tests.
- **Addon versus core:** Robust service extension requiring core protocol, persistence, simulator, and script changes; cannot be a drop-in addon alone.
- **Compatibility:** standalone and Robust deployment are mandatory; MySQL migrations need upgrade/rollback validation; Windows-neutral; HG permission ownership and foreign experience IDs require an explicit policy.
- **Viewer requirements:** an Experience-capable viewer; non-supporting viewers and scripts must degrade safely.
- **Licensing/provenance:** traceable Tranquillity/OpenSim lineage, but review every new file and contribution in the feature merge.
- **Tests required:** service/API contracts, permission lifecycle, parcel/estate controls, script restart, KV storage, attachments, MySQL concurrency/migration, Robust outage, HG boundaries, viewer interoperability.
- **Recommendation:** P1 but highest complexity; stage only after an architecture decision record.

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
- **Donor and commit:** Gunthar RegionWeb series; representative admin commit `b05cd2bb4bc0d325aa1a7c6771d8a933ec6d1405` and inventory carousel `2f9abda3877b406b2ca49b9fda55f85dc77e2bbf`.
- **Current Dev equivalent / missing behaviour:** no `RegionWeb` module in Dev. Existing HTTP/caps and map services do not provide this site.
- **Affected files and services:** `OpenSim/Region/OptionalModules/World/RegionWeb/RegionWebModule.cs`, static/template assets, region HTTP server, inventory/map and estate controls.
- **Addon versus core:** optional addon module; only narrowly justified host APIs should touch core.
- **Compatibility:** should run without Robust/database dependencies where possible; any account/admin data path needs MySQL and Robust tests; Windows path/case handling; HG content must be escaped and identity-aware.
- **Viewer requirements:** ordinary web browser; optionally used as viewer login splash.
- **Licensing/provenance:** audit source, templates, logos, images, fonts, and bundled JS separately; strip Vanilla-specific branding unless licensed and desired.
- **Tests required:** authentication/authorization, CSRF/XSS/path traversal, inventory privacy, HTTP concurrency, disabled-module startup, Windows paths, standalone/grid/HG deployment.
- **Recommendation:** P1/P2 optional package after a security and asset-license review.

### 9. Recovered operational addon set

- **Feature and intended behaviour:** optional Abuse Reports, Gloebit money, HoloPhysicsGuard, MoneyServer, OpenSimSearch, OpenSimTide, and OpenSimWeather capabilities recovered and previously reconciled in Continuum.
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

### 13. Mobius historical Display Names reference

- **Feature and intended behaviour:** historical Display Names caps and Hypergrid handlers.
- **Donor and commit:** Mobius-Team/Mobius, but the recovered `mobius-master` directory lacks `.git`; exact source commit is not yet established.
- **Current Dev equivalent / missing behaviour:** Dev lacks complete Display Names; the archive contains `DisplayNamesModule`, `GetDisplayNames` handlers, and HG connectors.
- **Affected files and services:** CAPS, region module interfaces, HG handlers/connectors, user lookup.
- **Addon versus core:** historical reference for a Robust service extension, not an import source.
- **Compatibility:** unknown until the exact Mobius revision is recovered; assume no current MySQL/Windows/Robust compatibility. HG protocol behaviour is useful comparative evidence.
- **Viewer requirements:** Display Names-capable viewer.
- **Licensing/provenance:** blocked for code use until commit and license are tied to the archive.
- **Tests required:** protocol comparison against Tranquillity and current viewers; no code tests until provenance is resolved.
- **Recommendation:** P3 reference only; prefer Tranquillity's traceable implementation history.

## Already-present and unsuitable observations

- Basic avatar/search infrastructure and a sample money module are already present in Dev; donor products must not be described as wholly missing merely because they offer richer behaviour.
- Several Tranquillity build/dependency commits and broad script-engine restructures are unsuitable for direct selection because current Dev has advanced independently; only independently reproducible fixes should proceed.
- Donor branding, curated destinations, forced configuration defaults, auto-stashing update scripts, and grid-specific endpoints are obsolete or unsuitable for official Dev.
- The previous Continuum region-scripting archive (`30211551ce76ebc2d7e0c0d048c04e5ef49a2ea1`) is a provenance lead for region crossing, attachments, and background map generation, not a single candidate. Those behaviours require issue-level decomposition against current Dev before classification.

## Next audit actions

1. Build minimal reproductions for the three P0 fixes and record whether current Dev is affected or already equivalent.
2. Produce protocol/data-flow maps for Display Names, aliases, and Experiences, including trust boundaries and MySQL migrations.
3. Trace RegionWeb and each recovered addon to its original repository/license and enumerate assets separately from code.
4. Convert each viable item into its own audit checkpoint with exact diff boundaries and test plan. No implementation should be ported until that item's audit is accepted.
