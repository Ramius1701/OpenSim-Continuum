using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenMetaverse;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace OpenSim.Continuum.WebUI
{
    internal sealed partial class WebUIServiceIntegration
    {
        private Hashtable ClassifiedRequest(string action, UserAccount user, Hashtable fields = null)
        {
            if (user == null || user.UserLevel < 0 || String.IsNullOrEmpty(_searchWriteSecret)) return null;
            fields ??= new Hashtable();
            fields["actor"] = user.PrincipalID.ToString(); fields["writeSecret"] = _searchWriteSecret; fields["action"] = action;
            return Search("continuum_classified_manage", fields);
        }
        private static bool ClassifiedSuccess(Hashtable response) => Boolean.TryParse(Text(response, "success"), out bool ok) && ok;
        private void AddClassifiedManagement(Dictionary<string, object> vars, UserAccount user)
        {
            vars["ClassifiedPending"] = new List<Dictionary<string, object>>();
            vars["ClassifiedManageAvailable"] = false; vars["ClassifiedCreationAvailable"] = false;
            vars["ClassifiedServiceUnavailable"] = false;
            Hashtable response = ClassifiedRequest("list", user);
            Hashtable info = (response?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault();
            if (!ClassifiedSuccess(response) || info == null)
            { vars["ClassifiedServiceUnavailable"] = !String.IsNullOrEmpty(_searchWriteSecret); return; }
            vars["ClassifiedManageAvailable"] = true;
            vars["ClassifiedCreationAvailable"] = Boolean.TryParse(Text(info, "paymentsAvailable"), out bool enabled) && enabled;
            vars["ClassifiedPending"] = (info["pending"] as ArrayList ?? new()).OfType<Hashtable>().Select(x => new Dictionary<string, object>
            { ["RequestID"] = H(Text(x, "requestID")), ["PendingName"] = H(Text(x, "name")), ["PendingPrice"] = H(Text(x, "price")) }).ToList();
            // Use the same authoritative backend for the post-save list, avoiding stale profile caches.
            vars["ClassifiedList"] = (info["listings"] as ArrayList ?? new()).OfType<Hashtable>().Select(x =>
            {
                DirectoryLocation(Text(x, "posglobal"), out int px, out int py, out int pz, Text(x, "simname"));
                return new Dictionary<string, object>
                {
                    ["ClassifiedID"] = H(Text(x, "classifieduuid")), ["Name"] = H(Text(x, "name")), ["Description"] = H(Text(x, "description")),
                    ["Category"] = H(Text(x, "category")), ["CreationDate"] = UnixDate(Text(x, "creationdate")),
                    ["ExpirationDate"] = UnixDate(Text(x, "expirationdate")), ["PriceForListing"] = H(Text(x, "priceforlisting")),
                    ["ClassifiedRegion"] = H(Text(x, "simname")), ["ClassifiedHop"] = Hop(Text(x, "simname"), px, py, pz),
                    ["Maturity"] = Int32.TryParse(Text(x, "classifiedflags"), out int flags) ? (flags & 64) != 0 ? "Adult" : (flags & 10) != 0 ? "Moderate" : "General" : "Unknown"
                };
            }).ToList();
            vars["HaveData"] = ((List<Dictionary<string, object>>)vars["ClassifiedList"]).Count > 0;
            vars["NoData"] = !(bool)vars["HaveData"];
        }
        private void AddClassifiedEditor(string page, IReadOnlyDictionary<string, string> parameters, UserAccount user, Dictionary<string, object> vars)
        {
            if (user == null) throw new ArgumentException("Log in to manage classifieds");
            bool edit = page == "user/classified_edit.html";
            Hashtable response = ClassifiedRequest("list", user);
            Hashtable info = (response?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault();
            bool enabled = ClassifiedSuccess(response) && info != null && (edit || Boolean.TryParse(Text(info, "paymentsAvailable"), out bool paid) && paid);
            vars["ClassifiedEditorAvailable"] = enabled; vars["ClassifiedEditorUnavailable"] = !enabled;
            vars["ClassifiedNew"] = !edit; vars["ClassifiedExisting"] = edit;
            vars["ClassifiedFormAction"] = page; vars["ClassifiedAction"] = edit ? "update" : "create";
            vars["ClassifiedID"] = ""; vars["ClassifiedEditToken"] = ""; vars["ClassifiedRequestID"] = Guid.NewGuid().ToString();
            vars["ClassifiedTitle"] = ""; vars["ClassifiedDescription"] = ""; vars["ClassifiedCategory"] = "1";
            vars["ClassifiedSnapshot"] = Guid.Empty.ToString(); vars["ClassifiedMinimumPrice"] = H(Text(info, "minimumPrice", "50"));
            vars["ClassifiedPrice"] = vars["ClassifiedMinimumPrice"];
            vars["ClassifiedExpiry"] = "";
            vars["ClassifiedRegions"] = (Safe(() => _grid?.GetOnlineRegions(UUID.Zero, 0, 0, 1000)) ?? new List<GridRegion>()).Select(x => new Dictionary<string, object>
            { ["RegionID"] = x.RegionID.ToString(), ["RegionName"] = H(x.RegionName) }).ToList();
            if (!edit || !enabled) return;
            Hashtable detail = ClassifiedRequest("detail", user, new Hashtable { ["classifiedID"] = Value(parameters, "classifiedid") });
            Hashtable row = (detail?["data"] as ArrayList)?.OfType<Hashtable>().FirstOrDefault();
            if (!ClassifiedSuccess(detail) || row == null) throw new ArgumentException("Classified not found in your account");
            vars["ClassifiedID"] = H(Text(row, "classifieduuid")); vars["ClassifiedEditToken"] = H(Text(row, "editToken"));
            vars["ClassifiedTitle"] = H(Text(row, "name")); vars["ClassifiedDescription"] = H(Text(row, "description"));
            vars["ClassifiedCategory"] = H(Text(row, "category")); vars["ClassifiedSnapshot"] = H(Text(row, "snapshotuuid"));
            vars["ClassifiedPrice"] = H(Text(row, "priceforlisting")); vars["ClassifiedExpiry"] = UnixDate(Text(row, "expirationdate"));
        }
        private bool ChangeClassified(IReadOnlyDictionary<string, string> form, UserAccount user, out string message)
        {
            message = "Classified management is unavailable. Contact your grid team";
            if (user == null || user.UserLevel < 0) { message = "Log in to manage classifieds"; return true; }
            if (String.IsNullOrEmpty(_searchWriteSecret)) return true;
            string action = Value(form, "action");
            Hashtable input = new();
            foreach (string key in new[] { "name", "description", "category", "snapshotID", "requestID", "classifiedID", "editToken", "price", "regionID", "maturity", "x", "y", "z" })
                input[key] = Value(form, key);
            if (action == "create")
            {
                if (Value(form, "confirmPayment") != "yes") { message = "Confirm the one-time listing payment before publishing"; return true; }
                if (!UUID.TryParse(Value(form, "regionID"), out UUID regionID)) { message = "Choose a valid region"; return true; }
                GridRegion region = Safe(() => _grid?.GetRegionByUUID(UUID.Zero, regionID));
                if (region == null || !Double.TryParse(Value(form, "x"), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                    || !Double.TryParse(Value(form, "y"), NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    || !Double.IsFinite(x) || !Double.IsFinite(y) || x < 0 || y < 0 || x >= region.RegionSizeX || y >= region.RegionSizeY)
                { message = "The landing coordinates must be inside the chosen region"; return true; }
            }
            else if (action != "update" && action != "delete" && action != "resume") { message = "Unknown classified operation"; return true; }
            Hashtable result = ClassifiedRequest(action, user, input);
            message = ClassifiedSuccess(result) ? action == "delete" ? "Successfully deleted classified" : "Successfully saved classified"
                : result == null ? "Unable to confirm the operation. Open My classifieds and resume any pending listing before paying again"
                : Text(result, "errorMessage", "Unable to change classified");
            return true;
        }
    }
}
