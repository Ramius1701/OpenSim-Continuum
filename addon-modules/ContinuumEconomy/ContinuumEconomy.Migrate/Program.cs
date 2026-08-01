using System;
using OpenSim.Continuum.Economy;

namespace ContinuumEconomy.Migrate
{
    internal static class Program
    {
        private const string ConnectionVariable = "CONTINUUM_ECONOMY_CONNECTION_STRING";
        private const string ImportConfirmation = "--confirm=IMPORT-LEGACY-MONEYSERVER";
        private const string SchemaConfirmation = "--confirm=CREATE-CONTINUUM-ECONOMY-SCHEMA";

        private static int Main(string[] args)
        {
            if (args.Length == 0 || (args[0] != "analyze" && args[0] != "initialize" && args[0] != "import"))
            {
                Usage();
                return 2;
            }

            string connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
            if (String.IsNullOrWhiteSpace(connectionString))
            {
                Console.Error.WriteLine("The {0} environment variable is required; credentials are never accepted as command arguments.", ConnectionVariable);
                return 2;
            }

            try
            {
                if (args[0] == "initialize")
                {
                    if (Array.IndexOf(args, SchemaConfirmation) < 0)
                    {
                        Console.Error.WriteLine("Schema initialization refused without the literal confirmation flag.");
                        Usage();
                        return 2;
                    }

                    new MySqlEconomyLedger(connectionString).EnsureSchema();
                    Console.WriteLine("ContinuumEconomy schema is initialized. Legacy MoneyServer tables were not altered.");
                    return 0;
                }

                LegacyMoneyServerImporter importer = new(connectionString);
                if (args[0] == "analyze")
                {
                    Print(importer.Analyze());
                    return 0;
                }

                if (Array.IndexOf(args, "--moneyserver-stopped") < 0 ||
                    Array.IndexOf(args, "--database-snapshot-complete") < 0 ||
                    Array.IndexOf(args, ImportConfirmation) < 0)
                {
                    Console.Error.WriteLine("Import refused. Stop MoneyServer, snapshot the database, and provide all three confirmation flags.");
                    Usage();
                    return 2;
                }

                LegacyImportReport report = importer.Analyze();
                Print(report);
                if (report.InvalidAccountCount != 0 || report.TargetAccountCount != 0)
                {
                    Console.Error.WriteLine("Import refused by preflight analysis.");
                    return 3;
                }

                Print(importer.Import());
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ContinuumEconomy migration failed: {0}", e.Message);
                return 1;
            }
        }

        private static void Print(LegacyImportReport report)
        {
            Console.WriteLine("Legacy accounts:       {0}", report.LegacyAccountCount);
            Console.WriteLine("Legacy balance total:  {0}", report.LegacyBalanceTotal);
            Console.WriteLine("Legacy history rows:   {0}", report.LegacyTransactionCount);
            Console.WriteLine("Invalid accounts:      {0}", report.InvalidAccountCount);
            Console.WriteLine("Target accounts:       {0}", report.TargetAccountCount);
            Console.WriteLine("Reconcile mismatches:  {0}", report.ReconciliationMismatchCount);
            if (report.Imported)
                Console.WriteLine("Import committed:      {0}", report.ImportID);
        }

        private static void Usage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  ContinuumEconomy.Migrate analyze");
            Console.Error.WriteLine("  ContinuumEconomy.Migrate initialize {0}", SchemaConfirmation);
            Console.Error.WriteLine("  ContinuumEconomy.Migrate import --moneyserver-stopped --database-snapshot-complete {0}", ImportConfirmation);
        }
    }
}
