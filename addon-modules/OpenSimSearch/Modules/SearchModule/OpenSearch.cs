using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Xml;
using OpenMetaverse;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Mono.Addins;

using DirFindFlags = OpenMetaverse.DirectoryManager.DirFindFlags;

[assembly: Addin("OpenSimSearch", OpenSim.VersionInfo.VersionNumber + "0.4")]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]
[assembly: AddinDescription("OpenSimSearch module.")]
[assembly: AddinAuthor("Unknown")]


namespace OpenSimSearch.Modules.OpenSearch
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "OpenSimSearch")]
    public class OpenSearchModule : ISearchModule, ISharedRegionModule
    {
        //
        // Log module
        //
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        //
        // Module vars
        //
        private readonly List<Scene> m_Scenes = new();
        private string m_SearchServer = "";
        private bool m_Enabled = true;
        private int m_RequestTimeoutMs = 5000;
        private int m_MaxConcurrentRequests = 8;
        private int m_ActiveRequests;

        #region IRegionModuleBase implementation
        public void Initialise(IConfigSource config)
        {
            IConfig searchConfig = config.Configs["Search"];

            if (searchConfig is null)
            {
                m_Enabled = false;
                return;
            }
            if (searchConfig.GetString("Module", "OpenSimSearch") != "OpenSimSearch")
            {
                m_Enabled = false;
                return;
            }

            m_SearchServer = searchConfig.GetString("SearchURL", "").Trim();
            if (!Uri.TryCreate(m_SearchServer, UriKind.Absolute, out Uri searchUri) ||
                (searchUri.Scheme != Uri.UriSchemeHttp && searchUri.Scheme != Uri.UriSchemeHttps))
            {
                m_log.Error("[SEARCH] SearchURL must be an absolute HTTP or HTTPS URL; module disabled");
                m_Enabled = false;
                return;
            }

            if (searchUri.Scheme != Uri.UriSchemeHttps)
                m_log.Warn("[SEARCH] SearchURL is not HTTPS; queries and results can be observed or modified in transit");

            m_RequestTimeoutMs = Math.Clamp(
                searchConfig.GetInt("RequestTimeoutMs", m_RequestTimeoutMs),
                1000,
                30000);
            m_MaxConcurrentRequests = Math.Clamp(
                searchConfig.GetInt("MaxConcurrentRequests", m_MaxConcurrentRequests),
                1,
                64);

            m_log.InfoFormat(
                "[SEARCH] OpenSimSearch module is active; endpoint {0}, timeout {1}ms, concurrency {2}",
                m_SearchServer,
                m_RequestTimeoutMs,
                m_MaxConcurrentRequests);
            m_Enabled = true;
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            bool added;
            lock(m_Scenes)
            {
                added = !m_Scenes.Contains(scene);
                if (added)
                    m_Scenes.Add(scene);
            }
            if (!added)
                return;

            scene.EventManager.OnNewClient += OnNewClient;
            scene.RegisterModuleInterface<ISearchModule>(this);
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            scene.UnregisterModuleInterface<ISearchModule>(this);

            scene.EventManager.OnNewClient -= OnNewClient;
            scene.ForEachClient(UnsubscribeClient);

            lock(m_Scenes)
            {
                m_Scenes.Remove(scene);
            }
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
            Scene[] scenes;
            lock (m_Scenes)
                scenes = m_Scenes.ToArray();
            foreach (Scene scene in scenes)
                RemoveRegion(scene);
        }

        public string Name
        {
            get { return "OpenSimSearch"; }
        }

        public bool IsSharedModule
        {
            get { return true; }
        }
        #endregion

        /// New Client Event Handler
        private void OnNewClient(IClientAPI client)
        {
            client.OnDirPlacesQuery += OnDirPlacesQuery;
            client.OnDirFindQuery += OnDirFindQuery;
            client.OnDirPopularQuery += OnDirPopularQuery;
            client.OnDirLandQuery += OnDirLandQuery;
            client.OnDirClassifiedQuery += OnDirClassifiedQuery;
            client.OnEventInfoRequest += OnEventInfoRequest;
            client.OnClassifiedInfoRequest += OnClassifiedInfoRequest;
            client.OnMapItemRequest += OnMapItemRequest;
        }

        private void UnsubscribeClient(IClientAPI client)
        {
            client.OnDirPlacesQuery -= OnDirPlacesQuery;
            client.OnDirFindQuery -= OnDirFindQuery;
            client.OnDirPopularQuery -= OnDirPopularQuery;
            client.OnDirLandQuery -= OnDirLandQuery;
            client.OnDirClassifiedQuery -= OnDirClassifiedQuery;
            client.OnEventInfoRequest -= OnEventInfoRequest;
            client.OnClassifiedInfoRequest -= OnClassifiedInfoRequest;
            client.OnMapItemRequest -= OnMapItemRequest;
        }

        private void OnDirPlacesQuery(IClientAPI remote, UUID query, string text, int flags, int category, string sim, int start) =>
            ExecuteSearchRequest(remote, "places", () => DirPlacesQuery(remote, query, text, flags, category, sim, start));
        private void OnDirFindQuery(IClientAPI remote, UUID query, string text, uint flags, int start) =>
            ExecuteSearchRequest(remote, "directory", () => DirFindQuery(remote, query, text, flags, start));
        private void OnDirPopularQuery(IClientAPI remote, UUID query, uint flags) =>
            ExecuteSearchRequest(remote, "popular", () => DirPopularQuery(remote, query, flags));
        private void OnDirLandQuery(IClientAPI remote, UUID query, uint flags, uint type, int price, int area, int start) =>
            ExecuteSearchRequest(remote, "land", () => DirLandQuery(remote, query, flags, type, price, area, start));
        private void OnDirClassifiedQuery(IClientAPI remote, UUID query, string text, uint flags, uint category, int start) =>
            ExecuteSearchRequest(remote, "classifieds", () => DirClassifiedQuery(remote, query, text, flags, category, start));
        private void OnEventInfoRequest(IClientAPI remote, uint eventID) =>
            ExecuteSearchRequest(remote, "event details", () => EventInfoRequest(remote, eventID));
        private void OnClassifiedInfoRequest(UUID classifiedID, IClientAPI remote) =>
            ExecuteSearchRequest(remote, "classified details", () => ClassifiedInfoRequest(classifiedID, remote));
        private void OnMapItemRequest(IClientAPI remote, uint flags, uint estate, bool godlike, uint type, ulong handle) =>
            ExecuteSearchRequest(remote, "map items", () => HandleMapItemRequest(remote, flags, estate, godlike, type, handle));

        private void ExecuteSearchRequest(IClientAPI client, string operation, Action request)
        {
            int activeRequests = Interlocked.Increment(ref m_ActiveRequests);
            if (activeRequests > m_MaxConcurrentRequests)
            {
                Interlocked.Decrement(ref m_ActiveRequests);
                m_log.WarnFormat(
                    "[SEARCH]: Rejected {0} request because {1} backend requests are already active",
                    operation,
                    m_MaxConcurrentRequests);
                client.SendAgentAlertMessage("Search is busy. Please try again.", false);
                return;
            }

            // An external search service may consume the complete timeout. Keep
            // that wait off the simulator client-event thread, while bounding
            // work so a failed backend cannot exhaust the shared worker pool.
            Util.FireAndForget(_ =>
            {
                try
                {
                    request();
                }
                catch (Exception e)
                {
                    m_log.WarnFormat("[SEARCH]: Rejected malformed {0} response: {1}", operation, e.Message);
                    client.SendAgentAlertMessage("Unable to search at this time.", false);
                }
                finally
                {
                    Interlocked.Decrement(ref m_ActiveRequests);
                }
            }, null, $"OpenSimSearch.{operation}");
        }

        //
        // Make external XMLRPC request
        //
        private Hashtable GenericXMLRPCRequest(Hashtable ReqParams, string method)
        {
            ArrayList SendParams = new()
            {
                ReqParams
            };

            // Send Request
            XmlRpcResponse Resp;
            try
            {
                XmlRpcRequest Req = new(method, SendParams);
                Resp = Req.Send(m_SearchServer, m_RequestTimeoutMs);
            }
            catch (WebException ex)
            {
                m_log.ErrorFormat("[SEARCH]: Unable to connect to Search " +
                        "Server {0}.  Exception {1}", m_SearchServer, ex);

                return ErrorResponse();
            }
            catch (SocketException ex)
            {
                m_log.ErrorFormat(
                        "[SEARCH]: Unable to connect to Search Server {0}. " +
                        "Exception {1}", m_SearchServer, ex);

                return ErrorResponse();
            }
            catch (XmlException ex)
            {
                m_log.ErrorFormat(
                        "[SEARCH]: Unable to connect to Search Server {0}. " +
                        "Exception {1}", m_SearchServer, ex);

                return ErrorResponse();
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat(
                    "[SEARCH]: Invalid or failed {0} response from Search Server {1}: {2}",
                    method,
                    m_SearchServer,
                    ex);
                return ErrorResponse();
            }
            if (Resp == null || Resp.IsFault)
                return ErrorResponse();
            if (Resp.Value is not Hashtable respData)
            {
                m_log.ErrorFormat(
                    "[SEARCH]: Search Server {0} returned an invalid {1} response",
                    m_SearchServer,
                    method);
                return ErrorResponse();
            }

            if (!TryGetBoolean(respData, "success", out bool success) || !success)
                return ErrorResponse();
            if (respData["data"] is not ArrayList responseData)
            {
                m_log.ErrorFormat(
                    "[SEARCH]: Search Server {0} returned invalid data for {1}",
                    m_SearchServer,
                    method);
                return ErrorResponse();
            }

            // Viewer directory replies use at most 100 entries plus a paging
            // sentinel. Never allow an external backend to drive unbounded
            // per-request iteration or allocation in the simulator.
            if (responseData.Count > 101)
            {
                ArrayList bounded = new(101);
                for (int i = 0; i < 101; ++i)
                    bounded.Add(responseData[i]);
                respData["data"] = bounded;
            }

            respData["success"] = true;
            return respData;
        }

        private static bool TryGetBoolean(Hashtable data, string key, out bool value)
        {
            value = false;
            if (data == null || !data.ContainsKey(key) || data[key] == null)
                return false;
            try
            {
                value = Convert.ToBoolean(data[key], CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        private static Hashtable ErrorResponse()
        {
            return new Hashtable
            {
                ["success"] = false,
                ["errorMessage"] = "Unable to search at this time. ",
                ["errorURI"] = ""
            };
        }

        protected void DirPlacesQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, int queryFlags, int category, string simName,
                int queryStart)
        {
            Hashtable ReqHash = new()
            {
                ["text"] = queryText,
                ["flags"] = queryFlags.ToString(),
                ["category"] = category.ToString(),
                ["sim_name"] = simName,
                ["query_start"] = queryStart.ToString()
            };

            Hashtable result = GenericXMLRPCRequest(ReqHash, "dir_places_query");

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];

            int count = (dataArray.Count > 100) ? 101 : dataArray.Count;

            DirPlacesReplyData[] data = new DirPlacesReplyData[count];

            int i = 0;

            foreach (Object o in dataArray)
            {
                Hashtable d = (Hashtable)o;

                data[i] = new DirPlacesReplyData
                {
                    parcelID = new UUID(d["parcel_id"].ToString()),
                    name = d["name"].ToString(),
                    forSale = Convert.ToBoolean(d["for_sale"]),
                    auction = Convert.ToBoolean(d["auction"]),
                    dwell = Convert.ToSingle(d["dwell"])
                };

                if (++i >= count)
                    break;
            }

            remoteClient.SendDirPlacesReply(queryID, data);
        }

        public void DirPopularQuery(IClientAPI remoteClient, UUID queryID, uint queryFlags)
        {
            Hashtable ReqHash = new()
            {
                ["flags"] = queryFlags.ToString()
            };

            Hashtable result = GenericXMLRPCRequest(ReqHash, "dir_popular_query");

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];

            int count = (dataArray.Count > 100) ? 101 : dataArray.Count;

            DirPopularReplyData[] data = new DirPopularReplyData[count];

            int i = 0;

            foreach (Object o in dataArray)
            {
                Hashtable d = (Hashtable)o;

                data[i] = new DirPopularReplyData
                {
                    parcelID = new UUID(d["parcel_id"].ToString()),
                    name = d["name"].ToString(),
                    dwell = Convert.ToSingle(d["dwell"])
                };

                if (++i >= count)
                    break;
            }

            remoteClient.SendDirPopularReply(queryID, data);
        }

        public void DirLandQuery(IClientAPI remoteClient, UUID queryID,
                uint queryFlags, uint searchType, int price, int area,
                int queryStart)
        {
            Hashtable ReqHash = new()
            {
                ["flags"] = queryFlags.ToString(),
                ["type"] = searchType.ToString(),
                ["price"] = price.ToString(),
                ["area"] = area.ToString(),
                ["query_start"] = queryStart.ToString()
            };

            Hashtable result = GenericXMLRPCRequest(ReqHash, "dir_land_query");

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];
            int count = 0;

            /* Count entries in dataArray with valid region name to */
            /* prevent allocating data array with too many entries. */
            foreach (Object o in dataArray)
            {
                Hashtable d = (Hashtable)o;

                if (d["name"] is not null)
                    ++count;
            }

            count = (count > 100) ? 101 : count;

            DirLandReplyData[] data = new DirLandReplyData[count];

            int i = 0;

            foreach (Object o in dataArray)
            {
                Hashtable d = (Hashtable)o;

                if (d["name"] is null)
                    continue;

                data[i] = new DirLandReplyData
                {
                    parcelID = new UUID(d["parcel_id"].ToString()),
                    name = d["name"].ToString(),
                    auction = Convert.ToBoolean(d["auction"]),
                    forSale = Convert.ToBoolean(d["for_sale"]),
                    salePrice = Convert.ToInt32(d["sale_price"]),
                    actualArea = Convert.ToInt32(d["area"])
                };

                if (++i >= count)
                    break;
            }

            remoteClient.SendDirLandReply(queryID, data);
        }

        public void DirFindQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, uint queryFlags, int queryStart)
        {
            if (((DirFindFlags)queryFlags & DirFindFlags.DateEvents) == DirFindFlags.DateEvents)
            {
                DirEventsQuery(remoteClient, queryID, queryText, queryFlags,
                        queryStart);
                return;
            }
        }

        public void DirEventsQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, uint queryFlags, int queryStart)
        {
            Hashtable ReqHash = new()
            {
                ["text"] = queryText,
                ["flags"] = queryFlags.ToString(),
                ["query_start"] = queryStart.ToString()
            };

            Hashtable result = GenericXMLRPCRequest(ReqHash, "dir_events_query");

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];

            int count = (dataArray.Count > 100) ? 101 : dataArray.Count;

            DirEventsReplyData[] data = new DirEventsReplyData[count];

            int i = 0;

            foreach (Object o in dataArray)
            {
                Hashtable d = (Hashtable)o;

                data[i] = new DirEventsReplyData
                {
                    ownerID = new UUID(d["owner_id"].ToString()),
                    name = d["name"].ToString(),
                    eventID = Convert.ToUInt32(d["event_id"]),
                    date = d["date"].ToString(),
                    unixTime = Convert.ToUInt32(d["unix_time"]),
                    eventFlags = Convert.ToUInt32(d["event_flags"])
                };

                if (++i >= count)
                    break;
            }

            remoteClient.SendDirEventsReply(queryID, data);
        }

        public void DirClassifiedQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, uint queryFlags, uint category,
                int queryStart)
        {
            Hashtable ReqHash = new()
            {
                ["text"] = queryText,
                ["flags"] = queryFlags.ToString(),
                ["category"] = category.ToString(),
                ["query_start"] = queryStart.ToString()
            };

            Hashtable result = GenericXMLRPCRequest(ReqHash, "dir_classified_query");

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];

            int count = (dataArray.Count > 100) ? 101 : dataArray.Count;

            DirClassifiedReplyData[] data = new DirClassifiedReplyData[count];

            int i = 0;

            foreach (Object o in dataArray)
            {
                Hashtable d = (Hashtable)o;

                data[i] = new DirClassifiedReplyData
                {
                    classifiedID = new UUID(d["classifiedid"].ToString()),
                    name = d["name"].ToString(),
                    classifiedFlags = Convert.ToByte(d["classifiedflags"]),
                    creationDate = Convert.ToUInt32(d["creation_date"]),
                    expirationDate = Convert.ToUInt32(d["expiration_date"]),
                    price = Convert.ToInt32(d["priceforlisting"])
                };

                if (++i >= count)
                    break;
            }

            remoteClient.SendDirClassifiedReply(queryID, data);
        }

        public void EventInfoRequest(IClientAPI remoteClient, uint queryEventID)
        {
            Hashtable ReqHash = new()
            {
                ["eventID"] = queryEventID.ToString()
            };

            Hashtable result = GenericXMLRPCRequest(ReqHash, "event_info_query");

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["data"];
            if (dataArray.Count == 0)
            {
                // something bad happened here, if we could return an
                // event after the search,
                // we should be able to find it here
                // TODO do some (more) sensible error-handling here
                remoteClient.SendAgentAlertMessage("Couldn't find this event.",
                        false);
                return;
            }

            Hashtable d = (Hashtable)dataArray[0];
            EventData data = new()
            {
                eventID = Convert.ToUInt32(d["event_id"]),
                creator = d["creator"].ToString(),
                name = d["name"].ToString(),
                category = d["category"].ToString(),
                description = d["description"].ToString(),
                date = d["date"].ToString(),
                dateUTC = Convert.ToUInt32(d["dateUTC"]),
                duration = Convert.ToUInt32(d["duration"]),
                cover = Convert.ToUInt32(d["covercharge"]),
                amount = Convert.ToUInt32(d["coveramount"]),
                simName = d["simname"].ToString()
            };
            Vector3.TryParse(d["globalposition"].ToString(), out data.globalPos);
            data.eventFlags = Convert.ToUInt32(d["eventflags"]);

            remoteClient.SendEventInfoReply(data);
        }

        public void ClassifiedInfoRequest(UUID queryClassifiedID, IClientAPI remoteClient)
        {
            Hashtable ReqHash = new()
            {
                ["classifiedID"] = queryClassifiedID.ToString()
            };

            Hashtable result = GenericXMLRPCRequest(ReqHash, "classifieds_info_query");

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(result["errorMessage"].ToString(), false);
                return;
            }

            //The viewer seems to issue an info request even when it is
            //creating a new classified which means the data hasn't been
            //saved to the database yet so there is no info to find.
            ArrayList dataArray = (ArrayList)result["data"];
            if (dataArray.Count == 0)
            {
                // Something bad happened here if we could not return an
                // event after the search. We should be able to find it here.
                // TODO do some (more) sensible error-handling here
//                remoteClient.SendAgentAlertMessage("Couldn't find data for classified ad.",
//                        false);
                return;
            }

            Hashtable d = (Hashtable)dataArray[0];

            Vector3 globalPos = new();
            Vector3.TryParse(d["posglobal"].ToString(), out globalPos);

            remoteClient.SendClassifiedInfoReply(
                    new UUID(d["classifieduuid"].ToString()),
                    new UUID(d["creatoruuid"].ToString()),
                    Convert.ToUInt32(d["creationdate"]),
                    Convert.ToUInt32(d["expirationdate"]),
                    Convert.ToUInt32(d["category"]),
                    d["name"].ToString(),
                    d["description"].ToString(),
                    new UUID(d["parceluuid"].ToString()),
                    Convert.ToUInt32(d["parentestate"]),
                    new UUID(d["snapshotuuid"].ToString()),
                    d["simname"].ToString(),
                    globalPos,
                    d["parcelname"].ToString(),
                    Convert.ToByte(d["classifiedflags"]),
                    Convert.ToInt32(d["priceforlisting"]));
        }

        public void HandleMapItemRequest(IClientAPI remoteClient, uint flags,
                                         uint EstateID, bool godlike,
                                         uint itemtype, ulong regionhandle)
        {
            //The following constant appears to be from GridLayerType enum
            //defined in OpenMetaverse/GridManager.cs of libopenmetaverse.
            if (itemtype == (uint)OpenMetaverse.GridItemType.LandForSale)
            {
                Hashtable ReqHash = new()
                {
                    //The flags are: SortAsc (1 << 15), PerMeterSort (1 << 17)
                    ["flags"] = "163840",
                    ["type"] = "4294967295", //This is -1 in 32 bits
                    ["price"] = "0",
                    ["area"] = "0",
                    ["query_start"] = "0"
                };

                Hashtable result = GenericXMLRPCRequest(ReqHash, "dir_land_query");

                if (!Convert.ToBoolean(result["success"]))
                {
                    remoteClient.SendAgentAlertMessage(result["errorMessage"].ToString(), false);
                    return;
                }

                ArrayList dataArray = (ArrayList)result["data"];

                List<mapItemReply> mapitems = new();
                Scene[] scenes;
                lock (m_Scenes)
                    scenes = m_Scenes.ToArray();
                Scene searchScene = scenes.Length == 0 ? null : scenes[0];

                foreach (Object o in dataArray)
                {
                    Hashtable d = (Hashtable)o;

                    if (d["name"] is null)
                        continue;

                    mapItemReply mapitem = new();
                    if (!TryResolveLandMapPosition(d, scenes, searchScene, out mapitem.x, out mapitem.y))
                        continue;

                    mapitem.id = new UUID(d["parcel_id"].ToString());
                    mapitem.Extra = Convert.ToInt32(d["area"]);
                    mapitem.Extra2 = Convert.ToInt32(d["sale_price"]);
                    mapitem.name = d["name"].ToString();

                    mapitems.Add(mapitem);
                }

                remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
                mapitems.Clear();
            }

            if (itemtype == (uint)OpenMetaverse.GridItemType.PgEvent ||
                itemtype == (uint)OpenMetaverse.GridItemType.MatureEvent ||
                itemtype == (uint)OpenMetaverse.GridItemType.AdultEvent)
            {

                //Find the maturity level
                int maturity = (1 << 24);

                //Find the maturity level
                if (itemtype == (uint)OpenMetaverse.GridItemType.MatureEvent)
                    maturity = (1 << 25);
                else
                {
                    if (itemtype == (uint)OpenMetaverse.GridItemType.AdultEvent)
                        maturity = (1 << 26);
                }

                //The flags are: SortAsc (1 << 15), PerMeterSort (1 << 17)
                maturity |= 163840;

                //When character before | is a u get upcoming/in-progress events
                //Character before | is number of days before/after current date
                //Characters after | is the number for a category
                Hashtable ReqHash = new()
                {
                    ["text"] = "u|0",
                    ["flags"] = maturity.ToString(),
                    ["query_start"] = "0"
                };

                Hashtable result = GenericXMLRPCRequest(ReqHash, "dir_events_query");

                if (!Convert.ToBoolean(result["success"]))
                {
                    remoteClient.SendAgentAlertMessage(result["errorMessage"].ToString(), false);
                    return;
                }

                ArrayList dataArray = (ArrayList)result["data"];

                List<mapItemReply> mapitems = new();
                int event_id;
                string[] landingpoint;

                foreach (Object o in dataArray)
                {
                    Hashtable d = (Hashtable)o;

                    if (d["name"] is null)
                        continue;

                    mapItemReply mapitem = new();

                    //Events use a comma separator in the landing point
                    landingpoint = d["landing_point"].ToString().Split(',');
                    mapitem.x = Convert.ToUInt32(landingpoint[0]);
                    mapitem.y = Convert.ToUInt32(landingpoint[1]);

                    //This is a crazy way to pass the event ID back to the
                    //viewer but that is the way it wants the information.
                    event_id = Convert.ToInt32(d["event_id"]);
                    mapitem.id = new UUID("00000000-0000-0000-0000-0000" +
                                            event_id.ToString("X8"));

                    mapitem.Extra = Convert.ToInt32(d["unix_time"]);
                    mapitem.Extra2 = 0; //FIXME: No idea what to do here
                    mapitem.name = d["name"].ToString();

                    mapitems.Add(mapitem);
                }

                remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
                mapitems.Clear();
            }
        }

        private static bool TryResolveLandMapPosition(Hashtable data, Scene[] scenes,
            Scene searchScene, out uint globalX, out uint globalY)
        {
            globalX = 0;
            globalY = 0;
            if (data["region_UUID"] == null || data["landing_point"] == null ||
                !UUID.TryParse(data["region_UUID"].ToString(), out UUID regionID) || regionID == UUID.Zero)
                return false;

            string[] landing = data["landing_point"].ToString().Split('/');
            if (landing.Length < 2 ||
                !Decimal.TryParse(landing[0], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal localX) ||
                !Decimal.TryParse(landing[1], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal localY) ||
                localX < 0 || localY < 0)
                return false;

            decimal baseX = -1;
            decimal baseY = -1;
            foreach (Scene scene in scenes)
            {
                if (scene.RegionInfo.RegionID == regionID)
                {
                    baseX = scene.RegionInfo.WorldLocX;
                    baseY = scene.RegionInfo.WorldLocY;
                    break;
                }
            }

            if (baseX < 0 && searchScene?.GridService != null)
            {
                try
                {
                    OpenSim.Services.Interfaces.GridRegion region = searchScene.GridService.GetRegionByUUID(
                        searchScene.RegionInfo.ScopeID, regionID);
                    if (region != null)
                    {
                        baseX = region.RegionLocX;
                        baseY = region.RegionLocY;
                    }
                }
                catch (Exception e)
                {
                    m_log.WarnFormat("[SEARCH]: Unable to resolve region {0} for a land map result: {1}",
                        regionID, e.Message);
                }
            }

            decimal x = baseX + localX;
            decimal y = baseY + localY;
            if (baseX < 0 || baseY < 0 || x < 0 || y < 0 || x > UInt32.MaxValue || y > UInt32.MaxValue)
                return false;

            globalX = Decimal.ToUInt32(x);
            globalY = Decimal.ToUInt32(y);
            return true;
        }

        public void Refresh()
        {
        }
    }
}
