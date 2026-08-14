# Physics and scripting donor-parity audit

## ubODE physics

### Verified state

The current Continuum ubODE, shared physics base and `ExtraPhysicsData` files
match current official `upstream/master` exactly. The earlier Continuum realism
series—avatar contact smoothing, water lift, rolling resistance, social
collisions, material tuning and related changes—is not present in the current
candidate.

Gunthar `6c7021cc36fd6890db27200cd65fd4bb37bd60fd` remains heavily
divergent from current official ubODE. Comparing current Continuum with Gunthar
shows 2,120 donor lines removed and only 92 current lines added across five
ubODE files. That branch is behavioral research, not a narrow production port.

### Classification and recommendation

- **Current ubODE:** already present in OpenSim Dev; retain as production
  baseline.
- **Gunthar/previous Continuum realism tuning:** experimental feature; do not
  reintroduce wholesale.
- **Potential individual fixes:** only classify as upstream-quality after one
  observed official bug, one minimal donor diff and a reproducible regression.
- **Databases/Robust/Hypergrid:** physics is simulator-local; crossings must
  serialize compatible object/avatar state but no remote grid controls physics.
- **Windows:** test the exact native ODE library and architecture shipped with
  the Windows build.
- **Viewer:** no custom viewer; viewer prediction makes movement regressions
  especially sensitive to latency and frame rate.

### Test gate

- Official ubODE regression suite plus standing, walking, running, jumping,
  flying, falling, stairs, slopes and avatar/object collisions.
- Prim materials, restitution, friction, density, inertia, linked objects,
  vehicles, buoyancy and water entry.
- Region crossings and teleports while seated, moving, colliding and carrying
  attachments.
- Variable regions, frame-rate degradation, high object counts and long uptime.
- Compare any future donor micro-port against an unchanged official control
  build with recorded scene, settings and measurements.

## LSL/OSSL scripting

### Scale and provenance

Scripting is the largest current core divergence. Relative to official Dev, 16
files change, dominated by `LSL_Api.cs`; the aggregate is approximately 11,135
added and 1,247 removed lines. Commit labels identify Gunthar, Mobius and
Tranquillity sources, but the current aggregate must be verified function by
function because later reconciliations combined and disabled duplicate blocks.

The main API file currently retains large `#if false` donor aggregates beside
the selected implementations. These blocks do not execute, but they materially
obstruct review and provenance. They should be removed in a later dedicated
cleanup only after the selected code is diffed and tested; cleanup must not
change runtime behavior.

The existing compatibility audit reports 514 Second Life function names and no
stub-name gap. This is compile-surface coverage, not behavioral compatibility.
Several functions intentionally report unsupported behavior because OpenSim
lacks their backend. The referenced `doc/script-engine-regression` bundle is not
present in the current commit, so the claimed regression harness is currently
not reproducible.

### Candidate groups

| Group | Donor/current status | Classification and decision |
|---|---|---|
| Inventory/ownership (`llGiveAgentInventory`, `llTransferOwnership`) | Gunthar-derived, later reconciled | Narrow core compatibility patches; verify permissions, atomicity and inventory delivery. |
| Estate/parcel (`llReturnObjects*`, `llSetGroundTexture`, `llSetParcelForSale`) | Gunthar-derived | Narrow core compatibility patches; require exact estate/parcel authority tests. |
| Identity/group (`llMatchGroup`, attachment filtering, rezzer detection) | Gunthar/current adaptations | Narrow compatibility; enforce privacy and HG/local identity boundaries. |
| Notecard/RSA/string helpers | Gunthar-derived and Continuum-adapted | Upstream-quality compatibility candidates after limits, encoding and cryptographic tests. |
| Render materials/GLTF | Gunthar plus Continuum reconciliation | Experimental compatibility until viewer round-trip and asset/override semantics pass. |
| EEP environment APIs | Gunthar-derived | Narrow compatibility where backed by current environment storage; per-parameter unsupported paths must not claim success. |
| Experiences/KVP/events | Tranquillity backend plus Gunthar script surface | Governed by the separate Experiences parity audit and its service/consent tests. |
| Pathfinding character APIs | Names exposed, no navmesh backend | Experimental/unsupported; returning `PU_FAILURE_NO_NAVMESH` is honest but not SL feature parity. |
| Combat2 damage/events | Partial transaction/event surface | Experimental; mutable damage adjustment and complete event metadata are not proven. |
| Sculpt animation | Function exposed, no viewer/backend implementation | Obsolete or unsuitable as a production claim until protocol support exists. |
| `osTriggerSoundAtPos` | Mobius-derived | Optional OSSL compatibility patch; verify threat level, asset and region bounds. |
| YEngine orphan-resume guard | Runtime stabilization | Upstream-quality bug-fix candidate; verify corrupted/old state recovery without silently dropping valid events. |

### Required behavior matrix

- Restore a checked-in, deterministic regression manifest and scripts before
  declaring the API ready for grid testing.
- For every added function record donor commit, exact selected implementation,
  permissions, sleeps/throttles, return values, errors, events, persistence,
  cross-region behavior and unsupported cases.
- Compile and run scripts after simulator, region and full-grid restarts; test
  state save/restore across the old and current serialized formats.
- Inventory/ownership tests must cover copy/transfer/modify permutations,
  nested folders, links, offline recipients, object recipients, failures and
  concurrent deletion.
- Estate/parcel operations must test owner, manager, group role, ordinary
  resident and foreign visitor against parcel/estate changes and return lists.
- Experience tests must cover simultaneous consent, deny/block, timeout,
  disconnect, crossing, KVP quota/concurrency and land admission.
- EEP and GLTF tests must round-trip through current Firestorm and region
  persistence; never report success for data that cannot be stored/read back.
- RSA tests require supported algorithm names, malformed PEM, size limits,
  Unicode bytes, execution cost and protection against private-key logging.
- Pathfinding, Combat2 and sculpt functions must be clearly reported as partial
  or unsupported until real backends pass behavioral tests.
- Stress YEngine with timers, HTTP, dataserver, Experience events, damage,
  script reset/removal, object deletion and simulator crash/restart.

### Database, Windows, viewer and Hypergrid implications

- Most LSL execution is region-local, but Experiences/KVP and identity services
  require SQLite, MySQL/MariaDB and PostgreSQL parity as documented separately.
- Windows paths, line endings, culture, regex and crypto providers require
  direct testing.
- Current Firestorm is the viewer reference for EEP, GLTF, Experiences and
  inventory behavior; function-name compilation alone is insufficient.
- Foreign Hypergrid avatars must not gain estate, parcel, inventory, group,
  Experience or environment authority through local script APIs.
- Preserve OpenSim/Mobius/Tranquillity/Gunthar BSD-style provenance on every
  retained implementation and identify Continuum-only adaptation explicitly.

## Release disposition

Official ubODE is suitable as the controlled physics baseline. The scripting
aggregate is not production-approved: it needs a restored executable regression
bundle, function-level provenance table, removal of disabled donor duplication
after verification, and live behavior certification. Unsupported APIs must not
be advertised as completed SL features.
