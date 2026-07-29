using OpenSim.Framework;

namespace OpenSim.Data
{
    public interface IAbuseReportsData
    {
        bool Store(AbuseReportData data);
    }
}
