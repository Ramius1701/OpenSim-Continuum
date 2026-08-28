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
        private readonly int _adminLevel;
        private readonly string _hopBase;
        private readonly string _textureBase;
        private readonly string _publicBase;
        private readonly string _mapBase;
        private readonly bool _allowRegistration;
        private readonly string _searchUrl;
        private readonly string _economyUrl;
        private readonly string _economySecret;
        private readonly int _rpcTimeoutMs;

        internal WebUIServiceIntegration(IConfigSource config, IConfig section, IUserAccountService accounts)
        {
            _accounts = accounts;
            _adminLevel = Math.Clamp(section.GetInt("AdminUserLevel", 200), 1, 255);
            _hopBase = section.GetString("HopURLBase", "hop://").Trim();
            _textureBase = section.GetString("TextureURL", string.Empty).TrimEnd('/');
            _publicBase = section.GetString("PublicURL", string.Empty).TrimEnd('/');
            _mapBase = section.GetString("MapServiceURL", _publicBase).TrimEnd('/');
            _allowRegistration = section.GetBoolean("AllowRegistration", true);
            _searchUrl = section.GetString("SearchServiceURL", string.Empty).Trim();
            _economyUrl = section.GetString("EconomyServiceURL", string.Empty).Trim();
            _economySecret = section.GetString("EconomySharedSecret", string.Empty);
            _rpcTimeoutMs = Math.Clamp(section.GetInt("ServiceTimeoutMilliseconds", 5000), 500, 30000);
            _grid = Load<IGridService>(config, section, "GridService", "OpenSim.Services.GridService.dll:GridService");
            _gridUsers = Load<IGridUserService>(config, section, "GridUserService", "OpenSim.Services.UserAccountService.dll:GridUserService");
            _friends = Load<IFriendsService>(config, section, "FriendsService", "OpenSim.Services.FriendsService.dll:FriendsService");
            _profiles = Load<IUserProfilesService>(config, section, "UserProfilesService", "OpenSim.Services.UserProfilesService.dll:UserProfilesService");
            _abuse = Load<IAbuseReportsService>(config, section, "AbuseReportsService", "OpenSim.Services.AbuseReportsService.dll:AbuseReportsService");
            _experiences = Load<IExperienceService>(config, section, "ExperienceService", "OpenSim.Services.ExperienceService.dll:ExperienceService");
            _groups = Load<IGroupsService>(config, section, "GroupsService", "OpenSim.Addons.Groups.dll:GroupsService");
        }

        internal bool IsAdmin(UserAccount account) => account != null && account.UserLevel >= _adminLevel;

        internal string ResolvePage(string relative)
        {
            return relative.ToLowerInvariant() switch
            {
                "world_map.html" => "world.html",
                "events.html" => "events/events.html",
                "classifieds.html" => "classifieds/classifieds.html",
                "destinations.html" => "destinations.html",
                "profile.html" => "user/profile.html",
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
            string key = page.ToLowerInvariant();
            if (key is "index.html" or "home.html" or "welcomescreen/index.html" or "welcomescreen/gridstatus.html")
                AddGridStatus(vars);
            if (key is "region_list.html" or "region_search.html")
                AddRegions(vars, parameters.TryGetValue("regionname", out string name) ? name : string.Empty);
            if (key == "world.html") AddWorldMap(vars);
            if (key == "events/events.html") AddEvents(vars, parameters);
            if (key == "classifieds/classifieds.html") AddClassifieds(vars, parameters);
            if (key == "destinations.html") AddDestinations(vars, parameters);
            if (key == "online_users.html") AddOnlineUsers(vars);
            if (key == "user_search.html") AddUserSearch(vars, parameters, currentUser);
            if (key == "register.html") AddRegistration(vars);
            if (key is "userhome.html" or "user/userhome.html") AddUserHome(vars, currentUser);
            if (key == "user/transactions.html") AddTransactions(vars, currentUser, parameters);
            if (key is "user/profile.html" or "webprofile/modal_profile.html")
                AddProfile(vars, AccountFromParameter(parameters, currentUser));
            if (key == "user/profile_edit.html") AddEditableProfile(vars, currentUser);
            if (key == "webprofile/modal_picks.html") AddPicks(vars, AccountFromParameter(parameters, currentUser));
            if (key == "webprofile/modal_regions.html") AddOwnedRegions(vars, AccountFromParameter(parameters, currentUser));
            if (key == "webprofile/modal_groups.html") AddPublicGroups(vars, AccountFromParameter(parameters, currentUser));
            if (key == "regionprofile/modal_profile.html") AddRegionProfile(vars, parameters);
            if (key == "regionprofile/modal_parcels.html") AddRegionParcels(vars, parameters);
            if (key == "user/friends.html") AddFriends(vars, currentUser);
            if (key == "user/groups.html") AddGroups(vars, currentUser);
            if (key == "user/region_manager.html") AddManagedRegions(vars, currentUser, false);
            if (key == "admin/region_manager.html") AddManagedRegions(vars, currentUser, true);
            if (key == "experiences.html") AddExperiences(vars, currentUser, parameters);
            if (key == "admin/abuse_manager.html") AddAbuseList(vars, currentUser, parameters);
            if (key == "admin/abuse_report.html") AddAbuseReport(vars, currentUser, parameters);
            if (key == "admin/user_manager.html") AddUserManager(vars, currentUser);
            if (key is "admin/user_edit.html" or "admin/user_password.html")
                AddAdminUser(vars, currentUser, parameters);
            if (key == "user/email.html" && currentUser != null) vars["UserEmail"] = H(currentUser.Email);
        }

        internal bool HandlePost(string page, IReadOnlyDictionary<string, string> form,
            UserAccount currentUser, out string message)
        {
            message = string.Empty;
            switch (page.ToLowerInvariant())
            {
                case "register.html": return Register(form, out message);
                case "user/password.html": return ChangePassword(form, currentUser, out message);
                case "user/email.html": return ChangeEmail(form, currentUser, out message);
                case "user/profile_edit.html": return UpdateProfile(form, currentUser, out message);
                case "admin/abuse_report.html": return UpdateAbuse(form, currentUser, out message);
                case "admin/user_edit.html": return UpdateUser(form, currentUser, out message);
                case "admin/user_password.html": return ResetUserPassword(form, currentUser, out message);
                default: return false;
            }
        }

        private void AddRegistration(Dictionary<string, object> vars)
        {
            vars["Registrations"] = _allowRegistration;
            vars["NoRegistrations"] = !_allowRegistration;
            vars["SubmitURL"] = "register.html";
            vars["AvatarName"] = string.Empty;
            vars["ToSMessage"] = "By creating an account, you agree to this grid's terms of service.";
            vars["TermsOfServiceAccept"] = "I accept the terms of service";
            vars["Months"] = Enumerable.Range(1, 12).Select(n => Option(n.ToString("00", CultureInfo.InvariantCulture))).ToList();
            vars["Days"] = Enumerable.Range(1, 31).Select(n => Option(n.ToString("00", CultureInfo.InvariantCulture))).ToList();
            int year = DateTime.UtcNow.Year;
            vars["Years"] = Enumerable.Range(year - 100, 87).Reverse().Select(n => Option(n.ToString(CultureInfo.InvariantCulture))).ToList();
            vars["AvatarArchive"] = new List<Dictionary<string, object>>();
            vars["RegionList"] = (Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 1000)) ?? new List<GridRegion>())
                .Select(r => new Dictionary<string, object> { ["RegionUUID"] = r.RegionID.ToString(), ["RegionName"] = H(r.RegionName) }).ToList();
        }

        private bool Register(IReadOnlyDictionary<string, string> form, out string message)
        {
            message = string.Empty;
            if (!_allowRegistration) { message = "Registration is disabled"; return true; }
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
            if (!form.TryGetValue("ToSAccept", out string accepted) || accepted != "Accepted") { message = "The terms of service must be accepted"; return true; }
            if (_accounts.GetUserAccount(UUID.Zero, first, last) != null) { message = "That avatar name is already registered"; return true; }
            try
            {
                MethodInfo create = _accounts.GetType().GetMethod("CreateUser", new[] { typeof(UUID), typeof(UUID), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) });
                if (create == null) { message = "Account creation is unavailable on this service"; return true; }
                var created = create.Invoke(_accounts, new object[] { UUID.Zero, UUID.Random(), first, last, password, email, string.Empty }) as UserAccount;
                if (created == null) { message = "Account creation failed"; return true; }
                if (_authenticationForPassword == null || !_authenticationForPassword.SetPassword(created.PrincipalID, password))
                {
                    message = "Account was created, but its password could not be stored; contact a grid administrator";
                    return true;
                }
                if (_gridUsers != null && form.TryGetValue("UserHomeRegion", out string homeText) && UUID.TryParse(homeText, out UUID home))
                    _gridUsers.SetHome(created.PrincipalID.ToString(), home, new Vector3(128, 128, 25), new Vector3(0, 1, 0));
                message = "Account created successfully";
            }
            catch (TargetInvocationException e) { message = "Account creation failed: " + H(e.InnerException?.Message ?? e.Message); }
            catch (Exception e) { message = "Account creation failed: " + H(e.Message); }
            return true;
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
            if (email != Value(form, "emailnewconf").Trim() || email.Length > 254 || !email.Contains('@')) { message = "Email addresses do not match or are invalid"; return true; }
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
            if (notes.Length > 8192) notes = notes.Substring(0, 8192);
            message = _abuse.UpdateReport(id, status, notes, admin.PrincipalID, admin.Name) ? "Abuse report updated" : "Abuse report update failed";
            return true;
        }

        private void AddGridStatus(Dictionary<string, object> vars)
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
            vars["GridStatus"] = "status";
            vars["GridOnline"] = regions.Count > 0 ? "Online" : "Offline";
            vars["TotalUserCount"] = "Residents";
            vars["UserCount"] = accounts.Count;
            vars["TotalRegionCount"] = "Online regions";
            vars["RegionCount"] = regions.Count;
            vars["UniqueVisitors"] = "Registered residents";
            vars["UniqueVisitorCount"] = accounts.Count;
            vars["OnlineNow"] = "Online now";
            vars["OnlineNowCount"] = online;
            vars["VoiceActiveLabel"] = "Voice";
            vars["VoiceActive"] = "Configured by grid";
            vars["CurrencyActiveLabel"] = "Currency";
            vars["CurrencyActive"] = "Configured by grid";
        }

        private void AddRegions(Dictionary<string, object> vars, string search)
        {
            List<GridRegion> regions = string.IsNullOrWhiteSpace(search)
                ? Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 10000))
                : Safe(() => _grid?.GetRegionsByName(UUID.Zero, search.Trim(), 100));
            regions ??= new List<GridRegion>();
            var rows = regions.Select(RegionRow).ToList();
            vars["RegionList"] = rows;
            vars["RegionsList"] = rows;
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
            var rows = new List<Dictionary<string, object>>();
            foreach (UserAccount account in accounts.Take(10000))
            {
                GridUserInfo info = Safe(() => _gridUsers?.GetGridUserInfo(account.PrincipalID.ToString()));
                GridRegion region = info == null ? null : Safe(() => _grid?.GetRegionByUUID(UUID.Zero, info.LastRegionID));
                rows.Add(new Dictionary<string, object>
                {
                    ["UserID"] = account.PrincipalID.ToString(), ["UserName"] = H(account.FormattedName),
                    ["UserDisplayName"] = string.Empty, ["UserType"] = H(AccountType(account)),
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
                if (email.Length > 254 || !email.Contains('@')) { message = "Email address is invalid"; return true; }
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

        private void AddRegionProfile(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters)
        {
            GridRegion region = UUID.TryParse(Value(parameters, "regionid"), out UUID regionID)
                ? Safe(() => _grid?.GetRegionByUUID(UUID.Zero, regionID)) : null;
            if (region == null) return;

            UserAccount owner = region.EstateOwner == UUID.Zero ? null
                : Safe(() => _accounts.GetUserAccount(UUID.Zero, region.EstateOwner));
            var residents = new List<Dictionary<string, object>>();
            if (_gridUsers != null)
            {
                List<UserAccount> accounts = Safe(() => _accounts.GetUserAccountsWhere(UUID.Zero, "1=1")) ?? new();
                foreach (UserAccount account in accounts.Take(10000))
                {
                    GridUserInfo info = Safe(() => _gridUsers.GetGridUserInfo(account.PrincipalID.ToString()));
                    if (info?.Online != true || info.LastRegionID != region.RegionID) continue;
                    residents.Add(new Dictionary<string, object>
                    {
                        ["UserUUID"] = account.PrincipalID.ToString(), ["UserName"] = H(account.FormattedName)
                    });
                }
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
            vars["NumberOfParcelsInRegion"] = "Available in parcel view"; vars["ParcelsInRegionText"] = "Parcels";
            vars["NumberOfUsersInRegionText"] = "Residents currently in region";
            vars["NumberOfUsersInRegion"] = residents.Count; vars["UsersInRegion"] = residents;
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

        private void AddOnlineUsers(Dictionary<string, object> vars)
        {
            var rows = new List<Dictionary<string, object>>();
            if (_gridUsers != null)
            {
                List<UserAccount> accounts = Safe(() => _accounts.GetUserAccountsWhere(UUID.Zero, "1=1")) ?? new();
                foreach (UserAccount account in accounts.Take(10000))
                {
                    GridUserInfo info = Safe(() => _gridUsers.GetGridUserInfo(account.PrincipalID.ToString()));
                    if (info == null || !info.Online) continue;
                    GridRegion region = Safe(() => _grid?.GetRegionByUUID(UUID.Zero, info.LastRegionID));
                    rows.Add(new Dictionary<string, object>
                    {
                        ["UserName"] = H(account.FormattedName), ["UserID"] = account.PrincipalID.ToString(),
                        ["UserRegion"] = H(region?.RegionName ?? "Unknown"),
                        ["UserRegionID"] = info.LastRegionID.ToString(),
                        ["UserLocation"] = H(info.LastPosition.ToString()),
                        ["HopUrl"] = region == null ? string.Empty : Hop(region, info.LastPosition)
                    });
                }
            }
            vars["UsersOnlineList"] = rows;
        }

        private void AddUserSearch(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters,
            UserAccount currentUser)
        {
            string search = Value(parameters, "username").Trim();
            List<UserAccount> accounts = search.Length < 2
                ? new List<UserAccount>()
                : Safe(() => _accounts.GetUserAccounts(UUID.Zero, search)) ?? new List<UserAccount>();
            var rows = new List<Dictionary<string, object>>();
            foreach (UserAccount account in accounts.Take(100))
            {
                var profile = new UserProfileProperties { UserId = account.PrincipalID };
                string result = string.Empty;
                Safe(() => _profiles?.AvatarPropertiesRequest(ref profile, ref result));
                rows.Add(new Dictionary<string, object>
                {
                    ["UserID"] = account.PrincipalID.ToString(), ["UserName"] = H(account.FormattedName),
                    ["UserType"] = H(AccountType(account)),
                    ["UserPictureURL"] = Texture(profile.ImageId, "static/icons/no_avatar.jpg")
                });
            }
            vars["UsersList"] = rows; vars["UserSearchText"] = "Resident search";
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
            vars["UserName"] = H(account.FormattedName);
            vars["UserType"] = H(AccountType(account));
            vars["AccountType"] = "Account type";
            vars["UserHomeRegion"] = H(home?.RegionName ?? "Not set");
            vars["UserLastLogin"] = gridUser?.Login.ToString("u", CultureInfo.InvariantCulture) ?? "Unknown";
            vars["UserBorn"] = DateTimeOffset.FromUnixTimeSeconds(account.Created).UtcDateTime.ToString("d", CultureInfo.InvariantCulture);
            vars["UserPictureURL"] = Texture(UUID.Zero, "static/icons/no_avatar.jpg");
            Hashtable statement = EconomyStatement(account.PrincipalID, null, 1);
            vars["UserBalance"] = statement != null && statement.ContainsKey("balance") ? statement["balance"] : "Unavailable";
            AddGroups(vars, account);
        }

        private void AddEvents(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters)
        {
            string text = Value(parameters, "text");
            Hashtable response = Search("dir_events_query", new Hashtable
            {
                ["text"] = text, ["flags"] = "0", ["query_start"] = "0"
            });
            var rows = new List<Dictionary<string, object>>();
            if (response?["data"] is ArrayList data)
            {
                foreach (Hashtable summary in data.OfType<Hashtable>().Take(50))
                {
                    Hashtable detailResponse = Search("event_info_query", new Hashtable { ["eventID"] = Text(summary, "event_id") });
                    Hashtable detail = (detailResponse?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault() ?? summary;
                    DateTime.TryParse(Text(detail, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime date);
                    rows.Add(new Dictionary<string, object>
                    {
                        ["EventDateTimeUTC"] = date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                        ["EventDateTime"] = date == default ? H(Text(summary, "date")) : date.Day.ToString(CultureInfo.InvariantCulture),
                        ["EventHourTime"] = date == default ? string.Empty : date.ToString("MMM", CultureInfo.InvariantCulture),
                        ["EventMinuteTime"] = date == default ? string.Empty : date.Year.ToString(CultureInfo.InvariantCulture),
                        ["EventTime"] = date == default ? string.Empty : date.ToString("t", CultureInfo.InvariantCulture),
                        ["Name"] = H(Text(detail, "name")), ["Description"] = H(Text(detail, "description")),
                        ["Category"] = H(Text(detail, "category")), ["CategoryImage"] = "static/icons/event.png",
                        ["SimName"] = H(Text(detail, "simname")), ["Maturity"] = H(Text(detail, "eventflags")),
                        ["CoverCharge"] = H(Text(detail, "coveramount")), ["DurationText"] = H(Text(detail, "duration"))
                    });
                }
            }
            vars["EventList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["EventsText"] = "Events"; vars["AddEventText"] = "Add event"; vars["SearchText"] = "Search";
            vars["CategoryType"] = SelectOptions(new[] { "All", "Discussion", "Sports", "Live Music", "Commercial" }, Value(parameters, "category"));
            vars["TimeFrame"] = SelectOptions(new[] { "Today", "Tomorrow", "This week" }, Value(parameters, "timeframe"));
            vars["PG_checked"] = "checked"; vars["MA_checked"] = "checked"; vars["AO_checked"] = string.Empty;
        }

        private void AddClassifieds(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters)
        {
            Hashtable response = Search("dir_classified_query", new Hashtable
            {
                ["text"] = Value(parameters, "text"), ["flags"] = "0",
                ["category"] = Value(parameters, "category"), ["query_start"] = "0"
            });
            var rows = new List<Dictionary<string, object>>();
            if (response?["data"] is ArrayList data)
            {
                foreach (Hashtable summary in data.OfType<Hashtable>().Take(50))
                {
                    Hashtable detailResponse = Search("classifieds_info_query", new Hashtable { ["classifiedID"] = Text(summary, "classifiedid") });
                    Hashtable detail = (detailResponse?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault() ?? summary;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["Name"] = H(Text(detail, "name")), ["Description"] = H(Text(detail, "description")),
                        ["CreationDate"] = UnixDate(Text(detail, "creationdate", Text(summary, "creation_date"))),
                        ["ExpirationDate"] = UnixDate(Text(detail, "expirationdate", Text(summary, "expiration_date"))),
                        ["Maturity"] = H(Text(detail, "classifiedflags", Text(summary, "classifiedflags"))),
                        ["PriceForListing"] = H(Text(detail, "priceforlisting", Text(summary, "priceforlisting")))
                    });
                }
            }
            vars["ClassifiedList"] = rows; vars["HaveData"] = rows.Count > 0; vars["NoData"] = rows.Count == 0;
            vars["ClassifiedsText"] = "Classifieds"; vars["SearchText"] = "Search";
            vars["CategoryType"] = SelectOptions(new[] { "All", "Shopping", "Land rental", "Property rental", "Special attraction", "New products", "Employment", "Wanted", "Service", "Personal" }, Value(parameters, "category"));
            vars["PG_checked"] = "checked"; vars["MA_checked"] = "checked"; vars["AO_checked"] = string.Empty;
        }

        private void AddDestinations(Dictionary<string, object> vars, IReadOnlyDictionary<string, string> parameters)
        {
            string text = Value(parameters, "q").Trim();
            string tab = Value(parameters, "tab").Trim().ToLowerInvariant();
            if (tab is not ("popular" or "featured" or "discover")) tab = "popular";
            int.TryParse(Value(parameters, "cat"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int category);
            int flags = MaturityFlags(Value(parameters, "m"));
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
                    Hashtable detailsResponse = Search("parcel_info_query", new Hashtable { ["parcel_id"] = Text(summary, "parcel_id") });
                    Hashtable detail = (detailsResponse?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault();
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

        private static string CategoryName(string value) => value switch
        {
            "3" => "Arts & Culture", "4" => "Business", "5" => "Education", "6" => "Gaming",
            "7" => "Hangout", "8" => "Newcomer", "9" => "Parks & Nature", "10" => "Residential",
            "11" => "Shopping", "14" => "Rental", _ => "Other"
        };

        private static string MaturityName(string value) => value switch { "1" => "Mature", "2" => "Adult", _ => "General" };

        private void AddTransactions(Dictionary<string, object> vars, UserAccount account, IReadOnlyDictionary<string, string> parameters)
        {
            var rows = new List<Dictionary<string, object>>();
            if (account != null)
            {
                DateTime? before = null;
                if (DateTime.TryParse(Value(parameters, "date_end"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime parsed)) before = parsed.ToUniversalTime().AddDays(1);
                Hashtable statement = EconomyStatement(account.PrincipalID, before, 500);
                if (statement?["history"] is ArrayList history)
                {
                    foreach (Hashtable item in history.OfType<Hashtable>())
                    {
                        UUID.TryParse(Text(item, "counterpartyID"), out UUID counterpartyID);
                        UserAccount counterparty = counterpartyID == UUID.Zero ? null : Safe(() => _accounts.GetUserAccount(UUID.Zero, counterpartyID));
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
                }
            }
            vars["TransactionsList"] = rows; vars["TransactionsText"] = "Transactions";
            vars["DateStart"] = H(Value(parameters, "date_start")); vars["DateEnd"] = H(Value(parameters, "date_end"));
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

        private void AddProfile(Dictionary<string, object> vars, UserAccount account)
        {
            if (account == null) return;
            var profile = new UserProfileProperties { UserId = account.PrincipalID };
            string result = string.Empty;
            bool found = Safe(() => _profiles?.AvatarPropertiesRequest(ref profile, ref result)) ?? false;
            GridUserInfo gridUser = Safe(() => _gridUsers?.GetGridUserInfo(account.PrincipalID.ToString()));
            GridRegion region = gridUser == null ? null : Safe(() => _grid?.GetRegionByUUID(UUID.Zero, gridUser.LastRegionID));
            UserAccount partner = found && profile.PartnerId != UUID.Zero ? Safe(() => _accounts.GetUserAccount(UUID.Zero, profile.PartnerId)) : null;
            vars["UserName"] = H(account.FormattedName);
            vars["UserType"] = H(AccountType(account));
            vars["UserBorn"] = DateTimeOffset.FromUnixTimeSeconds(account.Created).UtcDateTime.ToString("d", CultureInfo.InvariantCulture);
            vars["UserPartner"] = H(partner?.FormattedName ?? "None");
            vars["UserAboutMe"] = H(found ? profile.AboutText : string.Empty);
            vars["UserPictureURL"] = Texture(found ? profile.ImageId : UUID.Zero, "static/icons/no_avatar.jpg");
            vars["IsOnline"] = gridUser?.Online == true ? "Yes" : "No";
            vars["UserIsOnline"] = gridUser?.Online == true;
            vars["OnlineLocation"] = H(region?.RegionName ?? string.Empty);
            vars["Groups"] = new List<Dictionary<string, object>>();
            vars["GroupsJoined"] = 0;
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
            { message = "Profile update failed: " + H(result); return true; }
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
                foreach (FriendInfo friend in Safe(() => _friends.GetFriends(account.PrincipalID)) ?? Array.Empty<FriendInfo>())
                {
                    string idText = friend.Friend;
                    int semicolon = idText.IndexOf(';');
                    if (semicolon >= 0) idText = idText.Substring(0, semicolon);
                    if (!UUID.TryParse(idText, out UUID friendID)) continue;
                    UserAccount friendAccount = Safe(() => _accounts.GetUserAccount(UUID.Zero, friendID));
                    GridUserInfo info = Safe(() => _gridUsers?.GetGridUserInfo(friendID.ToString()));
                    GridRegion region = info == null ? null : Safe(() => _grid?.GetRegionByUUID(UUID.Zero, info.LastRegionID));
                    rows.Add(new Dictionary<string, object>
                    {
                        ["FriendID"] = friendID.ToString(), ["FriendName"] = H(friendAccount?.FormattedName ?? friendID.ToString()),
                        ["FriendRegion"] = H(info?.Online == true ? region?.RegionName ?? "Unknown" : "Offline"),
                        ["FriendRegionID"] = info?.LastRegionID.ToString() ?? UUID.Zero.ToString(),
                        ["FriendLocation"] = H(info?.Online == true ? info.LastPosition.ToString() : "Offline"),
                        ["HopUrl"] = info?.Online == true && region != null ? Hop(region, info.LastPosition) : string.Empty
                    });
                }
            }
            vars["UserFriendsList"] = rows;
            vars["HaveData"] = rows.Count > 0;
            vars["NoData"] = rows.Count == 0;
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

        private void AddPublicGroups(Dictionary<string, object> vars, UserAccount account)
        {
            AddGroups(vars, account);
            if (vars.TryGetValue("Groups", out object value) && value is List<Dictionary<string, object>> rows)
            {
                rows.RemoveAll(row => !String.Equals(row["GroupProfileListed"]?.ToString(), "Yes", StringComparison.Ordinal));
                vars["GroupsJoined"] = rows.Count;
                vars["HaveData"] = rows.Count > 0;
                vars["NoData"] = rows.Count == 0;
            }
            if (account != null) { vars["UserName"] = H(account.FormattedName); vars["UserID"] = account.PrincipalID.ToString(); }
        }

        private void AddPicks(Dictionary<string, object> vars, UserAccount account)
        {
            var rows = new List<Dictionary<string, object>>();
            if (account != null && _profiles != null && Safe(() => _profiles.AvatarPicksRequest(account.PrincipalID)) is OSDArray picks)
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

        private void AddOwnedRegions(Dictionary<string, object> vars, UserAccount account)
        {
            List<GridRegion> regions = account == null ? new() :
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
            string search = parameters.TryGetValue("q", out string query) ? query.Trim() : string.Empty;
            if (_experiences != null)
            {
                if (!string.IsNullOrEmpty(search)) experiences = Safe(() => _experiences.FindExperiencesByName(search)) ?? Array.Empty<ExperienceInfo>();
                else if (account != null)
                {
                    UUID[] ids = Safe(() => _experiences.GetAgentExperiences(account.PrincipalID)) ?? Array.Empty<UUID>();
                    experiences = ids.Length == 0 ? Array.Empty<ExperienceInfo>() : Safe(() => _experiences.GetExperienceInfos(ids)) ?? Array.Empty<ExperienceInfo>();
                }
            }
            foreach (ExperienceInfo experience in experiences)
                rows.Add(new Dictionary<string, object>
                {
                    ["ExperienceID"] = experience.public_id.ToString(), ["ExperienceName"] = H(experience.name),
                    ["ExperienceDescription"] = H(experience.description), ["ExperienceMaturity"] = experience.maturity,
                    ["ExperienceOwner"] = experience.owner_id.ToString()
                });
            vars["Experiences"] = rows;
            vars["HaveData"] = rows.Count > 0;
            vars["NoData"] = rows.Count == 0;
            vars["ExperienceSearch"] = H(search);
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
            AbuseReportData report = Safe(() => _abuse.GetReport(reportID, false));
            if (report == null) return;
            vars["CardNumber"] = report.ReportID; vars["Category"] = H(report.Category);
            vars["Summary"] = H(report.Summary); vars["AbuserName"] = H(report.AbuserName);
            vars["ReporterName"] = H(report.SenderName); vars["RegionName"] = H(report.AbuseRegionName);
            vars["ObjectName"] = string.Empty; vars["ObjectPosition"] = H(report.Position);
            vars["ObjectUUID"] = report.ObjectID.ToString(); vars["Details"] = H(report.Details);
            vars["Notes"] = H(report.ModeratorNotes); vars["AssignedTo"] = H(report.ModeratorName);
            vars["ScreenshotURL"] = "static/icons/no_picture.jpg"; vars["HopUrl"] = string.Empty;
            vars["IsActive"] = SelectOptions(new[] { "Open", "Investigating", "Resolved", "Closed" }, report.Status);
            vars["IsChecked"] = SelectOptions(new[] { "No", "Yes" }, report.CheckFlags != 0 ? "Yes" : "No");
            vars["AdminUsersList"] = new List<Dictionary<string, object>>();
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
        private static bool ValidName(string value) => value.Length is >= 1 and <= 31 && Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9'-]*$");
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
