using OpenSim.Framework;

namespace OpenSim.Data
{
    public interface IAbuseReportsData
    {
        bool Store(AbuseReportData data);
        AbuseReportData Get(int reportID, bool includeImage);
        AbuseReportData[] Get(int start, int count, string status);
        bool UpdateModeration(int reportID, string status, string notes,
            OpenMetaverse.UUID moderatorID, string moderatorName, int lastUpdated);
    }
}
