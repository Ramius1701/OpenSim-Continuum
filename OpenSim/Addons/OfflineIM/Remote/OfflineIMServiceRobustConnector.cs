/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.ServiceAuth;
using OpenSim.Server.Handlers.Base;
using log4net;
using OpenMetaverse;

namespace OpenSim.OfflineIM
{
    public class OfflineIMServiceRobustConnector : ServiceConnector
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IOfflineIMService m_OfflineIMService;
        private string m_ConfigName = "Messaging";

        public OfflineIMServiceRobustConnector(IConfigSource config, IHttpServer server, string configName) :
            base(config, server, configName)
        {
            if (configName != String.Empty)
                m_ConfigName = configName;

            m_log.DebugFormat("[OfflineIM.V2.RobustConnector]: Starting with config name {0}", m_ConfigName);

            m_OfflineIMService = new OfflineIMService(config);

            IServiceAuth auth = ServiceAuth.Create(config, m_ConfigName);

            server.AddStreamHandler(new OfflineIMServicePostHandler(m_OfflineIMService, auth));
        }
    }

    public class OfflineIMServicePostHandler : BaseStreamHandler
    {
        private const int MaxRequestBodyBytes = 1024 * 1024;
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IOfflineIMService m_OfflineIMService;

        public OfflineIMServicePostHandler(IOfflineIMService service, IServiceAuth auth) :
            base("POST", "/offlineim", auth)
        {
            m_OfflineIMService = service;
        }

        protected override byte[] ProcessRequest(string path, Stream requestData,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            try
            {
                string body = ReadBoundedBody(requestData, httpRequest, MaxRequestBodyBytes).Trim();
                Dictionary<string, object> request =
                        ServerUtils.ParseQueryString(body);

                if (!request.ContainsKey("METHOD"))
                    return FailureResult();

                string method = request["METHOD"].ToString();
                request.Remove("METHOD");

                switch (method)
                {
                    case "GET":
                        return HandleGet(request);
                    case "STORE":
                        return HandleStore(request);
                    case "DELETE":
                        return HandleDelete(request);
                }
                m_log.DebugFormat("[OFFLINE IM HANDLER]: unknown method request: {0}", method);
            }
            catch (Exception e)
            {
                m_log.Error(string.Format("[OFFLINE IM HANDLER]: Exception {0} ", e.Message), e);
            }

            return FailureResult();
        }

        byte[] HandleStore(Dictionary<string, object> request)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            GridInstantMessage im = OfflineIMDataUtils.GridInstantMessage(request);

            string reason = string.Empty;

            bool success = m_OfflineIMService.StoreMessage(im, out reason);

            result["RESULT"] = success.ToString();
            if (!success)
                result["REASON"] = reason;

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleGet(Dictionary<string, object> request)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            if (!request.TryGetValue("PrincipalID", out object rawPrincipalID)
                || !UUID.TryParse(rawPrincipalID?.ToString(), out UUID principalID))
                NullResult(result, "Bad network data");
            else
            {
                List<GridInstantMessage> ims = m_OfflineIMService.GetMessages(principalID);

                Dictionary<string, object> dict = new Dictionary<string, object>();
                int i = 0;
                foreach (GridInstantMessage m in ims)
                    dict["im-" + i++] = OfflineIMDataUtils.GridInstantMessage(m);

                result["RESULT"] = dict;
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[XXX]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] HandleDelete(Dictionary<string, object> request)
        {
            if (!request.TryGetValue("UserID", out object rawUserID)
                || !UUID.TryParse(rawUserID?.ToString(), out UUID userID))
            {
                return FailureResult();
            }
            else
            {
                m_OfflineIMService.DeleteMessages(userID);

                return SuccessResult();
            }
        }

        #region Helpers

        private static string ReadBoundedBody(Stream input, IOSHttpRequest request, int maximumBytes)
        {
            if (input == null)
                throw new InvalidDataException("Request body is unavailable.");
            if (request != null && request.ContentLength64 > maximumBytes)
                throw new InvalidDataException("Request body exceeds the service limit.");

            using MemoryStream body = new MemoryStream();
            byte[] buffer = new byte[8192];
            int total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                    throw new InvalidDataException("Request body exceeds the service limit.");
                body.Write(buffer, 0, read);
            }
            return Encoding.UTF8.GetString(body.ToArray());
        }

        private void NullResult(Dictionary<string, object> result, string reason)
        {
            result["RESULT"] = "NULL";
            result["REASON"] = reason;
        }

        private byte[] FailureResult()
        {
            return BoolResult(false);
        }

        private byte[] SuccessResult()
        {
            return BoolResult(true);
        }

        private byte[] BoolResult(bool value)
        {
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration,
                    "", "");

            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse",
                    "");

            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "RESULT", "");
            result.AppendChild(doc.CreateTextNode(value.ToString()));

            rootElement.AppendChild(result);

            return Util.DocToBytes(doc);
        }

        #endregion
    }
}
