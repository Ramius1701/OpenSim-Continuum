using OpenSim.Framework;

namespace OpenSim.Services.Interfaces
{
    public interface IAbuseReportsService
    {
        bool ReportAbuse(AbuseReportData report);
        AbuseReportData GetReport(int reportID, bool includeImage);
        AbuseReportData[] GetReports(int start, int count, string status);
        bool UpdateReport(int reportID, string status, string notes,
            OpenMetaverse.UUID moderatorID, string moderatorName);
    }
}
