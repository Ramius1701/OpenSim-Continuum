# RegionWeb, Weather and Tide donor-parity audit

## RegionWeb

### Lineage and behavior

Gunthar revision `b3511ea070501c32612e24949ded5612c437e8dc` is the
packaged authoritative donor checkpoint (source blob
`db48bab8ff7e115dcb97cd3e4eb54b44d1bd3468`). Its RegionWeb history includes the per-region/estate site,
inventory carousels, protected estate administration, wallet presentation and
optional PayPal checkout. This is not WhiteCore WebUI and is not a grid-wide
public administration interface.

Continuum packages that source as a separately built addon. The source delta is
limited to Mono.Addins assembly metadata and Continuum-facing branding; donor
behavior remains intact. It remains a candidate until the public and
authenticated HTTP surfaces pass live tests.

### Classification and decision

- **Classification:** optional addon module.
- **Current Dev equivalent:** none.
- **Addon versus core:** packaged under `addon-modules/RegionWeb`; it remains
  disabled by default and does not alter the official optional-module assembly.
- **RegionCurrency:** the earlier split portal is deprecated compatibility
  material, never a ledger, and must disable itself whenever RegionWeb is on.
- **Economy:** RegionWeb is a client of exactly one selected `IMoneyModule`.
  It never owns balances or writes ledger tables.
- **PayPal:** sandbox-only until an explicit financial/security release gate.
  Captured payments and in-world credits are distinct operations requiring
  durable reconciliation; donations and future region invoices must not mint
  currency implicitly.
- **Recommendation:** controlled runtime test after economy selection, not a
  production-enabled default.

### Compatibility and tests

- Test Windows content/config paths, region rename/reload, duplicate scene
  callbacks, occupied routes and complete teardown.
- Test all public pages, maps, parcels, assets and inventory carousels with
  Unicode and hostile stored content.
- Require HTTPS and Secure cookies for wallet and Estate Admin; test proxy TLS,
  CSRF, token expiry/reuse, authorization, request bounds and concurrent edits.
- Verify an estate owner cannot edit files outside the enumerated configuration
  set or cross into another simulator's authority.
- Test wallet balance, statement, transfer, disabled purchase and backend
  outage against MoneyServer Compatibility and ContinuumEconomy separately.
- Hypergrid visitors may view public pages, but receive no local estate or
  financial authority from their foreign identity.
- Preserve the OpenSim/Gunthar BSD-style header and exact donor commit.

## OpenSimWeather

### Lineage and divergence

Gunthar `6c7021cc36fd6890db27200cd65fd4bb37bd60fd` is the runtime
baseline. Its module is approximately 1,207 lines. Continuum Weather 0.3.4 is
approximately 2,595 lines and adds multi-emitter coverage, configurable assets,
environment profiles, indoor suppression, surface overlays and extensive
lifecycle controls.

The current module is therefore not a simple cherry-pick or a donor release.
It is an experimental feature built around a donor core. Version 0.3.4 already
corrects the earlier 0.3.3 default/lifecycle mistakes, but compilation and
static review do not prove the expanded simulation behavior.

### Classification and decision

- **Donor weather lifecycle and particle behavior:** optional addon module.
- **Continuum environment, active-area and indoor suppression:** experimental
  feature.
- **Puddle/snow surface overlays:** experimental feature, off by default.
- **Current Dev equivalent:** wind, environment and particle primitives exist,
  but official Dev has no integrated weather controller.
- **Databases/Robust:** region-local; no database or Robust service required.
  Persistent EEP changes affect region state and require explicit permission.
- **Windows:** repository-relative build paths and invariant parsing are
  required.
- **Viewer:** standard particles, sounds and EEP/LightShare support; exact
  appearance varies by viewer and graphics settings.
- **Hypergrid:** weather is visited-region state. Do not propagate control or
  configuration to a foreign grid.
- **Licensing:** BSD-3-Clause-style OpenSimulator license is present.
- **Recommendation:** keep 0.3.4 disabled by default. Establish donor-mode
  parity first, then enable extensions one group at a time.

### Required test ladder

1. Run a donor-equivalent full-region rain/storm/snow cycle and compare emitter
   count, distribution, wind, lightning, thunder, clearing and shutdown.
2. Test normal and variable-sized regions, adjacent regions, crossings and
   multiple regions in one simulator.
3. Interrupt creation at every stage and prove no orphan emitter, timer,
   environment or temporary object remains.
4. Restart during every weather state and verify generated-object cleanup does
   not delete unrelated temporary objects.
5. Test ActiveArea movement, covered areas, exclusion volumes and avatar
   arrival/departure without emitter leaks or unsafe scene-thread access.
6. Test persistent environment opt-in and restoration; document that hard
   process failure cannot guarantee restoration.
7. Enable surface effects last; verify caps, slopes, terrain bounds, cleanup
   and asset UUID validation.

## OpenSimTide

### Lineage and decision

OpenSimTide is a separate JakDaniels/OpenSimTide v0.2 addon, not a Gunthar
module. Continuum adds configuration validation, UTC cycle calculation,
invariant script broadcasts, exact announcement counts, original-water restore
and region ownership/lifecycle hardening.

- **Classification:** optional addon module.
- **Current Dev equivalent:** official water height controls exist, but no tide
  cycle or script broadcast service.
- **Robust/databases:** none; state is simulator-local and restarts recalculate
  the configured cycle.
- **Windows:** UTC/invariant formatting is appropriate; test config discovery
  and daylight-saving boundaries.
- **Viewer:** no custom viewer; changing region water produces known seams when
  adjacent regions have different heights.
- **Hypergrid:** visited-region visual/script state only.
- **Licensing/provenance:** the checked-in README names the donor repository and
  author, but the reviewed source lacks a clear license header and the addon
  directory has no license file. Establish redistribution permission before a
  production release.
- **Recommendation:** retain disabled for compatibility testing; do not label
  release-ready until licensing is resolved.

### Required tests

- Low-to-high-to-low cycle timing, reversed/invalid values and zero
  announcements.
- Exact invariant messages on both script channels.
- Varregion coordinates, simulator daylight-saving transition and long uptime.
- Region removal/replacement, module close and restoration of the original
  water height.
- Adjacent-region seam behavior documented as a known viewer limitation.

## Release disposition

RegionWeb is the closest donor-derived candidate but has a large security and
financial test surface. Weather is experimental beyond its Gunthar baseline.
Tide is technically narrow but has unresolved redistribution provenance. None
of the three should be enabled by default or called production-approved before
their respective gates pass.
