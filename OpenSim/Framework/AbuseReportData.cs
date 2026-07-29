using System;
using OpenMetaverse;

namespace OpenSim.Framework
{
    public class AbuseReportData
    {
        public int ReportID; // Assigned by SQL.
        public UUID SenderID = UUID.Zero;
        public string SenderName = string.Empty;
        public int Time;

        public UUID AbuseRegionID = UUID.Zero;
        public string AbuseRegionName = string.Empty;
        public UUID AbuserID = UUID.Zero;
        public string AbuserName = string.Empty;
        public string Category = string.Empty;
        public int CheckFlags;
        public string Details = string.Empty;
        public UUID ObjectID = UUID.Zero;
        public string Position = string.Empty;
        public int ReportType;
        public string Summary = string.Empty;
        public string Version = string.Empty;
        public byte[] ImageData = Array.Empty<byte>();
    }
}
