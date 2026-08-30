using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;

namespace OpenSim.Continuum.WebUI
{
    /// <summary>
    /// Adapts the WhiteCore WebUI page model to native OpenSimulator services.
    /// The adapter never queries provider tables directly, so MySQL, PostgreSQL
    /// and SQLite remain behind their existing service implementations.
    /// </summary>
    internal sealed class WebUIServiceIntegration
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(WebUIServiceIntegration));
        private readonly IUserAccountService _accounts;
        private readonly IGridService _grid;
        private readonly IGridUserService _gridUsers;
        private readonly IFriendsService _friends;
        private readonly IUserProfilesService _profiles;
        private readonly IAbuseReportsService _abuse;
        private readonly IExperienceService _experiences;
        private readonly IGroupsService _groups;
        private readonly IEstateDataService _estates;
        private readonly int _adminLevel;
        private readonly string _hopBase;
        private readonly string _textureBase;
        private readonly string _publicBase;
        private readonly string _mapBase;
        private readonly bool _allowRegistration;
        private readonly int _minimumRegistrationAge;
        private readonly string _searchUrl;
        private readonly string _economyUrl;
        private readonly string _economySecret;
        private readonly int _rpcTimeoutMs;
        private readonly TimeSpan _gridStatusCacheLifetime;
        private readonly object _gridStatusLock = new();
        private DateTime _gridStatusExpiresUtc;
        private int _cachedRegionCount;
        private int _cachedAccountCount;
        private int _cachedOnlineCount;

        internal WebUIServiceIntegration(IConfigSource config, IConfig section, IUserAccountService accounts)
        {
            _accounts = accounts;
            _adminLevel = Math.Clamp(section.GetInt("AdminUserLevel", 200), 1, 255);
            string hopBase = section.GetString("HopURLBase", string.Empty).Trim();
            if (!Uri.TryCreate(hopBase, UriKind.Absolute, out Uri hopURI)
                || (hopURI.Scheme != "hop" && hopURI.Scheme != "secondlife")
                || string.IsNullOrWhiteSpace(hopURI.Host))
                throw new InvalidOperationException("ContinuumWebUI HopURLBase must be a hop:// or secondlife:// URI with the externally reachable grid authority");
            _hopBase = hopBase.TrimEnd('/');
            _textureBase = section.GetString("TextureURL", string.Empty).TrimEnd('/');
            _publicBase = section.GetString("PublicURL", string.Empty).TrimEnd('/');
            _mapBase = section.GetString("MapServiceURL", _publicBase).TrimEnd('/');
            _allowRegistration = section.GetBoolean("AllowRegistration", true);
            _minimumRegistrationAge = Math.Clamp(section.GetInt("MinimumRegistrationAge", 16), 0, 100);
            _searchUrl = section.GetString("SearchServiceURL", string.Empty).Trim();
            _economyUrl = section.GetString("EconomyServiceURL", string.Empty).Trim();
            _economySecret = section.GetString("EconomySharedSecret", string.Empty);
            _rpcTimeoutMs = Math.Clamp(section.GetInt("ServiceTimeoutMilliseconds", 5000), 500, 30000);
            _gridStatusCacheLifetime = TimeSpan.FromSeconds(Math.Clamp(section.GetInt("GridStatusCacheSeconds", 30), 5, 300));
            _grid = Load<IGridService>(config, section, "GridService", "OpenSim.Services.GridService.dll:GridService");
            _gridUsers = Load<IGridUserService>(config, section, "GridUserService", "OpenSim.Services.UserAccountService.dll:GridUserService");
            _friends = Load<IFriendsService>(config, section, "FriendsService", "OpenSim.Services.FriendsService.dll:FriendsService");
            _profiles = Load<IUserProfilesService>(config, section, "UserProfilesService", "OpenSim.Services.UserProfilesService.dll:UserProfilesService");
            _abuse = Load<IAbuseReportsService>(config, section, "AbuseReportsService", "OpenSim.Services.AbuseReportsService.dll:AbuseReportsService");
            _experiences = Load<IExperienceService>(config, section, "ExperienceService", "OpenSim.Services.ExperienceService.dll:ExperienceService");
            _groups = Load<IGroupsService>(config, section, "GroupsService", "OpenSim.Addons.Groups.dll:GroupsService");
            _estates = Load<IEstateDataService>(config, section, "EstateDataService", "OpenSim.Services.EstateService.dll:EstateDataService");
        }

        internal bool IsAdmin(UserAccount account) => account != null && account.UserLevel >= _adminLevel;

        internal bool IsExpectedOrigin(string origin, Uri requestUri)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri parsed)) return false;
            if (SameAuthority(parsed, requestUri)) return true;
            return Uri.TryCreate(_publicBase, UriKind.Absolute, out Uri publicUri)
                && SameAuthority(parsed, publicUri);
        }

        private static bool SameAuthority(Uri left, Uri right) => right != null
            && string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Authority, right.Authority, StringComparison.OrdinalIgnoreCase);

        internal static bool IsSuccessfulMutation(string message) => message is
            "Account created successfully" or "Password updated" or "Email address updated"
            or "Abuse report updated" or "User account updated" or "Profile updated";

        internal static bool IsWarningMutation(string message) => message is
            "Profile saved, but interests could not be updated";

        internal string ContentSecurityPolicy
        {
            get
            {
                var imageSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "'self'", "data:" };
                foreach (string source in new[] { _textureBase, _mapBase })
                    if (Uri.TryCreate(source, UriKind.Absolute, out Uri uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        imageSources.Add(uri.GetLeftPart(UriPartial.Authority));
                return "default-src 'self'; img-src " + string.Join(' ', imageSources)
                    + "; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; font-src 'self' data:; "
                    + "connect-src 'self'; form-action 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'self'";
            }
        }

        internal GridRegion GetMapRegion(int gridX, int gridY)
        {
            if (_grid == null || gridX < 0 || gridY < 0 || gridX > 8_000_000 || gridY > 8_000_000)
                return null;
            return Safe(() => _grid.GetRegionByPosition(UUID.Zero, checked(gridX * 256), checked(gridY * 256)));
        }

        internal string ResolvePage(string relative)
        {
            return relative.ToLowerInvariant() switch
            {
                "world_map.html" => "world.html",
                "events.html" => "events/events.html",
                "classifieds.html" => "classifieds/classifieds.html",
                "destinations.html" => "destinations.html",
                "profile.html" => "user/profile.html",
                "profile_edit.html" => "user/profile_edit.html",
                "friends.html" => "user/friends.html",
                "groups.html" => "user/groups.html",
                "transactions.html" => "user/transactions.html",
                "abuse_manager.html" => "admin/abuse_manager.html",
                "abuse_report.html" => "admin/abuse_report.html",
                _ => relative
            };
        }

        internal void Populate(string page, IReadOnlyDictionary<string, string> parameters,
            UserAccount currentUser, Dictionary<string, object> vars)
        {
            AddCommonLabels(vars);
            vars["MainServerURL"] = _publicBase;
            vars["WorldMapServiceURL"] = _mapBase;
            vars["HopURLBase"] = _hopBase;
            string key = page.ToLowerInvariant();
            if (key is "index.html" or "home.html" or "welcomescreen/index.html" or "welcomescreen/gridstatus.html")
                AddGridStatus(vars);
            if (key is "region_list.html" or "region_search.html")
                AddRegions(vars, parameters.TryGetValue("regionname", out string name) ? name : string.Empty);
            if (key == "world.html") AddWorldMap(vars);
            if (key == "events/events.html") AddEvents(vars, parameters, currentUser);
            if (key == "classifieds/classifieds.html") AddClassifieds(vars, parameters, currentUser);
            if (key == "destinations.html") AddDestinations(vars, parameters, currentUser);
            if (key == "online_users.html") AddOnlineUsers(vars);
            if (key == "user_search.html") AddUserSearch(vars, parameters, currentUser);
            if (key == "register.html") AddRegistration(vars);
            if (key == "admin/user_register.html") AddRegistration(vars, true);
            if (key is "userhome.html" or "user/userhome.html") AddUserHome(vars, currentUser);
            if (key == "user/transactions.html") AddTransactions(vars, currentUser, parameters);
            if (key is "user/profile.html" or "webprofile/modal_profile.html")
                AddProfile(vars, AccountFromParameter(parameters, currentUser), currentUser);
            if (key == "user/profile_edit.html") AddEditableProfile(vars, currentUser);
            if (key == "webprofile/modal_picks.html") AddPicks(vars, AccountFromParameter(parameters, currentUser), currentUser);
            if (key == "webprofile/modal_regions.html") AddOwnedRegions(vars, AccountFromParameter(parameters, currentUser), currentUser);
            if (key == "webprofile/modal_groups.html") AddPublicGroups(vars, AccountFromParameter(parameters, currentUser), currentUser);
            if (key == "regionprofile/modal_profile.html") AddRegionProfile(vars, parameters, currentUser);
            if (key == "regionprofile/modal_parcels.html") AddRegionParcels(vars, parameters);
            if (key == "user/friends.html") AddFriends(vars, currentUser);
            if (key == "user/groups.html") AddGroups(vars, currentUser);
            if (key == "user/region_manager.html") AddManagedRegions(vars, currentUser, false);
            if (key == "admin/region_manager.html") AddManagedRegions(vars, currentUser, true);
            if (key == "admin/statistics.html") AddStatistics(vars, currentUser);
            if (key == "admin/transactions.html") AddAdminTransactions(vars, currentUser, parameters);
            if (key == "user/estate_manager.html") AddEstates(vars, currentUser, false);
            if (key == "admin/estate_manager.html") AddEstates(vars, currentUser, true);
            if (key == "user/classifieds.html") AddOwnedClassifieds(vars, currentUser);
            if (key == "user/events.html") AddOwnedEvents(vars, currentUser);
            if (key == "experiences.html") AddExperiences(vars, currentUser, parameters);
            if (key == "admin/abuse_manager.html") AddAbuseList(vars, currentUser, parameters);
            if (key == "admin/abuse_report.html") AddAbuseReport(vars, currentUser, parameters);
            if (key == "admin/user_manager.html") AddUserManager(vars, currentUser);
            if (key is "admin/user_edit.html" or "admin/user_password.html")
                AddAdminUser(vars, currentUser, parameters);
            if (key == "user/email.html" && currentUser != null)
            {
                vars["UserEmail"] = H(currentUser.Email); vars["ChangeEmailText"] = "Change email";
                vars["NewEmailText"] = "New email"; vars["NewEmailConfirmationText"] = "Confirm new email";
                vars["CancelText"] = "Cancel";
            }
            if (key == "user/password.html")
            {
                vars["ChangePasswordText"] = "Change password"; vars["PasswordText"] = "Current password";
                vars["NewPasswordText"] = "New password"; vars["NewPasswordConfirmationText"] = "Confirm new password";
                vars["CancelText"] = "Cancel";
            }
        }

        internal bool HandlePost(string page, IReadOnlyDictionary<string, string> form,
            UserAccount currentUser, out string message)
        {
            message = string.Empty;
            switch (page.ToLowerInvariant())
            {
                case "register.html": return Register(form, out message);
                case "admin/user_register.html":
                    if (!IsAdmin(currentUser)) { message = "Administrator access required"; return true; }
                    return Register(form, out message, true);
                case "user/password.html": return ChangePassword(form, currentUser, out message);
                case "user/email.html": return ChangeEmail(form, currentUser, out message);
                case "user/profile_edit.html": return UpdateProfile(form, currentUser, out message);
                case "admin/abuse_report.html": return UpdateAbuse(form, currentUser, out message);
                case "admin/user_edit.html": return UpdateUser(form, currentUser, out message);
                case "admin/user_password.html": return ResetUserPassword(form, currentUser, out message);
                default: return false;
            }
        }

        private void AddRegistration(Dictionary<string, object> vars, bool administrator = false)
        {
            vars["Registrations"] = administrator || _allowRegistration;
            vars["NoRegistrations"] = !administrator && !_allowRegistration;
            vars["SubmitURL"] = administrator ? "admin/user_register.html" : "register.html";
            vars["AvatarName"] = string.Empty;
            vars["RegistrationText"] = administrator ? "Create resident" : "Create an account";
            vars["AvatarNameText"] = "Avatar name";
            vars["AvatarPasswordText"] = "Password";
            vars["AvatarPasswordConfirmationText"] = "Confirm password";
            vars["AvatarScopeText"] = "Scope ID";
            vars["FirstNameText"] = "First name";
            vars["LastNameText"] = "Last name";
            vars["UserCityText"] = "City";
            vars["UserDOBText"] = "Date of birth";
            vars["UserEmailText"] = "Email";
            vars["UserHomeRegionText"] = "Home region";
            vars["UserTypeText"] = "Account type";
            vars["TermsOfServiceText"] = "Terms of service";
            vars["Accept"] = "Accept";
            vars["ToSMessage"] = "By creating an account, you agree to this grid's terms of service.";
            vars["TermsOfServiceAccept"] = "I accept the terms of service";
            vars["Months"] = Enumerable.Range(1, 12).Select(n => Option(n.ToString("00", CultureInfo.InvariantCulture))).ToList();
            vars["Days"] = Enumerable.Range(1, 31).Select(n => Option(n.ToString("00", CultureInfo.InvariantCulture))).ToList();
            int year = DateTime.UtcNow.Year;
            int youngestYear = year - _minimumRegistrationAge;
            vars["Years"] = Enumerable.Range(year - 100, 101 - _minimumRegistrationAge).Reverse()
                .Select(n => Option(n.ToString(CultureInfo.InvariantCulture))).ToList();
            vars["AvatarArchive"] = new List<Dictionary<string, object>>();
            vars["RegionList"] = (Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 1000)) ?? new List<GridRegion>())
                .Select(r => new Dictionary<string, object> { ["RegionUUID"] = r.RegionID.ToString(), ["RegionName"] = H(r.RegionName) }).ToList();
        }

        private bool Register(IReadOnlyDictionary<string, string> form, out string message, bool administrator = false)
        {
            message = string.Empty;
            if (!_allowRegistration && !administrator) { message = "Registration is disabled"; return true; }
            string avatarName = Value(form, "AvatarName").Trim();
            string first = Value(form, "FirstName").Trim();
            string last = Value(form, "LastName").Trim();
            if (first.Length == 0 && avatarName.Length > 0)
            {
                string[] parts = avatarName.Replace('.', ' ').Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                first = parts.Length > 0 ? parts[0] : string.Empty;
                last = parts.Length > 1 ? parts[1] : "Resident";
            }
            if (last.Length == 0) last = "Resident";
            string password = Value(form, "AvatarPassword");
            string confirmation = Value(form, "AvatarPassword2");
            string email = Value(form, "UserEmail").Trim();
            if (!ValidName(first) || !ValidName(last)) { message = "Avatar name is invalid"; return true; }
            if (password.Length < 8 || password.Length > 128 || password != confirmation) { message = "Passwords must match and contain at least eight characters"; return true; }
            if (!ValidEmail(email)) { message = "Email address is invalid"; return true; }
            if (!DateTime.TryParseExact(Value(form, "UserDOBYear") + "-" + Value(form, "UserDOBMonth") + "-" + Value(form, "UserDOBDay"),
                "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime birthDate)
                || birthDate > DateTime.UtcNow.Date.AddYears(-_minimumRegistrationAge))
            { message = "A valid date of birth meeting the minimum registration age is required"; return true; }
            if (!form.TryGetValue("ToSAccept", out string accepted) || accepted != "Accepted") { message = "The terms of service must be accepted"; return true; }
            if (_accounts.GetUserAccount(UUID.Zero, first, last) != null) { message = "That avatar name is already registered"; return true; }
            try
            {
                UserAccount created = CreateUser(first, last, password, email);
                if (created == null) { message = "Account creation failed"; return true; }
                if (_gridUsers != null && form.TryGetValue("UserHomeRegion", out string homeText) && UUID.TryParse(homeText, out UUID home))
                    _gridUsers.SetHome(created.PrincipalID.ToString(), home, new Vector3(128, 128, 25), new Vector3(0, 1, 0));
                message = "Account created successfully";
            }
            catch (TargetInvocationException e)
            {
                Log.WarnFormat("[CONTINUUM WEBUI]: Account creation failed: {0}", e.InnerException?.Message ?? e.Message);
                message = "Account creation failed; contact a grid administrator";
            }
            catch (Exception e)
            {
                Log.WarnFormat("[CONTINUUM WEBUI]: Account creation failed: {0}", e.Message);
                message = "Account creation failed; contact a grid administrator";
            }
            return true;
        }

        private UserAccount CreateUser(string first, string last, string password, string email)
        {
            Type serviceType = _accounts.GetType();
            MethodInfo local = serviceType.GetMethod("CreateUser", new[] { typeof(UUID), typeof(UUID),
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) });
            if (local != null)
                return local.Invoke(_accounts, new object[] { UUID.Zero, UUID.Random(), first, last,
                    password, email, string.Empty }) as UserAccount;

            MethodInfo remote = serviceType.GetMethod("CreateUser", new[] { typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(UUID) });
            if (remote != null)
                return remote.Invoke(_accounts, new object[] { first, last, password, email, UUID.Zero }) as UserAccount;

            Log.ErrorFormat("[CONTINUUM WEBUI]: Account service {0} does not expose an OpenSim CreateUser method",
                serviceType.FullName);
            return null;
        }

        private bool ChangePassword(IReadOnlyDictionary<string, string> form, UserAccount account, out string message)
        {
            message = "Authentication required";
            if (account == null) return true;
            string oldPassword = Value(form, "password");
            string password = Value(form, "passwordnew");
            string confirmation = Value(form, "passwordconf");
            if (password.Length < 8 || password.Length > 128 || password != confirmation) { message = "New passwords must match and contain at least eight characters"; return true; }
            string token = _authenticationForPassword?.Authenticate(account.PrincipalID, Util.Md5Hash(oldPassword), 60);
            if (string.IsNullOrEmpty(token)) { message = "Current password is incorrect"; return true; }
            _authenticationForPassword.Release(account.PrincipalID, token);
            message = _authenticationForPassword.SetPassword(account.PrincipalID, password) ? "Password updated" : "Password update failed";
            return true;
        }

        private IAuthenticationService _authenticationForPassword;
        internal void SetAuthenticationService(IAuthenticationService authentication) => _authenticationForPassword = authentication;

        private bool ChangeEmail(IReadOnlyDictionary<string, string> form, UserAccount account, out string message)
        {
            message = "Authentication required";
            if (account == null) return true;
            string email = Value(form, "emailnew").Trim();
            if (email != Value(form, "emailnewconf").Trim() || !ValidEmail(email)) { message = "Email addresses do not match or are invalid"; return true; }
            account.Email = email;
            message = _accounts.StoreUserAccount(account) ? "Email address updated" : "Email update failed";
            return true;
        }

        private bool UpdateAbuse(IReadOnlyDictionary<string, string> form, UserAccount admin, out string message)
        {
            message = "Administrator access required";
            if (!IsAdmin(admin) || _abuse == null) return true;
            if (!TryInt(form, "cardid", out int id)) { message = "Invalid abuse report"; return true; }
            string status = Value(form, "Active");
            if (!new[] { "Open", "Investigating", "Resolved", "Closed" }.Contains(status, StringComparer.OrdinalIgnoreCase))
                status = "Open";
            string notes = Value(form, "AbuseNoteText");
            // The abuse service accepts at most 16 KiB of UTF-8 moderator notes.
            // Four UTF-8 bytes per UTF-16 code unit is a conservative cross-provider bound.
            if (notes.Length > 4096) notes = notes.Substring(0, 4096);
            message = _abuse.UpdateReport(id, status, notes, admin.PrincipalID, admin.Name) ? "Abuse report updated" : "Abuse report update failed";
            return true;
        }

        private void AddGridStatus(Dictionary<string, object> vars)
        {
            int regionCount;
            int accountCount;
            int onlineCount;
            lock (_gridStatusLock)
            {
                if (DateTime.UtcNow >= _gridStatusExpiresUtc)
                {
                    List<GridRegion> regions = Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 10000)) ?? new();
                    List<UserAccount> accounts = Safe(() => _accounts.GetUserAccountsWhere(UUID.Zero, "1=1")) ?? new();
                    int online = 0;
                    if (_gridUsers != null && accounts.Count > 0)
                    {
                        string[] ids = accounts.Take(10000).Select(a => a.PrincipalID.ToString()).ToArray();
                        GridUserInfo[] infos = Safe(() => _gridUsers.GetGridUserInfo(ids));
                        online = infos?.Count(i => i != null && i.Online) ?? 0;
                    }
                    _cachedRegionCount = regions.Count;
                    _cachedAccountCount = accounts.Count;
                    _cachedOnlineCount = online;
                    _gridStatusExpiresUtc = DateTime.UtcNow.Add(_gridStatusCacheLifetime);
                }
                regionCount = _cachedRegionCount;
                accountCount = _cachedAccountCount;
                onlineCount = _cachedOnlineCount;
            }
            vars["GridStatus"] = "status";
            vars["GridOnline"] = regionCount > 0 ? "Online" : "Offline";
            vars["TotalUserCount"] = "Residents";
            vars["UserCount"] = accountCount;
            vars["TotalRegionCount"] = "Online regions";
            vars["RegionCount"] = regionCount;
            vars["UniqueVisitors"] = "Registered residents";
            vars["UniqueVisitorCount"] = accountCount;
            vars["OnlineNow"] = "Online now";
            vars["OnlineNowCount"] = onlineCount;
            vars["VoiceActiveLabel"] = "Voice";
            vars["VoiceActive"] = "Configured by grid";
            vars["CurrencyActiveLabel"] = "Currency";
            vars["CurrencyActive"] = "Configured by grid";
        }

        private void AddRegions(Dictionary<string, object> vars, string search)
        {
            search = Trimmed(search, 128);
            List<GridRegion> regions = string.IsNullOrWhiteSpace(search)
                ? Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 10000))
                : Safe(() => _grid?.GetRegionsByName(UUID.Zero, search, 100));
            regions ??= new List<GridRegion>();
            var rows = regions.Select(RegionRow).ToList();
            vars["RegionList"] = rows;
            vars["RegionsList"] = rows;
            vars["HaveData"] = rows.Count > 0;
            vars["NoData"] = rows.Count == 0;
            vars["RegionSearch"] = H(search);
            vars["NoDetailsText"] = rows.Count == 0 ? "No matching regions" : string.Empty;
            vars["CurrentPage"] = 1;
            vars["BackOne"] = 0;
            vars["NextOne"] = 0;
        }

        private void AddWorldMap(Dictionary<string, object> vars)
        {
            List<GridRegion> regions = Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 10000)) ?? new();
            var rows = regions.Select(RegionRow).ToList();
            foreach (Dictionary<string, object> row in rows)
            {
                row["RegionWorldViewURL"] = "map-1-" + row["RegionLocX"] + "-" + row["RegionLocY"] + "-objects.jpg";
            }
            int centerX = regions.Count == 0 ? 1000 : (int)regions.Average(r => r.RegionCoordX);
            int centerY = regions.Count == 0 ? 1000 : (int)regions.Average(r => r.RegionCoordY);
            vars["RegionList"] = rows;
            vars["RegionListArray"] = rows;
            vars["RegionText"] = "Regions";
            vars["MapCenterX"] = centerX;
            vars["MapCenterY"] = centerY;
            vars["WorldRegionSize"] = 256;
        }

        private void AddUserManager(Dictionary<string, object> vars, UserAccount admin)
        {
            if (!IsAdmin(admin)) return;
            List<UserAccount> accounts = Safe(() => _accounts.GetUserAccountsWhere(UUID.Zero, "1=1")) ?? new();
            List<UserAccount> limitedAccounts = accounts.Take(10000).ToList();
            string[] accountIDs = limitedAccounts.Select(account => account.PrincipalID.ToString()).ToArray();
            GridUserInfo[] gridUsers = accountIDs.Length == 0 || _gridUsers == null ? Array.Empty<GridUserInfo>()
                : Safe(() => _gridUsers.GetGridUserInfo(accountIDs)) ?? Array.Empty<GridUserInfo>();
            var usersByID = new Dictionary<UUID, GridUserInfo>();
            foreach (GridUserInfo info in gridUsers)
                if (info != null && UUID.TryParse(info.UserID, out UUID userID)) usersByID[userID] = info;
            var regionsByID = new Dictionary<UUID, GridRegion>();
            var rows = new List<Dictionary<string, object>>();
            foreach (UserAccount account in limitedAccounts)
            {
                usersByID.TryGetValue(account.PrincipalID, out GridUserInfo info);
                GridRegion region = null;
                if (info != null && info.LastRegionID != UUID.Zero && !regionsByID.TryGetValue(info.LastRegionID, out region))
                {
                    region = Safe(() => _grid?.GetRegionByUUID(UUID.Zero, info.LastRegionID));
                    regionsByID[info.LastRegionID] = region;
                }
                rows.Add(new Dictionary<string, object>
                {
                    ["UserID"] = account.PrincipalID.ToString(), ["UserName"] = H(account.FormattedName),
                    ["UserDisplayName"] = H(account.DisplayName ?? string.Empty), ["UserType"] = H(AccountType(account)),
                    ["UserPictureURL"] = Texture(UUID.Zero, "static/icons/no_avatar.jpg"),
                    ["UserRegion"] = H(region?.RegionName ?? "Unknown"),
                    ["Position"] = H(info?.LastPosition.ToString() ?? string.Empty),
                    ["IsOnline"] = info?.Online == true ? "Yes" : "No"
                });
            }
            vars["UsersList"] = rows;
            vars["TotalUsersCount"] = rows.Count;
            vars["TotalUserCountText"] = "Registered residents:";
        }

        private void AddAdminUser(Dictionary<string, object> vars, UserAccount admin,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (!IsAdmin(admin)) return;
            UserAccount account = AccountFromParameter(parameters, null);
            if (account == null) return;
            vars["UserID"] = account.PrincipalID.ToString();
            vars["UserName"] = H(account.FormattedName);
            vars["EmailValue"] = H(account.Email);
            vars["UserType"] = H(AccountType(account));
            vars["UserTypeArray"] = new[] { (-1, "Disabled"), (0, "Resident"), (200, "Administrator") }
                .Select(item => new Dictionary<string, object>
                {
                    ["Index"] = item.Item1, ["Value"] = item.Item2,
                    ["selected"] = account.UserLevel == item.Item1 ? "selected" : string.Empty
                }).ToList();
            vars["EdittingText"] = "Edit resident"; vars["AdminSetUserTypeText"] = "Account access";
            vars["AdminUserTypeInfoText"] = "Disabled accounts cannot log in. Administrator access grants control of grid services exposed here.";
            vars["ChangeEmailText"] = "Email address"; vars["NewEmailText"] = "Email";
            vars["SaveUpdates"] = "Save";
        }

        private bool UpdateUser(IReadOnlyDictionary<string, string> form, UserAccount admin, out string message)
        {
            message = "Administrator access required";
            if (!IsAdmin(admin) || !UUID.TryParse(Value(form, "userid"), out UUID id)) return true;
            UserAccount account = Safe(() => _accounts.GetUserAccount(UUID.Zero, id));
            if (account == null) { message = "User account not found"; return true; }
            if (form.ContainsKey("setusertype") && int.TryParse(Value(form, "UserType"), out int level)
                && new[] { -1, 0, 200 }.Contains(level))
                account.UserLevel = level;
            else if (form.ContainsKey("updateemail"))
            {
                string email = Value(form, "email").Trim();
                if (!ValidEmail(email)) { message = "Email address is invalid"; return true; }
                account.Email = email;
            }
            else { message = "That WhiteCore account operation is not supported safely by OpenSim"; return true; }
            message = _accounts.StoreUserAccount(account) ? "User account updated" : "User account update failed";
            return true;
        }

        private bool ResetUserPassword(IReadOnlyDictionary<string, string> form, UserAccount admin, out string message)
        {
            message = "Administrator access required";
            if (!IsAdmin(admin) || !UUID.TryParse(Value(form, "userid"), out UUID id)) return true;
            if (_authenticationForPassword == null) { message = "Password service is unavailable"; return true; }
            string password = Value(form, "passwordnew");
            if (password.Length < 8 || password.Length > 128 || password != Value(form, "passwordconf"))
            { message = "Passwords must match and contain at least eight characters"; return true; }
            message = _authenticationForPassword.SetPassword(id, password) ? "Password updated" : "Password update failed";
            return true;
        }

        private Dictionary<string, object> RegionRow(GridRegion region)
        {
            return new Dictionary<string, object>
            {
                ["RegionName"] = H(region.RegionName), ["RegionID"] = region.RegionID.ToString(),
                ["RegionLocX"] = region.RegionCoordX, ["RegionLocY"] = region.RegionCoordY,
                ["RegionInfo"] = H(region.ServerURI), ["RegionStatus"] = "Online",
                ["MoreInfoText"] = "Details", ["HopUrl"] = Hop(region, Vector3.Zero)
            };
        }

        private void AddRegionProfile(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters,
            UserAccount viewer)
        {
            GridRegion region = UUID.TryParse(Value(parameters, "regionid"), out UUID regionID)
                ? Safe(() => _grid?.GetRegionByUUID(UUID.Zero, regionID)) : null;
            if (region == null) return;

            UserAccount owner = region.EstateOwner == UUID.Zero ? null
                : Safe(() => _accounts.GetUserAccount(UUID.Zero, region.EstateOwner));
            int residentsOnline = 0;
            bool showOccupancy = IsAdmin(viewer);
            if (showOccupancy && _gridUsers != null)
            {
                List<UserAccount> accounts = Safe(() => _accounts.GetUserAccountsWhere(UUID.Zero, "1=1")) ?? new();
                string[] ids = accounts.Take(10000).Select(item => item.PrincipalID.ToString()).ToArray();
                GridUserInfo[] users = ids.Length == 0 ? Array.Empty<GridUserInfo>()
                    : Safe(() => _gridUsers.GetGridUserInfo(ids)) ?? Array.Empty<GridUserInfo>();
                residentsOnline = users.Count(info => info?.Online == true && info.LastRegionID == region.RegionID);
            }

            vars["RegionID"] = region.RegionID.ToString(); vars["RegionName"] = H(region.RegionName);
            vars["RegionNameText"] = "Region"; vars["OwnerNameText"] = "Estate owner";
            vars["OwnerUUID"] = region.EstateOwner.ToString(); vars["OwnerName"] = H(owner?.FormattedName ?? "Unknown");
            vars["RegionTypeText"] = "Region type"; vars["RegionType"] = region.RegionSizeX + " x " + region.RegionSizeY;
            vars["RegionMaturityText"] = "Maturity"; vars["RegionMaturity"] = MaturityName(region.Maturity.ToString(CultureInfo.InvariantCulture));
            vars["RegionLocationText"] = "Grid location"; vars["RegionLocX"] = region.RegionCoordX; vars["RegionLocY"] = region.RegionCoordY;
            vars["RegionTerrainText"] = "Terrain"; vars["RegionTerrain"] = "Standard";
            vars["RegionOnlineText"] = "Status"; vars["RegionOnline"] = "Online";
            vars["RegionWorldViewURL"] = Texture(region.TerrainImage, "static/icons/no_picture.jpg");
            vars["HopUrl"] = Hop(region, new Vector3(128, 128, 50));
            vars["NumberOfParcelsInRegion"] = "Available in parcel view"; vars["ParcelsInRegionText"] = "Parcels";
            vars["NumberOfUsersInRegionText"] = "Residents currently in region";
            vars["NumberOfUsersInRegion"] = residentsOnline;
            vars["ShowOccupancy"] = showOccupancy;
            vars["MenuParcelTitle"] = "Parcels"; vars["UserNameText"] = "Resident";
        }

        private void AddRegionParcels(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters)
        {
            GridRegion region = UUID.TryParse(Value(parameters, "regionid"), out UUID regionID)
                ? Safe(() => _grid?.GetRegionByUUID(UUID.Zero, regionID)) : null;
            if (region == null) return;
            Hashtable response = Search("region_parcels_query", new Hashtable { ["region_id"] = region.RegionID.ToString() });
            var rows = new List<Dictionary<string, object>>();
            if (response?["data"] is ArrayList data)
            {
                foreach (Hashtable parcel in data.OfType<Hashtable>())
                {
                    ParseLanding(Text(parcel, "landing_point"), out int x, out int y, out int z);
                    rows.Add(new Dictionary<string, object>
                    {
                        ["ParcelID"] = H(Text(parcel, "parcel_id")), ["ParcelName"] = H(Text(parcel, "name")),
                        ["ParcelDescription"] = H(Text(parcel, "description")), ["ParcelArea"] = H(Text(parcel, "area")),
                        ["ParcelSnapshotURL"] = Texture(UUID.TryParse(Text(parcel, "snapshot_id"), out UUID image) ? image : UUID.Zero,
                            "static/icons/no_picture.jpg"),
                        ["ParcelHop"] = Hop(region.RegionName, x, y, z)
                    });
                }
            }
            vars["RegionID"] = region.RegionID.ToString(); vars["RegionName"] = H(region.RegionName);
            vars["RegionNameText"] = "Region"; vars["RegionTypeText"] = "Region type";
            vars["RegionType"] = region.RegionSizeX + " x " + region.RegionSizeY;
            vars["RegionLocationText"] = "Grid location"; vars["RegionLocX"] = region.RegionCoordX; vars["RegionLocY"] = region.RegionCoordY;
            vars["RegionImageURL"] = Texture(region.ParcelImage != UUID.Zero ? region.ParcelImage : region.TerrainImage,
                "static/icons/no_picture.jpg");
            vars["ParcelsInRegionText"] = "Published parcels"; vars["NumberOfParcelsInRegion"] = rows.Count;
            vars["ParcelInRegion"] = rows; vars["MenuRegionTitle"] = "Region";
        }

        private void AddManagedRegions(Dictionary<string, object> vars, UserAccount account, bool administratorView)
        {
            var regions = new List<GridRegion>();
            if (account != null && (!administratorView || IsAdmin(account)))
            {
                regions = Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 10000)) ?? new List<GridRegion>();
                if (!administratorView) regions.RemoveAll(region => region.EstateOwner != account.PrincipalID);
            }
            List<Dictionary<string, object>> rows = regions.OrderBy(region => region.RegionName)
                .Select(RegionRow).ToList();
            vars["RegionList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["UserName"] = H(account?.FormattedName ?? string.Empty); vars["RegionsText"] = "regions";
            vars["RegionListText"] = "regions"; vars["RegionText"] = "Region";
            vars["RegionLocXText"] = "Grid X"; vars["RegionLocYText"] = "Grid Y";
            vars["RegionOnlineText"] = "Status"; vars["ViewText"] = "View";
        }

        private void AddStatistics(Dictionary<string, object> vars, UserAccount administrator)
        {
            if (!IsAdmin(administrator)) return;
            List<GridRegion> regions = Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 10000)) ?? new();
            List<UserAccount> accounts = Safe(() => _accounts.GetUserAccountsWhere(UUID.Zero, "1=1")) ?? new();
            string[] accountIDs = accounts.Take(10000).Select(account => account.PrincipalID.ToString()).ToArray();
            GridUserInfo[] gridUsers = accountIDs.Length == 0 || _gridUsers == null ? Array.Empty<GridUserInfo>()
                : Safe(() => _gridUsers.GetGridUserInfo(accountIDs)) ?? Array.Empty<GridUserInfo>();
            int online = gridUsers.Count(info => info?.Online == true);

            using System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
            vars["StatisticsText"] = "Operational statistics";
            vars["ViewersText"] = "Grid services";
            vars["ViewersList"] = new List<Dictionary<string, object>>
            {
                new() { ["ViewerName"] = "Registered residents", ["ViewerCount"] = accounts.Count },
                new() { ["ViewerName"] = "Residents online", ["ViewerCount"] = online },
                new() { ["ViewerName"] = "Regions online", ["ViewerCount"] = regions.Count }
            };
            vars["GPUText"] = "Region capacity";
            vars["GPUList"] = regions.GroupBy(region => region.RegionSizeX + " x " + region.RegionSizeY)
                .Select(group => new Dictionary<string, object>
                {
                    ["GPUType"] = H(group.Key), ["GPUCount"] = group.Count()
                }).ToList();
            vars["PerformanceText"] = "Robust process"; vars["FPSText"] = "Regions online";
            vars["FPS"] = regions.Count; vars["RegionsVisitedText"] = "Registered residents";
            vars["RegionsVisited"] = accounts.Count; vars["AgentsInViewText"] = "Residents online";
            vars["AgentsInView"] = online; vars["RunTimeText"] = "Process uptime";
            vars["RunTime"] = (DateTime.Now - process.StartTime).ToString("d'.'hh':'mm':'ss", CultureInfo.InvariantCulture);
            vars["MemoryUseageText"] = "Working memory";
            vars["MemoryUseage"] = Math.Round(process.WorkingSet64 / 1048576d, 1).ToString(CultureInfo.InvariantCulture);
            vars["PingTimeText"] = "Generated"; vars["PingTime"] = DateTime.UtcNow.ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }

        private void AddEstates(Dictionary<string, object> vars, UserAccount account, bool administratorView)
        {
            var estates = new List<EstateSettings>();
            if (_estates != null && account != null && (!administratorView || IsAdmin(account)))
            {
                List<int> ids = administratorView
                    ? Safe(() => _estates.GetEstatesAll()) ?? new List<int>()
                    : Safe(() => _estates.GetEstatesByOwner(account.PrincipalID)) ?? new List<int>();
                foreach (int id in ids.Distinct().Take(10000))
                {
                    EstateSettings estate = Safe(() => _estates.LoadEstateSettings(id));
                    if (estate != null) estates.Add(estate);
                }
            }
            var rows = new List<Dictionary<string, object>>();
            foreach (EstateSettings estate in estates.OrderBy(estate => estate.EstateName))
            {
                UserAccount owner = estate.EstateOwner == UUID.Zero ? null
                    : Safe(() => _accounts.GetUserAccount(UUID.Zero, estate.EstateOwner));
                int regionCount = Safe(() => _estates.GetRegions((int)estate.EstateID))?.Count ?? 0;
                rows.Add(new Dictionary<string, object>
                {
                    ["EstateID"] = estate.EstateID, ["EstateName"] = H(estate.EstateName),
                    ["EstateOwner"] = H(owner?.FormattedName ?? estate.EstateOwner.ToString()),
                    ["PublicAccess"] = estate.PublicAccess ? "Yes" : "No",
                    ["AllowVoice"] = estate.AllowVoice ? "Yes" : "No", ["TaxFree"] = estate.TaxFree ? "Yes" : "No",
                    ["AllowDirectTeleport"] = estate.AllowDirectTeleport ? "Yes" : "No", ["RegionCount"] = regionCount
                });
            }
            vars["EstateList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["UserName"] = H(account?.FormattedName ?? string.Empty); vars["EstateManagerText"] = "estates";
            vars["EstateListText"] = "estates"; vars["EstateText"] = "Estate"; vars["EstateOwnerText"] = "Owner";
            vars["PublicAccessText"] = "Public"; vars["AllowVoiceText"] = "Voice";
            vars["TaxFreeText"] = "Access override"; vars["AllowDirectTeleportText"] = "Direct teleport";
            vars["RegionsText"] = "Regions"; vars["NoDetails"] = "No estates found";
        }

        private void AddOwnedClassifieds(Dictionary<string, object> vars, UserAccount account)
        {
            var rows = new List<Dictionary<string, object>>();
            if (account != null && _profiles != null && Safe(() => _profiles.AvatarClassifiedsRequest(account.PrincipalID)) is OSDArray records)
            {
                foreach (OSD value in records.Take(1000))
                {
                    if (value is not OSDMap summary || !UUID.TryParse(summary["classifieduuid"].AsString(), out UUID classifiedID)) continue;
                    var classified = new UserClassifiedAdd { ClassifiedId = classifiedID, CreatorId = account.PrincipalID };
                    string result = string.Empty;
                    if (!Safe(() => _profiles.ClassifiedInfoRequest(ref classified, ref result))) continue;
                    DirectoryLocation(classified.GlobalPos, out int x, out int y, out int z);
                    rows.Add(new Dictionary<string, object>
                    {
                        ["ClassifiedID"] = classified.ClassifiedId.ToString(), ["Name"] = H(classified.Name),
                        ["Description"] = H(classified.Description), ["Category"] = classified.Category,
                        ["CreationDate"] = UnixDate(classified.CreationDate.ToString(CultureInfo.InvariantCulture)),
                        ["ExpirationDate"] = UnixDate(classified.ExpirationDate.ToString(CultureInfo.InvariantCulture)),
                        ["Maturity"] = (classified.Flags & 4) != 0 ? "Mature" : "General",
                        ["PriceForListing"] = classified.Price, ["ClassifiedRegion"] = H(classified.SimName),
                        ["ClassifiedHop"] = string.IsNullOrWhiteSpace(classified.SimName) ? string.Empty : Hop(classified.SimName, x, y, z)
                    });
                }
            }
            vars["ClassifiedList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["UserName"] = H(account?.FormattedName ?? string.Empty); vars["ClassifiedsText"] = "classifieds";
            vars["CreationDateText"] = "Created"; vars["ExpirationDateText"] = "Expires";
            vars["CategoryText"] = "Category"; vars["ClassifiedNameText"] = "Classified";
            vars["DescriptionText"] = "Description"; vars["MaturityText"] = "Maturity";
            vars["PriceOfListingText"] = "Listing price";
        }

        private void AddOwnedEvents(Dictionary<string, object> vars, UserAccount account)
        {
            var rows = new List<Dictionary<string, object>>();
            Hashtable response = account == null ? null : Search("continuum_owner_events_query", new Hashtable
            {
                ["creatoruuid"] = account.PrincipalID.ToString(), ["query_start"] = "0"
            });
            if (response?["data"] is ArrayList data)
            {
                foreach (Hashtable item in data.OfType<Hashtable>().Take(1000))
                {
                    DirectoryLocation(Text(item, "globalposition", Text(item, "landing_point")), out int x, out int y, out int z);
                    string simName = Text(item, "simname");
                    bool hasDate = TryEventDateUtc(item, item, out DateTime date);
                    rows.Add(new Dictionary<string, object>
                    {
                        ["EventID"] = H(Text(item, "event_id")),
                        ["EventDateUTC"] = hasDate ? EventDisplayDate(date).ToString("yyyy-MM-dd HH:mm 'SLT'", CultureInfo.InvariantCulture) : H(Text(item, "date")),
                        ["SimName"] = H(simName), ["EventHop"] = string.IsNullOrWhiteSpace(simName) ? string.Empty : Hop(simName, x, y, z),
                        ["Name"] = H(Text(item, "name")), ["Description"] = H(Text(item, "description")),
                        ["Category"] = H(EventCategoryName(Text(item, "category"))),
                        ["Maturity"] = MaturityName(Text(item, "eventflags")),
                        ["Duration"] = H(Text(item, "duration")) + " minutes",
                        ["CoverCharge"] = Text(item, "covercharge") == "0" ? "Free" : H(Text(item, "coveramount"))
                    });
                }
            }
            vars["EventList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["UserName"] = H(account?.FormattedName ?? string.Empty); vars["EventsText"] = "Events";
            vars["EventDateText"] = "Date"; vars["LocationText"] = "Location"; vars["DescriptionText"] = "Event";
            vars["CategoryText"] = "Category"; vars["MaturityText"] = "Maturity";
            vars["DurationText"] = "Duration"; vars["CoverChargeText"] = "Cover charge";
        }

        private void AddOnlineUsers(Dictionary<string, object> vars)
        {
            var rows = new List<Dictionary<string, object>>();
            if (_gridUsers != null)
            {
                List<UserAccount> accounts = Safe(() => _accounts.GetUserAccountsWhere(UUID.Zero, "1=1")) ?? new();
                Dictionary<UUID, UserAccount> accountsByID = accounts.Take(10000).ToDictionary(item => item.PrincipalID);
                string[] ids = accountsByID.Keys.Select(id => id.ToString()).ToArray();
                GridUserInfo[] users = ids.Length == 0 ? Array.Empty<GridUserInfo>()
                    : Safe(() => _gridUsers.GetGridUserInfo(ids)) ?? Array.Empty<GridUserInfo>();
                foreach (GridUserInfo info in users)
                {
                    if (info == null || !info.Online) continue;
                    if (!UUID.TryParse(info.UserID, out UUID userID) || !accountsByID.TryGetValue(userID, out UserAccount account)) continue;
                    GridRegion region = Safe(() => _grid?.GetRegionByUUID(UUID.Zero, info.LastRegionID));
                    rows.Add(new Dictionary<string, object>
                    {
                        ["UserName"] = H(account.FormattedName), ["UserID"] = account.PrincipalID.ToString(),
                        ["UserRegion"] = H(region?.RegionName ?? "Unknown"),
                        ["UserRegionID"] = info.LastRegionID.ToString()
                    });
                }
            }
            vars["UsersOnlineList"] = rows;
        }

        private void AddUserSearch(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters,
            UserAccount currentUser)
        {
            string search = Trimmed(Value(parameters, "username"), 128);
            List<UserAccount> accounts = search.Length < 2
                ? new List<UserAccount>()
                : Safe(() => _accounts.GetUserAccounts(UUID.Zero, search)) ?? new List<UserAccount>();
            var rows = new List<Dictionary<string, object>>();
            bool administrator = IsAdmin(currentUser);
            foreach (UserAccount account in accounts.Where(item => item.UserLevel >= 0 || administrator).Take(100))
            {
                var profile = new UserProfileProperties { UserId = account.PrincipalID };
                string result = string.Empty;
                bool profileFound = Safe(() => _profiles?.AvatarPropertiesRequest(ref profile, ref result)) ?? false;
                bool showProfileImage = profileFound && (profile.PublishProfile || administrator
                    || currentUser?.PrincipalID == account.PrincipalID);
                rows.Add(new Dictionary<string, object>
                {
                    ["UserID"] = account.PrincipalID.ToString(), ["UserName"] = H(account.FormattedName),
                    ["UserType"] = H(AccountType(account)),
                    ["UserPictureURL"] = Texture(showProfileImage ? profile.ImageId : UUID.Zero, "static/icons/no_avatar.jpg")
                });
            }
            vars["UsersList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["UserSearchText"] = "Resident search";
            vars["UserNameText"] = "Resident name"; vars["SearchForUserText"] = "Search for a resident";
            vars["SearchResultForUserText"] = "Search results"; vars["Search"] = "Search";
            vars["NoDetailsText"] = search.Length < 2 ? "Enter at least two characters" :
                rows.Count == 0 ? "No matching residents" : string.Empty;
            vars["CanEdit"] = IsAdmin(currentUser);
        }

        private void AddUserHome(Dictionary<string, object> vars, UserAccount account)
        {
            if (account == null) return;
            GridUserInfo gridUser = Safe(() => _gridUsers?.GetGridUserInfo(account.PrincipalID.ToString()));
            GridRegion home = gridUser == null ? null : Safe(() => _grid?.GetRegionByUUID(UUID.Zero, gridUser.HomeRegionID));
            var profile = new UserProfileProperties { UserId = account.PrincipalID };
            string profileResult = string.Empty;
            bool profileFound = Safe(() => _profiles?.AvatarPropertiesRequest(ref profile, ref profileResult)) ?? false;
            vars["UserName"] = H(account.FormattedName);
            vars["UserType"] = H(AccountType(account));
            vars["AccountType"] = "Account type";
            vars["UserHomeRegion"] = H(home?.RegionName ?? "Not set");
            vars["UserLastLogin"] = gridUser?.Login.ToString("u", CultureInfo.InvariantCulture) ?? "Unknown";
            vars["UserBorn"] = DateTimeOffset.FromUnixTimeSeconds(account.Created).UtcDateTime.ToString("d", CultureInfo.InvariantCulture);
            vars["UserPictureURL"] = Texture(profileFound ? profile.ImageId : UUID.Zero, "static/icons/no_avatar.jpg");
            Hashtable statement = EconomyStatement(account.PrincipalID, null, 1);
            vars["UserBalance"] = statement != null && statement.ContainsKey("balance") ? statement["balance"] : "Unavailable";
            AddGroups(vars, account);
        }

        private void AddEvents(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters,
            UserAccount currentUser)
        {
            string text = Trimmed(Value(parameters, "text"), 128);
            string categoryValue = Value(parameters, "category");
            int category = EventCategory(categoryValue);
            string timeframeValue = Value(parameters, "timeframe");
            string day = timeframeValue.Equals("Tomorrow", StringComparison.OrdinalIgnoreCase) ? "1" :
                timeframeValue.Equals("This week", StringComparison.OrdinalIgnoreCase) ? "u" : "0";
            bool maturitySpecified = parameters.ContainsKey("display_pg") || parameters.ContainsKey("display_ma")
                || parameters.ContainsKey("display_ao");
            int maturityFlags = !maturitySpecified ? 0x01000000 | (currentUser != null ? 0x02000000 : 0) : 0;
            if (parameters.ContainsKey("display_pg")) maturityFlags |= 0x01000000;
            if (currentUser != null && parameters.ContainsKey("display_ma")) maturityFlags |= 0x02000000;
            if (currentUser != null && parameters.ContainsKey("display_ao")) maturityFlags |= 0x04000000;
            if (maturityFlags == 0) maturityFlags = 0x01000000;
            Hashtable response = Search("dir_events_query", new Hashtable
            {
                ["text"] = day + "|" + category.ToString(CultureInfo.InvariantCulture) + "|" + text,
                ["flags"] = maturityFlags.ToString(CultureInfo.InvariantCulture), ["query_start"] = "0"
            });
            var rows = new List<Dictionary<string, object>>();
            if (response?["data"] is ArrayList data)
            {
                foreach (Hashtable summary in data.OfType<Hashtable>().Take(50))
                {
                    Hashtable detail = summary;
                    if (!detail.ContainsKey("description") || !detail.ContainsKey("category"))
                    {
                        Hashtable detailResponse = Search("event_info_query", new Hashtable { ["eventID"] = Text(summary, "event_id") });
                        detail = (detailResponse?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault() ?? summary;
                    }
                    bool hasDate = TryEventDateUtc(detail, summary, out DateTime date);
                    DateTime displayDate = hasDate ? EventDisplayDate(date) : default;
                    DirectoryLocation(Text(detail, "globalposition", Text(detail, "landing_point")), out int x, out int y, out int z);
                    string simName = Text(detail, "simname");
                    rows.Add(new Dictionary<string, object>
                    {
                        ["EventDateTimeUTC"] = hasDate ? date.ToString("o", CultureInfo.InvariantCulture) : string.Empty,
                        ["EventDateTime"] = hasDate ? displayDate.Day.ToString(CultureInfo.InvariantCulture) : H(Text(summary, "date")),
                        ["EventHourTime"] = hasDate ? displayDate.ToString("MMM", CultureInfo.InvariantCulture) : string.Empty,
                        ["EventMinuteTime"] = hasDate ? displayDate.Year.ToString(CultureInfo.InvariantCulture) : string.Empty,
                        ["EventTime"] = hasDate ? displayDate.ToString("HH:mm 'SLT'", CultureInfo.InvariantCulture) : string.Empty,
                        ["Name"] = H(Text(detail, "name")), ["Description"] = H(Text(detail, "description")),
                        ["Category"] = H(EventCategoryName(Text(detail, "category"))), ["CategoryImage"] = "static/icons/event.png",
                        ["SimName"] = H(simName), ["Maturity"] = MaturityName(Text(detail, "eventflags")),
                        ["CoverCharge"] = H(Text(detail, "coveramount")), ["DurationText"] = H(Text(detail, "duration")),
                        ["EventHop"] = string.IsNullOrWhiteSpace(simName) ? string.Empty : Hop(simName, x, y, z)
                    });
                }
            }
            vars["EventList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["EventsText"] = "Events"; vars["AddEventText"] = "Add event"; vars["SearchText"] = "Search";
            vars["CategoryType"] = SelectOptions(new[] { "All", "Discussion", "Sports", "Live Music", "Commercial",
                "Nightlife/Entertainment", "Games/Contests", "Pageants", "Education", "Arts and Culture",
                "Charity/Support Groups", "Miscellaneous" },
                Value(parameters, "category"));
            vars["TimeFrame"] = SelectOptions(new[] { "Today", "Tomorrow", "This week" }, Value(parameters, "timeframe"));
            vars["PG_checked"] = (maturityFlags & 0x01000000) != 0 ? "checked" : string.Empty;
            vars["MA_checked"] = (maturityFlags & 0x02000000) != 0 ? "checked" : string.Empty;
            vars["AO_checked"] = (maturityFlags & 0x04000000) != 0 ? "checked" : string.Empty;
        }

        private static int EventCategory(string value) => value.Trim().ToLowerInvariant() switch
        {
            "discussion" => 18, "sports" => 19, "live music" => 20, "commercial" => 22,
            "nightlife/entertainment" => 23, "games/contests" => 24, "pageants" => 25,
            "education" => 26, "arts and culture" => 27, "charity/support groups" => 28, "miscellaneous" => 29,
            _ => Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0
        };

        private static string EventCategoryName(string value) => EventCategory(value) switch
        {
            18 => "Discussion", 19 => "Sports", 20 => "Live Music", 22 => "Commercial",
            23 => "Nightlife/Entertainment", 24 => "Games/Contests", 25 => "Pageants", 26 => "Education",
            27 => "Arts and Culture", 28 => "Charity/Support Groups", 29 => "Miscellaneous", _ => "General"
        };

        private void AddClassifieds(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters,
            UserAccount currentUser)
        {
            bool maturitySpecified = parameters.ContainsKey("display_pg") || parameters.ContainsKey("display_ma")
                || parameters.ContainsKey("display_ao");
            int maturityFlags = !maturitySpecified ? 5 | (currentUser != null ? 10 : 0) : 0;
            if (parameters.ContainsKey("display_pg")) maturityFlags |= 5;
            if (currentUser != null && parameters.ContainsKey("display_ma")) maturityFlags |= 10;
            if (currentUser != null && parameters.ContainsKey("display_ao")) maturityFlags |= 64;
            if (maturityFlags == 0) maturityFlags = 5;
            Hashtable response = Search("dir_classified_query", new Hashtable
            {
                ["text"] = Trimmed(Value(parameters, "text"), 128), ["flags"] = maturityFlags.ToString(CultureInfo.InvariantCulture),
                ["category"] = ClassifiedCategory(Value(parameters, "category")).ToString(CultureInfo.InvariantCulture),
                ["query_start"] = "0"
            });
            var rows = new List<Dictionary<string, object>>();
            if (response?["data"] is ArrayList data)
            {
                foreach (Hashtable summary in data.OfType<Hashtable>().Take(50))
                {
                    Hashtable detail = summary;
                    if (!detail.ContainsKey("description") || !detail.ContainsKey("simname"))
                    {
                        Hashtable detailResponse = Search("classifieds_info_query", new Hashtable { ["classifiedID"] = Text(summary, "classifiedid") });
                        detail = (detailResponse?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault() ?? summary;
                    }
                    DirectoryLocation(Text(detail, "posglobal"), out int x, out int y, out int z);
                    string simName = Text(detail, "simname");
                    rows.Add(new Dictionary<string, object>
                    {
                        ["Name"] = H(Text(detail, "name")), ["Description"] = H(Text(detail, "description")),
                        ["CreationDate"] = UnixDate(Text(detail, "creationdate", Text(summary, "creation_date"))),
                        ["ExpirationDate"] = UnixDate(Text(detail, "expirationdate", Text(summary, "expiration_date"))),
                        ["Maturity"] = H(Text(detail, "classifiedflags", Text(summary, "classifiedflags"))),
                        ["PriceForListing"] = H(Text(detail, "priceforlisting", Text(summary, "priceforlisting"))),
                        ["ClassifiedImage"] = Texture(UUID.TryParse(Text(detail, "snapshotuuid"), out UUID image) ? image : UUID.Zero,
                            "static/icons/no_picture.jpg"),
                        ["ClassifiedRegion"] = H(simName),
                        ["ClassifiedHop"] = string.IsNullOrWhiteSpace(simName) ? string.Empty : Hop(simName, x, y, z)
                    });
                }
            }
            vars["ClassifiedList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["ClassifiedsText"] = "Classifieds"; vars["SearchText"] = "Search";
            vars["CategoryType"] = SelectOptions(new[] { "All", "Shopping", "Land rental", "Property rental", "Special attraction", "New products", "Employment", "Wanted", "Service", "Personal" }, Value(parameters, "category"));
            vars["PG_checked"] = (maturityFlags & 5) != 0 ? "checked" : string.Empty;
            vars["MA_checked"] = (maturityFlags & 10) != 0 ? "checked" : string.Empty;
            vars["AO_checked"] = (maturityFlags & 64) != 0 ? "checked" : string.Empty;
        }

        private static int ClassifiedCategory(string value) => value.Trim().ToLowerInvariant() switch
        {
            "shopping" => 1, "land rental" => 2, "property rental" => 3, "special attraction" => 4,
            "new products" => 5, "employment" => 6, "wanted" => 7, "service" => 8, "personal" => 9,
            _ => Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? Math.Clamp(parsed, 0, 9) : 0
        };

        private void AddDestinations(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters,
            UserAccount currentUser)
        {
            string text = Trimmed(Value(parameters, "q"), 128);
            string tab = Trimmed(Value(parameters, "tab"), 16).ToLowerInvariant();
            if (tab is not ("popular" or "featured" or "discover")) tab = "popular";
            int.TryParse(Value(parameters, "cat"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int category);
            string maturity = currentUser == null ? "general" : Value(parameters, "m");
            int flags = MaturityFlags(maturity);
            string method = tab == "popular" && text.Length == 0 && category == 0 ? "dir_popular_query" : "dir_places_query";
            Hashtable response = Search(method, new Hashtable
            {
                ["text"] = text.Length == 0 ? "%" : text, ["flags"] = flags.ToString(CultureInfo.InvariantCulture),
                ["category"] = category.ToString(CultureInfo.InvariantCulture), ["query_start"] = "0"
            });
            var rows = new List<Dictionary<string, object>>();
            if (response?["data"] is ArrayList data)
            {
                foreach (Hashtable summary in data.OfType<Hashtable>().Take(100))
                {
                    Hashtable detail = summary;
                    if (!detail.ContainsKey("region_name") || !detail.ContainsKey("description"))
                    {
                        Hashtable detailsResponse = Search("parcel_info_query", new Hashtable { ["parcel_id"] = Text(summary, "parcel_id") });
                        detail = (detailsResponse?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault();
                    }
                    if (detail == null) continue;
                    string regionName = Text(detail, "region_name");
                    ParseLanding(Text(detail, "landing_point"), out int x, out int y, out int z);
                    rows.Add(new Dictionary<string, object>
                    {
                        ["DestinationName"] = H(Text(detail, "name")), ["DestinationDescription"] = H(Text(detail, "description")),
                        ["DestinationRegion"] = H(regionName), ["DestinationCategory"] = H(CategoryName(Text(detail, "category"))),
                        ["DestinationMaturity"] = H(MaturityName(Text(detail, "maturity"))), ["DestinationDwell"] = H(Text(detail, "dwell")),
                        ["DestinationArea"] = H(Text(detail, "area")), ["DestinationPrice"] = H(Text(detail, "sale_price")),
                        ["DestinationForSale"] = String.Equals(Text(detail, "for_sale"), "True", StringComparison.OrdinalIgnoreCase) ? "For sale" : string.Empty,
                        ["DestinationImage"] = Texture(UUID.TryParse(Text(detail, "snapshot_id"), out UUID image) ? image : UUID.Zero, "static/icons/no_picture.jpg"),
                        ["DestinationHop"] = Hop(regionName, x, y, z)
                    });
                }
            }
            vars["Destinations"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["DestinationSearch"] = H(text); vars["DestinationTab"] = H(tab);
        }

        private static int MaturityFlags(string maturity) => maturity.Trim().ToLowerInvariant() switch
        {
            "general" or "pg" => 0x01000000,
            "mature" => 0x02000000,
            "adult" => 0x04000000,
            _ => 0x01000000 | 0x02000000 | 0x04000000
        };

        private static void ParseLanding(string value, out int x, out int y, out int z)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            x = parts.Length > 0 && Int32.TryParse(parts[0], out int px) ? Math.Clamp(px, 0, 4096) : 128;
            y = parts.Length > 1 && Int32.TryParse(parts[1], out int py) ? Math.Clamp(py, 0, 4096) : 128;
            z = parts.Length > 2 && Int32.TryParse(parts[2], out int pz) ? Math.Clamp(pz, 0, 10000) : 25;
        }

        private static void DirectoryLocation(string value, out int x, out int y, out int z)
        {
            string[] parts = (value ?? string.Empty).Trim('<', '>', ' ').Split(',');
            static int Coordinate(string text, bool horizontal, int fallback)
            {
                if (!Double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return fallback;
                int result = (int)Math.Floor(parsed);
                if (horizontal && result >= 256) result %= 256;
                return Math.Clamp(result, 0, horizontal ? 4096 : 10000);
            }
            x = Coordinate(parts.Length > 0 ? parts[0] : null, true, 128);
            y = Coordinate(parts.Length > 1 ? parts[1] : null, true, 128);
            z = Coordinate(parts.Length > 2 ? parts[2] : null, false, 25);
        }

        private static string CategoryName(string value) => value switch
        {
            "3" => "Arts & Culture", "4" => "Business", "5" => "Education", "6" => "Gaming",
            "7" => "Hangout", "8" => "Newcomer", "9" => "Parks & Nature", "10" => "Residential",
            "11" => "Shopping", "14" => "Rental", _ => "Other"
        };

        private static string MaturityName(string value) => value switch { "1" => "Mature", "2" => "Adult", _ => "General" };

        private void AddTransactions(Dictionary<string, object> vars, UserAccount account, IReadOnlyDictionary<string, string> parameters)
        {
            List<Dictionary<string, object>> rows = TransactionRows(account, parameters);
            vars["TransactionsList"] = rows; vars["TransactionsText"] = "Transactions";
            vars["DateStart"] = H(Value(parameters, "date_start")); vars["DateEnd"] = H(Value(parameters, "date_end"));
            vars["DateStartText"] = "From"; vars["DateEndText"] = "Through"; vars["Search"] = "Filter";
            vars["TransactionDateText"] = "Date"; vars["TransactionFromAgentText"] = "From";
            vars["TransactionToAgentText"] = "To"; vars["TransactionDetailText"] = "Description";
            vars["TransactionAmountText"] = "Amount"; vars["TransactionBalanceText"] = "Resulting balance";
        }

        private void AddAdminTransactions(Dictionary<string, object> vars, UserAccount administrator,
            IReadOnlyDictionary<string, string> parameters)
        {
            string search = Trimmed(Value(parameters, "user_name"), 128);
            string normalized = search.Replace('.', ' ');
            int separator = normalized.IndexOf(' ');
            UserAccount account = IsAdmin(administrator) && separator > 0 && separator < normalized.Length - 1
                ? Safe(() => _accounts.GetUserAccount(UUID.Zero, normalized.Substring(0, separator), normalized.Substring(separator + 1).Trim())) : null;
            vars["TransactionsList"] = TransactionRows(account, parameters);
            vars["TransactionsText"] = "Resident transactions"; vars["SearchUser"] = H(search);
            vars["DateStart"] = H(Value(parameters, "date_start")); vars["DateEnd"] = H(Value(parameters, "date_end"));
            vars["DateStartText"] = "From"; vars["DateEndText"] = "Through"; vars["SearchUserText"] = "Resident";
            vars["Search"] = "Search"; vars["TransactionDateText"] = "Date";
            vars["TransactionFromAgentText"] = "From"; vars["TransactionToAgentText"] = "To";
            vars["TransactionDetailText"] = "Description"; vars["TransactionAmountText"] = "Amount";
            vars["TransactionBalanceText"] = "Resulting balance";
        }

        private List<Dictionary<string, object>> TransactionRows(UserAccount account,
            IReadOnlyDictionary<string, string> parameters)
        {
            var rows = new List<Dictionary<string, object>>();
            if (account == null) return rows;
            DateTime? start = DateTime.TryParse(Value(parameters, "date_start"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime parsedStart) ? parsedStart.ToUniversalTime() : null;
            DateTime? before = DateTime.TryParse(Value(parameters, "date_end"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out DateTime parsedEnd) ? parsedEnd.ToUniversalTime().AddDays(1) : null;
            Hashtable statement = EconomyStatement(account.PrincipalID, before, 500);
            if (statement?["history"] is not ArrayList history) return rows;
            List<Hashtable> visibleHistory = history.OfType<Hashtable>().Where(item =>
            {
                return !start.HasValue || !DateTime.TryParse(Text(item, "createdUtc"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out DateTime created) || created.ToUniversalTime() >= start.Value;
            }).ToList();
            List<string> counterpartyIDs = visibleHistory.Select(item => Text(item, "counterpartyID"))
                .Where(value => UUID.TryParse(value, out UUID id) && id != UUID.Zero)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<UserAccount> counterparties = counterpartyIDs.Count == 0 ? new List<UserAccount>()
                : Safe(() => _accounts.GetUserAccounts(UUID.Zero, counterpartyIDs)) ?? new List<UserAccount>();
            Dictionary<UUID, UserAccount> accountsByID = counterparties.GroupBy(item => item.PrincipalID)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (Hashtable item in visibleHistory)
            {
                UUID.TryParse(Text(item, "counterpartyID"), out UUID counterpartyID);
                accountsByID.TryGetValue(counterpartyID, out UserAccount counterparty);
                long.TryParse(Text(item, "amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount);
                rows.Add(new Dictionary<string, object>
                {
                    ["Date"] = H(Text(item, "createdUtc")),
                    ["FromAgent"] = H(amount >= 0 ? counterparty?.FormattedName ?? "System" : account.FormattedName),
                    ["ToAgent"] = H(amount >= 0 ? account.FormattedName : counterparty?.FormattedName ?? "System"),
                    ["Description"] = H(Text(item, "description")), ["Amount"] = amount,
                    ["ToBalance"] = H(Text(item, "balance"))
                });
            }
            return rows;
        }

        private Hashtable Search(string method, Hashtable parameters)
        {
            if (string.IsNullOrEmpty(_searchUrl)) return null;
            return Rpc(_searchUrl, method, parameters);
        }

        private Hashtable EconomyStatement(UUID account, DateTime? before, int limit)
        {
            if (string.IsNullOrEmpty(_economyUrl) || string.IsNullOrEmpty(_economySecret)) return null;
            return Rpc(_economyUrl, "GetAccountStatement", new Hashtable
            {
                ["continuumSecret"] = _economySecret, ["accountID"] = account.ToString(),
                ["beforeUtc"] = before?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? string.Empty,
                ["limit"] = Math.Clamp(limit, 1, 500).ToString(CultureInfo.InvariantCulture)
            });
        }

        private Hashtable Rpc(string url, string method, Hashtable parameters)
        {
            try
            {
                XmlRpcResponse response = new XmlRpcRequest(method, new ArrayList { parameters }).Send(url, _rpcTimeoutMs);
                if (response == null || response.IsFault || response.Value is not Hashtable result) return null;
                return result;
            }
            catch (Exception e) { Log.WarnFormat("[CONTINUUM WEBUI]: {0} RPC failed: {1}", method, e.Message); return null; }
        }

        private static string Text(Hashtable values, string key, string fallback = "") => values != null && values.ContainsKey(key) && values[key] != null ? values[key].ToString() : fallback;
        private static string UnixDate(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("d", CultureInfo.InvariantCulture) : H(value);

        private static bool TryEventDateUtc(Hashtable detail, Hashtable summary, out DateTime date)
        {
            date = default;
            string value = Text(detail, "dateUTC", Text(detail, "unix_time",
                Text(summary, "dateUTC", Text(summary, "unix_time"))));
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
            {
                try
                {
                    date = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Fall through for older Search services that only return a formatted date.
                }
            }

            value = Text(detail, "date", Text(summary, "date"));
            return DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
        }

        private static DateTime EventDisplayDate(DateTime utc)
        {
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
            catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        }

        private void AddProfile(Dictionary<string, object> vars, UserAccount account, UserAccount viewer)
        {
            if (account == null) return;
            var profile = new UserProfileProperties { UserId = account.PrincipalID };
            string result = string.Empty;
            bool found = Safe(() => _profiles?.AvatarPropertiesRequest(ref profile, ref result)) ?? false;
            bool canViewProfile = viewer != null && (viewer.PrincipalID == account.PrincipalID || IsAdmin(viewer))
                || !found || profile.PublishProfile;
            vars["ProfileVisible"] = canViewProfile;
            vars["ProfileHidden"] = !canViewProfile;
            bool canSeeOnline = canViewProfile && viewer != null
                && (viewer.PrincipalID == account.PrincipalID || IsAdmin(viewer));
            bool canSeeLocation = canSeeOnline;
            if (!canSeeOnline && viewer != null && _friends != null)
            {
                FriendInfo friendship = (Safe(() => _friends.GetFriends(viewer.PrincipalID)) ?? Array.Empty<FriendInfo>())
                    .FirstOrDefault(friend => FriendID(friend.Friend) == account.PrincipalID);
                canSeeOnline = friendship != null && friendship.TheirFlags != -1
                    && (friendship.TheirFlags & (int)FriendRights.CanSeeOnline) != 0;
                canSeeLocation = friendship != null && friendship.TheirFlags != -1
                    && (friendship.TheirFlags & (int)FriendRights.CanSeeOnMap) != 0;
            }
            GridUserInfo gridUser = canSeeOnline
                ? Safe(() => _gridUsers?.GetGridUserInfo(account.PrincipalID.ToString()))
                : null;
            bool isOnline = gridUser?.Online == true;
            GridRegion region = isOnline && canSeeLocation
                ? Safe(() => _grid?.GetRegionByUUID(UUID.Zero, gridUser.LastRegionID))
                : null;
            UserAccount partner = canViewProfile && found && profile.PartnerId != UUID.Zero
                ? Safe(() => _accounts.GetUserAccount(UUID.Zero, profile.PartnerId)) : null;
            vars["UserName"] = H(account.FormattedName);
            vars["UserType"] = H(AccountType(account));
            vars["UserBorn"] = DateTimeOffset.FromUnixTimeSeconds(account.Created).UtcDateTime.ToString("d", CultureInfo.InvariantCulture);
            vars["UserPartner"] = H(partner?.FormattedName ?? "None");
            vars["UserAboutMe"] = H(canViewProfile && found ? profile.AboutText : string.Empty);
            vars["UserPictureURL"] = Texture(canViewProfile && found ? profile.ImageId : UUID.Zero, "static/icons/no_avatar.jpg");
            vars["IsOnline"] = isOnline ? "Yes" : "No";
            vars["UserStatusVisible"] = canSeeOnline;
            vars["UserStatusHidden"] = !canSeeOnline;
            vars["UserIsOnline"] = isOnline && canSeeLocation;
            vars["OnlineLocation"] = H(region?.RegionName ?? string.Empty);
            AddGroups(vars, canViewProfile ? account : null);
            if (vars.TryGetValue("Groups", out object value) && value is List<Dictionary<string, object>> groups)
            {
                groups.RemoveAll(group => !String.Equals(group["GroupProfileListed"]?.ToString(), "Yes", StringComparison.Ordinal));
                vars["GroupsJoined"] = groups.Count;
            }
        }

        private void AddEditableProfile(Dictionary<string, object> vars, UserAccount account)
        {
            if (account == null) return;
            var profile = new UserProfileProperties { UserId = account.PrincipalID };
            string result = string.Empty;
            Safe(() => _profiles?.AvatarPropertiesRequest(ref profile, ref result));
            vars["UserName"] = H(account.FormattedName); vars["UserID"] = account.PrincipalID.ToString();
            vars["ProfileAbout"] = H(profile.AboutText); vars["ProfileFirstLife"] = H(profile.FirstLifeText);
            vars["ProfileWebURL"] = H(profile.WebUrl); vars["ProfileImageID"] = profile.ImageId.ToString();
            vars["ProfileFirstLifeImageID"] = profile.FirstLifeImageId.ToString();
            vars["ProfileWantText"] = H(profile.WantToText); vars["ProfileSkillsText"] = H(profile.SkillsText);
            vars["ProfileLanguage"] = H(profile.Language); vars["ProfileWantMask"] = profile.WantToMask;
            vars["ProfileSkillsMask"] = profile.SkillsMask;
            vars["ProfilePublishChecked"] = profile.PublishProfile ? "checked" : string.Empty;
            vars["ProfileMatureChecked"] = profile.PublishMature ? "checked" : string.Empty;
        }

        private bool UpdateProfile(IReadOnlyDictionary<string, string> form, UserAccount account, out string message)
        {
            message = "Authentication required";
            if (account == null) return true;
            if (_profiles == null) { message = "Profile service is unavailable"; return true; }
            var profile = new UserProfileProperties { UserId = account.PrincipalID };
            string result = string.Empty;
            if (!Safe(() => _profiles.AvatarPropertiesRequest(ref profile, ref result)))
            { message = "Profile service is unavailable"; return true; }
            string about = Value(form, "about").Trim(); string firstLife = Value(form, "firstlife").Trim();
            string webUrl = Value(form, "weburl").Trim(); string wants = Value(form, "wants").Trim();
            string skills = Value(form, "skills").Trim(); string language = Value(form, "language").Trim();
            if (about.Length > 8192 || firstLife.Length > 8192 || wants.Length > 4096 || skills.Length > 4096 || language.Length > 255)
            { message = "One or more profile fields are too long"; return true; }
            if (webUrl.Length > 1024 || (webUrl.Length > 0 && (!Uri.TryCreate(webUrl, UriKind.Absolute, out Uri parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))))
            { message = "Profile web URL must use HTTP or HTTPS"; return true; }
            if (!UUID.TryParse(Value(form, "imageid"), out UUID image)) image = profile.ImageId;
            if (!UUID.TryParse(Value(form, "firstlifeimageid"), out UUID firstLifeImage)) firstLifeImage = profile.FirstLifeImageId;
            profile.AboutText = about; profile.FirstLifeText = firstLife; profile.WebUrl = webUrl;
            profile.ImageId = image; profile.FirstLifeImageId = firstLifeImage;
            profile.PublishProfile = form.ContainsKey("publish"); profile.PublishMature = form.ContainsKey("mature");
            if (!Safe(() => _profiles.AvatarPropertiesUpdate(ref profile, ref result)))
            { message = "Profile update failed"; return true; }
            profile.WantToText = wants; profile.SkillsText = skills; profile.Language = language;
            Int32.TryParse(Value(form, "wantmask"), NumberStyles.Integer, CultureInfo.InvariantCulture, out profile.WantToMask);
            Int32.TryParse(Value(form, "skillsmask"), NumberStyles.Integer, CultureInfo.InvariantCulture, out profile.SkillsMask);
            message = Safe(() => _profiles.AvatarInterestsUpdate(profile, ref result)) ? "Profile updated" : "Profile saved, but interests could not be updated";
            return true;
        }

        private void AddFriends(Dictionary<string, object> vars, UserAccount account)
        {
            var rows = new List<Dictionary<string, object>>();
            if (account != null && _friends != null)
            {
                List<(FriendInfo Friend, UUID ID)> friendships = (Safe(() => _friends.GetFriends(account.PrincipalID))
                    ?? Array.Empty<FriendInfo>()).Select(friend => (Friend: friend, ID: FriendID(friend.Friend)))
                    .Where(item => item.ID != UUID.Zero).Take(1000).ToList();
                List<string> friendIDs = friendships.Select(item => item.ID.ToString()).Distinct().ToList();
                List<UserAccount> friendAccounts = friendIDs.Count == 0 ? new List<UserAccount>()
                    : Safe(() => _accounts.GetUserAccounts(UUID.Zero, friendIDs)) ?? new List<UserAccount>();
                Dictionary<UUID, UserAccount> accountsByID = friendAccounts.GroupBy(item => item.PrincipalID)
                    .ToDictionary(group => group.Key, group => group.First());
                string[] presenceIDs = friendships.Where(item => item.Friend.TheirFlags != -1
                        && (item.Friend.TheirFlags & (int)FriendRights.CanSeeOnline) != 0)
                    .Select(item => item.ID.ToString()).Distinct().ToArray();
                GridUserInfo[] gridUsers = presenceIDs.Length == 0 || _gridUsers == null ? Array.Empty<GridUserInfo>()
                    : Safe(() => _gridUsers.GetGridUserInfo(presenceIDs)) ?? Array.Empty<GridUserInfo>();
                Dictionary<UUID, GridUserInfo> usersByID = gridUsers.Where(item => item != null
                        && UUID.TryParse(item.UserID, out UUID id) && id != UUID.Zero)
                    .GroupBy(item => UUID.Parse(item.UserID)).ToDictionary(group => group.Key, group => group.First());
                var regionsByID = new Dictionary<UUID, GridRegion>();
                foreach ((FriendInfo friend, UUID friendID) in friendships)
                {
                    accountsByID.TryGetValue(friendID, out UserAccount friendAccount);
                    bool canSeeOnline = friend.TheirFlags != -1
                        && (friend.TheirFlags & (int)FriendRights.CanSeeOnline) != 0;
                    bool canSeeLocation = friend.TheirFlags != -1
                        && (friend.TheirFlags & (int)FriendRights.CanSeeOnMap) != 0;
                    GridUserInfo info = canSeeOnline && usersByID.TryGetValue(friendID, out GridUserInfo foundInfo)
                        ? foundInfo : null;
                    bool isOnline = info?.Online == true;
                    GridRegion region = null;
                    if (isOnline && canSeeLocation && info.LastRegionID != UUID.Zero
                        && !regionsByID.TryGetValue(info.LastRegionID, out region))
                    {
                        region = Safe(() => _grid?.GetRegionByUUID(UUID.Zero, info.LastRegionID));
                        regionsByID[info.LastRegionID] = region;
                    }
                    rows.Add(new Dictionary<string, object>
                    {
                        ["FriendID"] = friendID.ToString(), ["FriendName"] = H(friendAccount?.FormattedName ?? friendID.ToString()),
                        ["FriendRegion"] = H(!isOnline ? "Offline" : canSeeLocation ? region?.RegionName ?? "Unknown" : "Online"),
                        ["FriendRegionID"] = region?.RegionID.ToString() ?? UUID.Zero.ToString(),
                        ["FriendLocation"] = H(isOnline && canSeeLocation ? info.LastPosition.ToString() : isOnline ? "Location hidden" : "Offline"),
                        ["HopUrl"] = isOnline && canSeeLocation && region != null ? Hop(region, info.LastPosition) : string.Empty,
                        ["IfCanSeeLocation"] = isOnline && canSeeLocation && region != null,
                        ["IfCannotSeeLocation"] = !isOnline || !canSeeLocation || region == null
                    });
                }
            }
            vars["UserFriendsList"] = rows;
            vars["HaveData"] = rows.Count > 0;
            vars["NoData"] = rows.Count == 0;
        }

        private static UUID FriendID(string value)
        {
            if (string.IsNullOrEmpty(value)) return UUID.Zero;
            int semicolon = value.IndexOf(';');
            if (semicolon >= 0) value = value.Substring(0, semicolon);
            return UUID.TryParse(value, out UUID id) ? id : UUID.Zero;
        }

        private void AddGroups(Dictionary<string, object> vars, UserAccount account)
        {
            var rows = new List<Dictionary<string, object>>();
            if (account != null && _groups != null)
            {
                List<GroupMembershipData> memberships = Safe(() => _groups.GetAgentGroupMemberships(
                    account.PrincipalID.ToString(), account.PrincipalID.ToString())) ?? new();
                foreach (GroupMembershipData membership in memberships.OrderBy(m => m.GroupName).Take(1000))
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["GroupID"] = membership.GroupID.ToString(), ["GroupName"] = H(membership.GroupName),
                        ["GroupTitle"] = H(membership.GroupTitle), ["GroupCharter"] = H(membership.Charter),
                        ["GroupPictureURL"] = Texture(membership.GroupPicture, "static/icons/no_picture.jpg"),
                        ["GroupActive"] = membership.Active ? "Active" : string.Empty,
                        ["GroupNotices"] = membership.AcceptNotices ? "Yes" : "No",
                        ["GroupProfileListed"] = membership.ListInProfile ? "Yes" : "No"
                    });
                }
            }
            vars["Groups"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["GroupsJoined"] = rows.Count;
        }

        private void AddPublicGroups(Dictionary<string, object> vars, UserAccount account, UserAccount viewer)
        {
            bool visible = CanViewPublishedProfile(account, viewer);
            AddGroups(vars, visible ? account : null);
            if (vars.TryGetValue("Groups", out object value) && value is List<Dictionary<string, object>> rows)
            {
                rows.RemoveAll(row => !String.Equals(row["GroupProfileListed"]?.ToString(), "Yes", StringComparison.Ordinal));
                vars["GroupsJoined"] = rows.Count;
                vars["HaveData"] = rows.Count > 0;
                vars["NoData"] = rows.Count == 0;
            }
            if (account != null) { vars["UserName"] = H(account.FormattedName); vars["UserID"] = account.PrincipalID.ToString(); }
        }

        private void AddPicks(Dictionary<string, object> vars, UserAccount account, UserAccount viewer)
        {
            var rows = new List<Dictionary<string, object>>();
            if (CanViewPublishedProfile(account, viewer) && account != null && _profiles != null
                && Safe(() => _profiles.AvatarPicksRequest(account.PrincipalID)) is OSDArray picks)
            {
                foreach (OSD value in picks.Take(100))
                {
                    if (value is not OSDMap summary || !UUID.TryParse(summary["pickuuid"].AsString(), out UUID pickID)) continue;
                    var pick = new UserProfilePick { CreatorId = account.PrincipalID, PickId = pickID };
                    string result = string.Empty;
                    if (!Safe(() => _profiles.PickInfoRequest(ref pick, ref result))) continue;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["PickID"] = pick.PickId.ToString(), ["PickName"] = H(pick.Name),
                        ["PickDescription"] = H(pick.Desc), ["PickRegion"] = H(pick.SimName),
                        ["PickLocation"] = H(pick.GlobalPos),
                        ["PickSnapshotURL"] = Texture(pick.SnapshotId, "static/icons/no_picture.jpg"),
                        ["PickHop"] = PickHop(pick)
                    });
                }
            }
            vars["Picks"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            if (account != null)
            {
                vars["UserName"] = H(account.FormattedName); vars["UserID"] = account.PrincipalID.ToString();
                vars["UserPictureURL"] = Texture(UUID.Zero, "static/icons/no_avatar.jpg");
            }
        }

        private void AddOwnedRegions(Dictionary<string, object> vars, UserAccount account, UserAccount viewer)
        {
            List<GridRegion> regions = account == null || !CanViewPublishedProfile(account, viewer) ? new() :
                (Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 10000)) ?? new())
                    .Where(region => region.EstateOwner == account.PrincipalID).ToList();
            var rows = regions.Select(region =>
            {
                Dictionary<string, object> row = RegionRow(region);
                row["RegionImageURL"] = Texture(region.TerrainImage, "static/icons/no_picture.jpg");
                return row;
            }).ToList();
            vars["RegionList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            if (account != null) { vars["UserName"] = H(account.FormattedName); vars["UserID"] = account.PrincipalID.ToString(); }
        }

        private bool CanViewPublishedProfile(UserAccount account, UserAccount viewer)
        {
            if (account == null) return false;
            if (viewer != null && (viewer.PrincipalID == account.PrincipalID || IsAdmin(viewer))) return true;
            if (_profiles == null) return true;
            var profile = new UserProfileProperties { UserId = account.PrincipalID };
            string result = string.Empty;
            bool found = Safe(() => _profiles.AvatarPropertiesRequest(ref profile, ref result));
            return !found || profile.PublishProfile;
        }

        private string PickHop(UserProfilePick pick)
        {
            string position = (pick.GlobalPos ?? string.Empty).Trim('<', '>', ' ');
            ParseLanding(position.Replace(" ", string.Empty), out int x, out int y, out int z);
            return String.IsNullOrWhiteSpace(pick.SimName) ? string.Empty : Hop(pick.SimName, x, y, z);
        }

        private void AddExperiences(Dictionary<string, object> vars, UserAccount account,
            IReadOnlyDictionary<string, string> parameters)
        {
            var rows = new List<Dictionary<string, object>>();
            ExperienceInfo[] experiences = Array.Empty<ExperienceInfo>();
            Dictionary<UUID, bool> permissions = account == null || _experiences == null
                ? new Dictionary<UUID, bool>()
                : Safe(() => _experiences.FetchExperiencePermissions(account.PrincipalID)) ?? new Dictionary<UUID, bool>();
            string search = parameters.TryGetValue("q", out string query) ? Trimmed(query, 128) : string.Empty;
            if (_experiences != null)
            {
                if (!string.IsNullOrEmpty(search)) experiences = Safe(() => _experiences.FindExperiencesByName(search)) ?? Array.Empty<ExperienceInfo>();
                else if (account != null)
                {
                    UUID[] ids = Safe(() => _experiences.GetAgentExperiences(account.PrincipalID)) ?? Array.Empty<UUID>();
                    experiences = ids.Length == 0 ? Array.Empty<ExperienceInfo>() : Safe(() => _experiences.GetExperienceInfos(ids)) ?? Array.Empty<ExperienceInfo>();
                }
            }
            bool administrator = IsAdmin(account);
            List<ExperienceInfo> visibleExperiences = experiences.Where(experience =>
            {
                ExperienceFlags flags = (ExperienceFlags)experience.properties;
                bool related = account != null && (experience.owner_id == account.PrincipalID
                    || permissions.ContainsKey(experience.public_id));
                if ((flags & ExperienceFlags.Private) != 0 && !related && !administrator) return false;
                return ((flags & (ExperienceFlags.Suspended | ExperienceFlags.Disabled)) == 0)
                    || related || administrator;
            }).Take(100).ToList();
            List<string> ownerIDs = visibleExperiences.Select(experience => experience.owner_id)
                .Where(id => id != UUID.Zero).Distinct().Select(id => id.ToString()).ToList();
            List<UserAccount> owners = ownerIDs.Count == 0 ? new List<UserAccount>()
                : Safe(() => _accounts.GetUserAccounts(UUID.Zero, ownerIDs)) ?? new List<UserAccount>();
            Dictionary<UUID, UserAccount> ownersByID = owners.GroupBy(owner => owner.PrincipalID)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (ExperienceInfo experience in visibleExperiences)
            {
                ownersByID.TryGetValue(experience.owner_id, out UserAccount owner);
                string permission = permissions.TryGetValue(experience.public_id, out bool allowed)
                    ? allowed ? "Allowed" : "Blocked"
                    : account != null && experience.owner_id == account.PrincipalID ? "Owned" : "Not granted";
                ExperienceFlags flags = (ExperienceFlags)experience.properties;
                string status = (flags & ExperienceFlags.Suspended) != 0 ? "Suspended"
                    : (flags & ExperienceFlags.Disabled) != 0 ? "Disabled"
                    : (flags & ExperienceFlags.Private) != 0 ? "Private"
                    : (flags & ExperienceFlags.Grid) != 0 ? "Grid" : "Active";
                string destination = SafeExperienceUrl(experience.slurl);
                rows.Add(new Dictionary<string, object>
                {
                    ["ExperienceID"] = experience.public_id.ToString(), ["ExperienceName"] = H(experience.name),
                    ["ExperienceDescription"] = H(experience.description), ["ExperienceMaturity"] = H(MaturityName(experience.maturity.ToString(CultureInfo.InvariantCulture))),
                    ["ExperienceOwner"] = H(owner?.FormattedName ?? experience.owner_id.ToString()),
                    ["ExperiencePermission"] = permission, ["ExperienceStatus"] = status,
                    ["ExperienceLogoURL"] = Texture(experience.logo, "static/icons/no_picture.jpg"),
                    ["ExperienceDestination"] = destination, ["HasDestination"] = destination.Length > 0
                });
            }
            vars["Experiences"] = rows;
            vars["HaveData"] = rows.Count > 0;
            vars["NoData"] = rows.Count == 0;
            vars["ExperienceSearch"] = H(search);
            vars["ExperiencesTitle"] = search.Length == 0 && account != null ? "My experiences" : "Experience search";
        }

        private static string SafeExperienceUrl(string value)
        {
            if (!Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out Uri uri)) return string.Empty;
            return uri.Scheme is "http" or "https" or "hop" or "secondlife" ? H(uri.AbsoluteUri) : string.Empty;
        }

        private void AddAbuseList(Dictionary<string, object> vars, UserAccount admin,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (!IsAdmin(admin) || _abuse == null) return;
            string status = parameters.TryGetValue("status", out string requested) ? requested : string.Empty;
            AbuseReportData[] reports = Safe(() => _abuse.GetReports(0, 500, status)) ?? Array.Empty<AbuseReportData>();
            var rows = reports.Select(report => new Dictionary<string, object>
            {
                ["CardNumber"] = report.ReportID, ["Category"] = H(report.Category),
                ["ReporterName"] = H(report.SenderName), ["Abusername"] = H(report.AbuserName),
                ["Summary"] = H(report.Summary), ["AssignedTo"] = H(report.ModeratorName),
                ["Active"] = H(report.Status), ["MoreInfoText"] = "Review"
            }).ToList();
            vars["AbuseReportsList"] = rows;
            vars["HaveData"] = rows.Count > 0;
            vars["NoData"] = rows.Count == 0;
        }

        private void AddAbuseReport(Dictionary<string, object> vars, UserAccount admin,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (!IsAdmin(admin) || _abuse == null || !TryInt(parameters, "cardid", out int reportID)) return;
            AbuseReportData report = Safe(() => _abuse.GetReport(reportID, true));
            if (report == null) return;
            vars["CardNumber"] = report.ReportID; vars["Category"] = H(report.Category);
            vars["Summary"] = H(report.Summary); vars["AbuserName"] = H(report.AbuserName);
            vars["ReporterName"] = H(report.SenderName); vars["RegionName"] = H(report.AbuseRegionName);
            vars["ObjectName"] = string.Empty; vars["ObjectPosition"] = H(report.Position);
            vars["ObjectUUID"] = report.ObjectID.ToString(); vars["Details"] = H(report.Details);
            vars["Notes"] = H(report.ModeratorNotes); vars["AssignedTo"] = H(report.ModeratorName);
            vars["ScreenshotURL"] = BrowserImage(report.ImageData) ?? "static/icons/no_picture.jpg";
            ParseLanding(report.Position, out int x, out int y, out int z);
            vars["HopUrl"] = string.IsNullOrWhiteSpace(report.AbuseRegionName)
                ? string.Empty : Hop(report.AbuseRegionName, x, y, z);
            vars["IsActive"] = SelectOptions(new[] { "Open", "Investigating", "Resolved", "Closed" }, report.Status);
            vars["IsChecked"] = SelectOptions(new[] { "No", "Yes" }, report.CheckFlags != 0 ? "Yes" : "No");
            vars["AdminUsersList"] = new List<Dictionary<string, object>>();
        }

        private static string BrowserImage(byte[] data)
        {
            if (data == null || data.Length < 4) return null;
            string mime = data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4e && data[3] == 0x47 ? "image/png"
                : data[0] == 0xff && data[1] == 0xd8 ? "image/jpeg"
                : data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38 ? "image/gif"
                : data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
                    && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50 ? "image/webp" : null;
            return mime == null ? null : "data:" + mime + ";base64," + Convert.ToBase64String(data);
        }

        private UserAccount AccountFromParameter(IReadOnlyDictionary<string, string> parameters, UserAccount fallback)
        {
            if (parameters.TryGetValue("userid", out string value) && UUID.TryParse(value, out UUID id))
                return Safe(() => _accounts.GetUserAccount(UUID.Zero, id));
            return fallback;
        }

        private string Texture(UUID id, string fallback) => id == UUID.Zero || string.IsNullOrEmpty(_textureBase)
            ? fallback : _textureBase + "/" + id;
        private string Hop(GridRegion region, Vector3 position) => Hop(region.RegionName,
            (int)position.X, (int)position.Y, (int)position.Z);
        private string Hop(string regionName, int x, int y, int z) => _hopBase +
            (_hopBase.EndsWith("/", StringComparison.Ordinal) ? string.Empty : "/") +
            Uri.EscapeDataString(regionName) + "/" + x + "/" + y + "/" + z;
        private static string AccountType(UserAccount account) => account.UserLevel >= 200 ? "Administrator" : account.UserLevel < 0 ? "Disabled" : "Resident";
        private static string H(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
        private static string Value(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
        private static string Trimmed(string value, int maximum)
        {
            string result = (value ?? string.Empty).Trim();
            return result.Length <= maximum ? result : result.Substring(0, maximum);
        }
        private static bool ValidName(string value) => value.Length is >= 1 and <= 31 && Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9'-]*$");
        private static bool ValidEmail(string value) => value.Length is > 2 and <= 254
            && System.Net.Mail.MailAddress.TryCreate(value, out System.Net.Mail.MailAddress address)
            && String.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        private static Dictionary<string, object> Option(string value) => new() { ["Value"] = value };
        private static bool TryInt(IReadOnlyDictionary<string, string> values, string key, out int result)
        {
            result = 0;
            return values.TryGetValue(key, out string value)
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        private static List<Dictionary<string, object>> SelectOptions(IEnumerable<string> values, string selected) =>
            values.Select((value, index) => new Dictionary<string, object> { ["Index"] = value, ["Value"] = value,
                ["selected"] = string.Equals(value, selected, StringComparison.OrdinalIgnoreCase) ? "selected" : string.Empty }).ToList();

        private static void AddCommonLabels(Dictionary<string, object> vars)
        {
            vars["HaveData"] = false; vars["NoData"] = true;
            vars["RegionListText"] = "Regions"; vars["RegionSearchText"] = "Region search";
            vars["SearchForRegionText"] = "Search for a region"; vars["SearchResultForRegionText"] = "Results";
            vars["OnlineUsersText"] = "Online users"; vars["DashboardNameText"] = "Dashboard";
            vars["UserProfileFor"] = "Profile for"; vars["UserFriendsText"] = "friends";
            vars["AbuseReportText"] = "Abuse reports"; vars["MoreInfoText"] = "Details";
            vars["Submit"] = "Submit"; vars["Cancel"] = "Cancel";
        }

        private static T Load<T>(IConfigSource config, IConfig section, string key, string defaultPlugin) where T : class
        {
            string plugin = section.GetString(key, defaultPlugin).Trim();
            if (plugin.Length == 0) return null;
            try
            {
                T service = ServerUtils.LoadPlugin<T>(plugin, new object[] { config });
                return service ?? ServerUtils.LoadPlugin<T>(plugin, new object[] { config, key });
            }
            catch (Exception e) { Log.WarnFormat("[CONTINUUM WEBUI]: Optional {0} unavailable: {1}", key, e.Message); return null; }
        }

        private static T Safe<T>(Func<T> operation)
        {
            if (operation == null) return default;
            try { return operation(); }
            catch (Exception e) { Log.WarnFormat("[CONTINUUM WEBUI]: Service query failed: {0}", e.Message); return default; }
        }
    }
}
