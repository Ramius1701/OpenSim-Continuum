# Display Names donor-parity audit

## Lineage

| Stage | Commit | Behavior |
|---|---|---|
| Mobius introduction | `20f50f7502` | Mutable names, account/grid-user persistence, CAPS, login/search/script surfaces and service plumbing. |
| Mobius Hypergrid follow-up | `924deef165` | Home-grid `/get_display_names` endpoint and remote connector for foreign users. |
| Tranquillity integration | `0e0953667c` | Reworked the Mobius feature onto its newer OpenSim base, using `UserAccount.DisplayName`/`NameChanged` and a smaller simulator CAPS module. |
| Tranquillity 1.x | current `develop` `6180f402603f31e5e01e18da68fdde59d589f939` | No later behavioral Display Names commit after `0e0953667c`; subsequent changes are project/plugin restructuring. |
| WhiteCore reference | `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` | Central CAPS service persists profile display names and broadcasts `DisplayNameUpdate` to all clients known to the region CAPS service. |

Mobius origin and Tranquillity integration are one lineage. They must not be
applied as independent patches.

## Current Continuum disposition

| Behavior | Disposition | Evidence / decision |
|---|---|---|
| Local account persistence and weekly timestamp | Retain Tranquillity lineage with current service-side atomic throttle | Continuum stores `DisplayName` and `NameChanged` through the grid-wide account service. |
| Viewer `SetDisplayName` and `GetDisplayNames` | Retain with current-OpenSim adaptations | Continuum uses current CAPS/LLSD infrastructure, bounds requests and validates values. |
| Login, nearby/search and script lookup | Retain reconciled implementation | These surfaces consume the same account/UserManagement fields. |
| Same-simulator update event | Retain donor behavior | Tranquillity and WhiteCore both broadcast `DisplayNameUpdate` to connected viewers after a successful change. |
| Cross-simulator local-grid propagation | Continuum adaptation pending runtime proof | Tranquillity has no inter-simulator invalidation. WhiteCore's central CAPS architecture broadcasts more widely but is not directly portable. Continuum currently refreshes authoritative account data on root entry and a bounded interval; this is not donor code and must be explicitly tested or replaced by a service event mechanism. |
| Hypergrid display-name lookup | **Missing donor behavior** | Continuum does not contain Mobius `924deef165`'s remote home-grid lookup. Current UserManagement can reconstruct foreign legacy identity but cannot fetch the foreign mutable display name. |

## Hypergrid port decision

Mobius proves the intended behavior, but its original endpoint is not suitable for
a blind port: it is unauthenticated, performs DNS/IP rewriting, accepts an
unbounded ID collection and uses older synchronous request infrastructure.

The required narrow compatibility port must preserve Mobius semantics while
adapting to current OpenSim service contracts:

- resolve foreign users through their trusted home URI, never a viewer-supplied
  arbitrary endpoint;
- authenticate the home-grid request using the current Robust service-auth model;
- accept only bounded, distinct, nonzero UUIDs and return bounded structured data;
- cache remote display names with a short expiry without overwriting legacy UUI;
- fail back to the foreign legacy name when the home grid is unavailable;
- avoid exposing local account search or mutation through the HG endpoint;
- test HTTPS, DNS change, timeout, malformed/oversized replies and hostile grids.

This is a **narrow core compatibility patch**, sourced from Mobius
`924deef165`, not a newly invented Display Names feature.

## Compatibility and tests

- **Robust:** local persistence remains authoritative; HG lookup must use a
  separately authenticated handler.
- **Databases:** DisplayName/NameChanged migrations and reads require SQLite,
  MySQL/MariaDB and PostgreSQL clean-install and upgrade coverage.
- **Windows:** verify timers/shutdown only for the current propagation adaptation;
  HG connector must use current portable HTTP infrastructure.
- **Hypergrid:** two-grid test for lookup, rename, cache expiry, home-grid outage,
  untrusted endpoint rejection and legacy-name fallback.
- **Viewer:** clean cache, rename/reset, weekly throttle, relog, full grid restart,
  two simulators with existing observers, search/nearby/nameplate consistency.

## Gate

Display Names are not donor-parity complete until the Mobius HG behavior is
ported safely and the current cross-simulator local-grid propagation path passes
live testing. No other Display Names rewrite is justified by the donor evidence.
