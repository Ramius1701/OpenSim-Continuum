/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenSim.Services.Interfaces;

namespace OpenSim.Continuum.WebUI
{
    public sealed class WebUIPortalSettings
    {
        public int Revision { get; set; }
        public string Heading { get; set; } = "A world worth making your own.";
        public string Introduction { get; set; } = "Meet your community, discover new places and bring your ideas to life.";
        public string NoticeTitle { get; set; } = "";
        public string NoticeText { get; set; } = "";
        public bool NoticeEnabled { get; set; }
        public string SupportText { get; set; } = "Contact your grid operator for help with your account.";
        public bool ShowSlideshow { get; set; } = true;
        public string[] HiddenNavigation { get; set; } = Array.Empty<string>();
    }

    /// <summary>Portal presentation only. Native service policy and authorization remain authoritative.</summary>
    public sealed class WebUIPortalSettingsStore
    {
        public static readonly string[] Navigation = { "news", "search", "world_map", "destinations", "events", "classifieds", "experiences", "user_search", "region_search" };
        private readonly string _path;
        private readonly object _gate = new();
        public WebUIPortalSettingsStore(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("PortalSettingsPath is required");
            _path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            Read();
        }
        public WebUIPortalSettings Read()
        {
            lock (_gate)
            {
                if (!File.Exists(_path)) return new WebUIPortalSettings();
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 32768) throw new InvalidDataException("Portal settings exceed their storage bound");
                WebUIPortalSettings value;
                try { value = JsonSerializer.Deserialize<WebUIPortalSettings>(stream); }
                catch (JsonException e) { throw new InvalidDataException("Invalid portal settings", e); }
                if (value == null || value.Revision < 1) throw new InvalidDataException("Invalid portal settings revision");
                try { Validate(value); }
                catch (ArgumentException e) { throw new InvalidDataException("Invalid portal settings", e); }
                return value;
            }
        }
        private static void Validate(WebUIPortalSettings value)
        {
            bool Text(string text, int maximum, bool required = false) => text != null && text.Length <= maximum
                && (!required || !String.IsNullOrWhiteSpace(text)) && !text.Any(c => Char.IsControl(c) && c != '\n' && c != '\r' && c != '\t');
            if (!Text(value.Heading, 160, true) || !Text(value.Introduction, 2000, true)
                || !Text(value.NoticeTitle, 160) || !Text(value.NoticeText, 4000) || !Text(value.SupportText, 4000, true)
                || (value.NoticeEnabled && (String.IsNullOrWhiteSpace(value.NoticeTitle) || String.IsNullOrWhiteSpace(value.NoticeText)))
                || value.HiddenNavigation == null || value.HiddenNavigation.Length > Navigation.Length
                || value.HiddenNavigation.Distinct(StringComparer.Ordinal).Count() != value.HiddenNavigation.Length
                || value.HiddenNavigation.Any(x => !Navigation.Contains(x, StringComparer.Ordinal)))
                throw new ArgumentException("Check the field lengths, announcement content and supported navigation choices");
        }
        public void Save(WebUIPortalSettings value, int expectedRevision)
        {
            Validate(value);
            lock (_gate)
            {
                using var lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var current = Read();
                if (current.Revision != expectedRevision) throw new InvalidOperationException("Portal settings changed. Reload before saving");
                value.Revision = checked(current.Revision + 1);
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
                if (bytes.Length > 32768) throw new ArgumentException("Portal settings exceed their storage bound");
                using (var stream = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(bytes); stream.Flush(true); }
                File.Move(_path + ".tmp", _path, true);
            }
        }
    }

    public sealed class WebUIGridConfiguration
    {
        public string Revision { get; set; }
        public string GridName { get; set; }
        public string GridNick { get; set; }
        public string WelcomeMessage { get; set; }
        public bool RemoteWelcome { get; set; }
    }
    /// <summary>Explicit operator-selected native INI only; no request-controlled paths or arbitrary keys.</summary>
    public sealed class WebUIGridConfigurationStore
    {
        private readonly string _path;
        private readonly object _gate = new();
        public WebUIGridConfigurationStore(string path)
        {
            _path = Path.GetFullPath(path);
            if (!String.Equals(Path.GetExtension(_path), ".ini", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("GridConfigurationPath must select a native INI file");
            Read();
        }
        private byte[] Bytes()
        {
            if ((File.GetAttributes(_path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked configuration files cannot be edited");
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 1024 * 1024) throw new IOException("Grid configuration exceeds its size bound");
            using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
        }
        private static string Token(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        public WebUIGridConfiguration Read()
        {
            lock (_gate)
            {
                byte[] before = Bytes();
                // Fail closed on invalid UTF-8 rather than rewriting a differently encoded secret-bearing INI.
                new System.Text.UTF8Encoding(false, true).GetString(before);
                var source = new Nini.Config.IniConfigSource(_path);
                if (Token(before) != Token(Bytes())) throw new InvalidOperationException("Grid configuration changed. Reload before continuing");
                var grid = source.Configs["GridInfoService"]; var login = source.Configs["LoginService"];
                if (grid == null || login == null || source.Configs["ContinuumWebUI"] == null)
                    throw new InvalidOperationException("Select the loaded native ROBUST INI containing GridInfoService, LoginService and ContinuumWebUI");
                return new WebUIGridConfiguration { Revision = Token(before), GridName = grid.GetString("gridname", ""), GridNick = grid.GetString("gridnick", ""),
                    WelcomeMessage = login.GetString("WelcomeMessage", "Welcome to OpenSim!"), RemoteWelcome = !String.IsNullOrWhiteSpace(login.GetString("MessageUrl", "")) };
            }
        }
        public void Save(string revision, string name, string nick, string welcome)
        {
            bool Text(string value, int maximum) => !String.IsNullOrWhiteSpace(value) && value.Length <= maximum
                && !value.Any(Char.IsControl) && !value.Contains('"') && !value.Contains("${", StringComparison.Ordinal);
            if (!Text(name, 128) || !Text(welcome, 2000) || !Text(nick, 64)
                || nick.Any(c => !Char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_' && c != '.'))
                throw new ArgumentException("Enter a grid name, short name and welcome text within the field limits; quotes and configuration substitutions are unsupported");
            lock (_gate)
            {
                using var lease = new FileStream(_path + ".continuum.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                byte[] original = Bytes();
                if (Token(original) != revision) throw new InvalidOperationException("Grid configuration changed. Reload before saving");
                Read();
                string text = new System.Text.UTF8Encoding(false, true).GetString(original).TrimStart('\uFEFF');
                string Replace(string section, string key, string value)
                {
                    var lines = text.Replace("\r\n", "\n").Split('\n').ToList(); string active = ""; int end = -1; var matches = new List<int>();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        string line = lines[i].Trim();
                        if (line.StartsWith('[') && line.Contains(']'))
                        {
                            if (active.Equals(section, StringComparison.OrdinalIgnoreCase) && end < 0) end = i;
                            active = line.Substring(1, line.IndexOf(']') - 1).Trim();
                        }
                        if (active.Equals(section, StringComparison.OrdinalIgnoreCase) && !line.StartsWith(';') && !line.StartsWith('#'))
                        {
                            int equals = line.IndexOf('=');
                            if (equals > 0 && line.Substring(0, equals).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) matches.Add(i);
                            if (i == lines.Count - 1 && end < 0) end = lines.Count;
                        }
                    }
                    if (matches.Count > 1 || end < 0) throw new InvalidOperationException("Configuration has duplicate keys or unsupported sections; resolve it before editing");
                    string replacement = "    " + key + " = \"" + value + "\"";
                    if (matches.Count == 1) lines[matches[0]] = replacement; else lines.Insert(end, replacement);
                    return String.Join("\n", lines);
                }
                text = Replace("GridInfoService", "gridname", name); text = Replace("GridInfoService", "gridnick", nick);
                text = Replace("LoginService", "WelcomeMessage", welcome); text = Replace("ContinuumWebUI", "SystemName", name);
                string temporary = _path + ".continuum-" + Guid.NewGuid().ToString("N") + ".tmp";
                string backup = _path + ".continuum-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".bak";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(text); if (bytes.Length > 1024 * 1024) throw new ArgumentException("Updated grid configuration exceeds its size bound"); stream.Write(bytes); stream.Flush(true); }
                    if (Token(Bytes()) != revision) throw new InvalidOperationException("Grid configuration changed. Reload before saving");
                    File.Replace(temporary, _path, backup);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
    }

    internal sealed partial class WebUIServiceIntegration
    {
        private readonly Dictionary<string, string> _consoleEndpoints;
        private static Dictionary<string, string> ConfigureConsoles(Nini.Config.IConfigSource config)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var section = config.Configs["ContinuumWebUIConsoles"];
            if (section == null) return result;
            if (section.GetKeys().Length > 64) throw new InvalidOperationException("Configure at most 64 native console endpoints");
            foreach (string name in section.GetKeys())
            {
                string value = section.GetString(name, "").Trim();
                if (String.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(Char.IsControl) || value.Length > 1024
                    || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || String.IsNullOrWhiteSpace(uri.Host)
                    || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                    || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                    throw new InvalidOperationException("Native consoles require named HTTPS base URLs without credentials, query or fragment; HTTP is limited to loopback");
                result.Add(name, uri.AbsoluteUri.TrimEnd('/') + "/");
            }
            return result;
        }
        private void AddConsole(UserAccount user, Dictionary<string, object> vars)
        {
            if (!IsAdmin(user)) throw new ArgumentException("Administrator access required");
            vars["ConsoleConfigured"] = _consoleEndpoints != null && _consoleEndpoints.Count > 0;
            vars["ConsoleUnconfigured"] = _consoleEndpoints == null || _consoleEndpoints.Count == 0;
            vars["ConsoleEndpoints"] = (_consoleEndpoints ?? new Dictionary<string, string>()).Select(row => new Dictionary<string, object>
                { ["ConsoleName"] = H(row.Key), ["ConsoleURL"] = H(row.Value) }).ToList();
        }
        private void AddCurrencyCredits(string page, IReadOnlyDictionary<string, string> parameters, UserAccount user, Dictionary<string, object> vars)
        {
            bool admin = page.StartsWith("admin/", StringComparison.Ordinal);
            if (user == null || (admin && !IsAdmin(user))) throw new ArgumentException("Authorized account access required");
            string Value(string key) => parameters.TryGetValue(key, out var value) ? value.Trim() : "";
            UserAccount target = admin ? null : user;
            string search = Value("resident");
            if (admin && search.Length <= 128 && search.Length > 0)
            {
                if (OpenMetaverse.UUID.TryParse(search, out var id)) target = _accounts.GetUserAccount(OpenMetaverse.UUID.Zero, id);
                else { var parts = search.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries); if (parts.Length == 2) target = _accounts.GetUserAccount(OpenMetaverse.UUID.Zero, parts[0], parts[1]); }
            }
            if (target != null && !target.LocalToGrid) target = null;
            vars["CurrencyCreditAdmin"] = admin; vars["CurrencyCreditSearch"] = H(search);
            vars["CurrencyCreditTarget"] = H(target?.FormattedName ?? "Choose a local resident");
            vars["CurrencyCreditRows"] = new List<Dictionary<string, object>>(); vars["CurrencyCreditHasMore"] = false;
            vars["CurrencyCreditAvailable"] = false; vars["CurrencyCreditUnavailable"] = false; vars["CurrencyCreditEmpty"] = false;
            vars["CurrencyCreditNeedsResident"] = target == null;
            if (target == null) return;
            Hashtable result = String.IsNullOrEmpty(_economyUrl) || String.IsNullOrEmpty(_economySecret) ? null : Rpc(_economyUrl, "GetCurrencyCreditHistory", new System.Collections.Hashtable
            {
                ["continuumSecret"] = _economySecret, ["accountID"] = target.PrincipalID.ToString(),
                ["beforeUtc"] = Value("beforeUtc"), ["beforeID"] = Value("beforeID")
            });
            bool available = result != null && Text(result, "success").Equals("true", StringComparison.OrdinalIgnoreCase) && result["history"] is System.Collections.ArrayList;
            vars["CurrencyCreditAvailable"] = available; vars["CurrencyCreditUnavailable"] = !available;
            if (!available) return;
            var rows = ((System.Collections.ArrayList)result["history"]).OfType<System.Collections.Hashtable>().Take(50).Select(row => new Dictionary<string, object>
            {
                ["CreditDate"] = H(Text(row, "createdUtc")), ["CreditID"] = H(Text(row, "transactionID")), ["CreditAmount"] = H(Text(row, "amount")),
                ["CreditBalance"] = H(Text(row, "balance")), ["CreditDescription"] = H(Text(row, "description"))
            }).ToList();
            vars["CurrencyCreditRows"] = rows; vars["CurrencyCreditEmpty"] = rows.Count == 0;
            vars["CurrencyCreditHasMore"] = Text(result, "hasMore").Equals("true", StringComparison.OrdinalIgnoreCase);
            string menu = admin ? "admin-purchases" : "user-purchases";
            string query = "beforeUtc=" + Uri.EscapeDataString(Text(result, "beforeUtc")) + "&beforeID=" + Uri.EscapeDataString(Text(result, "beforeID"))
                + (admin ? "&resident=" + Uri.EscapeDataString(search) : "");
            vars["CurrencyCreditPage"] = menu; vars["CurrencyCreditNext"] = H(query);
        }
        private readonly WebUIGridConfigurationStore _gridConfiguration;
        private void AddGridConfiguration(UserAccount user, Dictionary<string, object> vars)
        {
            if (!IsAdmin(user)) throw new ArgumentException("Administrator access required");
            vars["GridConfigurationEnabled"] = _gridConfiguration != null; vars["GridConfigurationDisabled"] = _gridConfiguration == null;
            if (_gridConfiguration == null) return;
            var settings = _gridConfiguration.Read();
            vars["GridConfigurationRevision"] = settings.Revision; vars["GridConfigurationName"] = H(settings.GridName);
            vars["GridConfigurationNick"] = H(settings.GridNick); vars["GridConfigurationWelcome"] = H(settings.WelcomeMessage);
            vars["GridConfigurationRemoteWelcome"] = settings.RemoteWelcome;
        }
        private bool ChangeGridConfiguration(IReadOnlyDictionary<string, string> form, UserAccount user, out string message)
        {
            message = "Grid configuration editing is unavailable";
            if (!IsAdmin(user)) { message = "Administrator access required"; return true; }
            if (_gridConfiguration == null) return true;
            string Value(string key) => form.TryGetValue(key, out string value) ? value.Trim() : "";
            try
            {
                if (Value("action") != "save" || Value("confirm") != "true") throw new ArgumentException("Confirm the configuration change and restart requirement");
                _gridConfiguration.Save(Value("revision"), Value("gridName"), Value("gridNick"), Value("welcome"));
                Log.InfoFormat("[CONTINUUM WEBUI]: Native grid identity configuration saved by {0}; ROBUST restart required", user.PrincipalID);
                message = "Grid configuration saved; ROBUST restart required";
            }
            catch (ArgumentException e) { message = e.Message; }
            catch (InvalidOperationException e) { message = e.Message; }
            catch (Exception) { message = "Grid configuration save could not be confirmed. Reload and contact the grid operator"; Log.Warn("[CONTINUUM WEBUI]: Native grid configuration save could not be confirmed"); }
            return true;
        }
        private readonly bool _allowPartnerships;
        private void AddPartnership(UserAccount user, Dictionary<string, object> vars)
        {
            bool enabled = _allowPartnerships && _profiles is IUserPartnershipService service && service.SupportsPartnerships;
            vars["PartnershipEnabled"] = enabled; vars["PartnershipDisabled"] = !enabled;
            if (!enabled) return;
            if (user == null || !user.LocalToGrid) throw new ArgumentException("A local account is required");
            var profile = new OpenSim.Framework.UserProfileProperties { UserId = user.PrincipalID }; string result = "";
            if (!_profiles.AvatarPropertiesRequest(ref profile, ref result)) throw new InvalidOperationException("Native profile storage is unavailable");
            string Name(OpenMetaverse.UUID id) => H(_accounts.GetUserAccount(OpenMetaverse.UUID.Zero, id)?.FormattedName ?? id.ToString());
            vars["PartnershipHasPartner"] = profile.PartnerId != OpenMetaverse.UUID.Zero;
            vars["PartnershipUnpartnered"] = profile.PartnerId == OpenMetaverse.UUID.Zero;
            vars["PartnershipPartner"] = profile.PartnerId == OpenMetaverse.UUID.Zero ? "None" : Name(profile.PartnerId);
            vars["PartnershipPartnerID"] = profile.PartnerId.ToString();
            vars["PartnershipRequestID"] = OpenMetaverse.UUID.Random().ToString();
            var proposals = ((IUserPartnershipService)_profiles).GetPartnershipProposals(user.PrincipalID);
            Dictionary<string, object> Row(OpenSim.Framework.PartnershipProposal row, bool incoming) => new()
            {
                ["ProposalID"] = row.ID.ToString(), ["ProposalOtherID"] = (incoming ? row.Sender : row.Recipient).ToString(),
                ["ProposalName"] = Name(incoming ? row.Sender : row.Recipient), ["ProposalMessage"] = H(row.Message),
                ["ProposalExpiry"] = H(DateTimeOffset.FromUnixTimeSeconds(row.Expires).ToString("u", CultureInfo.InvariantCulture))
            };
            vars["PartnershipIncoming"] = proposals.Where(row => row.Recipient == user.PrincipalID).Select(row => Row(row, true)).ToList();
            vars["PartnershipOutgoing"] = proposals.Where(row => row.Sender == user.PrincipalID).Select(row => Row(row, false)).ToList();
        }
        private bool ChangePartnership(IReadOnlyDictionary<string, string> form, UserAccount user, out string message)
        {
            message = "Partnerships are unavailable";
            if (!_allowPartnerships || _profiles is not IUserPartnershipService service || !service.SupportsPartnerships) return true;
            if (user == null || !user.LocalToGrid) { message = "A local account is required"; return true; }
            string Value(string key) => form.TryGetValue(key, out string value) ? value.Trim() : "";
            try
            {
                string action = Value("action");
                if (action is "accept" or "dissolve" && Value("confirm") != "true") throw new ArgumentException("Confirm the partnership change before continuing");
                if (!OpenMetaverse.UUID.TryParse(Value("requestID"), out var proposal) || proposal == OpenMetaverse.UUID.Zero)
                    throw new ArgumentException("Reload the current partnership page before continuing");
                UserAccount other;
                if (OpenMetaverse.UUID.TryParse(Value("other"), out var id)) other = _accounts.GetUserAccount(OpenMetaverse.UUID.Zero, id);
                else
                {
                    var parts = Value("other").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    other = parts.Length == 2 ? _accounts.GetUserAccount(OpenMetaverse.UUID.Zero, parts[0], parts[1]) : null;
                }
                if (other == null || !other.LocalToGrid || other.PrincipalID == user.PrincipalID
                    || (action is "propose" or "accept" && other.UserLevel < 0)) throw new ArgumentException("Choose a different active local resident");
                message = service.ChangePartnership(user.PrincipalID, other.PrincipalID, proposal, action, Value("message"));
            }
            catch (ArgumentException e) { message = e.Message; }
            catch (InvalidOperationException e) { message = e.Message; }
            catch (Exception) { message = "Partnership change could not be confirmed. Reload before retrying"; Log.Warn("[CONTINUUM WEBUI]: Partnership operation could not be confirmed"); }
            return true;
        }
        private readonly bool _allowAccountClosure;
        private readonly HashSet<OpenMetaverse.UUID> _protectedClosureAccounts;
        private void AddAccountClosure(UserAccount user, Dictionary<string, object> vars)
        {
            bool enabled = _allowAccountClosure && _accounts is IUserAccountArchiveService archive && archive.SupportsAccountArchive
                && user != null && user.LocalToGrid && user.UserLevel == 0 && !_protectedClosureAccounts.Contains(user.PrincipalID);
            vars["AccountClosureEnabled"] = enabled; vars["AccountClosureDisabled"] = !enabled;
        }
        private bool CloseAccount(IReadOnlyDictionary<string, string> form, UserAccount user, out string message)
        {
            message = "Account closure is unavailable. Contact the grid operator";
            if (!_allowAccountClosure || user == null || !user.LocalToGrid || user.UserLevel != 0
                || _protectedClosureAccounts.Contains(user.PrincipalID) || _accounts is not IUserAccountArchiveService archive || !archive.SupportsAccountArchive) return true;
            string Value(string key) => form.TryGetValue(key, out string value) ? value : "";
            if (Value("action") != "close" || Value("confirm") != "true") { message = "Confirm account closure before continuing"; return true; }
            if (Value("password").Length is < 1 or > 128) { message = "Enter your current password"; return true; }
            try
            {
                if (_gridUsers == null) return true;
                var info = _gridUsers.GetGridUserInfo(user.PrincipalID.ToString());
                if (info?.Online == true) { message = "Log out of the viewer before closing your account"; return true; }
                if (_estates == null) return true;
                var estates = _estates.GetEstatesByOwner(user.PrincipalID);
                if (estates == null) return true;
                if (estates.Count > 0) { message = "Transfer your estates before closing your account"; return true; }
                string token = _authenticationForPassword?.Authenticate(user.PrincipalID, OpenSim.Framework.Util.Md5Hash(Value("password")), 60);
                if (String.IsNullOrEmpty(token)) { message = "Current password is incorrect"; return true; }
                _authenticationForPassword.Release(user.PrincipalID, token);
                if (!archive.TryArchiveAccount(user.PrincipalID)) { message = "Account status changed. Reload before continuing"; return true; }
                Log.InfoFormat("[CONTINUUM WEBUI]: Account {0} archived by its authenticated owner", user.PrincipalID);
                message = "Account closed; identity and records retained";
            }
            catch (Exception)
            {
                Log.Warn("[CONTINUUM WEBUI]: Account closure could not be confirmed; check current account status");
                message = "Account closure could not be confirmed. Check your account status before retrying";
            }
            return true;
        }
        private readonly bool _residentContactEnabled;
        private readonly string _contactPrivacyNotice;
        private void AddContact(UserAccount user, Dictionary<string, object> vars)
        {
            bool enabled = _residentContactEnabled && _profiles is IUserContactService;
            vars["ContactEnabled"] = enabled; vars["ContactDisabled"] = !enabled;
            vars["ContactPrivacyNotice"] = H(_contactPrivacyNotice ?? "");
            if (!enabled) return;
            if (user == null || !user.LocalToGrid) throw new ArgumentException("A local account is required");
            var contact = ((IUserContactService)_profiles).GetContact(user.PrincipalID);
            vars["ContactAddress"] = H(contact.Address); vars["ContactCity"] = H(contact.City);
            vars["ContactPostalCode"] = H(contact.PostalCode); vars["ContactCountry"] = H(contact.Country);
        }
        private bool ChangeContact(IReadOnlyDictionary<string, string> form, UserAccount user, out string message)
        {
            message = "Contact details could not be saved. Reload before retrying";
            if (!_residentContactEnabled || _profiles is not IUserContactService service)
            { message = "Contact storage is disabled by the grid operator"; return true; }
            if (user == null || !user.LocalToGrid) { message = "A local account is required"; return true; }
            string Value(string key) => form.TryGetValue(key, out string value) ? value.Trim() : "";
            try
            {
                if (Value("action") is not ("save" or "clear")) throw new ArgumentException("Choose save or clear contact details");
                var contact = new OpenSim.Framework.UserContactInfo();
                if (Value("action") == "save")
                {
                    if (Value("consent") != "true") throw new ArgumentException("Confirm the contact-data notice before saving");
                    contact.Address = Value("address"); contact.City = Value("city");
                    contact.PostalCode = Value("postalCode"); contact.Country = Value("country");
                }
                contact.Validate();
                if (service.StoreContact(user.PrincipalID, contact)) message = "Contact details updated";
            }
            catch (ArgumentException e) { message = e.Message; }
            catch (Exception) { Log.Warn("[CONTINUUM WEBUI]: Contact storage could not confirm an operation"); }
            return true;
        }
        private readonly WebUIPortalSettingsStore _portalStore;
        private void AddPortal(string page, UserAccount user, Dictionary<string, object> vars)
        {
            // Uninitialized adapters used by older isolated tests retain the built-in defaults.
            var settings = _portalStore?.Read() ?? new WebUIPortalSettings();
            vars["PortalHeading"] = H(settings.Heading);
            vars["PortalIntroduction"] = H(settings.Introduction);
            vars["PortalNoticeTitle"] = H(settings.NoticeTitle);
            vars["PortalNoticeText"] = H(settings.NoticeText);
            vars["PortalNoticeEnabled"] = settings.NoticeEnabled;
            vars["PortalSupportText"] = H(settings.SupportText);
            vars["ShowSlideshowBar"] = settings.ShowSlideshow;
            vars["PortalRegistrationEnabled"] = _allowRegistration;
            if (vars.TryGetValue("MenuItems", out var menus) && menus is List<Dictionary<string, object>> rows)
                foreach (var row in rows)
                    if (settings.HiddenNavigation.Contains((string)row["MenuItemID"], StringComparer.Ordinal)) row["ShowInMenu"] = false;
            if (page is not ("admin/welcomescreen_manager.html" or "admin/settings_manager.html" or "admin/page_manager.html" or "admin/factory_reset.html")) return;
            if (!IsAdmin(user)) throw new ArgumentException("Administrator access required");
            vars["PortalRevision"] = settings.Revision;
            vars["PortalNoticeChecked"] = settings.NoticeEnabled ? "checked" : "";
            vars["PortalSlideshowChecked"] = settings.ShowSlideshow ? "checked" : "";
            vars["PortalNavigationRows"] = WebUIPortalSettingsStore.Navigation.Select(id => new Dictionary<string, object>
            { ["NavigationID"] = id, ["NavigationLabel"] = id.Replace('_', ' '), ["NavigationChecked"] = settings.HiddenNavigation.Contains(id) ? "" : "checked" }).ToList();
        }
        private bool ChangePortal(string page, IReadOnlyDictionary<string, string> form, UserAccount user, out string message)
        {
            if (!IsAdmin(user)) { message = "Administrator access required"; return true; }
            string Value(string key) => form.TryGetValue(key, out var value) ? value : "";
            try
            {
                if (!Int32.TryParse(Value("revision"), NumberStyles.None, CultureInfo.InvariantCulture, out int revision))
                    throw new ArgumentException("Reload the current portal settings before saving");
                var settings = _portalStore.Read();
                if (page == "admin/factory_reset.html")
                {
                    if (Value("confirm") != "true") throw new ArgumentException("Confirm the portal reset before continuing");
                    if (Value("action") == "navigation") settings.HiddenNavigation = Array.Empty<string>();
                    else if (Value("action") == "presentation")
                    {
                        var defaults = new WebUIPortalSettings();
                        settings.Heading = defaults.Heading; settings.Introduction = defaults.Introduction;
                        settings.NoticeTitle = defaults.NoticeTitle; settings.NoticeText = defaults.NoticeText;
                        settings.NoticeEnabled = defaults.NoticeEnabled; settings.SupportText = defaults.SupportText;
                        settings.ShowSlideshow = defaults.ShowSlideshow;
                    }
                    else throw new ArgumentException("Choose navigation or presentation defaults");
                }
                else if (page == "admin/welcomescreen_manager.html")
                {
                    settings.Heading = Value("heading").Trim(); settings.Introduction = Value("introduction").Trim();
                    settings.NoticeTitle = Value("noticeTitle").Trim(); settings.NoticeText = Value("noticeText").Trim();
                    settings.NoticeEnabled = Value("noticeEnabled") == "true";
                }
                else if (page == "admin/settings_manager.html")
                { settings.SupportText = Value("supportText").Trim(); settings.ShowSlideshow = Value("slideshow") == "true"; }
                else settings.HiddenNavigation = WebUIPortalSettingsStore.Navigation.Where(id => Value("nav_" + id) != "true").ToArray();
                _portalStore.Save(settings, revision);
                Log.InfoFormat("[CONTINUUM WEBUI]: Portal settings revision {0} saved by {1} via {2}", settings.Revision, user.PrincipalID, page);
                message = "Successfully saved portal settings";
            }
            catch (ArgumentException e) { message = e.Message; }
            catch (InvalidOperationException e) { message = e.Message; }
            catch (IOException) { message = "Portal settings are unavailable or busy. Reload and try again"; }
            catch (UnauthorizedAccessException) { message = "Portal settings are not writable. Contact a grid administrator"; }
            return true;
        }
    }
}
