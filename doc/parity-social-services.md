# Aliases, mute lists and groups donor-parity audit

## User aliases

### Meaning and lineage

Tranquillity's User Alias service maps an alternate UUID to a local account
UUID, primarily for OAR/archive identity recovery and administrative lookup.
The traceable series begins with `3262f34912`, adds connectors/handlers through
`815ad0dd71` and `cd9094f66f`, adds lookup/create commands in `3c557606c1`, and
adds create/delete behavior in `b553234b8c`.

This service is **not Display Names**. It does not represent a resident-selected
name, nameplate, search label or viewer profile field. The two services must not
share tables, CAPS or cache semantics.

### Current disposition

- **Classification:** Robust service extension.
- **Current Dev equivalent:** no complete official alias service.
- **Continuum status:** local/remote service, handlers and SQLite,
  MySQL/MariaDB and PostgreSQL providers are present. Continuum additionally
  bounds descriptions, completes remote mutations and hardens failures.
- **Addon versus core:** retain as a narrow service extension because archive
  and account resolution integrate with core service paths.
- **Hypergrid:** only a local authority may assert that an alias maps to its
  local account. Never accept a foreign alias mutation as local truth.
- **Viewer requirements:** none; this is administrative/archive plumbing.
- **Recommendation:** retain and provider-test; do not expose public mutation.

### Test gate

- Create, resolve, enumerate and delete aliases across all three databases.
- Reject zero IDs, duplicate/conflicting mappings, overlong descriptions and
  mappings to nonexistent/nonlocal accounts.
- Restart Robust and repeat local and authenticated remote operations.
- Import/export an archive containing known aliases and prove ownership resolves
  without silently assigning content to the region owner.
- Reject unauthorized and foreign-grid mutation attempts.

## Mute lists

### Lineage and boundary

Mobius carries the grid service/data implementation and historical PHP addon.
The core lineage includes the explicit Hypergrid decision in `c34f07b6f3` to
avoid persisting mutes for foreign visitors because proper cross-grid mute
authority is undefined. Tranquillity carries the service forward and includes a
modernized addon wrapper. Lickx contains the older PHP/MySQL helper.

Continuum has the in-process module, local/remote service connectors, Robust
handler and SQLite, MySQL/MariaDB and PostgreSQL storage. The PHP helpers are
obsolete and unsuitable.

### Current disposition

- **Classification:** already-present grid service / Robust extension.
- **Current Dev equivalent:** official Dev contains mute delivery behavior, but
  the donor service supplies persistent grid-wide state.
- **Genuinely missing behavior:** no justified cross-grid federation. This is a
  privacy boundary, not a feature to fill speculatively.
- **Addon versus core:** retain the current service/module integration; do not
  add the recovered PHP module.
- **Hypergrid:** local residents' mute lists remain private local-grid data.
  A foreign visitor's viewer cache may suppress locally, but the visited grid
  must not claim authority to mutate the home grid's list.
- **Viewer:** standard mute/block UI; no custom viewer.
- **Recommendation:** retain and test cross-simulator persistence and privacy.

### Test gate

- Add/update/remove avatar, object, group and by-name entries with every flag.
- Verify CRC/no-change and full-list viewer delivery, Unicode names and list
  bounds.
- Cross regions, restart simulators/Robust/full grid and confirm persistence.
- Run concurrent updates from two simulators without lost entries.
- Verify muted chat, IM, inventory offers and object sounds/particles according
  to the viewer/core responsibilities actually supported.
- Test local and foreign Hypergrid residents without leaking or overwriting the
  home-grid mute list.
- Run identical CRUD/migration tests on SQLite, MySQL/MariaDB and PostgreSQL.

## Groups

### Lineage

Official OpenSim Dev already contains the primary Groups service, region module,
messaging and Hypergrid connectors. Mobius and lickx largely carry that same
lineage. Tranquillity modernizes its project/runtime structure. Continuum's
current divergence from official Dev is concentrated in:

- Gunthar-derived invite message compatibility (`e3a9c3e32f`);
- fee reservation before benefits (`b743709dee`);
- module/connector teardown and region routing;
- connector failure containment and synchronized request caching; and
- confirmation of automatic invitation behavior.

WhiteCore supplies behavioral evidence for group bans and member-data CAPS,
group land/profile WebUI, messaging processing, and group accounting. Its group
service/database architecture is not a drop-in donor.

### Candidate classification

| Candidate | Classification | Recommendation |
|---|---|---|
| Official OpenSim Groups and HG service | Already present in OpenSim Dev | Keep as authority. |
| Gunthar invite-message overload | Narrow core compatibility patch | Retain if viewer/script tests prove the overload is required. |
| Continuum lifecycle/cache failure hardening | Upstream-quality bug fix | Retain with focused concurrency and teardown tests. |
| Success-only/reserved group fees | Narrow core compatibility patch | Retain; test separately with both economy products and no economy. |
| WhiteCore `GroupAPIv1` bans and `GroupMemberData` CAPS | Narrow viewer compatibility patch | Firestorm source confirms both capabilities. Official Dev already supplies `GroupMemberData`; Continuum now adds permission-checked, bounded `GroupAPIv1` ban persistence for SQLite, MySQL/MariaDB and PostgreSQL. Foreign-group mutation remains fail-closed. |
| WhiteCore group land/profile pages | Optional integrated WebUI module | Port faithfully during the WebUI phase using the selected Groups service. |
| WhiteCore group currency/accounting | Robust service extension | Covered by the separate economy design; never embed ledger ownership in Groups. |
| Recovered XML-RPC/PHP group services | Obsolete or unsuitable | Do not add when current authenticated services cover the behavior. |

### Compatibility and test gate

- **Robust:** Groups is grid-wide and requires authenticated service endpoints.
- **Databases:** run clean/upgrade and behavior parity on MySQL/MariaDB and
  PostgreSQL; test SQLite where the selected standalone configuration supports
  the Groups provider.
- **Windows:** test service reload, timers/caches and configuration paths.
- **Hypergrid:** test foreign membership/import, roles, notices, IM and group
  identity without granting a foreign service local administrative authority.
- **Viewer:** group create/join/leave, profile, roles, members, notices,
  invitations, bans, land and chat on current Firestorm.
- Test duplicate/replacement region callbacks, connector outage/recovery,
  concurrent cache access and full teardown without stale subscriptions.
- Verify group creation/join fees charge exactly once only after confirmed
  success under MoneyServer Compatibility and ContinuumEconomy.
- Verify automatic invites, expired/duplicate invites, role power enforcement,
  owner transfer and last-owner protection.
- Test `GroupAPIv1` list/add/remove, the 100-entry request and 500-entry group
  limits, owner protection, permission denial, restart persistence and banned
  join refusal on current Firestorm. `GroupMemberData` remains the official
  implementation.

## Licensing and provenance

The reviewed OpenSim, Mobius, Tranquillity and Continuum service files carry the
OpenSimulator BSD-style license lineage. WhiteCore behavior/code carries its
BSD-3-Clause-style notices. Historical PHP helpers are audit evidence only
unless their license and security model are independently established.

Aliases and mute lists are ready for controlled service testing. Groups remains
the official implementation plus candidate hardening; the WhiteCore CAPS delta
requires a viewer-capture comparison before implementation.
