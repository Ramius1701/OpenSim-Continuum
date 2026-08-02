using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenSim.Continuum.Economy
{
    internal static class EconomySchemaResources
    {
        public static void Apply(IDbConnection connection, EconomyStorageProvider provider)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (connection.State != ConnectionState.Open)
                connection.Open();

            string marker = provider switch
            {
                EconomyStorageProvider.SQLite => ".sqlite_",
                EconomyStorageProvider.PostgreSql => ".pgsql_",
                _ => throw new NotSupportedException("MySQL uses its existing guarded migration sequence")
            };
            Assembly assembly = typeof(EconomySchemaResources).Assembly;
            string[] resources = assembly.GetManifestResourceNames()
                .Where(name => name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (resources.Length == 0)
                throw new InvalidOperationException($"No embedded {provider} economy migrations were found");

            using IDbTransaction transaction = connection.BeginTransaction();
            try
            {
                foreach (string resource in resources)
                {
                    using Stream stream = assembly.GetManifestResourceStream(resource) ??
                        throw new InvalidOperationException("Migration resource disappeared: " + resource);
                    using StreamReader reader = new(stream);
                    using IDbCommand command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = reader.ReadToEnd();
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
