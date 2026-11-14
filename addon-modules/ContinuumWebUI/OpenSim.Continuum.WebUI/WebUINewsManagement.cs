/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenSim.Services.Interfaces;

namespace OpenSim.Continuum.WebUI
{
    internal sealed partial class WebUIServiceIntegration
    {
        private readonly WebUIContentStore _contentStore;

        private void AddNews(string page, IReadOnlyDictionary<string, string> parameters,
            UserAccount user, Dictionary<string, object> vars)
        {
            var rows = _contentStore.Read();
            bool admin = page.StartsWith("admin/", StringComparison.Ordinal) && IsAdmin(user);
            if (!admin) rows = rows.Where(x => x.Published).ToArray();
            int offset = 0;
            if (parameters.TryGetValue("newsoffset", out string rawOffset)
                && (!Int32.TryParse(rawOffset, NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset > 500))
                throw new ArgumentException("Invalid news page");
            vars["News"] = "Grid news"; vars["NewsItemTitle"] = "Title"; vars["NewsItemText"] = "Article";
            vars["AddNewsText"] = "Create news article"; vars["EditNewsText"] = "Edit news article";
            vars["NewsRows"] = rows.Skip(offset).Take(20).Select(x => new Dictionary<string, object>
            {
                ["NewsID"] = x.ID.ToString(), ["NewsTitle"] = H(x.Title), ["NewsText"] = H(x.Text),
                ["NewsDate"] = H(x.CreatedUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)),
                ["NewsState"] = x.Published ? "Published" : "Draft", ["NewsRevision"] = x.Revision
            }).ToList();
            vars["NewsCount"] = rows.Length;
            vars["NewsHasPrevious"] = offset > 0;
            vars["NewsHasNext"] = offset + 20 < rows.Length;
            vars["NewsPreviousOffset"] = Math.Max(0, offset - 20);
            vars["NewsNextOffset"] = offset + 20;
            vars["NewsID"] = ""; vars["NewsRevision"] = 0; vars["NewsTitle"] = ""; vars["NewsText"] = "";
            vars["NewsPublished"] = "";
            if (page == "admin/news_edit.html")
            {
                if (!parameters.TryGetValue("newsid", out string raw) || !Guid.TryParse(raw, out Guid id))
                    throw new ArgumentException("Choose an article from News management");
                var row = rows.FirstOrDefault(x => x.ID == id)
                    ?? throw new ArgumentException("News article not found");
                vars["NewsID"] = row.ID.ToString(); vars["NewsRevision"] = row.Revision;
                vars["NewsTitle"] = H(row.Title); vars["NewsText"] = H(row.Text);
                vars["NewsPublished"] = row.Published ? "checked" : "";
            }
        }
        private bool ChangeNews(string page, IReadOnlyDictionary<string, string> form,
            UserAccount user, out string message)
        {
            if (!IsAdmin(user)) { message = "Administrator access required"; return true; }
            string Value(string key) => form.TryGetValue(key, out var value) ? value : "";
            try
            {
                Guid id = Guid.Empty;
                int revision = 0;
                if (page != "admin/news_add.html"
                    && (!Guid.TryParse(Value("newsid"), out id) || id == Guid.Empty
                        || !Int32.TryParse(Value("revision"), NumberStyles.None, CultureInfo.InvariantCulture, out revision)))
                    throw new ArgumentException("Choose an article and reload its current revision");
                if (page == "admin/news_manager.html")
                {
                    if (Value("action") != "delete") throw new ArgumentException("Unknown news operation");
                    _contentStore.Delete(id, revision); message = "Successfully deleted news article";
                    Log.InfoFormat("[CONTINUUM WEBUI]: News article {0} deleted by {1}", id, user.PrincipalID);
                }
                else
                {
                    Guid saved = _contentStore.Save(id, revision, Guid.Parse(user.PrincipalID.ToString()),
                        Value("NewsTitle"), Value("NewsText"), Value("published") == "true");
                    Log.InfoFormat("[CONTINUUM WEBUI]: News article {0} saved by {1}", saved, user.PrincipalID);
                    message = "Successfully saved news article";
                }
            }
            catch (ArgumentException e) { message = e.Message; }
            catch (InvalidOperationException e) { message = e.Message; }
            catch (System.IO.IOException) { message = "News storage is unavailable or busy. Reload and try again"; }
            catch (UnauthorizedAccessException) { message = "News storage is not writable. Contact a grid administrator"; }
            return true;
        }
    }
}
