# Continuum donor re-baseline — 2026-08-14

This checkpoint restarts the integration audit from donor evidence. Existing
Continuum work remains preserved, but it is treated as an unverified candidate
until each feature passes donor parity, current-OpenSim compatibility and runtime
tests. No destructive history operation is authorized or required.

## Verified checkpoints

| Source | Evidence checkpoint | Role |
|---|---|---|
| Official OpenSim master | `78cb44c0c93dcb2af2b73b46f8cbdb56d2d7e0b7` | Current core baseline; candidate contains it and is zero commits behind. |
| Original OpenSim Dev baseline | `247b9182c1ca0f11743de06a2808f003bc8e2a90` | Both baseline tags and `baseline/opensim-dev-2026-08-01` resolve here. |
| Continuum candidate | `7ab7ab719421a855edefdd2c0e4607ad72c88663` | Frozen audit input; not the behavioral specification. |
| Preserved stabilization work | `065769b51ce356819aad23ec7d8d93621aa20831` | Retained on `origin/fix/continuum-runtime-stabilization`. |
| Mobius | `7b69b4d545b03cc909afced09b1a5431f4412349` | Original Display Names and Abuse Reports lineage; audit ref `mobius-current/master`. |
| Tranquillity develop | `6180f402603f31e5e01e18da68fdde59d589f939` | Current donor development line. |
| Tranquillity 1.x release | `40703bb4fccb91a06ec9091c7bf2cde32bb6ab46` | `release/v1.0` / `tranquillity-rel-1.0.37.rc` evidence. |
| Gunthar | `6c7021cc36fd6890db27200cd65fd4bb37bd60fd` | Active OpenSim-derived fixes and optional modules. |
| WhiteCore | `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` | Behavioral, viewer-protocol, economy, search and later WebUI reference. |
| opensim-lickx | `6614599b7506b63861763e6cae5eefde861f8749` | User-preserved last available archive; keep untouched. |

The isolated worktree has no tracked modifications. Generated
`bin/*.runtimeconfig.json` files remain untracked build artifacts.

## Mandatory decision sequence

For every candidate:

1. Identify original donor files and commits, including later donor fixes.
2. Identify current official OpenSim behavior and interfaces.
3. Compare the candidate implementation line-by-line with both.
4. Classify it using the donor-inventory categories.
5. Prefer donor code and semantics. Adapt only for demonstrated current-OpenSim,
   database, Windows, Robust, Hypergrid, security or licensing requirements.
6. Record every retained, replaced and newly written behavior.
7. Build focused projects and the complete solution.
8. Run automated tests, then the documented live-grid matrix.

Viewer behavior is an acceptance-test target. It is not used to invent private
Second Life server implementation details.

## Mobius-to-Tranquillity lineage rule

Mobius and Tranquillity are not assumed to be independent implementations.
Tranquillity incorporated portions of Mobius, then carried later fixes and
enhancements. For each affected feature the audit must establish:

1. the original Mobius introduction and follow-up commits;
2. the point at which that code entered Tranquillity;
3. Tranquillity-only changes after incorporation; and
4. any later Tranquillity 1.x replacement or redesign.

The selected implementation is the latest compatible lineage, not the union of
duplicate Mobius and Tranquillity patches. Mobius retains original attribution;
Tranquillity contributors retain attribution for their deltas. A behavior already
carried forward by Tranquillity must not be ported a second time from Mobius.

## Audit order

1. Display Names: trace Mobius `20f50f7502` and `924deef165` into Tranquillity
   `0e0953667c`, then audit only the Tranquillity and current 1.x deltas;
   WhiteCore remains protocol evidence. Findings are recorded in
   [`parity-display-names.md`](parity-display-names.md).
2. Experiences: Tranquillity `26d3971448`, current 1.x conformance commit
   `81e5c2449d`, Gunthar script surface, Mobius archive evidence and WhiteCore
   viewer protocol. Findings are recorded in
   [`parity-experiences.md`](parity-experiences.md).
3. Abuse Reports: Mobius `8687793883`, any carried-forward Tranquillity version,
   WhiteCore moderation behavior and the Continuum Robust/database adaptations.
4. MoneyServer Compatibility and ContinuumEconomy: DTL/NSL, opensim-lickx,
   Gunthar RegionCurrency and WhiteCore economy behavior kept as distinct lines.
5. Search, RegionWeb, Weather, Tide, Marketplace, Groups, aliases, mute list,
   voice/WebRTC, rendering, physics, scripting and every recovered addon.
6. Package a controlled OpenSim live-test candidate.
7. Start the separate OpenSim-Grid-Interface and WhiteCore WebUI phase.

No candidate advances because it compiles. Runtime certification remains a
separate gate.

## WhiteCore WebUI port contract

The portal target is WhiteCore-Dev's integrated WebUI at
`f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa`, not a replacement website designed
from scratch and not Gunthar RegionWeb. The donor contains all of the following
and they must be inventoried together:

- `WhiteCore/Modules/Web/WebInterface.cs` and the `IWebInterfacePage` contract;
- page/controller source under `WhiteCore/Modules/Web/html/**/*.cs`;
- deployable HTML templates under `WhiteCoreSim/bin/html/**/*.html`;
- CSS, JavaScript, map resources, fonts, images, translations and configuration;
- public, resident and administrator page families, including Abuse Reports,
  estates, regions, users, purchases, transactions, search, map and profiles.

Porting may replace WhiteCore registry, generic-data, authentication and service
calls with OpenSim/Robust adapters, but it must preserve the donor's page content,
route structure, navigation, permissions and visible workflows unless a specific
security, licensing or current-platform incompatibility is documented. Embedded
or generated HTML must not be overlooked. A newly invented website does not
satisfy this requirement.

### OpenSim-Grid-Interface gap-fill rule

The local `S:\Github\OpenSim-Grid-Interface` repository is recorded at
`ecb377d42b5a1f0ec7a969a65f48596c8e5dbe87` on `main`, under the MIT license.
It is the secondary implementation donor used to complete unfinished or missing
WhiteCore WebUI behavior. Its README's production-ready claim is donor metadata,
not Continuum certification.

For each portal capability:

1. retain the WhiteCore WebUI implementation when it is complete and compatible;
2. identify a concrete missing, stubbed or broken WhiteCore workflow;
3. use the corresponding OpenSim-Grid-Interface implementation to fill that gap;
4. present the result through the WhiteCore WebUI route, page structure and visual
   workflow unless a documented compatibility requirement prevents it;
5. avoid duplicate routes, account systems, admin pages and portal shells; and
6. write new behavior only when neither donor provides a viable implementation.

OpenSim-Grid-Interface is particularly relevant for current viewer/Robust
endpoints, map tiles, search, avatar registration/picker, messaging, grid status,
destinations, accounts and administration. Its PHP/MySQL assumptions, bundled
dependencies, live-style configuration files, helper endpoints and security
boundaries require audit before use. Gloebit and Podex remain out of scope.
