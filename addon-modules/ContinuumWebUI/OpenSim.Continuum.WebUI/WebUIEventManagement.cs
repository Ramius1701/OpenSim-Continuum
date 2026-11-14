using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenMetaverse;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenSim.Services.Interfaces;

namespace OpenSim.Continuum.WebUI
{
    internal sealed partial class WebUIServiceIntegration
    {
        private readonly string _searchWriteSecret;
        private void AddEventEditor(string page, IReadOnlyDictionary<string, string> parameters,
            UserAccount user, Dictionary<string, object> vars)
        {
            if (user == null) throw new ArgumentException("Log in to manage your events");
            vars["EventWritesAvailable"] = !String.IsNullOrEmpty(_searchWriteSecret);
            vars["EventWritesUnavailable"] = String.IsNullOrEmpty(_searchWriteSecret);
            vars["EventID"] = "0"; vars["EventRevision"] = "0";
            vars["EventRequestID"] = Guid.NewGuid().ToString();
            vars["EventTitle"] = ""; vars["EventDescription"] = "";
            vars["EventStart"] = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
            vars["EventMinutes"] = "60"; vars["EventCategoryValue"] = "0"; vars["EventMaturityValue"] = "0";
            vars["EventX"] = "128"; vars["EventY"] = "128"; vars["EventZ"] = "25";
            vars["EventFormAction"] = page;
            string selectedSim = "";
            if (page == "user/edit_event.html")
            {
                if (!Int32.TryParse(Value(parameters, "eventid"), out int id) || id <= 0)
                    throw new ArgumentException("Choose an event from My events");
                Hashtable response = Search("event_info_query", new Hashtable { ["eventID"] = id.ToString(CultureInfo.InvariantCulture) });
                Hashtable row = (response?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault();
                if (row == null || Text(row, "creator") != user.PrincipalID.ToString())
                    throw new ArgumentException("Event not found in your account");
                vars["EventID"] = id; vars["EventRevision"] = H(Text(row, "revision"));
                vars["EventTitle"] = H(Text(row, "name")); vars["EventDescription"] = H(Text(row, "description"));
                if (Int64.TryParse(Text(row, "dateUTC"), out long seconds))
                    vars["EventStart"] = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
                vars["EventMinutes"] = H(Text(row, "duration")); vars["EventCategoryValue"] = H(Text(row, "category"));
                vars["EventMaturityValue"] = H(Text(row, "eventflags")); selectedSim = Text(row, "simname");
                string[] coordinates = Text(row, "globalposition").Trim('<', '>').Split(',');
                if (coordinates.Length == 3)
                { vars["EventX"] = H(coordinates[0]); vars["EventY"] = H(coordinates[1]); vars["EventZ"] = H(coordinates[2]); }
            }
            var regions = (Safe(() => _grid?.GetRegionsByName(UUID.Zero, "", 1000)) ?? new List<GridRegion>())
                .Where(x => x.EstateOwner == user.PrincipalID).ToList();
            GridRegion selected = regions.FirstOrDefault(x => x.RegionName == selectedSim);
            if (selected != null)
            {
                if (Double.TryParse(vars["EventX"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double gx))
                    vars["EventX"] = (gx - selected.RegionLocX).ToString(CultureInfo.InvariantCulture);
                if (Double.TryParse(vars["EventY"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double gy))
                    vars["EventY"] = (gy - selected.RegionLocY).ToString(CultureInfo.InvariantCulture);
            }
            vars["EventRegions"] = regions.Select(x => new Dictionary<string, object>
                { ["RegionID"] = x.RegionID.ToString(), ["RegionName"] = H(x.RegionName), ["RegionSelected"] = x.RegionName == selectedSim ? "selected" : "" }).ToList();
        }

        private bool ChangeEvent(IReadOnlyDictionary<string, string> form, UserAccount user, out string message)
        {
            message = "Event management is unavailable. Contact your grid team";
            if (user == null || user.UserLevel < 0) { message = "Log in to manage your events"; return true; }
            if (String.IsNullOrEmpty(_searchWriteSecret)) return true;
            string action = Value(form, "action");
            Hashtable input = new() { ["writeSecret"] = _searchWriteSecret, ["actor"] = user.PrincipalID.ToString(),
                ["action"] = action, ["eventID"] = Value(form, "eventid"), ["revision"] = Value(form, "revision"),
                ["requestID"] = Value(form, "requestid") };
            if (action != "delete")
            {
                if (!UUID.TryParse(Value(form, "regionid"), out UUID regionID)) { message = "Choose a region you own"; return true; }
                GridRegion region = Safe(() => _grid?.GetRegionByUUID(UUID.Zero, regionID));
                if (region == null || region.EstateOwner != user.PrincipalID) { message = "Choose a region you own"; return true; }
                if (!DateTime.TryParseExact(Value(form, "start"), "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime start))
                { message = "Enter the event date and time in UTC"; return true; }
                if (!Double.TryParse(Value(form, "x"), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                    || !Double.TryParse(Value(form, "y"), NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    || !Double.IsFinite(x) || !Double.IsFinite(y) || x < 0 || y < 0 || x >= region.RegionSizeX || y >= region.RegionSizeY)
                { message = "The landing point must be inside your chosen region"; return true; }
                foreach (string key in new[] { "name", "description", "category", "duration", "eventflags", "x", "y", "z" }) input[key] = Value(form, key);
                input["regionID"] = regionID.ToString(); input["dateUTC"] = new DateTimeOffset(start).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            }
            Hashtable result = Search("continuum_event_write", input);
            bool success = result != null && Boolean.TryParse(Text(result, "success"), out bool ok) && ok;
            message = success ? (action == "delete" ? "Successfully deleted event" : "Successfully saved event")
                : result == null ? "Unable to confirm the change. Retry the same form before creating another event" : Text(result, "errorMessage", "Unable to save event");
            return true;
        }
    }
}
