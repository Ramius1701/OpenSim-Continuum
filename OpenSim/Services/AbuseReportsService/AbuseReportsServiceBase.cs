using System;
using Nini.Config;
using OpenSim.Data;
using OpenSim.Services.Base;

namespace OpenSim.Services.AbuseReportsService
{
    public class AbuseReportsServiceBase : ServiceBase
    {
        protected IAbuseReportsData m_Database;

        public AbuseReportsServiceBase(IConfigSource config)
            : base(config)
        {
            string dllName = string.Empty;
            string connectionString = string.Empty;

            IConfig databaseConfig = config.Configs["DatabaseService"];
            if (databaseConfig != null)
            {
                dllName = databaseConfig.GetString("StorageProvider", string.Empty);
                connectionString = databaseConfig.GetString("ConnectionString", string.Empty);
            }

            IConfig serviceConfig = config.Configs["AbuseReportsService"];
            if (serviceConfig != null)
            {
                dllName = serviceConfig.GetString("StorageProvider", dllName);
                connectionString = serviceConfig.GetString("ConnectionString", connectionString);
            }

            if (string.IsNullOrWhiteSpace(dllName))
                throw new Exception("No StorageProvider configured for AbuseReportsService");

            m_Database = LoadPlugin<IAbuseReportsData>(dllName, new object[] { connectionString });
            if (m_Database == null)
                throw new Exception("Could not load IAbuseReportsData from " + dllName);
        }
    }
}
