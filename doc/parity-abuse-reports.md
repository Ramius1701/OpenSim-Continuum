# Abuse Reports donor-parity audit

## Lineage and authority

- Mobius commit `8687793883` is the original OpenSim-derived grid-wide donor.
  It supplies viewer CAPS submission, screenshot upload, the Robust service and
  connectors, and MySQL persistence.
- Tranquillity current `develop` at
  `6180f4027e7e055360124112408286217137bf8e` contains no Abuse Reports tree.
  It is therefore not a carried implementation or behavioral authority for
  this feature.
- WhiteCore commit `f2f772770449d17cd95d2bbc3a0a3bd0cf5dd3fa` is the donor for
  moderation workflow evidence: active reports, assignment and notes, estate
  email notification, WebAPI access, and the integrated administrator pages.
- Continuum's current implementation is an expanded Mobius lineage. Its added
  database providers, remote moderation operations and hardening are candidate
  adaptations until runtime tests prove them.

## Current Continuum comparison

| Slice | Current status | Donor decision |
|---|---|---|
| Viewer report submission | Present from Mobius lineage | Retain and verify the exact viewer request fields and response shapes. |
| Screenshot upload | Present with bounded upload and lifecycle hardening | Retain only after success, rejection, timeout, disconnect and concurrent-upload tests. |
| Grid-wide Robust storage | Present | This is the intended Mobius architecture; verify authenticated simulator-to-Robust operation and restart persistence. |
| MySQL/MariaDB | Present from donor and later migrations | Verify clean install and every schema upgrade represented in the migration file. |
| SQLite and PostgreSQL | Continuum adaptations | Required support; retain only after provider-parity and migration tests. |
| Moderation list/get/update | Continuum extension informed by WhiteCore | Direction is correct. Confirm pagination, image exclusion from lists, status transitions, assignment identity and notes persistence against WhiteCore behavior. |
| Console moderation | Present | Useful administrative surface, but it does not replace the integrated WebUI workflow. |
| Estate-owner email | Implemented from WhiteCore behavior | After successful grid-wide storage, Continuum asynchronously uses the estate's `AbuseEmailToEstateOwner`/`AbuseEmail` settings and the configured SMTP module. Missing SMTP, throttling or delivery failure cannot fail or roll back the report. |
| Integrated WebUI manager/detail pages | Present | ContinuumWebUI retains the WhiteCore manager/detail workflow through authenticated native OpenSim service adapters; deployment acceptance remains. |
| Public/admin WebAPI | Partial equivalent through Continuum Robust handler | Reconcile authorization and response contracts with the WhiteCore WebAPI before exposing it to the integrated UI. Do not copy WhiteCore's password-in-method contract unchanged. |
| Hypergrid reporting | Local-grid authority only | A visitor may submit a report about conduct in the visited region, but the report and moderation authority remain with that grid. Do not forward moderator credentials or private evidence to a foreign home grid. |

## Exact donor differences

Mobius exposes only `ReportAbuse(AbuseReportData)` and stores the viewer report
centrally. Continuum adds `GetReport`, paged `GetReports`, and `UpdateReport`,
plus status, moderator notes and moderator identity. Those additions are needed
for an operable grid-wide system, but they are not part of commit `8687793883`.

WhiteCore demonstrates the missing operational workflow:

- report count and paged active-report retrieval;
- report detail retrieval and update;
- assignment, notes and active/closed state;
- optional email to the estate abuse address when estate policy enables it;
- an administrator manager page and report-detail page backed by its WebAPI.

WhiteCore's viewer CAPS block is explicitly disabled in its module and must not
replace the working Mobius CAPS lineage. Its unauthenticated overloads and
password-style connector contract are architectural evidence, not code to port
verbatim.

## Classification and recommendation

| Candidate | Classification | Recommendation |
|---|---|---|
| Mobius submission/CAPS/Robust path | Robust service extension | Retain as the feature base. |
| Continuum SQLite/PostgreSQL providers | Robust service extension | Retain after mandatory parity tests. |
| Continuum moderation API and console | Robust service extension | Reconcile with WhiteCore workflow and retain with authenticated private access. |
| WhiteCore estate email | Optional addon/module behavior | Port narrowly after the core report path passes; failures must be non-fatal. |
| WhiteCore abuse administration pages | Optional integrated WebUI module | Port faithfully in the scheduled WhiteCore WebUI phase. |
| WhiteCore disabled viewer CAPS | Obsolete or unsuitable | Do not port. Mobius is the closer working donor. |
| Cross-grid moderation or evidence forwarding | Experimental feature | Exclude without an explicit trust and privacy design. |

## Compatibility and provenance

- **Robust:** required for grid mode; simulator submissions and moderation
  operations use the private authenticated service endpoint.
- **Databases:** SQLite is required for standalone; MySQL/MariaDB and PostgreSQL
  are required for grid mode. Schema, field length, ordering and update behavior
  must match across all three.
- **Windows:** paths, screenshot byte handling, console commands and service
  configuration must pass on the supported Windows deployment.
- **Viewer:** test current Firestorm report submission with and without a
  screenshot. No custom viewer is required.
- **Licensing:** Mobius files retain their original OpenSim-derived licensing
  and commit provenance. WhiteCore is BSD-3-Clause-style licensed; preserve its
  notices for any code actually ported. This audit records behavior only.

## Required test gate

- Submit reports with every viewer report type, Unicode details, missing object,
  present object, avatar target and no target.
- Submit without a screenshot and with valid, empty, oversized, interrupted and
  duplicate screenshot uploads.
- Confirm one report per submission and no orphan upload handler after success,
  timeout, disconnect or simulator shutdown.
- Restart the simulator and Robust, then retrieve and update the same report.
- List by open/closed status with deterministic paging; lists must not return
  screenshot bytes, while authorized detail retrieval may do so.
- Exercise assignment, notes and state transitions concurrently and confirm
  moderator attribution.
- Run clean-install and upgrade migrations plus equivalent CRUD tests on SQLite,
  MySQL/MariaDB and PostgreSQL.
- Verify a foreign Hypergrid visitor can report conduct locally without gaining
  moderation access and without evidence being sent to the home grid.
- During the WebUI phase, verify authenticated manager/detail pages, CSRF and
  authorization checks, and optional estate email with both working and failing
  mail transports.

Abuse Reports is implementation-complete enough for controlled runtime testing,
not production-approved. The core test gate comes before the optional email and
WebUI ports.
