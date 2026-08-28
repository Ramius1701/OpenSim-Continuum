using System;
using System.Collections;
using System.Net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers.HttpServer;

namespace ContinuumSearch.Service
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "self-test")
                {
                    if (args.Length == 2)
                        return SearchAcceptanceSuite.Run("SQLite", "Data Source=" + args[1] + ";Version=3;");
                    return SearchAcceptanceSuite.Run(
                        Environment.GetEnvironmentVariable("CONTINUUM_SEARCH_STORAGE_PROVIDER"),
                        Environment.GetEnvironmentVariable("CONTINUUM_SEARCH_CONNECTION_STRING"));
                }
                string ini = args.Length > 0 ? args[0] : "ContinuumSearch.ini";
                IniConfigSource source = new(ini);
                IConfig service = source.Configs["ContinuumSearchService"] ??
                    throw new InvalidOperationException("[ContinuumSearchService] is required");
                IConfig database = source.Configs["Database"] ??
                    throw new InvalidOperationException("[Database] is required");

                using SearchStore store = SearchStore.Open(
                    database.GetString("StorageProvider", String.Empty),
                    database.GetString("ConnectionString", String.Empty));
                store.EnsureSchema();

                MainConsole.Instance = new LocalConsole("ContinuumSearch ");
                uint port = checked((uint)service.GetInt("Port", 8010));
                BaseHttpServer server = new(port);
                SearchRpc rpc = new(store);
                rpc.Register(server);
                using SnapshotCrawler crawler = new(store, service);
                server.AddSimpleStreamHandler(new SimpleStreamHandler(
                    service.GetString("RegistrationPath", "/search/register"), crawler.Register));
                server.Start();
                crawler.Start();
                Console.WriteLine("ContinuumSearch.Service listening on port {0}. Press Ctrl+C to stop.", port);
                using System.Threading.ManualResetEventSlim stop = new(false);
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
                stop.Wait();
                crawler.Stop();
                server.Stop();
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ContinuumSearch.Service failed: {0}", e);
                return 1;
            }
        }
    }

    internal sealed class SearchRpc
    {
        private readonly SearchStore m_store;
        internal SearchRpc(SearchStore store) { m_store = store; }

        internal void Register(BaseHttpServer server)
        {
            server.AddXmlRPCHandler("dir_places_query", Places);
            server.AddXmlRPCHandler("dir_popular_query", Popular);
            server.AddXmlRPCHandler("parcel_info_query", ParcelInfo);
            server.AddXmlRPCHandler("region_parcels_query", RegionParcels);
            server.AddXmlRPCHandler("dir_land_query", Land);
            server.AddXmlRPCHandler("dir_events_query", Events);
            server.AddXmlRPCHandler("dir_classified_query", Classifieds);
            server.AddXmlRPCHandler("event_info_query", EventInfo);
            server.AddXmlRPCHandler("classifieds_info_query", ClassifiedInfo);
            server.AddXmlRPCHandler("continuum_search_health", Health);
        }

        private XmlRpcResponse Places(XmlRpcRequest request, IPEndPoint remote) => Invoke(() => m_store.FindPlaces(Args(request)));
        private XmlRpcResponse Popular(XmlRpcRequest request, IPEndPoint remote) => Invoke(() => m_store.FindPopular(Args(request)));
        private XmlRpcResponse ParcelInfo(XmlRpcRequest request, IPEndPoint remote) => Invoke(() => m_store.GetParcel(Args(request)));
        private XmlRpcResponse RegionParcels(XmlRpcRequest request, IPEndPoint remote) => Invoke(() => m_store.GetRegionParcels(Args(request)));
        private XmlRpcResponse Land(XmlRpcRequest request, IPEndPoint remote) => Invoke(() => m_store.FindLand(Args(request)));
        private XmlRpcResponse Events(XmlRpcRequest request, IPEndPoint remote) => Invoke(() => m_store.FindEvents(Args(request)));
        private XmlRpcResponse Classifieds(XmlRpcRequest request, IPEndPoint remote) => Invoke(() => m_store.FindClassifieds(Args(request)));
        private XmlRpcResponse EventInfo(XmlRpcRequest request, IPEndPoint remote) => Invoke(() => m_store.GetEvent(Args(request)));
        private XmlRpcResponse ClassifiedInfo(XmlRpcRequest request, IPEndPoint remote) => Invoke(() => m_store.GetClassified(Args(request)));
        private XmlRpcResponse Health(XmlRpcRequest request, IPEndPoint remote) => Reply(new ArrayList
        {
            new Hashtable { ["service"] = "ContinuumSearch.Service", ["provider"] = m_store.Provider }
        });

        private static Hashtable Args(XmlRpcRequest request) =>
            request?.Params?.Count > 0 && request.Params[0] is Hashtable values ? values : new Hashtable();

        private static XmlRpcResponse Reply(ArrayList data) => new()
        {
            Value = new Hashtable { ["success"] = true, ["errorMessage"] = String.Empty, ["data"] = data }
        };

        private static XmlRpcResponse Invoke(Func<ArrayList> query)
        {
            try { return Reply(query()); }
            catch (Exception e)
            {
                Console.Error.WriteLine("ContinuumSearch query failed: {0}", e.Message);
                return new XmlRpcResponse
                {
                    Value = new Hashtable
                    {
                        ["success"] = false,
                        ["errorMessage"] = "Unable to search at this time.",
                        ["errorURI"] = String.Empty
                    }
                };
            }
        }
    }
}
