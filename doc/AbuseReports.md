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
- The current storage implementation is MySQL/MariaDB.

This is intentionally a core service integration. It is not a
region-only addon module.

## Enabling in Standalone

In `OpenSim.ini`:

```ini
[Messaging]
    AbuseReportsModule = AbuseReportsModule

[AbuseReports]
    Enabled = true
```

In the active Standalone configuration:

```ini
[Modules]
    AbuseReportsService = "LocalAbuseReportsServicesConnector"

[AbuseReportsService]
    LocalServiceModule = "OpenSim.Services.AbuseReportsService.dll:AbuseReportsService"
```

The service inherits `StorageProvider` and `ConnectionString` from
`[DatabaseService]`. The included data provider is MySQL/MariaDB only.

## Enabling in Grid or GridHypergrid

On every region simulator:

```ini
[Messaging]
    AbuseReportsModule = AbuseReportsModule

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
```

The `/abuse` endpoint belongs on the ROBUST private port. It should not
be exposed publicly.

## Database migration

`OpenSim/Data/MySQL/Resources/AbuseReports.migrations` creates the table
at version 1 and repairs older Mobius-derived schemas at version 2:

- `Category` is stored as text.
- `ReportType` is stored as an integer.
- The table is converted from MyISAM to InnoDB.
- The table character set is converted to `utf8mb4`.

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
- Region shutdown and reload.
