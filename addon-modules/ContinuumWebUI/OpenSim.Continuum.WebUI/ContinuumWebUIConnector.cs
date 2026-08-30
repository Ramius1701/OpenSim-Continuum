/*
 * Continuum adaptation of the WhiteCore integrated WebUI.
 * WhiteCore portions are BSD-3-Clause; see THIRD_PARTY_NOTICES.md.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Server.Base;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Continuum.WebUI
{
    public sealed class ContinuumWebUIConnector : ServiceConnector
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ContinuumWebUIConnector));
        public ContinuumWebUIConnector(IConfigSource config, IHttpServer server, string configName)
        {
            string sectionName = string.IsNullOrWhiteSpace(configName) ? "ContinuumWebUI" : configName;
            IConfig section = config.Configs[sectionName];
            if (section == null || !section.GetBoolean("Enabled", false))
            {
                Log.Info("[CONTINUUM WEBUI]: Disabled");
                return;
            }

            string mountPath = NormalizeMountPath(section.GetString("MountPath", "/webui"));
            string systemName = section.GetString("SystemName", "OpenSim Continuum").Trim();
            string accountsPlugin = section.GetString("UserAccountService", "OpenSim.Services.UserAccountService.dll:UserAccountService");
            string authenticationPlugin = section.GetString("AuthenticationService", "OpenSim.Services.AuthenticationService.dll:PasswordAuthenticationService");
            IUserAccountService accounts = ServerUtils.LoadPlugin<IUserAccountService>(accountsPlugin, new object[] { config });
            IAuthenticationService authentication = ServerUtils.LoadPlugin<IAuthenticationService>(authenticationPlugin, new object[] { config });
            if (accounts == null || authentication == null)
                throw new InvalidOperationException("ContinuumWebUI could not load its account or authentication service");
            int sessionSeconds = Math.Clamp(section.GetInt("SessionLifetimeSeconds", 3600), 300, 86400);
            int loginAttempts = Math.Clamp(section.GetInt("LoginAttemptsPerWindow", 10), 1, 1000);
            int loginWindow = Math.Clamp(section.GetInt("LoginAttemptWindowSeconds", 300), 10, 86400);
            int registrationAttempts = Math.Clamp(section.GetInt("RegistrationAttemptsPerWindow", 3), 1, 1000);
            int registrationWindow = Math.Clamp(section.GetInt("RegistrationAttemptWindowSeconds", 3600), 10, 86400);
            var integration = new WebUIServiceIntegration(config, section, accounts);
            integration.SetAuthenticationService(authentication);
            var site = new WhiteCoreSite(mountPath, systemName, accounts, authentication, integration, sessionSeconds,
                loginAttempts, loginWindow, registrationAttempts, registrationWindow);
            server.AddSimpleStreamHandler(new SimpleStreamHandler(mountPath, site.HandleRoot));
            server.AddSimpleStreamHandler(new SimpleStreamHandler(mountPath, site.Handle), true);
            Log.InfoFormat("[CONTINUUM WEBUI]: WhiteCore integrated portal mounted at {0}/", mountPath);
        }

        private static string NormalizeMountPath(string value)
        {
            string path = string.IsNullOrWhiteSpace(value) ? "/webui" : value.Trim();
            if (!path.StartsWith('/')) path = "/" + path;
            path = path.TrimEnd('/');
            if (path.Length == 0 || path.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("ContinuumWebUI MountPath must be a non-root URL path");
            return path;
        }
    }

    internal sealed class WhiteCoreSite
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(WhiteCoreSite));
        private static readonly Assembly ThisAssembly = typeof(WhiteCoreSite).Assembly;
        private static readonly System.Lazy<Dictionary<string, byte[]>> Content = new(LoadContent, true);
        private static readonly Regex Token = new(@"\{[A-Za-z][A-Za-z0-9]*\}", RegexOptions.Compiled);
        private static readonly HashSet<string> UnsupportedLegacyPages = new(StringComparer.OrdinalIgnoreCase)
        {
            "forgot_pass.html", "classifieds/add_classified.html", "events/add_event.html",
            "admin/estate_edit.html", "admin/factory_reset.html", "admin/gridsettings_manager.html",
            "admin/news_add.html", "admin/news_edit.html", "admin/news_manager.html",
            "admin/page_manager.html", "admin/purchases.html", "admin/region_edit.html",
            "admin/settings_manager.html", "admin/sim_console.html",
            "admin/welcomescreen_manager.html", "user/contact.html", "user/deleteaccount.html",
            "user/edit_event.html", "user/estate_edit.html", "user/partnership.html",
            "user/purchases.html", "user/region_edit.html", "user/update_user.html"
        };
        private readonly string _mountPath;
        private readonly string _systemName;
        private readonly IUserAccountService _accounts;
        private readonly IAuthenticationService _authentication;
        private readonly WebUIServiceIntegration _integration;
        private readonly int _sessionSeconds;
        private readonly int _loginAttempts;
        private readonly TimeSpan _loginWindow;
        private readonly int _registrationAttempts;
        private readonly TimeSpan _registrationWindow;
        private readonly object _attemptsLock = new();
        private readonly Dictionary<string, Queue<DateTime>> _attempts = new(StringComparer.Ordinal);

        internal WhiteCoreSite(string mountPath, string systemName, IUserAccountService accounts,
            IAuthenticationService authentication, WebUIServiceIntegration integration, int sessionSeconds,
            int loginAttempts, int loginWindowSeconds, int registrationAttempts, int registrationWindowSeconds)
        {
            _mountPath = mountPath;
            _systemName = string.IsNullOrWhiteSpace(systemName) ? "OpenSim Continuum" : systemName;
            _accounts = accounts;
            _authentication = authentication;
            _integration = integration;
            _sessionSeconds = sessionSeconds;
            _loginAttempts = loginAttempts;
            _loginWindow = TimeSpan.FromSeconds(loginWindowSeconds);
            _registrationAttempts = registrationAttempts;
            _registrationWindow = TimeSpan.FromSeconds(registrationWindowSeconds);
        }

        internal void HandleRoot(IOSHttpRequest request, IOSHttpResponse response)
        {
            if (string.Equals(request.UriPath, _mountPath, StringComparison.Ordinal))
                response.Redirect(_mountPath + "/", HttpStatusCode.MovedPermanently);
            else if (string.Equals(request.UriPath, _mountPath + "/", StringComparison.Ordinal))
                Handle(request, response);
            else
                WriteError(response, HttpStatusCode.NotFound, "Not found");
        }

        internal void Handle(IOSHttpRequest request, IOSHttpResponse response)
        {
            if (string.Equals(request.UriPath, _mountPath, StringComparison.Ordinal))
            {
                response.Redirect(_mountPath + "/", HttpStatusCode.MovedPermanently);
                return;
            }
            if (!request.UriPath.StartsWith(_mountPath + "/", StringComparison.Ordinal))
            {
                WriteError(response, HttpStatusCode.NotFound, "Not found");
                return;
            }
            if (request.HttpMethod != "GET" && request.HttpMethod != "HEAD" && request.HttpMethod != "POST")
            {
                response.AddHeader("Allow", "GET, HEAD, POST");
                WriteError(response, HttpStatusCode.MethodNotAllowed, "Method not allowed");
                return;
            }

            string relative = request.UriPath.Substring((_mountPath + "/").Length);
            if (string.IsNullOrEmpty(relative)) relative = "index.html";
            if (!TryNormalize(relative, out relative))
            {
                WriteError(response, HttpStatusCode.BadRequest, "Invalid path");
                return;
            }
            relative = _integration.ResolvePage(relative);
            if (UnsupportedLegacyPages.Contains(relative))
            {
                WriteError(response, HttpStatusCode.NotFound, "This legacy WhiteCore workflow is not available through OpenSim services");
                return;
            }

            if (request.HttpMethod == "POST" && relative == "login.html")
            {
                if (!IsSameOrigin(request)) { WriteError(response, HttpStatusCode.Forbidden, "Cross-site request rejected"); return; }
                HandleLogin(request, response);
                return;
            }
            if (request.HttpMethod == "POST" && relative == "logout.html")
            {
                if (!IsSameOrigin(request)) { WriteError(response, HttpStatusCode.Forbidden, "Cross-site request rejected"); return; }
                HandleLogout(request, response);
                return;
            }
            UserAccount currentUser = GetAuthenticatedUser(request);
            bool accountPage = relative.StartsWith("user/", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("userhome.html", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("friends.html", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("transactions.html", StringComparison.OrdinalIgnoreCase);
            if (accountPage && currentUser == null)
            {
                WriteError(response, HttpStatusCode.Unauthorized, "Authentication required");
                return;
            }
            bool administratorPage = relative.StartsWith("admin/", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("online_users.html", StringComparison.OrdinalIgnoreCase);
            if (administratorPage && !_integration.IsAdmin(currentUser))
            {
                WriteError(response, HttpStatusCode.Forbidden, "Administrator access required");
                return;
            }
            Dictionary<string, string> parameters = RequestParameters(request);
            if (relative.Equals("get-region-name-by-coords", StringComparison.OrdinalIgnoreCase))
            {
                HandleMapRegionLookup(response, parameters);
                return;
            }
            if (request.HttpMethod == "POST")
            {
                if (!IsSameOrigin(request)) { WriteError(response, HttpStatusCode.Forbidden, "Cross-site request rejected"); return; }
                if (relative.Equals("register.html", StringComparison.OrdinalIgnoreCase)
                    && !AllowAttempt("register:" + request.RemoteIPEndPoint?.Address, _registrationAttempts, _registrationWindow))
                { WriteError(response, (HttpStatusCode)429, "Too many registration attempts; try again later"); return; }
                if (request.ContentLength64 < 0 || request.ContentLength64 > 64 * 1024)
                {
                    WriteError(response, HttpStatusCode.RequestEntityTooLarge, "Request is too large");
                    return;
                }
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, true, 1024, true);
                var form = System.Web.HttpUtility.ParseQueryString(reader.ReadToEnd());
                foreach (string key in form.AllKeys)
                    if (key != null) parameters[key] = form[key];
                if (_integration.HandlePost(relative, parameters, currentUser, out string message))
                {
                    string responseMessage = WebUIServiceIntegration.IsSuccessfulMutation(message) ? message
                        : WebUIServiceIntegration.IsWarningMutation(message) ? "~" + message : "!" + message;
                    WriteText(response, HttpStatusCode.OK, responseMessage);
                    return;
                }
            }

            byte[] content = ReadResource(relative);
            if (content == null)
            {
                WriteError(response, HttpStatusCode.NotFound, "Not found");
                return;
            }

            bool templatedContent = relative.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                || relative.EndsWith("menu.js", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("map/mapapi.js", StringComparison.OrdinalIgnoreCase);
            if (templatedContent || relative.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string source = Encoding.UTF8.GetString(content);
                    if (templatedContent)
                    {
                        var variables = DefaultVariables(currentUser);
                        _integration.Populate(relative, parameters, currentUser, variables);
                        source = Render(relative, source, variables, currentUser != null, _integration.IsAdmin(currentUser), 0);
                    }
                    source = RewriteAssetPaths(source);
                    content = Encoding.UTF8.GetBytes(source);
                }
                catch (Exception e)
                {
                    Log.ErrorFormat("[CONTINUUM WEBUI]: Failed to render {0}: {1}", relative, e);
                    throw;
                }
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = MimeType(relative);
            response.AddHeader("X-Content-Type-Options", "nosniff");
            response.AddHeader("Referrer-Policy", "same-origin");
            response.AddHeader("Content-Security-Policy", _integration.ContentSecurityPolicy);
            if (templatedContent)
            {
                response.AddHeader("Cache-Control", "no-store");
                response.AddHeader("Vary", "Cookie");
            }
            response.RawBuffer = request.HttpMethod == "HEAD" ? Array.Empty<byte>() : content;
        }

        private Dictionary<string, object> DefaultVariables(UserAccount currentUser)
        {
            bool authenticated = currentUser != null;
            bool administrator = _integration.IsAdmin(currentUser);
            var menus = new List<Dictionary<string, object>>
            {
                Menu("home", "Home", "home.html"),
                Menu("world_map", "World map", "world_map.html"),
                Menu("destinations", "Destinations", "destinations.html"),
                Menu("events", "Events", "events.html"),
                Menu("classifieds", "Classifieds", "classifieds.html"),
                Menu("experiences", "Experiences", "experiences.html"),
                Menu("user_search", "Residents", "user_search.html"),
                Menu("region_search", "Regions", "region_search.html"),
                MenuGroup("account-tools", "Account", "userhome.html", authenticated, new[]
                {
                    Child("userhome", "Dashboard", "userhome.html"), Child("profile_edit", "Edit profile", "user/profile_edit.html"),
                    Child("user-email", "Change email", "user/email.html"), Child("user-password", "Change password", "user/password.html"),
                    Child("friends", "Friends", "friends.html"), Child("groups", "Groups", "groups.html"),
                    Child("user-region_manager", "My regions", "user/region_manager.html"), Child("user-estate_manager", "My estates", "user/estate_manager.html"),
                    Child("user-classifieds", "My classifieds", "user/classifieds.html"), Child("user-events", "My events", "user/events.html"),
                    Child("user-transactions", "My transactions", "user/transactions.html")
                }),
                MenuGroup("administration", "Administration", "admin/statistics.html", administrator, new[]
                {
                    Child("admin-statistics", "Grid statistics", "admin/statistics.html"), Child("online_users", "Online users", "online_users.html"),
                    Child("admin-region_manager", "Grid regions", "admin/region_manager.html"), Child("admin-estate_manager", "Grid estates", "admin/estate_manager.html"),
                    Child("admin-transactions", "Grid transactions", "admin/transactions.html"), Child("admin-user_manager", "Resident administration", "admin/user_manager.html"),
                    Child("admin-user-register", "Create resident", "admin/user_register.html"),
                    Child("abuse_manager", "Abuse reports", "abuse_manager.html")
                }),
                Menu("login", "Log in", "login.html", false),
                Menu("register", "Sign up", "register.html", false),
                Menu("userhome", "Account", "userhome.html", false),
                Menu("profile_edit", "Edit profile", "user/profile_edit.html", false),
                Menu("user-email", "Change email", "user/email.html", false),
                Menu("user-password", "Change password", "user/password.html", false),
                Menu("friends", "Friends", "friends.html", false),
                Menu("groups", "Groups", "groups.html", false),
                Menu("user-region_manager", "My regions", "user/region_manager.html", false),
                Menu("user-estate_manager", "My estates", "user/estate_manager.html", false),
                Menu("user-classifieds", "My classifieds", "user/classifieds.html", false),
                Menu("user-events", "My events", "user/events.html", false),
                Menu("user-transactions", "My transactions", "user/transactions.html", false),
                Menu("admin-region_manager", "Grid regions", "admin/region_manager.html", false),
                Menu("admin-estate_manager", "Grid estates", "admin/estate_manager.html", false),
                Menu("admin-statistics", "Grid statistics", "admin/statistics.html", false),
                Menu("online_users", "Online users", "online_users.html", false),
                Menu("admin-transactions", "Grid transactions", "admin/transactions.html", false),
                Menu("admin-user_manager", "Resident administration", "admin/user_manager.html", false),
                Menu("abuse_manager", "Abuse reports", "abuse_manager.html", false),
                Menu("logout", "Log out", "logout.html", false)
            };
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["SystemName"] = _systemName,
                ["SystemURL"] = _mountPath + "/",
                ["WorldMapAPIServiceURL"] = _mountPath,
                ["MainServerURL"] = string.Empty,
                ["WorldMapServiceURL"] = string.Empty,
                ["MenuItems"] = menus,
                ["ModalItems"] = new List<Dictionary<string, object>>
                {
                    Menu("webprofile-modal_profile", "Resident profile", "webprofile/modal_profile.html"),
                    Menu("webprofile-modal_groups", "Resident groups", "webprofile/modal_groups.html"),
                    Menu("webprofile-modal_picks", "Resident picks", "webprofile/modal_picks.html"),
                    Menu("webprofile-modal_regions", "Resident regions", "webprofile/modal_regions.html"),
                    Menu("regionprofile-modal_profile", "Region profile", "regionprofile/modal_profile.html"),
                    Menu("regionprofile-modal_parcels", "Region parcels", "regionprofile/modal_parcels.html")
                },
                ["Languages"] = new List<Dictionary<string, object>>(),
                ["Maintenance"] = false,
                ["NoMaintenance"] = true,
                ["LocalPage"] = false,
                ["ShowLanguageTranslatorBar"] = false,
                ["ShowSlideshowBar"] = true,
                ["PagesUpdateRequired"] = string.Empty,
                ["SettingsUpdateRequired"] = string.Empty,
                ["LocalFrontPage"] = string.Empty,
                ["HomeText"] = "Home",
                ["HomeTextWelcome"] = "Welcome",
                ["HomeTextTips"] = string.Empty,
                ["WelcomeScreen"] = "Welcome",
                ["WelcomeToText"] = "Welcome to " + _systemName,
                ["UserLogin"] = true,
                ["UserName"] = currentUser?.Name ?? string.Empty,
                ["UserID"] = currentUser?.PrincipalID.ToString() ?? string.Empty,
                ["ErrorMessage"] = string.Empty,
                ["Login"] = "Log in",
                ["UserNameText"] = "User name",
                ["PasswordText"] = "Password",
                ["ForgotPassword"] = "Forgot password",
                ["Submit"] = "Submit",
                ["GalleryImages"] = GalleryImages()
            };
        }

        private static Dictionary<string, object> Menu(string id, string title, string location, bool show = true) => new()
        {
            ["MenuItemID"] = id,
            ["MenuItemTitle"] = title,
            ["MenuItemTitleHelp"] = title,
            ["MenuItemLocation"] = location,
            ["ShowInMenu"] = show,
            ["HasChildren"] = false,
            ["HasNoChildren"] = true,
            ["ChildrenMenuItems"] = new List<Dictionary<string, object>>()
        };

        private static Dictionary<string, object> MenuGroup(string id, string title, string location, bool show,
            IEnumerable<Dictionary<string, object>> children) => new()
        {
            ["MenuItemID"] = id, ["MenuItemTitle"] = title, ["MenuItemTitleHelp"] = title,
            ["MenuItemLocation"] = location, ["ShowInMenu"] = show,
            ["HasChildren"] = true, ["HasNoChildren"] = false,
            ["ChildrenMenuItems"] = children.ToList()
        };

        private static Dictionary<string, object> Child(string id, string title, string location) => new()
        {
            ["ChildMenuItemID"] = id, ["ChildMenuItemTitle"] = title,
            ["ChildMenuItemTitleHelp"] = title, ["ChildMenuItemLocation"] = location,
            ["ChildShowInMenu"] = true
        };

        private List<Dictionary<string, object>> GalleryImages()
        {
            return Content.Value.Keys
                .Where(n => n.StartsWith("static/images/gallery/", StringComparison.Ordinal)
                    && (n.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
                .Take(20)
                .Select(n => new Dictionary<string, object>
                {
                    ["ImageSRC"] = _mountPath + "/" + n,
                    ["ImageTitle"] = _systemName,
                    ["ImageInfo"] = string.Empty
                }).ToList();
        }

        private string Render(string original, string source, Dictionary<string, object> variables,
            bool authenticated, bool admin, int depth)
        {
            if (depth > 12) return string.Empty;
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            var output = new StringBuilder(source.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                string clean = lines[i].Trim();
                if (clean.StartsWith("<!--#include file=\"", StringComparison.Ordinal))
                {
                    int start = clean.IndexOf('"') + 1;
                    int end = clean.IndexOf('"', start);
                    if (start > 0 && end > start && TryNormalize(clean.Substring(start, end - start).TrimStart('/'), out string include))
                    {
                        byte[] bytes = ReadResource(include);
                        if (bytes != null) output.AppendLine(Render(include, Encoding.UTF8.GetString(bytes), variables, authenticated, admin, depth + 1));
                    }
                    continue;
                }

                if (TryMarker(clean, "ArrayBegin}", out string arrayKey))
                {
                    int end = FindEnd(lines, i + 1, "{" + arrayKey + "ArrayEnd}");
                    if (end < 0) continue;
                    string block = string.Join("\n", lines.Skip(i + 1).Take(end - i - 1));
                    if (variables.TryGetValue(arrayKey, out object value) && value is IEnumerable<Dictionary<string, object>> rows)
                    {
                        foreach (Dictionary<string, object> row in rows)
                        {
                            var merged = new Dictionary<string, object>(variables, StringComparer.Ordinal);
                            foreach (var pair in row) merged[pair.Key] = pair.Value;
                            output.AppendLine(Render(original, block, merged, authenticated, admin, depth + 1));
                        }
                    }
                    i = end;
                    continue;
                }

                if (clean.StartsWith("{If", StringComparison.Ordinal) && clean.EndsWith("Begin}", StringComparison.Ordinal))
                {
                    string key = clean.Substring(3, clean.Length - 9);
                    int end = FindEnd(lines, i + 1, "{If" + key + "End}");
                    if (end < 0) continue;
                    bool enabled = variables.TryGetValue(key, out object value) && value is bool flag && flag;
                    if (enabled)
                    {
                        string block = string.Join("\n", lines.Skip(i + 1).Take(end - i - 1));
                        output.AppendLine(Render(original, block, variables, authenticated, admin, depth + 1));
                    }
                    i = end;
                    continue;
                }

                if (clean.StartsWith("{Is", StringComparison.Ordinal) && clean.EndsWith("AuthenticatedBegin}", StringComparison.Ordinal))
                {
                    string endMarker = clean.Replace("Begin}", "End}", StringComparison.Ordinal);
                    int end = FindEnd(lines, i + 1, endMarker);
                    bool show;
                    if (clean.StartsWith("{IsNotAdminAuthenticated", StringComparison.Ordinal)) show = !admin;
                    else if (clean.StartsWith("{IsAdminAuthenticated", StringComparison.Ordinal)) show = admin;
                    else show = clean.StartsWith("{IsNotAuthenticated", StringComparison.Ordinal) ? !authenticated : authenticated;
                    if (show && end >= 0)
                    {
                        string block = string.Join("\n", lines.Skip(i + 1).Take(end - i - 1));
                        output.AppendLine(Render(original, block, variables, authenticated, admin, depth + 1));
                    }
                    if (end >= 0) i = end;
                    continue;
                }

                output.AppendLine(ReplaceScalars(lines[i], variables));
            }
            return output.ToString();
        }

        private static bool TryMarker(string line, string suffix, out string key)
        {
            key = null;
            if (!line.StartsWith('{') || !line.EndsWith(suffix, StringComparison.Ordinal)) return false;
            key = line.Substring(1, line.Length - suffix.Length - 1);
            return key.Length > 0;
        }

        private static int FindEnd(string[] lines, int start, string marker)
        {
            for (int i = start; i < lines.Length; i++)
                if (string.Equals(lines[i].Trim(), marker, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static string ReplaceScalars(string line, Dictionary<string, object> variables)
        {
            foreach (var pair in variables)
                if (pair.Value is not System.Collections.IEnumerable || pair.Value is string)
                    line = line.Replace("{" + pair.Key + "}", pair.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
            return Token.Replace(line, match => match.Value.EndsWith("Text}", StringComparison.Ordinal)
                ? Humanize(match.Value.Substring(1, match.Value.Length - 6)) : string.Empty);
        }

        private static string Humanize(string value)
        {
            return Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2");
        }

        private string RewriteAssetPaths(string value)
        {
            return value.Replace("../static/", _mountPath + "/static/", StringComparison.Ordinal)
                .Replace("\"/static/", "\"" + _mountPath + "/static/", StringComparison.Ordinal)
                .Replace("'/static/", "'" + _mountPath + "/static/", StringComparison.Ordinal)
                .Replace("\"static/", "\"" + _mountPath + "/static/", StringComparison.Ordinal)
                .Replace("'static/", "'" + _mountPath + "/static/", StringComparison.Ordinal)
                .Replace("url(/static/", "url(" + _mountPath + "/static/", StringComparison.Ordinal)
                .Replace("url(static/", "url(" + _mountPath + "/static/", StringComparison.Ordinal)
                .Replace("\"/welcomescreen/", "\"" + _mountPath + "/welcomescreen/", StringComparison.Ordinal)
                .Replace("'/welcomescreen/", "'" + _mountPath + "/welcomescreen/", StringComparison.Ordinal)
                .Replace("\"local/static/", "\"" + _mountPath + "/static/", StringComparison.Ordinal);
        }

        private static bool TryNormalize(string value, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string decoded;
            try { decoded = Uri.UnescapeDataString(value).Replace('\\', '/'); }
            catch { return false; }
            if (decoded.StartsWith('/') || decoded.Contains('\0')) return false;
            string[] parts = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(p => p == "." || p == "..")) return false;
            normalized = string.Join('/', parts);
            return normalized.Length > 0;
        }

        private static byte[] ReadResource(string relative)
        {
            return Content.Value.TryGetValue(relative, out byte[] bytes) ? bytes : null;
        }

        private void HandleLogin(IOSHttpRequest request, IOSHttpResponse response)
        {
            string attemptKey = "login:" + request.RemoteIPEndPoint?.Address;
            if (!AllowAttempt(attemptKey, _loginAttempts, _loginWindow))
            {
                WriteError(response, (HttpStatusCode)429, "Too many login attempts; try again later");
                return;
            }
            if (request.ContentLength64 < 0 || request.ContentLength64 > 16 * 1024)
            {
                WriteError(response, HttpStatusCode.RequestEntityTooLarge, "Login request is too large");
                return;
            }
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8,
                true, 1024, true)) body = reader.ReadToEnd();
            var form = System.Web.HttpUtility.ParseQueryString(body);
            string username = (form["username"] ?? string.Empty).Trim();
            string password = form["password"] ?? string.Empty;
            UserAccount account = FindAccount(username);
            string token = account == null ? string.Empty : _authentication.Authenticate(
                account.PrincipalID, Util.Md5Hash(password), SessionLifetimeMinutes);
            if (string.IsNullOrEmpty(token))
            {
                WriteError(response, HttpStatusCode.Unauthorized, "Invalid user name or password");
                return;
            }
            ClearAttempts(attemptKey);
            string cookie = "ContinuumWebUI=" + account.PrincipalID + "." + Uri.EscapeDataString(token)
                + "; Path=" + _mountPath + "; Max-Age=" + _sessionSeconds + "; HttpOnly; SameSite=Lax"
                + (request.IsSecured ? "; Secure" : string.Empty);
            response.AddHeader("Set-Cookie", cookie);
            response.Redirect(_mountPath + "/", HttpStatusCode.SeeOther);
        }

        private void HandleLogout(IOSHttpRequest request, IOSHttpResponse response)
        {
            if (TryReadCookie(request, out UUID principal, out string token))
                _authentication.Release(principal, token);
            response.AddHeader("Set-Cookie", "ContinuumWebUI=; Path=" + _mountPath + "; Max-Age=0; HttpOnly; SameSite=Lax");
            response.Redirect(_mountPath + "/", HttpStatusCode.SeeOther);
        }

        private void HandleMapRegionLookup(IOSHttpResponse response, IReadOnlyDictionary<string, string> parameters)
        {
            string callback = parameters.TryGetValue("var", out string requested) ? requested : "wcRegionName";
            if (!Regex.IsMatch(callback, "^[A-Za-z_$][A-Za-z0-9_$]{0,63}$")
                || !Int32.TryParse(parameters.TryGetValue("grid_x", out string x) ? x : string.Empty, out int gridX)
                || !Int32.TryParse(parameters.TryGetValue("grid_y", out string y) ? y : string.Empty, out int gridY))
            {
                WriteError(response, HttpStatusCode.BadRequest, "Invalid map coordinates");
                return;
            }

            OpenSim.Services.Interfaces.GridRegion region = _integration.GetMapRegion(gridX, gridY);
            object payload = region == null
                ? new { error = true }
                : new
                {
                    error = false,
                    regionName = region.RegionName,
                    xloc = region.RegionCoordX,
                    yloc = region.RegionCoordY,
                    xsize = region.RegionSizeX,
                    ysize = region.RegionSizeY
                };
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/javascript; charset=utf-8";
            response.AddHeader("Cache-Control", "no-store");
            response.AddHeader("X-Content-Type-Options", "nosniff");
            response.RawBuffer = Encoding.UTF8.GetBytes(callback + " = " + JsonSerializer.Serialize(payload) + ";");
        }

        private UserAccount GetAuthenticatedUser(IOSHttpRequest request)
        {
            if (!TryReadCookie(request, out UUID principal, out string token)) return null;
            if (!_authentication.Verify(principal, token, SessionLifetimeMinutes)) return null;
            return _accounts.GetUserAccount(UUID.Zero, principal);
        }

        // OpenSim authentication lifetimes are expressed in minutes, while HTTP Max-Age is seconds.
        private int SessionLifetimeMinutes => Math.Max(1, (_sessionSeconds + 59) / 60);

        private bool AllowAttempt(string key, int maximum, TimeSpan window)
        {
            DateTime now = DateTime.UtcNow;
            lock (_attemptsLock)
            {
                if (!_attempts.ContainsKey(key) && _attempts.Count >= 10000)
                {
                    foreach (string existingKey in _attempts.Keys.ToArray())
                    {
                        Queue<DateTime> existing = _attempts[existingKey];
                        TimeSpan retention = existingKey.StartsWith("register:", StringComparison.Ordinal)
                            ? _registrationWindow : _loginWindow;
                        while (existing.Count > 0 && now - existing.Peek() >= retention) existing.Dequeue();
                        if (existing.Count == 0) _attempts.Remove(existingKey);
                    }
                    if (_attempts.Count >= 10000) return false;
                }
                if (!_attempts.TryGetValue(key, out Queue<DateTime> entries))
                    _attempts[key] = entries = new Queue<DateTime>();
                while (entries.Count > 0 && now - entries.Peek() >= window) entries.Dequeue();
                if (entries.Count >= maximum) return false;
                entries.Enqueue(now);
                return true;
            }
        }

        private void ClearAttempts(string key)
        {
            lock (_attemptsLock) _attempts.Remove(key);
        }

        private static bool TryReadCookie(IOSHttpRequest request, out UUID principal, out string token)
        {
            principal = UUID.Zero;
            token = null;
            string header = request.Headers["Cookie"];
            if (string.IsNullOrEmpty(header)) return false;
            foreach (string item in header.Split(';'))
            {
                string part = item.Trim();
                if (!part.StartsWith("ContinuumWebUI=", StringComparison.Ordinal)) continue;
                string value = part.Substring("ContinuumWebUI=".Length);
                int dot = value.IndexOf('.');
                if (dot < 1 || !UUID.TryParse(value.Substring(0, dot), out principal)) return false;
                token = Uri.UnescapeDataString(value.Substring(dot + 1));
                return token.Length > 0;
            }
            return false;
        }

        private UserAccount FindAccount(string username)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length > 128) return null;
            string normalized = username.Replace('.', ' ');
            int split = normalized.IndexOf(' ');
            if (split > 0)
                return _accounts.GetUserAccount(UUID.Zero, normalized.Substring(0, split), normalized.Substring(split + 1).Trim());
            List<UserAccount> matches = _accounts.GetUserAccounts(UUID.Zero, normalized);
            return matches?.FirstOrDefault(a => string.Equals(a.Name, normalized, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(a.LastName, "Resident", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.FirstName, normalized, StringComparison.OrdinalIgnoreCase)));
        }

        private static Dictionary<string, string> RequestParameters(IOSHttpRequest request)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in request.QueryAsDictionary)
                values[pair.Key] = pair.Value;
            return values;
        }

        private bool IsSameOrigin(IOSHttpRequest request)
        {
            string origin = request.Headers["Origin"];
            if (string.IsNullOrWhiteSpace(origin)) return true;
            return _integration.IsExpectedOrigin(origin, request.Url);
        }

        private static Dictionary<string, byte[]> LoadContent()
        {
            string resource = ThisAssembly.GetManifestResourceNames()
                .Single(n => n.EndsWith("WhiteCoreWebUI.zip", StringComparison.Ordinal));
            using Stream stream = ThisAssembly.GetManifestResourceStream(resource);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string key = entry.FullName.Replace('\\', '/').TrimStart('/');
                using Stream input = entry.Open();
                using var memory = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
                input.CopyTo(memory);
                result[key] = memory.ToArray();
            }
            return result;
        }

        private static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8", ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8", ".json" or ".map" => "application/json",
            ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif",
            ".svg" => "image/svg+xml", ".ico" => "image/x-icon", ".webmanifest" => "application/manifest+json",
            ".woff" => "font/woff", ".woff2" => "font/woff2", ".ttf" => "font/ttf", ".otf" => "font/otf",
            _ => "application/octet-stream"
        };

        private static void WriteError(IOSHttpResponse response, HttpStatusCode status, string message)
        {
            response.StatusCode = (int)status;
            response.ContentType = "text/plain; charset=utf-8";
            response.AddHeader("Cache-Control", "no-store");
            response.AddHeader("X-Content-Type-Options", "nosniff");
            response.RawBuffer = Encoding.UTF8.GetBytes(message);
        }


        private static void WriteText(IOSHttpResponse response, HttpStatusCode status, string message)
        {
            response.StatusCode = (int)status;
            response.ContentType = "text/plain; charset=utf-8";
            response.AddHeader("Cache-Control", "no-store");
            response.AddHeader("X-Content-Type-Options", "nosniff");
            response.RawBuffer = Encoding.UTF8.GetBytes(message ?? string.Empty);
        }
    }
}
