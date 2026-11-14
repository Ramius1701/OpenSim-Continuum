# Donor repository refresh — 2026-11-14

| Donor | Verified checkout or fetched ref | Result |
| --- | --- | --- |
| Official OpenSim | `9d00a5555225b17d3d1063cb0f3496fb1b6217f9` | Local `opensim-master/master` fast-forwarded 59 commits; matches `origin/master`. Continuum's `upstream/master` also points here. |
| Tranquillity | `0b09871c308118515c61ac96254501b0988b4af9` | Local `master` fast-forwarded; matches `origin/master`. There are 1,750 newly reachable commits, including merged history. |
| Tranquillity develop | `1f93669fc8ae8f77e666656d3338acd5fe855b6c` | Remote-tracking ref refreshed. |
| Tranquillity release | `584bc952deddac11e249aaf01a48e0273ed2c823` and `628bdc7b09c701657693dd2d8cdc2be328f66dcb` | Fetched `release/v1.1` and `release/v1.1.2`. |
| WhiteCore | `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` | Fetched; local master matches the remote. |
| Mobius | `7b69b4d545b03cc909afced09b1a5431f4412349` | Fetched via the historical `opensim-enhanced` clone; Mobius master unchanged. |
| Gunthar | `6c7021cc36fd6890db27200cd65fd4bb37bd60fd` | Fetched to Continuum's `gunthar/master`; matches the recorded donor pin. |
| lickx | `6614599b7506b63861763e6cae5eefde861f8749` | Frozen archive with no remote; preserved. |

Continuum's development HEAD (`d963e57be8`) and the refreshed upstream share
`58b7b39db676a166c68d47256f50588cdf330630` as their merge base. There are
74 upstream commits absent from Continuum by ancestry and 586 commits on the
Continuum side. This count does not establish whether individual changes are
already present in the worktree. Audit the upstream delta against the current
source before integration, especially where it overlaps local changes.

OpenSim-Grid-Interface is a separate project and was outside this refresh.
No upstream changes were merged during the initial refresh.

## Continuum integration

The development merge includes the 74 upstream commits identified above.
Conflict resolution retains Continuum's Hypergrid URL handling, bounded map
texture work, LSL extensions and typed PostgreSQL profile operations. It
incorporates upstream estate-list transactions, profile row filters and the
SQLite picks gatekeeper column. The SQLite column is added only in the version 5
migration so both fresh databases and existing version 4 databases apply it once.

Before deploying, verify welcome/login, Firestorm inventory and profiles,
Hypergrid travel and the PostgreSQL estate/profile operations on disposable
databases. The Continuum partnership migration now uses SQLite
profile version 6, after upstream's gatekeeper migration. No live grid was deployed or tested.

Publication checks: full Release build passed with two Gloebit obsolete API
warnings. The lickx, script completion, auction, and WebUI suites passed,
including separate auction, event reminder, and currency credit HTTP runs.
Fresh SQLite profile schema and v4/v5 upgrades passed; PostgreSQL integration
and live-grid acceptance remain open.
