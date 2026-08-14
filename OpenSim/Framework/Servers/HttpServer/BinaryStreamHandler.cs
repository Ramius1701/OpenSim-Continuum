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
using System.IO;
using System.Net;
using System.Text;

namespace OpenSim.Framework.Servers.HttpServer
{
    public delegate string BinaryMethod(byte[] data, string path, string param);

    public class BinaryStreamHandler : BaseStreamHandler
    {
        private BinaryMethod m_method;
        private readonly int m_maximumRequestBytes;

        public BinaryStreamHandler(string httpMethod, string path, BinaryMethod binaryMethod)
            : this(httpMethod, path, binaryMethod, null, null) {}

        public BinaryStreamHandler(string httpMethod, string path, BinaryMethod binaryMethod, string name, string description)
            : this(httpMethod, path, binaryMethod, name, description, -1)
        {
        }

        public BinaryStreamHandler(string httpMethod, string path, BinaryMethod binaryMethod,
            string name, string description, int maximumRequestBytes)
            : base(httpMethod, path, name, description)
        {
            m_method = binaryMethod;
            m_maximumRequestBytes = maximumRequestBytes;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if (request == null ||
                (m_maximumRequestBytes >= 0 && httpRequest != null &&
                 httpRequest.ContentLength64 > m_maximumRequestBytes))
            {
                request?.Dispose();
                httpResponse.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                return Array.Empty<byte>();
            }

            byte[] data;
            using (request)
            using (MemoryStream ms = new MemoryStream())
            {
                if (request.CanSeek)
                    request.Seek(0, SeekOrigin.Begin);

                byte[] buffer = new byte[8192];
                int total = 0;
                int read;
                while ((read = request.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (m_maximumRequestBytes >= 0 && total > m_maximumRequestBytes)
                    {
                        httpResponse.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
                        return Array.Empty<byte>();
                    }
                    ms.Write(buffer, 0, read);
                }
                data = ms.ToArray();
            }

            string param = GetParam(path);
            string responseString = m_method(data, path, param);
            return Encoding.UTF8.GetBytes(responseString);
        }
    }
}
