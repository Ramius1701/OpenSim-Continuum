# Abuse Reports

OpenSim Continuum's Abuse Reports feature implements the viewer's
`SendUserReport` and `SendUserReportWithScreenshot` capabilities as a
grid-wide service stack.

## Architecture

- The region CAPS module receives the report from the viewer.
- Standalone uses `LocalAbuseReportsServicesConnector`.
- Grid and GridHypergrid use `RemoteAbuseReportsServicesConnector`.
- ROBUST exposes the private `/abuse` service endpoint.
- Reports are stored centrally through `IAbuseReportsData`.
- Storage providers are included for SQLite, MySQL/MariaDB, and PostgreSQL.
  Clean-install and upgrade migration certification remains required.
- The authenticated private service supports paginated moderation retrieval and updates.
- Reports have `Open`, `In Review`, `Resolved`, or `Dismissed` state, moderator notes and audit identity.

This is intentionally a core service integration. It is not a
region-only addon module.

## Enabling in Standalone

The module is enabled by `OpenSimDefaults.ini`. To override it in `OpenSim.ini`:

```ini
[AbuseReports]
    Enabled = true
```

In the active Standalone configuration:

```ini
[Modules]
    AbuseReportsService = "LocalAbuseReportsServicesConnector"

[AbuseReportsService]
    LocalServiceModule = "OpenSim.Services.AbuseReportsService.dll:AbuseReportsService"
    MaxScreenshotBytes = 5242880
    MaxSummaryBytes = 4096
    MaxDetailsBytes = 65536
```

The service inherits `StorageProvider` and `ConnectionString` from
`[DatabaseService]`. SQLite, MySQL/MariaDB, and PostgreSQL providers are
included; select the same provider used by the surrounding deployment.

## Enabling in Grid or GridHypergrid

On every region simulator:

```ini
[Modules]
    AbuseReportsService = "RemoteAbuseReportsServicesConnector"

[AbuseReportsService]
    AbuseReportsServerURI = "${Const|PrivURL}:${Const|PrivatePort}"

[AbuseReports]
    Enabled = true
```

On ROBUST or ROBUST.HG:

```ini
[ServiceList]
    AbuseReportsServiceConnector = "${Const|PrivatePort}/OpenSim.Server.Handlers.dll:AbuseReportsServiceConnector"

[AbuseReportsService]
    LocalServiceModule = "OpenSim.Services.AbuseReportsService.dll:AbuseReportsService"
    MaxScreenshotBytes = 5242880
    MaxSummaryBytes = 4096
    MaxDetailsBytes = 65536
```

The `/abuse` endpoint belongs on the ROBUST private port. It should not
be exposed publicly.

`[Modules] AbuseReportsService` is the connector selection. No legacy
`[Messaging] AbuseReportsModule` switch is required; older Continuum builds
incorrectly required that undocumented setting and could therefore acknowledge
a viewer submission without registering the central storage connector.

## Database migration

`OpenSim/Data/MySQL/Resources/AbuseReports.migrations` creates the table
at version 1, repairs older Mobius-derived schemas at version 2, and adds
moderation workflow fields and indexes at version 3:

- `Category` is stored as text.
- `ReportType` is stored as an integer.
- The table is converted from MyISAM to InnoDB.
- The table character set is converted to `utf8mb4`.
- Moderation state, notes, moderator UUID/name and last-update time are stored.
- Status/time, reported-user and region indexes support bounded moderation queries.

## Moderation console

The commands are registered where the local Abuse Reports service is loaded:
ROBUST in grid mode and the simulator in Standalone mode.

```text
show abuse reports [open|in-review|resolved|dismissed|all] [count]
show abuse report <report-id>
update abuse report <report-id> <open|in-review|resolved|dismissed> [notes]
```

List operations never load screenshot blobs. The service also provides authenticated
private `list`, `get`, and `update` operations on `/abuse` for a later administration
client. The endpoint must remain on the ROBUST private port; it is not a public Web API.

WhiteCore's complete moderation lifecycle informed this design, but Continuum does not
copy its password-in-request administration model. Existing ROBUST service authentication
is required instead.

## Validation boundaries

A successful build proves compilation only. Runtime validation must
cover:

- Standalone submission without a screenshot.
- Standalone submission with a screenshot.
- Grid/ROBUST submission without a screenshot.
- Grid/ROBUST submission with a screenshot.
- Correct region attribution in a multi-region simulator process.
- Database migration from an existing version-1 table.
- Service-authenticated private-port routing.
- Paginated list filtering for every moderation state.
- Single-report retrieval with screenshots excluded and explicitly included.
- Moderator notes and state updates, including invalid state and oversized note rejection.
- Unauthorized access rejection for every private moderation operation.
- Simulator and ROBUST rejection of oversized metadata and screenshots.
- Index-backed query behavior with a large report table.
- Region shutdown and reload.
