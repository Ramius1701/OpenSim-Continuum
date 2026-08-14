# Experiences donor-parity audit

## Lineage and authority

The feature is treated as a carried lineage, not a collection of independent
patches:

- Mobius is retained as the stated original Experience lineage. Its current
  `master` does not contain the complete Experience tree, so exact file/commit
  ancestry still requires the recovered Mobius evidence.
- Tranquillity `26d3971448` is the first complete traceable service/data/CAPS/
  estate/script integration and remains the backend base.
- Gunthar's Experience-Lite work is a script-surface behavior donor, not a
  replacement grid service.
- Tranquillity 1.x commit `81e5c2449d` reconciles later Legion behavior into
  the Tranquillity backend. Its Experience changes must be selected by slice;
  the commit also bundles Phlox, SLua, bots and unrelated runtime work.
- WhiteCore `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` is viewer-CAPS and policy
  evidence. Its reviewed Experience handlers do not replace Tranquillity's
  complete Robust service.

## Current Continuum comparison

| Slice | Current status | Donor decision |
|---|---|---|
| Grid-wide service, connectors and MySQL | Present from Tranquillity lineage | Retain and compare with `26d3971448`. |
| SQLite and PostgreSQL persistence | Continuum adaptation | Retain only after clean/upgrade and parity tests; these are required beyond the MySQL donor. |
| Allowed, trusted and blocked estate policy | Present, including blocked persistence and CAPS | Matches the later Tranquillity 1.x direction; verify all three databases and viewer mutation. |
| Resident Allowed/Blocked preferences | Present | Retain Tranquillity single-table allow-bit model and verify restart/cross-simulator behavior. |
| Viewer panels and profile metadata | Mostly present | Current serializer carries marketplace and quota fields, but remaining 1.x CAP gaps below are real. |
| Find-by-name pagination | Partial | Continuum pages results but omits donor `next_page_url` and `previous_page_url`, which viewers use for navigation. |
| `ExperienceQuery` capability | Missing | Port Tranquillity 1.x's documented no-op response so viewers do not treat experiences as invalid merely because the CAP is absent. |
| Acquire/Create Experience workflow | Missing | Port the `AgentExperiences` GET/POST and `ExperienceCreators` policy slice; keep policy explicit and default-compatible. |
| Experience group reassignment | Over-restricted | Continuum rejects every group change. Tranquillity 1.x permits the Experience owner, but not a group administrator, to change it. Restore that donor rule. |
| Admin/contributor null/zero guards | Present in portions | Complete exact comparison before change; retain current stricter validation where equivalent. |
| KVP quota | Divergent | Continuum service enforces 16 MiB; Tranquillity 1.x raises it to 128 MiB. Do not change the constant alone: reconcile byte accounting for SQLite, MySQL and PostgreSQL and the script-reported quota first. |
| Script error table and details layout | Present from current reconciled work | Verify against the 1.x slice and Gunthar rather than rewriting. |
| Explicit block versus prompt | Corrected in candidate | The original Tranquillity condition tested `Allowed` twice; Continuum now uses `Blocked`. Retain as a donor defect correction. |
| Consent dialog | Incomplete relative to 1.x | Continuum has one per-script boolean subscription and no 300-second timeout/connection-close resolver. Port the 1.x correlation/timeout semantics into YEngine APIs without importing Phlox. |
| Grant/deny persistence | Present with recent corrections | Retain only where it matches Tranquillity 1.x's block-before-grant and cache-coherency ordering. |
| Land admission | Present with estate and parcel allow/block | Continuum is ahead of the 1.x estate-only slice in parcel data integration; verify against Gunthar and existing parcel storage before retaining. |
| KVP async script responses | Present through YEngine dataserver | Compare numeric error mappings and validation with both Gunthar and 1.x; do not port Phlox adapters wholesale. |

## Proven implementation slices to port

The following are **narrow donor ports** from Tranquillity 1.x
`81e5c2449d`, adapted to current OpenSim/YEngine:

1. `ExperienceQuery` CAPS response.
2. Experience acquisition policy and `AgentExperiences` POST.
3. Search next/previous page URLs and exact viewer paging shape.
4. Owner-only group reassignment while preserving administrator edits to other
   allowed profile fields.
5. Consent correlation by task and item, disconnect handling, one-shot
   resolution and timeout error 18.
6. Consistent marketplace/quota response fields after the quota decision.

The 128 MiB quota is a separate compatibility decision because the donor itself
documents SQLite character-count divergence. It cannot advance until all three
Continuum providers use the same UTF-8 byte accounting and migration/runtime
tests pass.

## Behavior retained from Continuum only with proof

- PostgreSQL and SQLite providers.
- Parcel-level allowed/blocked policy.
- Request/response bounds and service authorization.
- Module lifecycle and restart handling.
- Any candidate hardening not found in the donors.

Each must have a named incompatibility or test justification; compilation alone
does not establish it.

## Required test gate

- Clean and upgrade migrations on SQLite, MySQL/MariaDB and PostgreSQL.
- Robust/simulator restart with owned, allowed, blocked, trusted and group-owned
  Experiences.
- Viewer Search, Allowed, Blocked, Admin, Contributor, Owned and Events tabs.
- Acquire policy for ordinary resident, estate manager, region owner and admin.
- Owner/admin group-field update distinction.
- Consent yes/no, explicit block, disconnect, timeout, duplicate reply, two
  scripts in one object and two simultaneous requests.
- Estate and parcel allow/block/trusted precedence, crossing and attachments.
- KVP create/read/update/CAS/delete/count/keys/size, UTF-8 quota and concurrency.
- Hypergrid must not grant local Experience authority to a foreign identity
  without an explicit trust model.

Experiences remain incomplete until these donor slices are reconciled and this
matrix passes on the controlled test grid.
