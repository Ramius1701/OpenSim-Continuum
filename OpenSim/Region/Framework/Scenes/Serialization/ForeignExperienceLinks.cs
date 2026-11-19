/*
 * Copyright (c) Legion Builds
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

using System.IO;
using System.Text;
using System.Xml;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Region.Framework.Scenes.Serialization
{
    // Adapted from Tranquillity cab4048246. Sanitize before a foreign object
    // enters local storage; a saved YEngine state can otherwise restore its link.
    public static class ForeignExperienceLinks
    {
        private const int MaxXmlSize = 64 * 1024 * 1024;
        private const int JoinExperience = 0x2000;

        public static byte[] ClearObjectXml(byte[] data)
        {
            if (data is null || data.Length == 0 || data.Length > MaxXmlSize)
                return null;

            using var input = new MemoryStream(data, false);
            using XmlReader reader = XmlReader.Create(input, ReaderSettings());
            return TryClear(reader, out XmlDocument document, out bool changed)
                ? changed ? Encoding.UTF8.GetBytes(DocumentXml(document)) : data
                : null;
        }

        public static string ClearScriptState(string xml)
        {
            if (string.IsNullOrEmpty(xml) || xml.Length > MaxXmlSize)
                return null;
            using var input = new StringReader(xml);
            using XmlReader reader = XmlReader.Create(input, ReaderSettings());
            return TryClear(reader, out XmlDocument document, out bool changed)
                ? changed ? DocumentXml(document) : xml
                : null;
        }

        private static string DocumentXml(XmlDocument document)
        {
            if (document.FirstChild is XmlDeclaration declaration)
                declaration.Encoding = "utf-8";
            return document.OuterXml;
        }

        public static void ClearObject(SceneObjectGroup group)
        {
            group.AttachedExperienceID = UUID.Zero;
            foreach (SceneObjectPart part in group.Parts)
            {
                foreach (TaskInventoryItem item in part.Inventory.GetInventoryItems())
                {
                    if (item.ExperienceID != UUID.Zero || (item.PermsMask & JoinExperience) != 0)
                    {
                        item.PermsMask = 0;
                        item.PermsGranter = UUID.Zero;
                    }
                    item.ExperienceID = UUID.Zero;
                }
            }
        }

        private static XmlReaderSettings ReaderSettings() => new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxXmlSize
        };

        private static bool TryClear(XmlReader reader, out XmlDocument document, out bool changed)
        {
            document = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
            changed = false;
            try
            {
                document.Load(reader);
            }
            catch (XmlException)
            {
                return false;
            }

            string zero = UUID.Zero.ToString();
            foreach (XmlElement item in document.SelectNodes("//*[local-name()='TaskInventoryItem']"))
            {
                XmlNode mask = item.SelectSingleNode("./*[local-name()='PermsMask']");
                bool linked = false;
                foreach (XmlNode link in item.SelectNodes("./*[local-name()='ExperienceID']"))
                {
                    linked |= link.InnerText.Trim() != zero;
                    item.RemoveChild(link);
                    changed = true;
                }
                if (linked || (int.TryParse(mask?.InnerText, out int bits) && (bits & JoinExperience) != 0))
                {
                    if (mask is not null)
                        mask.InnerText = "0";
                    XmlNode granter = item.SelectSingleNode("./*[local-name()='PermsGranter']");
                    if (granter is not null)
                        granter.InnerText = zero;
                    changed = true;
                }
            }

            foreach (XmlElement state in document.SelectNodes("//*[local-name()='ScriptState']"))
            {
                bool linked = false;
                foreach (XmlNode key in state.SelectNodes("./*[local-name()='ExperienceKey']"))
                {
                    linked |= key.InnerText.Trim() != zero;
                    key.InnerText = zero;
                    changed = true;
                }
                foreach (XmlElement permissions in state.SelectNodes("./*[local-name()='Permissions']"))
                {
                    if (linked || (int.TryParse(permissions.GetAttribute("mask"), out int bits) && (bits & JoinExperience) != 0))
                    {
                        permissions.SetAttribute("mask", "0");
                        permissions.SetAttribute("granter", zero);
                        changed = true;
                    }
                }
            }
            return true;
        }
    }
}
