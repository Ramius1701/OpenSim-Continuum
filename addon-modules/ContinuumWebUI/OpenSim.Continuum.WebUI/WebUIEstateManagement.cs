/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections.Generic;
using System.Globalization;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Services.Interfaces;

namespace OpenSim.Continuum.WebUI
{
    internal sealed partial class WebUIServiceIntegration
    {
        private readonly Dictionary<UUID, string> _regionControlRooms;
        private static Dictionary<UUID, string> ConfigureRegionControlRooms(IConfigSource config)
        {
            var rooms = new Dictionary<UUID, string>();
            IConfig section = config.Configs["ContinuumWebUIRegionControlRooms"];
            if (section == null) return rooms;
            string[] keys = section.GetKeys();
            if (keys.Length > 1000) throw new InvalidOperationException("Configure at most 1000 region control-room links");
            foreach (string key in keys)
            {
                string value = section.GetString(key, "").Trim();
                if (!UUID.TryParse(key, out UUID id) || id == UUID.Zero || rooms.ContainsKey(id) || value.Length > 1024
                    || !Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps
                    || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                    throw new InvalidOperationException("Region control rooms require unique region UUIDs and absolute HTTPS URLs without credentials, query or fragment");
                rooms.Add(id, uri.AbsoluteUri);
            }
            return rooms;
        }

        private void AddRegionManagement(string page, IReadOnlyDictionary<string, string> parameters, UserAccount user, Dictionary<string, object> vars)
        {
            bool admin = page == "admin/region_edit.html";
            if (user == null || !user.LocalToGrid || user.UserLevel < 0 || (admin && !IsAdmin(user))) throw new ArgumentException("Authorized local account access required");
            if (!parameters.TryGetValue("regionid", out string raw) || !UUID.TryParse(raw, out UUID id) || id == UUID.Zero)
                throw new ArgumentException("Choose a region from the region manager");
            var region = _grid?.GetRegionByUUID(UUID.Zero, id);
            if (region == null) throw new ArgumentException("Region is unavailable");
            var estate = _estates?.LoadEstateSettings(id, false);
            if (!admin && (estate == null || estate.EstateOwner != user.PrincipalID)) throw new ArgumentException("You do not own this region's current estate");
            string controlRoom = "";
            bool configured = _regionControlRooms != null && _regionControlRooms.TryGetValue(id, out controlRoom);
            vars["RegionEditorID"] = id.ToString(); vars["RegionEditorName"] = H(region.RegionName);
            vars["RegionEditorLocation"] = region.RegionCoordX + ", " + region.RegionCoordY;
            vars["RegionEditorSize"] = region.RegionSizeX + " × " + region.RegionSizeY;
            vars["RegionEditorHost"] = H(region.ServerURI);
            vars["RegionEditorControlRoom"] = configured; vars["RegionEditorNoControlRoom"] = !configured;
            vars["RegionEditorControlRoomURL"] = H(controlRoom ?? "");
            vars["RegionEditorConsole"] = admin && _consoleEndpoints != null && _consoleEndpoints.Count > 0;
            vars["RegionEditorEstate"] = estate != null;
            vars["RegionEditorEstateID"] = estate?.EstateID ?? 0;
            vars["RegionEditorEstatePage"] = admin ? "admin-estate-edit" : "user-estate-edit";
        }
        private const string EstateSaved = "Estate settings saved; running simulators must reload estate data";
        private EstateSettings SelectedEstate(string page, IReadOnlyDictionary<string, string> values, UserAccount user)
        {
            if (user == null) throw new ArgumentException("Log in to manage your estates");
            bool adminRoute = page == "admin/estate_edit.html";
            if (adminRoute && !IsAdmin(user)) throw new ArgumentException("Administrator access required");
            if (!values.TryGetValue("estateid", out string raw) || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 1)
                throw new ArgumentException("Choose an estate from the estate manager");
            if (_estates == null) throw new InvalidOperationException("Estate management is unavailable");
            var estate = _estates.LoadEstateSettings(id);
            if (estate == null || estate.EstateID != (uint)id || (!adminRoute && estate.EstateOwner != user.PrincipalID))
                throw new ArgumentException("Estate not found or you do not own it");
            return estate;
        }

        private void AddEstateEditor(string page, IReadOnlyDictionary<string, string> parameters, UserAccount user, Dictionary<string, object> vars)
        {
            EstateSettings estate = SelectedEstate(page, parameters, user);
            bool enabled = _estates is IEstateSettingsUpdater updater && updater.SupportsEstateSettingsUpdate;
            vars["EstateEditingEnabled"] = enabled; vars["EstateEditingDisabled"] = !enabled;
            vars["EstateID"] = estate.EstateID; vars["EstateName"] = H(estate.EstateName);
            vars["EstateEditToken"] = EstateSettingsEdit.Token(estate); vars["EstateOwnerID"] = estate.EstateOwner.ToString();
            vars["EstatePrice"] = estate.PricePerMeter;
            vars["EstatePublicChecked"] = estate.PublicAccess ? "checked" : "";
            vars["EstateVoiceChecked"] = estate.AllowVoice ? "checked" : "";
            vars["EstateTeleportChecked"] = estate.AllowDirectTeleport ? "checked" : "";
            vars["EstateFormAction"] = page;
            vars["EstateReturnPage"] = page.StartsWith("admin/", StringComparison.Ordinal) ? "admin-estate_manager" : "user-estate_manager";
        }

        private bool ChangeEstate(string page, IReadOnlyDictionary<string, string> form, UserAccount user, out string message)
        {
            string Value(string key) => form.TryGetValue(key, out string value) ? value : "";
            try
            {
                EstateSettings current = SelectedEstate(page, form, user);
                if (_estates is not IEstateSettingsUpdater updater || !updater.SupportsEstateSettingsUpdate)
                    throw new InvalidOperationException("This estate service does not support conditional editing");
                if (Value("action") != "save") throw new ArgumentException("Unknown estate operation");
                if (!string.Equals(Value("editToken"), EstateSettingsEdit.Token(current), StringComparison.Ordinal))
                    throw new InvalidOperationException("Estate settings changed. Reload before saving");
                if (!int.TryParse(Value("price"), NumberStyles.None, CultureInfo.InvariantCulture, out int price))
                    throw new ArgumentException("Enter a reference price from 0 to 1,000,000");
                var updated = new EstateSettings
                {
                    EstateID = current.EstateID, EstateOwner = current.EstateOwner,
                    EstateName = Value("name").Trim(), PricePerMeter = price,
                    PublicAccess = Value("publicAccess") == "true", AllowVoice = Value("voice") == "true",
                    AllowDirectTeleport = Value("directTeleport") == "true"
                };
                EstateSettingsEdit.Validate(current, updated);
                if (!updater.TryUpdateEstateSettings(current, updated))
                    throw new InvalidOperationException("Estate settings changed or were removed. Reload before saving");
                Log.InfoFormat("[CONTINUUM WEBUI]: Estate {0} settings saved by {1} via {2}; simulator reload required", current.EstateID, user.PrincipalID, page);
                message = EstateSaved;
            }
            catch (ArgumentException e) { message = e.Message; }
            catch (InvalidOperationException e) { message = e.Message; }
            catch (Exception)
            {
                Log.Warn("[CONTINUUM WEBUI]: Estate settings operation could not complete; reload the current state before retrying");
                message = "Estate service could not confirm the save. Reload the current settings before retrying";
            }
            return true;
        }
    }
}
