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

using Nini.Config;
using log4net;
using System;
using System.Reflection;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using System.Collections.Generic;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.ServiceAuth;
using OpenMetaverse;

namespace OpenSim.Server.Handlers.UserAlias
{
    public class UserAliasServerPostHandler : BaseStreamHandler
    {
        private const int MaxRequestBodyBytes = 64 * 1024;
        private const int MaxAliasResults = 1000;
        private const int MaxDescriptionLength = 255;
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IUserAliasService m_UserAliasService;

        public UserAliasServerPostHandler(IUserAliasService service)
            : this(service, null, null) {}

        public UserAliasServerPostHandler(IUserAliasService service, IConfig config, IServiceAuth auth) :
                base("POST", "/useralias", auth)
        {
            m_UserAliasService = service;
        }

        protected override byte[] ProcessRequest(string path, Stream requestData,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            string body;
            try
            {
                body = ReadBoundedBody(requestData, httpRequest).Trim();
            }
            catch (InvalidDataException e)
            {
                m_log.WarnFormat("[USER ALIAS HANDLER]: Rejected request: {0}", e.Message);
                httpResponse.StatusCode = 413;
                return FailureResult();
            }

            // We need to check the authorization header
            //httpRequest.Headers["authorization"] ...

            string method = string.Empty;
            try
            {
                Dictionary<string, object> request = ServerUtils.ParseQueryString(body);

                if (!request.ContainsKey("METHOD"))
                    return FailureResult();

                method = request["METHOD"].ToString();

                switch (method)
                {
                    case "getuserforalias":
                        return GetUserForAlias(request);
                    case "getuseraliases":
                        return GetUserAliases(request);
                    case "createalias":
                        return CreateAlias(request);
                    case "deletealias":
                        return DeleteAlias(request);
                }

                m_log.DebugFormat("[USER SERVICE HANDLER]: unknown method request: {0}", method);
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[USER SERVICE HANDLER]: Exception in method {0}: {1}", method, e);
            }

            return FailureResult();
        }

        byte[] GetUserForAlias(Dictionary<string, object> request)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            if (request.TryGetValue("AliasID", out object otmp) && otmp != null)
            {
                if (UUID.TryParse(otmp.ToString(), out UUID aliasID))
                {
                    var alias = m_UserAliasService.GetUserForAlias(aliasID);
                    if (alias != null)
                    {
                        result["result"] = alias.ToKeyValuePairs();
                        return ResultToBytes(result);
                    }
                }
            }

            result["result"] = "null";
            return ResultToBytes(result);
        }

        byte[] GetUserAliases(Dictionary<string, object> request)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();

            if (request.TryGetValue("UserID", out object otmp) && otmp != null)
            {
                if (UUID.TryParse(otmp.ToString(), out UUID userID))
                {
                    var aliases = m_UserAliasService.GetUserAliases(userID);
                    if (aliases != null)
                    {
                        int i = 0;
                        foreach (Services.Interfaces.UserAlias alias in aliases)
                        {
                            if (i >= MaxAliasResults)
                                break;
                            Dictionary<string, object> rinfoDict = alias.ToKeyValuePairs();
                            result["alias" + i] = rinfoDict;
                            i++;
                        }

                        string xmlString = ServerUtils.BuildXmlResponse(result);
                        return Util.UTF8NoBomEncoding.GetBytes(xmlString);
                    }
                }
            }

            result["result"] = "null";
            return ResultToBytes(result);
        }

        private byte[] CreateAlias(Dictionary<string, object> request)
        {
            if (!TryGetUUID(request, "AliasID", out UUID aliasID) || aliasID == UUID.Zero ||
                !TryGetUUID(request, "UserID", out UUID userID) || userID == UUID.Zero)
            {
                return FailureResult();
            }

            string description = GetString(request, "Description");
            if (description.Length > MaxDescriptionLength)
                return FailureResult();

            Services.Interfaces.UserAlias alias =
                m_UserAliasService.CreateAlias(aliasID, userID, description);
            if (alias == null)
                return FailureResult();

            return ResultToBytes(new Dictionary<string, object>
            {
                ["result"] = alias.ToKeyValuePairs()
            });
        }

        private byte[] DeleteAlias(Dictionary<string, object> request)
        {
            bool deleted = TryGetUUID(request, "AliasID", out UUID aliasID) &&
                aliasID != UUID.Zero && m_UserAliasService.DeleteAlias(aliasID);
            return ResultToBytes(new Dictionary<string, object> { ["result"] = deleted });
        }

        private static bool TryGetUUID(
            Dictionary<string, object> request, string key, out UUID value)
        {
            value = UUID.Zero;
            return request.TryGetValue(key, out object raw) &&
                UUID.TryParse(raw?.ToString(), out value);
        }

        private static string GetString(Dictionary<string, object> request, string key)
        {
            return request.TryGetValue(key, out object value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
        }

        private static string ReadBoundedBody(Stream input, IOSHttpRequest request)
        {
            if (input == null)
                throw new InvalidDataException("Request body is unavailable.");
            if (request != null && request.ContentLength64 > MaxRequestBodyBytes)
                throw new InvalidDataException("Request body exceeds the service limit.");

            using MemoryStream body = new MemoryStream();
            byte[] buffer = new byte[8192];
            int total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > MaxRequestBodyBytes)
                    throw new InvalidDataException("Request body exceeds the service limit.");
                body.Write(buffer, 0, read);
            }
            return Encoding.UTF8.GetString(body.ToArray());
        }

        /*
        private byte[] SuccessResult()
        {
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration, "", "");
            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse", "");
            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "result", "");
            result.AppendChild(doc.CreateTextNode("Success"));

            rootElement.AppendChild(result);

            return Util.DocToBytes(doc);
        }
        */

        private static byte[] ResultFailureBytes = osUTF8.GetASCIIBytes("<?xml version =\"1.0\"?><ServerResponse><result>Failure</result></ServerResponse>");

        private byte[] FailureResult()
        {
            /*
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration, "", "");

            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse", "");

            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "result", "");
            result.AppendChild(doc.CreateTextNode("Failure"));

            rootElement.AppendChild(result);

            return Util.DocToBytes(doc);
            */
            return ResultFailureBytes;
        }

        private byte[] ResultToBytes(Dictionary<string, object> result)
        {
            string xmlString = ServerUtils.BuildXmlResponse(result);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }
    }
}
