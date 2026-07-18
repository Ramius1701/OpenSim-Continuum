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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;

namespace OpenSim.Region.OptionalModules.World.RegionCurrency
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "RegionCurrencyModule")]
    public class RegionCurrencyModule : ISharedRegionModule
    {
        private const string CurrencyFeatureTitle = "Viewer-visible local currency";
        private const string CurrencyFeatureBody =
            "The estate can run a local persistent currency ledger that sends live balances to the viewer, handles transfers, object payments, land/object purchases and simulator economy charges without requiring a separate currency server.";
        private const string CurrencyFeatureOverview =
            "The bundled BetaGridLikeMoneyModule now works as a lightweight local economy backend. It persists avatar balances in a tab-separated ledger, grants a configurable first-use balance, pushes MoneyBalanceReply updates so compatible viewers show the current balance, and applies the same balance path to viewer transfers, scripted money calls, object payments, land/object purchases, upload charges and group creation charges.";
        private const string CurrencySessionCookie = "RegionWebCurrency";
        private readonly object m_currencyAuthLock = new object();
        private readonly object m_currencyPurchaseLock = new object();
        private readonly object m_currencyPayPalLock = new object();
        private readonly Dictionary<string, CurrencyLoginChallenge> m_currencyChallenges = new Dictionary<string, CurrencyLoginChallenge>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CurrencyWebSession> m_currencySessions = new Dictionary<string, CurrencyWebSession>(StringComparer.Ordinal);
        private readonly Dictionary<UUID, DateTime> m_currencyLastChallengeUTCByAgent = new Dictionary<UUID, DateTime>();
        private readonly Dictionary<string, CurrencyPurchaseRequest> m_currencyPurchaseRequests = new Dictionary<string, CurrencyPurchaseRequest>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CurrencyPayPalOrder> m_currencyPayPalOrders = new Dictionary<string, CurrencyPayPalOrder>(StringComparer.OrdinalIgnoreCase);
        private bool m_currencyPortalEnabled = true;
        private bool m_currencyBuyEnabled = true;
        private bool m_currencyTransferEnabled = true;
        private bool m_payPalEnabled;
        private int m_currencyChallengeMinutes = 10;
        private int m_currencyChallengeCooldownSeconds = 20;
        private int m_currencySessionHours = 12;
        private int m_currencyStatementLimit = 30;
        private int m_currencyBuyLimit = 100000;
        private string m_currencyBuyMode = "grant";
        private string m_currencyPurchaseStoragePath = "Currency/regionweb-purchases.tsv";
        private string m_payPalEnvironment = "sandbox";
        private string m_payPalClientID = string.Empty;
        private string m_payPalClientSecret = string.Empty;
        private string m_payPalCurrencyCode = "EUR";
        private string m_payPalReturnBaseUrl = string.Empty;
        private string m_payPalOrderStoragePath = "Currency/regionweb-paypal-orders.tsv";
        private string m_absoluteCurrencyPurchaseStoragePath;
        private string m_absolutePayPalOrderStoragePath;
        private decimal m_payPalPricePerToken = 0.01m;

        private void HandleCurrencyCommand(string module, string[] cmd)
        {
            if (cmd == null || cmd.Length < 3)
            {
                MainConsole.Instance.Output("[REGION WEB]: Usage: regionweb currency pending|approve|deny");
                return;
            }

            string action = cmd[2].ToLowerInvariant();
            if (action == "pending")
            {
                List<CurrencyPurchaseRequest> pending;
                lock (m_currencyPurchaseLock)
                    pending = m_currencyPurchaseRequests.Values
                        .Where(r => (r.Status ?? string.Empty).Equals("pending", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(r => r.RequestedUTC)
                        .ToList();

                if (pending.Count == 0)
                {
                    MainConsole.Instance.Output("[REGION WEB]: No pending currency purchase requests.");
                    return;
                }

                foreach (CurrencyPurchaseRequest request in pending)
                {
                    MainConsole.Instance.Output(
                        "[REGION WEB]: {0}: {1} requested {2} tokens at {3}",
                        request.RequestID,
                        request.DisplayName,
                        request.Amount.ToString(CultureInfo.InvariantCulture),
                        request.RequestedUTC.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture));
                }
                return;
            }

            if (cmd.Length < 4)
            {
                MainConsole.Instance.Output("[REGION WEB]: Usage: regionweb currency {0} <request-id> [note]", action);
                return;
            }

            string requestID = cmd[3];
            string note = cmd.Length > 4 ? string.Join(" ", cmd.Skip(4).ToArray()) : string.Empty;
            if (action == "approve")
            {
                ApproveCurrencyPurchase(requestID, note, out string message);
                MainConsole.Instance.Output("[REGION WEB]: " + message);
                return;
            }

            if (action == "deny")
            {
                DenyCurrencyPurchase(requestID, note, out string message);
                MainConsole.Instance.Output("[REGION WEB]: " + message);
                return;
            }

            MainConsole.Instance.Output("[REGION WEB]: Usage: regionweb currency pending|approve|deny");
        }

        private void SendCurrencyPortal(string[] parts, IOSHttpRequest request, IOSHttpResponse response)
        {
            if (!m_currencyPortalEnabled)
            {
                SendNotFound(response, "Wallet is disabled.");
                return;
            }

            Dictionary<string, string> form = ReadForm(request);
            bool isPost = !string.IsNullOrEmpty(request.HttpMethod)
                && request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase);
            string action = isPost ? FormValue(form, "action") :
                (parts.Length >= 2 ? parts[1] : string.Empty);
            bool adminPath = parts.Length >= 2 && parts[1].Equals("admin", StringComparison.OrdinalIgnoreCase);

            CurrencyWebSession session = GetCurrencySession(request);

            if (!isPost && adminPath && parts.Length >= 3)
            {
                if (!IsCurrencyAdminSession(session))
                {
                    SendCurrencyAdminLogin(response, "Login before downloading admin exports.", FormValue(form, "avatar"));
                    return;
                }

                if (parts[2].Equals("requests.csv", StringComparison.OrdinalIgnoreCase))
                {
                    SendCurrencyAdminRequestsCsv(response);
                    return;
                }

                if (parts[2].Equals("balances.csv", StringComparison.OrdinalIgnoreCase))
                {
                    SendCurrencyAdminBalancesCsv(response);
                    return;
                }
            }

            if (!isPost && action.Equals("statement.csv", StringComparison.OrdinalIgnoreCase))
            {
                if (session == null)
                {
                    SendCurrencyLogin(response, "Login before downloading the statement.", FormValue(form, "avatar"));
                    return;
                }

                SendCurrencyStatementCsv(response, session);
                return;
            }

            if (!isPost && action.Equals("paypal-return", StringComparison.OrdinalIgnoreCase))
            {
                if (session == null)
                {
                    SendCurrencyLogin(response, "Login again, then reopen the PayPal return URL to finish the token purchase.", FormValue(form, "avatar"));
                    return;
                }

                HandleCurrencyPayPalReturn(session, form, response);
                return;
            }

            if (!isPost && action.Equals("paypal-cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (session == null)
                {
                    SendCurrencyLogin(response, "PayPal checkout was cancelled. Login again to return to the wallet.", FormValue(form, "avatar"));
                    return;
                }

                HandleCurrencyPayPalCancel(session, form, response);
                return;
            }

            if (action.Equals("logout", StringComparison.OrdinalIgnoreCase))
            {
                if (session != null && !ValidateCurrencyCsrf(session, form, out string csrfMessage))
                {
                    if (adminPath && IsCurrencyAdminSession(session))
                        SendCurrencyAdminDashboard(response, session, csrfMessage, "error");
                    else
                        SendCurrencyDashboard(response, session, csrfMessage, "error");
                    return;
                }

                string sessionToken = ReadCookie(request, CurrencySessionCookie);
                if (!string.IsNullOrEmpty(sessionToken))
                {
                    lock (m_currencyAuthLock)
                        m_currencySessions.Remove(sessionToken);
                }

                ClearCurrencySessionCookie(response);
                if (adminPath)
                    SendCurrencyAdminLogin(response, "You have been logged out.", string.Empty);
                else
                    SendCurrencyLogin(response, "You have been logged out.", string.Empty);
                return;
            }

            if (isPost && action.Equals("request-token", StringComparison.OrdinalIgnoreCase))
            {
                HandleCurrencyTokenRequest(form, response);
                return;
            }

            if (isPost && action.Equals("login", StringComparison.OrdinalIgnoreCase))
            {
                HandleCurrencyLogin(form, response);
                return;
            }

            if (isPost && action.Equals("admin-request-token", StringComparison.OrdinalIgnoreCase))
            {
                HandleCurrencyAdminTokenRequest(form, response);
                return;
            }

            if (isPost && action.Equals("admin-login", StringComparison.OrdinalIgnoreCase))
            {
                HandleCurrencyAdminLogin(form, response);
                return;
            }

            if (action.Equals("admin", StringComparison.OrdinalIgnoreCase)
                || action.StartsWith("admin-", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsCurrencyAdminSession(session))
                {
                    SendCurrencyAdminLogin(response, string.Empty, FormValue(form, "avatar"));
                    return;
                }

                if (isPost)
                {
                    string message;
                    string severity;
                    if (ValidateCurrencyCsrf(session, form, out message))
                        HandleCurrencyAdminAction(session, action, form, out message, out severity);
                    else
                        severity = "error";
                    SendCurrencyAdminDashboard(response, session, message, severity);
                    return;
                }

                SendCurrencyAdminDashboard(response, session, string.Empty, string.Empty);
                return;
            }

            if (session == null)
            {
                SendCurrencyLogin(response, string.Empty, FormValue(form, "avatar"));
                return;
            }

            if (isPost && action.Equals("buy", StringComparison.OrdinalIgnoreCase))
            {
                string message;
                string severity;
                if (ValidateCurrencyCsrf(session, form, out message))
                {
                    if (m_currencyBuyMode.Equals("paypal", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleCurrencyPayPalBuy(session, form, response);
                        return;
                    }

                    HandleCurrencyBuy(session, form, out message, out severity);
                }
                else
                    severity = "error";
                SendCurrencyDashboard(response, session, message, severity);
                return;
            }

            if (isPost && action.Equals("transfer", StringComparison.OrdinalIgnoreCase))
            {
                string message;
                string severity;
                if (ValidateCurrencyCsrf(session, form, out message))
                    HandleCurrencyTransfer(session, form, out message, out severity);
                else
                    severity = "error";
                SendCurrencyDashboard(response, session, message, severity);
                return;
            }

            SendCurrencyDashboard(response, session, string.Empty, string.Empty);
        }

        private void HandleCurrencyTokenRequest(Dictionary<string, string> form, IOSHttpResponse response)
        {
            string avatarName = FormValue(form, "avatar");
            if (!TryResolveAvatar(avatarName, out UUID agentID, out string displayName))
            {
                SendCurrencyLogin(response, "Avatar not found. Use the full avatar name, for example First Last.", avatarName);
                return;
            }

            if (!TryFindOnlineClient(agentID, out IClientAPI client))
            {
                SendCurrencyLogin(response, "Avatar resolved, but it must be online in one of these regions to receive the token inworld.", displayName);
                return;
            }

            string token = GenerateCurrencyChallengeToken();
            DateTime expires = DateTime.UtcNow.AddMinutes(m_currencyChallengeMinutes);
            lock (m_currencyAuthLock)
            {
                CleanupCurrencyAuthLocked();
                if (m_currencyChallengeCooldownSeconds > 0
                    && m_currencyLastChallengeUTCByAgent.TryGetValue(agentID, out DateTime lastChallengeUTC)
                    && (DateTime.UtcNow - lastChallengeUTC).TotalSeconds < m_currencyChallengeCooldownSeconds)
                {
                    SendCurrencyLogin(response, "Token already sent recently. Wait a few seconds before requesting another one.", displayName);
                    return;
                }

                m_currencyChallenges[token] = new CurrencyLoginChallenge
                {
                    AgentID = agentID,
                    DisplayName = displayName,
                    Token = token,
                    ExpiresUTC = expires
                };
                m_currencyLastChallengeUTCByAgent[agentID] = DateTime.UtcNow;
            }

            string message = "Wallet login token for " + displayName + ": " + token
                + " (expires in " + m_currencyChallengeMinutes.ToString(CultureInfo.InvariantCulture) + " minutes).";
            try
            {
                client.SendBlueBoxMessage(UUID.Zero, "Wallet", message);
            }
            catch
            {
                client.SendAgentAlertMessage(message, false);
            }

            SendCurrencyLogin(response, "Token sent inworld to " + displayName + ". Enter it below to open the wallet.", displayName);
        }

        private void HandleCurrencyLogin(Dictionary<string, string> form, IOSHttpResponse response)
        {
            string avatarName = FormValue(form, "avatar");
            string token = FormValue(form, "token").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(token))
            {
                SendCurrencyLogin(response, "Enter the token received inworld.", avatarName);
                return;
            }

            if (!TryResolveAvatar(avatarName, out UUID agentID, out string displayName))
            {
                SendCurrencyLogin(response, "Avatar not found. Request a new token using the exact avatar name.", avatarName);
                return;
            }

            CurrencyLoginChallenge challenge;
            lock (m_currencyAuthLock)
            {
                CleanupCurrencyAuthLocked();
                if (!m_currencyChallenges.TryGetValue(token, out challenge))
                {
                    SendCurrencyLogin(response, "Invalid or expired token. Request a new one inworld.", displayName);
                    return;
                }

                if (challenge.AgentID != agentID)
                {
                    SendCurrencyLogin(response, "That token belongs to a different avatar.", displayName);
                    return;
                }

                if (challenge.IsAdmin)
                {
                    SendCurrencyLogin(response, "That is an admin token. Use the money admin login page.", displayName);
                    return;
                }

                m_currencyChallenges.Remove(token);
            }

            string sessionToken = GenerateCurrencySessionToken();
            CurrencyWebSession session = new CurrencyWebSession
            {
                AgentID = agentID,
                DisplayName = challenge.DisplayName,
                CsrfToken = GenerateCurrencySessionToken(),
                ExpiresUTC = DateTime.UtcNow.AddHours(m_currencySessionHours)
            };

            lock (m_currencyAuthLock)
            {
                CleanupCurrencyAuthLocked();
                m_currencySessions[sessionToken] = session;
            }

            SetCurrencySessionCookie(response, sessionToken, session.ExpiresUTC);
            SendCurrencyDashboard(response, session, "Login successful.", "ok");
        }

        private void HandleCurrencyAdminTokenRequest(Dictionary<string, string> form, IOSHttpResponse response)
        {
            string avatarName = FormValue(form, "avatar");
            if (!TryResolveAvatar(avatarName, out UUID agentID, out string displayName))
            {
                SendCurrencyAdminLogin(response, "Avatar not found. Use the full avatar name, for example First Last.", avatarName);
                return;
            }

            if (!IsRegionWebSuperAdmin(agentID))
            {
                SendCurrencyAdminLogin(response, "Only an estate owner of a loaded region can access the wallet admin.", displayName);
                return;
            }

            if (!TryFindOnlineClient(agentID, out IClientAPI client))
            {
                SendCurrencyAdminLogin(response, "Admin avatar resolved, but it must be online in one of these regions to receive the token inworld.", displayName);
                return;
            }

            string token = GenerateCurrencyChallengeToken();
            DateTime expires = DateTime.UtcNow.AddMinutes(m_currencyChallengeMinutes);
            lock (m_currencyAuthLock)
            {
                CleanupCurrencyAuthLocked();
                if (m_currencyChallengeCooldownSeconds > 0
                    && m_currencyLastChallengeUTCByAgent.TryGetValue(agentID, out DateTime lastChallengeUTC)
                    && (DateTime.UtcNow - lastChallengeUTC).TotalSeconds < m_currencyChallengeCooldownSeconds)
                {
                    SendCurrencyAdminLogin(response, "Admin token already sent recently. Wait a few seconds before requesting another one.", displayName);
                    return;
                }

                m_currencyChallenges[token] = new CurrencyLoginChallenge
                {
                    AgentID = agentID,
                    DisplayName = displayName,
                    Token = token,
                    ExpiresUTC = expires,
                    IsAdmin = true
                };
                m_currencyLastChallengeUTCByAgent[agentID] = DateTime.UtcNow;
            }

            string message = "wallet admin token for " + displayName + ": " + token
                + " (expires in " + m_currencyChallengeMinutes.ToString(CultureInfo.InvariantCulture) + " minutes).";
            try
            {
                client.SendBlueBoxMessage(UUID.Zero, "Wallet", message);
            }
            catch
            {
                client.SendAgentAlertMessage(message, false);
            }

            SendCurrencyAdminLogin(response, "Admin token sent inworld to " + displayName + ". Enter it below to open money admin.", displayName);
        }

        private void HandleCurrencyAdminLogin(Dictionary<string, string> form, IOSHttpResponse response)
        {
            string avatarName = FormValue(form, "avatar");
            string token = FormValue(form, "token").Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(token))
            {
                SendCurrencyAdminLogin(response, "Enter the admin token received inworld.", avatarName);
                return;
            }

            if (!TryResolveAvatar(avatarName, out UUID agentID, out string displayName))
            {
                SendCurrencyAdminLogin(response, "Avatar not found. Request a new admin token using the exact avatar name.", avatarName);
                return;
            }

            if (!IsRegionWebSuperAdmin(agentID))
            {
                SendCurrencyAdminLogin(response, "Only an estate owner of a loaded region can access the wallet admin.", displayName);
                return;
            }

            CurrencyLoginChallenge challenge;
            lock (m_currencyAuthLock)
            {
                CleanupCurrencyAuthLocked();
                if (!m_currencyChallenges.TryGetValue(token, out challenge))
                {
                    SendCurrencyAdminLogin(response, "Invalid or expired admin token. Request a new one inworld.", displayName);
                    return;
                }

                if (challenge.AgentID != agentID)
                {
                    SendCurrencyAdminLogin(response, "That admin token belongs to a different avatar.", displayName);
                    return;
                }

                if (!challenge.IsAdmin)
                {
                    SendCurrencyAdminLogin(response, "That is a wallet token. Request an admin token from this page.", displayName);
                    return;
                }

                m_currencyChallenges.Remove(token);
            }

            string sessionToken = GenerateCurrencySessionToken();
            CurrencyWebSession session = new CurrencyWebSession
            {
                AgentID = agentID,
                DisplayName = challenge.DisplayName,
                CsrfToken = GenerateCurrencySessionToken(),
                ExpiresUTC = DateTime.UtcNow.AddHours(m_currencySessionHours),
                IsAdmin = true
            };

            lock (m_currencyAuthLock)
            {
                CleanupCurrencyAuthLocked();
                m_currencySessions[sessionToken] = session;
            }

            SetCurrencySessionCookie(response, sessionToken, session.ExpiresUTC);
            SendCurrencyAdminDashboard(response, session, "Admin login successful.", "ok");
        }

        private void HandleCurrencyAdminAction(CurrencyWebSession session, string action, Dictionary<string, string> form, out string message, out string severity)
        {
            severity = "error";
            if (!IsCurrencyAdminSession(session))
            {
                message = "Admin session expired.";
                return;
            }

            if (action.Equals("admin-approve", StringComparison.OrdinalIgnoreCase))
            {
                if (ApproveCurrencyPurchase(FormValue(form, "request"), FormValue(form, "note"), out message))
                    severity = "ok";
                return;
            }

            if (action.Equals("admin-deny", StringComparison.OrdinalIgnoreCase))
            {
                if (DenyCurrencyPurchase(FormValue(form, "request"), FormValue(form, "note"), out message))
                    severity = "ok";
                return;
            }

            IMoneyModule money = GetCurrencyMoneyModule();
            if (money == null)
            {
                message = "Currency module is not active.";
                return;
            }

            if (action.Equals("admin-set-balance", StringComparison.OrdinalIgnoreCase)
                || action.Equals("admin-credit", StringComparison.OrdinalIgnoreCase)
                || action.Equals("admin-debit", StringComparison.OrdinalIgnoreCase))
            {
                string avatar = FormValue(form, "avatar");
                if (!TryResolveAvatar(avatar, out UUID targetID, out string targetName))
                {
                    message = "Avatar not found. Use the full avatar name or UUID.";
                    return;
                }

                int amount;
                if (action.Equals("admin-set-balance", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseWholeAmount(FormValue(form, "amount"), out amount, out message))
                        return;
                }
                else if (!TryParsePositiveAmount(FormValue(form, "amount"), Int32.MaxValue, out amount, out message))
                {
                    return;
                }

                string note = FormValue(form, "note");
                bool result = false;
                string reason;
                if (action.Equals("admin-set-balance", StringComparison.OrdinalIgnoreCase))
                {
                    result = InvokeWebSetBalance(money, targetID, amount, note, out reason);
                    message = result ? "Set " + targetName + " balance to " + amount.ToString(CultureInfo.InvariantCulture) + "." : reason;
                }
                else if (action.Equals("admin-credit", StringComparison.OrdinalIgnoreCase))
                {
                    result = InvokeWebCreditCurrency(money, targetID, amount, note, out reason);
                    message = result ? "Credited " + amount.ToString(CultureInfo.InvariantCulture) + " tokens to " + targetName + "." : reason;
                }
                else
                {
                    result = InvokeWebDebitCurrency(money, targetID, amount, note, out reason);
                    message = result ? "Debited " + amount.ToString(CultureInfo.InvariantCulture) + " tokens from " + targetName + "." : reason;
                }

                if (result)
                    severity = "ok";
                else if (string.IsNullOrWhiteSpace(message))
                    message = "Money admin action failed.";
                return;
            }

            if (action.Equals("admin-transfer", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryResolveAvatar(FormValue(form, "from"), out UUID fromID, out string fromName)
                    || !TryResolveAvatar(FormValue(form, "to"), out UUID toID, out string toName))
                {
                    message = "Both source and destination avatars must resolve to known accounts.";
                    return;
                }

                if (!TryParsePositiveAmount(FormValue(form, "amount"), 0, out int amount, out message))
                    return;

                string note = FormValue(form, "note");
                string description = string.IsNullOrWhiteSpace(note) ? "Wallet admin transfer" : note;
                if (InvokeWebTransfer(money, fromID, toID, amount, description, out string reason))
                {
                    severity = "ok";
                    message = "Transferred " + amount.ToString(CultureInfo.InvariantCulture) + " tokens from " + fromName + " to " + toName + ".";
                }
                else
                {
                    message = string.IsNullOrWhiteSpace(reason) ? "Admin transfer failed." : reason;
                }
                return;
            }

            message = "Unknown admin action.";
        }

        private void HandleCurrencyBuy(CurrencyWebSession session, Dictionary<string, string> form, out string message, out string severity)
        {
            severity = "error";
            if (!IsCurrencyBuyAvailable())
            {
                message = "Token purchases are disabled on this wallet portal.";
                return;
            }

            if (!TryParsePositiveAmount(FormValue(form, "amount"), m_currencyBuyLimit, out int amount, out message))
                return;

            if (m_currencyBuyMode.Equals("paypal", StringComparison.OrdinalIgnoreCase))
            {
                message = "PayPal purchases must start from the wallet checkout button.";
                return;
            }

            if (m_currencyBuyMode.Equals("request", StringComparison.OrdinalIgnoreCase))
            {
                CurrencyPurchaseRequest request = CreateCurrencyPurchaseRequest(session, amount);
                severity = "ok";
                message = "Purchase request " + request.RequestID + " created for " + amount.ToString(CultureInfo.InvariantCulture)
                    + " tokens. Estate staff can approve it from the console.";
                NotifyCurrencyAvatar(session.AgentID, "Wallet purchase request " + request.RequestID
                    + " created for " + amount.ToString(CultureInfo.InvariantCulture) + " tokens.");
                return;
            }

            IMoneyModule money = GetCurrencyMoneyModule();
            if (money == null)
            {
                message = "Currency module is not active.";
                return;
            }

            if (InvokeWebBuyCurrency(money, session.AgentID, amount, out string reason))
            {
                severity = "ok";
                message = "Purchased " + amount.ToString(CultureInfo.InvariantCulture) + " tokens.";
            }
            else
            {
                message = string.IsNullOrWhiteSpace(reason) ? "Token purchase failed." : reason;
            }
        }

        private void HandleCurrencyPayPalBuy(CurrencyWebSession session, Dictionary<string, string> form, IOSHttpResponse response)
        {
            string message;
            if (!m_currencyBuyEnabled || m_currencyBuyMode.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                SendCurrencyDashboard(response, session, "Token purchases are disabled on this wallet portal.", "error");
                return;
            }

            if (!TryParsePositiveAmount(FormValue(form, "amount"), m_currencyBuyLimit, out int amount, out message))
            {
                SendCurrencyDashboard(response, session, message, "error");
                return;
            }

            if (!IsPayPalConfigured(out string configReason))
            {
                SendCurrencyDashboard(response, session, configReason, "error");
                return;
            }

            decimal fiatAmount = Decimal.Round(m_payPalPricePerToken * amount, 2, MidpointRounding.AwayFromZero);
            if (fiatAmount <= 0m)
            {
                SendCurrencyDashboard(response, session, "PayPalPricePerToken produces a zero checkout amount.", "error");
                return;
            }

            CurrencyPayPalOrder order = new CurrencyPayPalOrder
            {
                LocalID = GenerateCurrencyPayPalOrderID(),
                AgentID = session.AgentID,
                DisplayName = session.DisplayName,
                TokenAmount = amount,
                FiatAmount = fiatAmount,
                CurrencyCode = m_payPalCurrencyCode,
                Status = "creating",
                CreatedUTC = DateTime.UtcNow,
                UpdatedUTC = DateTime.UtcNow,
                Note = string.Empty
            };

            if (!CreatePayPalOrder(order, out string approvalUrl, out string reason))
            {
                order.Status = "failed";
                order.UpdatedUTC = DateTime.UtcNow;
                order.Note = reason;
                StoreCurrencyPayPalOrder(order);
                SendCurrencyDashboard(response, session, string.IsNullOrWhiteSpace(reason) ? "PayPal order creation failed." : reason, "error");
                return;
            }

            order.Status = "created";
            order.UpdatedUTC = DateTime.UtcNow;
            StoreCurrencyPayPalOrder(order);
            NotifyCurrencyAvatar(session.AgentID, "PayPal checkout " + order.LocalID + " created for "
                + amount.ToString(CultureInfo.InvariantCulture) + " tokens.");
            response.Redirect(approvalUrl, HttpStatusCode.Redirect);
        }

        private void HandleCurrencyPayPalReturn(CurrencyWebSession session, Dictionary<string, string> form, IOSHttpResponse response)
        {
            string orderID = FormValue(form, "token");
            string localID = FormValue(form, "local");
            CurrencyPayPalOrder order = FindCurrencyPayPalOrder(orderID, localID);
            if (order == null)
            {
                SendCurrencyDashboard(response, session, "PayPal order not found in local storage.", "error");
                return;
            }

            if (order.AgentID != session.AgentID)
            {
                SendCurrencyDashboard(response, session, "PayPal order belongs to a different avatar session.", "error");
                return;
            }

            if ((order.Status ?? string.Empty).Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                SendCurrencyDashboard(response, session, "PayPal order " + order.LocalID + " was already completed.", "ok");
                return;
            }

            if (string.IsNullOrWhiteSpace(order.PayPalOrderID))
            {
                SendCurrencyDashboard(response, session, "PayPal order has no remote order id.", "error");
                return;
            }

            bool alreadyCaptured = (order.Status ?? string.Empty).Equals("capture_pending_credit", StringComparison.OrdinalIgnoreCase);
            if (!alreadyCaptured)
            {
                MarkCurrencyPayPalOrder(order.LocalID, "capturing", "Capture requested from PayPal return.");
                if (!CapturePayPalOrder(order.PayPalOrderID, out string captureReason))
                {
                    MarkCurrencyPayPalOrder(order.LocalID, "capture_failed", captureReason);
                    SendCurrencyDashboard(response, session, string.IsNullOrWhiteSpace(captureReason) ? "PayPal capture failed." : captureReason, "error");
                    return;
                }
            }

            IMoneyModule money = GetCurrencyMoneyModule();
            if (money == null)
            {
                MarkCurrencyPayPalOrder(order.LocalID, "capture_pending_credit", "PayPal captured, but currency module is not active.");
                SendCurrencyDashboard(response, session, "PayPal payment captured, but the currency module is not active. Admin can credit from the order log.", "error");
                return;
            }

            if (!InvokeWebBuyCurrency(money, order.AgentID, order.TokenAmount, out string creditReason))
            {
                string failure = string.IsNullOrWhiteSpace(creditReason) ? "Currency credit failed after PayPal capture." : creditReason;
                MarkCurrencyPayPalOrder(order.LocalID, "capture_pending_credit", failure);
                SendCurrencyDashboard(response, session, failure, "error");
                return;
            }

            MarkCurrencyPayPalOrder(order.LocalID, "completed", "PayPal captured and tokens credited.");
            NotifyCurrencyAvatar(session.AgentID, "PayPal checkout " + order.LocalID + " completed: "
                + order.TokenAmount.ToString(CultureInfo.InvariantCulture) + " tokens credited.");
            SendCurrencyDashboard(response, session, "PayPal payment captured. Credited "
                + order.TokenAmount.ToString(CultureInfo.InvariantCulture) + " tokens.", "ok");
        }

        private void HandleCurrencyPayPalCancel(CurrencyWebSession session, Dictionary<string, string> form, IOSHttpResponse response)
        {
            string orderID = FormValue(form, "token");
            string localID = FormValue(form, "local");
            CurrencyPayPalOrder order = FindCurrencyPayPalOrder(orderID, localID);
            if (order != null && order.AgentID == session.AgentID
                && !(order.Status ?? string.Empty).Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                MarkCurrencyPayPalOrder(order.LocalID, "cancelled", "User cancelled PayPal approval.");
            }

            SendCurrencyDashboard(response, session, "PayPal checkout cancelled.", "ok");
        }

        private void HandleCurrencyTransfer(CurrencyWebSession session, Dictionary<string, string> form, out string message, out string severity)
        {
            severity = "error";
            if (!m_currencyTransferEnabled)
            {
                message = "Wallet transfers are disabled on this portal.";
                return;
            }

            if (!TryParsePositiveAmount(FormValue(form, "amount"), 0, out int amount, out message))
                return;

            string recipient = FormValue(form, "recipient");
            if (!TryResolveAvatar(recipient, out UUID recipientID, out string recipientName))
            {
                message = "Recipient avatar not found. Use the full avatar name.";
                return;
            }

            string description = FormValue(form, "description");
            if (description.Length > 160)
                description = description.Substring(0, 160);

            IMoneyModule money = GetCurrencyMoneyModule();
            if (money == null)
            {
                message = "Currency module is not active.";
                return;
            }

            if (InvokeWebTransfer(money, session.AgentID, recipientID, amount, description, out string reason))
            {
                severity = "ok";
                message = "Transferred " + amount.ToString(CultureInfo.InvariantCulture) + " tokens to " + recipientName + ".";
            }
            else
            {
                message = string.IsNullOrWhiteSpace(reason) ? "Transfer failed." : reason;
            }
        }

        private void AppendCurrencyGuideCallout(StringBuilder html)
        {
            html.Append("<section class=\"wallet-guide\"><div><span>Currency guide</span><h2>")
                .Append(Html(CurrencyFeatureTitle)).Append("</h2><p>")
                .Append(Html(CurrencyFeatureBody)).Append("</p></div><a href=\"")
                .Append(Html(m_basePath)).Append("/feature/")
                .Append(Url(MakeSlug(CurrencyFeatureTitle)))
                .Append("/\">Read guide</a></section>");
        }

        private void AppendMoneyAdminCallout(StringBuilder html)
        {
            html.Append("<section class=\"wallet-guide wallet-admin-callout\"><div><span>Estate owner tools</span><h2>Money Admin</h2>")
                .Append("<p>Manage pending token requests, avatar balances, exports and local currency operations for the loaded estate.</p></div><a href=\"")
                .Append(Html(m_basePath)).Append("/admin\">Open money admin</a></section>");
        }

        private void SendCurrencyLogin(IOSHttpResponse response, string message, string avatarName)
        {
            StringBuilder html = BeginPage("Avatar Wallet - " + m_defaultEstateTitle);
            html.Append("<main class=\"wrap wallet-page\">");
            AppendPageLinks(html,
                "Estate", m_basePath + "/");
            html.Append("<p class=\"feature-kicker\">Reserved area</p><h1>Avatar Wallet</h1>")
                .Append("<p class=\"lead\">Request a one-time token inworld, then use it here to view your balance, statement, token purchases and avatar transfers.</p>");
            AppendCurrencyGuideCallout(html);

            AppendCurrencyMessage(html, message, string.IsNullOrEmpty(message) || message.StartsWith("Token sent", StringComparison.Ordinal) || message.StartsWith("You have", StringComparison.Ordinal) ? "ok" : "error");

            html.Append("<section class=\"wallet-grid\"><article class=\"wallet-card\"><h2>1. Request inworld token</h2>")
                .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/\">")
                .Append("<input type=\"hidden\" name=\"action\" value=\"request-token\">")
                .Append("<label>Avatar name<input name=\"avatar\" value=\"").Append(Html(avatarName)).Append("\" required placeholder=\"First Last\"></label>")
                .Append("<button type=\"submit\">Send token inworld</button></form>")
                .Append("<p class=\"wallet-note\">The avatar must be online in one of the loaded regions so the wallet module can deliver the token through the viewer.</p></article>");

            html.Append("<article class=\"wallet-card\"><h2>2. Login</h2>")
                .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/\">")
                .Append("<input type=\"hidden\" name=\"action\" value=\"login\">")
                .Append("<label>Avatar name<input name=\"avatar\" value=\"").Append(Html(avatarName)).Append("\" required placeholder=\"First Last\"></label>")
                .Append("<label>Token<input name=\"token\" required autocomplete=\"one-time-code\" placeholder=\"8-character token\"></label>")
                .Append("<button type=\"submit\">Open wallet</button></form></article></section></main>")
                .Append(EndPage());

            SendHtml(response, html.ToString());
        }

        private void SendCurrencyAdminLogin(IOSHttpResponse response, string message, string avatarName)
        {
            StringBuilder html = BeginPage("Money Admin - " + m_defaultEstateTitle);
            html.Append("<main class=\"wrap wallet-page\">");
            AppendPageLinks(html,
                "Avatar wallet", m_basePath + "/",
                "Estate", m_basePath + "/");
            html.Append("<p class=\"feature-kicker\">Superadmin area</p><h1>Money Admin</h1>")
                .Append("<p class=\"lead\">Estate owners request a one-time inworld token before managing wallet requests and avatar balances.</p>");

            AppendCurrencyMessage(html, message, string.IsNullOrEmpty(message) || message.StartsWith("Admin token sent", StringComparison.Ordinal) ? "ok" : "error");

            html.Append("<section class=\"wallet-grid\"><article class=\"wallet-card\"><h2>1. Request admin token</h2>")
                .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/admin\">")
                .Append("<input type=\"hidden\" name=\"action\" value=\"admin-request-token\">")
                .Append("<label>Estate owner avatar<input name=\"avatar\" value=\"").Append(Html(avatarName)).Append("\" required placeholder=\"First Last\"></label>")
                .Append("<button type=\"submit\">Send admin token inworld</button></form>")
                .Append("<p class=\"wallet-note\">The avatar must be the estate owner of at least one loaded region and must be online.</p></article>");

            html.Append("<article class=\"wallet-card\"><h2>2. Login</h2>")
                .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/admin\">")
                .Append("<input type=\"hidden\" name=\"action\" value=\"admin-login\">")
                .Append("<label>Estate owner avatar<input name=\"avatar\" value=\"").Append(Html(avatarName)).Append("\" required placeholder=\"First Last\"></label>")
                .Append("<label>Admin token<input name=\"token\" required autocomplete=\"one-time-code\" placeholder=\"8-character token\"></label>")
                .Append("<button type=\"submit\">Open money admin</button></form></article></section></main>")
                .Append(EndPage());

            SendHtml(response, html.ToString());
        }

        private void SendCurrencyDashboard(IOSHttpResponse response, CurrencyWebSession session, string message, string severity)
        {
            IMoneyModule money = GetCurrencyMoneyModule();
            int balance = 0;
            bool hasBalance = false;
            if (money != null)
            {
                try
                {
                    balance = money.GetBalance(session.AgentID);
                    hasBalance = true;
                }
                catch
                {
                    hasBalance = false;
                }
            }

            StringBuilder html = BeginPage("Avatar Wallet - " + m_defaultEstateTitle);
            html.Append("<main class=\"wrap wallet-page\">");
            AppendPageLinks(html,
                "Estate", m_basePath + "/");
            html.Append("<p class=\"feature-kicker\">Reserved area</p><h1>Avatar Wallet</h1>");
            AppendCurrencyGuideCallout(html);

            AppendCurrencyMessage(html, message, severity);

            html.Append("<section class=\"wallet-summary\"><div><span>Avatar</span><strong>")
                .Append(Html(session.DisplayName)).Append("</strong></div><div><span>Balance</span><strong>")
                .Append(hasBalance ? balance.ToString(CultureInfo.InvariantCulture) : "Unavailable")
                .Append("</strong></div><div><span>Session expires</span><strong>")
                .Append(Html(session.ExpiresUTC.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture)))
                .Append("</strong></div></section>");

            if (IsRegionWebSuperAdmin(session.AgentID))
                AppendMoneyAdminCallout(html);

            if (money == null)
            {
                html.Append("<p class=\"wallet-message error\">Currency module is not active. Enable BetaGridLikeMoneyModule in [Economy].</p>");
            }
            else
            {
                html.Append("<section class=\"wallet-grid\"><article class=\"wallet-card\"><h2>Buy tokens</h2>");
                if (IsCurrencyBuyAvailable())
                {
                    html.Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/\">")
                        .Append("<input type=\"hidden\" name=\"action\" value=\"buy\">")
                        .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                        .Append("<label>Amount<input name=\"amount\" type=\"number\" min=\"1\" max=\"")
                        .Append(m_currencyBuyLimit.ToString(CultureInfo.InvariantCulture)).Append("\" required></label>")
                        .Append("<button type=\"submit\">")
                        .Append(m_currencyBuyMode.Equals("request", StringComparison.OrdinalIgnoreCase) ? "Request tokens"
                            : (m_currencyBuyMode.Equals("paypal", StringComparison.OrdinalIgnoreCase) ? "Pay with PayPal" : "Buy tokens"))
                        .Append("</button></form>");
                    if (m_currencyBuyMode.Equals("request", StringComparison.OrdinalIgnoreCase))
                        html.Append("<p class=\"wallet-note\">This creates a pending purchase request for estate staff approval from the console.</p>");
                    else if (m_currencyBuyMode.Equals("paypal", StringComparison.OrdinalIgnoreCase))
                        html.Append("<p class=\"wallet-note\">Checkout uses PayPal, then credits the local simulator ledger after payment capture. Price: ")
                            .Append(Html(m_payPalPricePerToken.ToString("0.00##", CultureInfo.InvariantCulture))).Append(" ")
                            .Append(Html(m_payPalCurrencyCode)).Append(" per token.</p>");
                    else
                        html.Append("<p class=\"wallet-note\">This credits the local simulator ledger and updates the viewer-visible balance.</p>");
                }
                else
                {
                    if (m_currencyBuyMode.Equals("paypal", StringComparison.OrdinalIgnoreCase) && !IsPayPalConfigured(out string payPalReason))
                        html.Append("<p class=\"wallet-note\">").Append(Html(payPalReason)).Append("</p>");
                    else
                        html.Append("<p class=\"wallet-note\">Token purchases are disabled on this portal.</p>");
                }
                html.Append("</article>");

                html.Append("<article class=\"wallet-card\"><h2>Transfer</h2>");
                if (m_currencyTransferEnabled)
                {
                    html.Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/\">")
                        .Append("<input type=\"hidden\" name=\"action\" value=\"transfer\">")
                        .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                        .Append("<label>Recipient avatar<input name=\"recipient\" required placeholder=\"First Last\"></label>")
                        .Append("<label>Amount<input name=\"amount\" type=\"number\" min=\"1\" required></label>")
                        .Append("<label>Description<input name=\"description\" maxlength=\"160\" placeholder=\"Optional note\"></label>")
                        .Append("<button type=\"submit\">Transfer tokens</button></form>");
                }
                else
                {
                    html.Append("<p class=\"wallet-note\">Avatar-to-avatar wallet transfers are disabled on this portal.</p>");
                }
                html.Append("</article></section>");

                AppendCurrencyStatement(html, money, session.AgentID);
                AppendCurrencyPurchaseRequests(html, session.AgentID);
            }

            html.Append("<form class=\"wallet-logout\" method=\"post\" action=\"").Append(Html(m_basePath)).Append("/\">")
                .Append("<input type=\"hidden\" name=\"action\" value=\"logout\">")
                .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                .Append("<button type=\"submit\">Logout</button></form>")
                .Append("</main>").Append(EndPage());
            SendHtml(response, html.ToString());
        }

        private void SendCurrencyAdminDashboard(IOSHttpResponse response, CurrencyWebSession session, string message, string severity)
        {
            IMoneyModule money = GetCurrencyMoneyModule();
            StringBuilder html = BeginPage("Money Admin - " + m_defaultEstateTitle);
            html.Append("<main class=\"wrap wallet-page\">");
            AppendPageLinks(html,
                "Avatar wallet", m_basePath + "/",
                "Estate", m_basePath + "/");
            html.Append("<p class=\"feature-kicker\">Superadmin area</p><h1>Money Admin</h1>");

            AppendCurrencyMessage(html, message, severity);

            html.Append("<section class=\"wallet-summary\"><div><span>Admin</span><strong>")
                .Append(Html(session.DisplayName)).Append("</strong></div><div><span>Role</span><strong>Estate owner</strong></div><div><span>Session expires</span><strong>")
                .Append(Html(session.ExpiresUTC.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture)))
                .Append("</strong></div></section>");

            if (money == null)
            {
                html.Append("<p class=\"wallet-message error\">Currency module is not active. Enable BetaGridLikeMoneyModule in [Economy].</p>");
            }
            else
            {
                AppendCurrencyAdminRequests(html, session);
                AppendCurrencyAdminPayPalOrders(html);

                html.Append("<section class=\"wallet-grid\"><article class=\"wallet-card\"><h2>Set balance</h2>")
                    .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/admin\">")
                    .Append("<input type=\"hidden\" name=\"action\" value=\"admin-set-balance\">")
                    .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                    .Append("<label>Avatar<input name=\"avatar\" required placeholder=\"First Last or UUID\"></label>")
                    .Append("<label>Balance<input name=\"amount\" type=\"number\" min=\"0\" required></label>")
                    .Append("<label>Note<input name=\"note\" maxlength=\"160\" placeholder=\"Optional audit note\"></label>")
                    .Append("<button type=\"submit\">Set balance</button></form></article>");

                html.Append("<article class=\"wallet-card\"><h2>Credit / debit</h2>")
                    .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/admin\">")
                    .Append("<input type=\"hidden\" name=\"action\" value=\"admin-credit\">")
                    .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                    .Append("<label>Avatar<input name=\"avatar\" required placeholder=\"First Last or UUID\"></label>")
                    .Append("<label>Amount<input name=\"amount\" type=\"number\" min=\"1\" required></label>")
                    .Append("<label>Note<input name=\"note\" maxlength=\"160\" placeholder=\"Optional audit note\"></label>")
                    .Append("<button type=\"submit\">Credit</button></form>")
                    .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/admin\">")
                    .Append("<input type=\"hidden\" name=\"action\" value=\"admin-debit\">")
                    .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                    .Append("<label>Avatar<input name=\"avatar\" required placeholder=\"First Last or UUID\"></label>")
                    .Append("<label>Amount<input name=\"amount\" type=\"number\" min=\"1\" required></label>")
                    .Append("<label>Note<input name=\"note\" maxlength=\"160\" placeholder=\"Optional audit note\"></label>")
                    .Append("<button type=\"submit\">Debit</button></form></article>");

                html.Append("<article class=\"wallet-card\"><h2>Transfer</h2>")
                    .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/admin\">")
                    .Append("<input type=\"hidden\" name=\"action\" value=\"admin-transfer\">")
                    .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                    .Append("<label>From<input name=\"from\" required placeholder=\"First Last or UUID\"></label>")
                    .Append("<label>To<input name=\"to\" required placeholder=\"First Last or UUID\"></label>")
                    .Append("<label>Amount<input name=\"amount\" type=\"number\" min=\"1\" required></label>")
                    .Append("<label>Note<input name=\"note\" maxlength=\"160\" placeholder=\"Optional audit note\"></label>")
                    .Append("<button type=\"submit\">Transfer</button></form></article></section>");

                AppendCurrencyAdminBalances(html, money);
            }

            html.Append("<form class=\"wallet-logout\" method=\"post\" action=\"").Append(Html(m_basePath)).Append("/admin\">")
                .Append("<input type=\"hidden\" name=\"action\" value=\"logout\">")
                .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                .Append("<button type=\"submit\">Logout</button></form>")
                .Append("</main>").Append(EndPage());
            SendHtml(response, html.ToString());
        }

        private void AppendCurrencyStatement(StringBuilder html, IMoneyModule money, UUID agentID)
        {
            List<Dictionary<string, string>> rows = GetCurrencyStatement(money, agentID);
            html.Append("<section class=\"wallet-card wallet-statement\"><h2>Statement</h2>");
            if (rows.Count == 0)
            {
                html.Append("<p class=\"wallet-note\">No ledger entries yet.</p></section>");
                return;
            }

            html.Append("<p class=\"wallet-note\"><a href=\"").Append(Html(m_basePath))
                .Append("/statement.csv\">Download CSV statement</a></p>");
            html.Append("<div class=\"wallet-table\"><table><thead><tr><th>Date</th><th>Type</th><th>Amount</th><th>Balance</th><th>Description</th></tr></thead><tbody>");
            string agentText = agentID.ToString();
            foreach (Dictionary<string, string> row in rows)
            {
                string amount = RowValue(row, "amount");
                string source = RowValue(row, "source");
                string destination = RowValue(row, "destination");
                bool credit = destination.Equals(agentText, StringComparison.OrdinalIgnoreCase)
                    && !source.Equals(agentText, StringComparison.OrdinalIgnoreCase);
                bool debit = source.Equals(agentText, StringComparison.OrdinalIgnoreCase)
                    && !destination.Equals(agentText, StringComparison.OrdinalIgnoreCase);
                string signedAmount = (credit ? "+" : (debit ? "-" : string.Empty)) + amount;

                html.Append("<tr><td>").Append(Html(FormatUtc(RowValue(row, "utc")))).Append("</td><td>")
                    .Append(Html(RowValue(row, "action"))).Append("</td><td class=\"")
                    .Append(credit ? "credit" : (debit ? "debit" : string.Empty)).Append("\">")
                    .Append(Html(signedAmount)).Append("</td><td>")
                    .Append(Html(RowValue(row, "balance"))).Append("</td><td>")
                    .Append(Html(RowValue(row, "description"))).Append("</td></tr>");
            }

            html.Append("</tbody></table></div></section>");
        }

        private void AppendCurrencyPurchaseRequests(StringBuilder html, UUID agentID)
        {
            List<CurrencyPurchaseRequest> requests;
            lock (m_currencyPurchaseLock)
            {
                requests = m_currencyPurchaseRequests.Values
                    .Where(r => r.AgentID == agentID)
                    .OrderByDescending(r => r.RequestedUTC)
                    .Take(12)
                    .ToList();
            }

            if (requests.Count == 0)
                return;

            html.Append("<section class=\"wallet-card wallet-statement\"><h2>Purchase requests</h2>")
                .Append("<div class=\"wallet-table\"><table><thead><tr><th>Date</th><th>ID</th><th>Amount</th><th>Status</th><th>Note</th></tr></thead><tbody>");

            foreach (CurrencyPurchaseRequest request in requests)
            {
                html.Append("<tr><td>").Append(Html(request.RequestedUTC.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture))).Append("</td><td>")
                    .Append(Html(request.RequestID)).Append("</td><td>")
                    .Append(request.Amount.ToString(CultureInfo.InvariantCulture)).Append("</td><td>")
                    .Append(Html(request.Status)).Append("</td><td>")
                    .Append(Html(request.Note)).Append("</td></tr>");
            }

            html.Append("</tbody></table></div></section>");
        }

        private void AppendCurrencyAdminRequests(StringBuilder html, CurrencyWebSession session)
        {
            List<CurrencyPurchaseRequest> pending;
            lock (m_currencyPurchaseLock)
                pending = m_currencyPurchaseRequests.Values
                    .Where(r => (r.Status ?? string.Empty).Equals("pending", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.RequestedUTC)
                    .ToList();

            html.Append("<section class=\"wallet-card wallet-statement\"><h2>Pending purchase requests</h2>");
            html.Append("<p class=\"wallet-note\"><a href=\"").Append(Html(m_basePath))
                .Append("/admin/requests.csv\">Download requests CSV</a></p>");
            if (pending.Count == 0)
            {
                html.Append("<p class=\"wallet-note\">No pending token purchase requests.</p></section>");
                return;
            }

            html.Append("<div class=\"wallet-table\"><table><thead><tr><th>Date</th><th>ID</th><th>Avatar</th><th>Amount</th><th>Action</th></tr></thead><tbody>");
            foreach (CurrencyPurchaseRequest request in pending)
            {
                html.Append("<tr><td>").Append(Html(request.RequestedUTC.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture))).Append("</td><td>")
                    .Append(Html(request.RequestID)).Append("</td><td>")
                    .Append(Html(request.DisplayName)).Append("</td><td>")
                    .Append(request.Amount.ToString(CultureInfo.InvariantCulture)).Append("</td><td>")
                    .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/admin\">")
                    .Append("<input type=\"hidden\" name=\"action\" value=\"admin-approve\">")
                    .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                    .Append("<input type=\"hidden\" name=\"request\" value=\"").Append(Html(request.RequestID)).Append("\">")
                    .Append("<input name=\"note\" maxlength=\"160\" placeholder=\"Note\">")
                    .Append("<button type=\"submit\">Approve</button></form>")
                    .Append("<form method=\"post\" action=\"").Append(Html(m_basePath)).Append("/admin\">")
                    .Append("<input type=\"hidden\" name=\"action\" value=\"admin-deny\">")
                    .Append("<input type=\"hidden\" name=\"csrf\" value=\"").Append(Html(session.CsrfToken)).Append("\">")
                    .Append("<input type=\"hidden\" name=\"request\" value=\"").Append(Html(request.RequestID)).Append("\">")
                    .Append("<input name=\"note\" maxlength=\"160\" placeholder=\"Reason\">")
                    .Append("<button type=\"submit\">Deny</button></form></td></tr>");
            }

            html.Append("</tbody></table></div></section>");
        }

        private void AppendCurrencyAdminPayPalOrders(StringBuilder html)
        {
            List<CurrencyPayPalOrder> orders;
            lock (m_currencyPayPalLock)
                orders = m_currencyPayPalOrders.Values
                    .OrderByDescending(r => r.CreatedUTC)
                    .Take(20)
                    .ToList();

            if (orders.Count == 0)
                return;

            html.Append("<section class=\"wallet-card wallet-statement\"><h2>Recent PayPal checkouts</h2>")
                .Append("<div class=\"wallet-table\"><table><thead><tr><th>Date</th><th>ID</th><th>Avatar</th><th>Tokens</th><th>Payment</th><th>Status</th><th>Note</th></tr></thead><tbody>");
            foreach (CurrencyPayPalOrder order in orders)
            {
                html.Append("<tr><td>")
                    .Append(Html(order.CreatedUTC.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture))).Append("</td><td>")
                    .Append(Html(order.LocalID)).Append("<br><span>").Append(Html(order.PayPalOrderID)).Append("</span></td><td>")
                    .Append(Html(order.DisplayName)).Append("</td><td>")
                    .Append(order.TokenAmount.ToString(CultureInfo.InvariantCulture)).Append("</td><td>")
                    .Append(Html(order.FiatAmount.ToString("0.00", CultureInfo.InvariantCulture))).Append(' ')
                    .Append(Html(order.CurrencyCode)).Append("</td><td>")
                    .Append(Html(order.Status)).Append("</td><td>")
                    .Append(Html(order.Note)).Append("</td></tr>");
            }

            html.Append("</tbody></table></div></section>");
        }

        private void AppendCurrencyAdminBalances(StringBuilder html, IMoneyModule money)
        {
            List<Dictionary<string, string>> rows = GetCurrencyBalances(money, 50);
            html.Append("<section class=\"wallet-card wallet-statement\"><h2>Top balances</h2>");
            html.Append("<p class=\"wallet-note\"><a href=\"").Append(Html(m_basePath))
                .Append("/admin/balances.csv\">Download balances CSV</a></p>");
            if (rows.Count == 0)
            {
                html.Append("<p class=\"wallet-note\">No balance rows yet.</p></section>");
                return;
            }

            html.Append("<div class=\"wallet-table\"><table><thead><tr><th>Avatar</th><th>UUID</th><th>Balance</th></tr></thead><tbody>");
            foreach (Dictionary<string, string> row in rows)
            {
                string agentText = RowValue(row, "agent_id");
                string displayName = agentText;
                if (UUID.TryParse(agentText, out UUID agentID))
                {
                    string resolved = LookupAvatarName(agentID);
                    if (!string.IsNullOrWhiteSpace(resolved))
                        displayName = resolved;
                }

                html.Append("<tr><td>").Append(Html(displayName)).Append("</td><td>")
                    .Append(Html(agentText)).Append("</td><td>")
                    .Append(Html(RowValue(row, "balance"))).Append("</td></tr>");
            }

            html.Append("</tbody></table></div></section>");
        }

        private void SendCurrencyStatementCsv(IOSHttpResponse response, CurrencyWebSession session)
        {
            IMoneyModule money = GetCurrencyMoneyModule();
            if (money == null)
            {
                response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                response.ContentType = "text/plain";
                response.RawBuffer = Encoding.UTF8.GetBytes("Currency module is not active.");
                return;
            }

            List<Dictionary<string, string>> rows = GetCurrencyStatement(money, session.AgentID);
            StringBuilder csv = new StringBuilder();
            csv.Append("utc,local_time,action,direction,amount,balance,description,source,destination,success\n");
            string agentText = session.AgentID.ToString();
            foreach (Dictionary<string, string> row in rows)
            {
                string source = RowValue(row, "source");
                string destination = RowValue(row, "destination");
                string direction = GetCurrencyDirection(agentText, source, destination);
                csv.Append(Csv(RowValue(row, "utc"))).Append(',')
                    .Append(Csv(FormatUtc(RowValue(row, "utc")))).Append(',')
                    .Append(Csv(RowValue(row, "action"))).Append(',')
                    .Append(Csv(direction)).Append(',')
                    .Append(Csv(RowValue(row, "amount"))).Append(',')
                    .Append(Csv(RowValue(row, "balance"))).Append(',')
                    .Append(Csv(RowValue(row, "description"))).Append(',')
                    .Append(Csv(source)).Append(',')
                    .Append(Csv(destination)).Append(',')
                    .Append(Csv(RowValue(row, "success"))).Append('\n');
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/csv";
            response.AddHeader("Content-Disposition", "attachment; filename=\"currency-statement-" + MakeSlug(session.DisplayName) + ".csv\"");
            response.RawBuffer = Encoding.UTF8.GetBytes(csv.ToString());
        }

        private void SendCurrencyAdminRequestsCsv(IOSHttpResponse response)
        {
            List<CurrencyPurchaseRequest> requests;
            lock (m_currencyPurchaseLock)
                requests = m_currencyPurchaseRequests.Values
                    .OrderByDescending(r => r.RequestedUTC)
                    .ToList();

            StringBuilder csv = new StringBuilder();
            csv.Append("request_id,requested_utc,local_time,agent_id,display_name,amount,status,updated_utc,operator,note\n");
            foreach (CurrencyPurchaseRequest request in requests)
            {
                csv.Append(Csv(request.RequestID)).Append(',')
                    .Append(Csv(request.RequestedUTC.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))).Append(',')
                    .Append(Csv(request.RequestedUTC.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture))).Append(',')
                    .Append(Csv(request.AgentID.ToString())).Append(',')
                    .Append(Csv(request.DisplayName)).Append(',')
                    .Append(request.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(Csv(request.Status)).Append(',')
                    .Append(Csv(request.UpdatedUTC.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))).Append(',')
                    .Append(Csv(request.OperatorName)).Append(',')
                    .Append(Csv(request.Note)).Append('\n');
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/csv";
            response.AddHeader("Content-Disposition", "attachment; filename=\"currency-admin-requests.csv\"");
            response.RawBuffer = Encoding.UTF8.GetBytes(csv.ToString());
        }

        private void SendCurrencyAdminBalancesCsv(IOSHttpResponse response)
        {
            IMoneyModule money = GetCurrencyMoneyModule();
            if (money == null)
            {
                response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                response.ContentType = "text/plain";
                response.RawBuffer = Encoding.UTF8.GetBytes("Currency module is not active.");
                return;
            }

            List<Dictionary<string, string>> rows = GetCurrencyBalances(money, 10000);
            StringBuilder csv = new StringBuilder();
            csv.Append("agent_id,display_name,balance\n");
            foreach (Dictionary<string, string> row in rows)
            {
                string agentText = RowValue(row, "agent_id");
                string displayName = agentText;
                if (UUID.TryParse(agentText, out UUID agentID))
                {
                    string resolved = LookupAvatarName(agentID);
                    if (!string.IsNullOrWhiteSpace(resolved))
                        displayName = resolved;
                }

                csv.Append(Csv(agentText)).Append(',')
                    .Append(Csv(displayName)).Append(',')
                    .Append(Csv(RowValue(row, "balance"))).Append('\n');
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/csv";
            response.AddHeader("Content-Disposition", "attachment; filename=\"currency-admin-balances.csv\"");
            response.RawBuffer = Encoding.UTF8.GetBytes(csv.ToString());
        }

        private void AppendCurrencyMessage(StringBuilder html, string message, string severity)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            string css = string.IsNullOrWhiteSpace(severity) ? "ok" : severity;
            html.Append("<p class=\"wallet-message ").Append(Html(css)).Append("\">")
                .Append(Html(message)).Append("</p>");
        }

        private static bool ValidateCurrencyCsrf(CurrencyWebSession session, Dictionary<string, string> form, out string message)
        {
            message = string.Empty;
            string token = FormValue(form, "csrf");
            if (session == null || string.IsNullOrEmpty(session.CsrfToken)
                || !session.CsrfToken.Equals(token, StringComparison.Ordinal))
            {
                message = "Security token expired. Reload the wallet page and try again.";
                return false;
            }

            return true;
        }

        private IMoneyModule GetCurrencyMoneyModule()
        {
            foreach (Scene scene in GetSceneSnapshot())
            {
                IMoneyModule money = scene.RequestModuleInterface<IMoneyModule>();
                if (money != null)
                    return money;
            }

            return null;
        }

        private bool InvokeWebBuyCurrency(IMoneyModule money, UUID agentID, int amount, out string reason)
        {
            reason = string.Empty;
            MethodInfo method = money.GetType().GetMethod("WebBuyCurrency", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                reason = "Currency module does not support purchases.";
                return false;
            }

            object[] args = new object[] { agentID, amount, reason };
            try
            {
                bool result = method.Invoke(money, args) is bool b && b;
                reason = args[2] as string ?? string.Empty;
                return result;
            }
            catch (Exception e)
            {
                reason = e.InnerException != null ? e.InnerException.Message : e.Message;
                return false;
            }
        }

        private bool InvokeWebTransfer(IMoneyModule money, UUID fromUser, UUID toUser, int amount, string description, out string reason)
        {
            reason = string.Empty;
            MethodInfo method = money.GetType().GetMethod("WebTransfer", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                method = money.GetType().GetMethod("WebTransferCurrency", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                reason = "Currency module does not support transfers.";
                return false;
            }

            object[] args = new object[] { fromUser, toUser, amount, description, reason };
            try
            {
                bool result = method.Invoke(money, args) is bool b && b;
                reason = args[4] as string ?? string.Empty;
                return result;
            }
            catch (Exception e)
            {
                reason = e.InnerException != null ? e.InnerException.Message : e.Message;
                return false;
            }
        }

        private bool InvokeWebSetBalance(IMoneyModule money, UUID agentID, int amount, string description, out string reason)
        {
            return InvokeMoneyAdminMethod(money, "WebSetBalance", agentID, amount, description, out reason);
        }

        private bool InvokeWebCreditCurrency(IMoneyModule money, UUID agentID, int amount, string description, out string reason)
        {
            return InvokeMoneyAdminMethod(money, "WebCreditCurrency", agentID, amount, description, out reason);
        }

        private bool InvokeWebDebitCurrency(IMoneyModule money, UUID agentID, int amount, string description, out string reason)
        {
            return InvokeMoneyAdminMethod(money, "WebDebitCurrency", agentID, amount, description, out reason);
        }

        private bool InvokeMoneyAdminMethod(IMoneyModule money, string methodName, UUID agentID, int amount, string description, out string reason)
        {
            reason = string.Empty;
            MethodInfo method = money.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                reason = "Currency module does not expose " + methodName + ".";
                return false;
            }

            object[] args = new object[] { agentID, amount, description ?? string.Empty, reason };
            try
            {
                bool result = method.Invoke(money, args) is bool b && b;
                reason = args[3] as string ?? string.Empty;
                return result;
            }
            catch (Exception e)
            {
                reason = e.InnerException != null ? e.InnerException.Message : e.Message;
                return false;
            }
        }

        private CurrencyPurchaseRequest CreateCurrencyPurchaseRequest(CurrencyWebSession session, int amount)
        {
            CurrencyPurchaseRequest request = new CurrencyPurchaseRequest
            {
                RequestID = GenerateCurrencyPurchaseRequestID(),
                RequestedUTC = DateTime.UtcNow,
                AgentID = session.AgentID,
                DisplayName = session.DisplayName,
                Amount = amount,
                Status = "pending",
                UpdatedUTC = DateTime.UtcNow,
                OperatorName = string.Empty,
                Note = string.Empty
            };

            lock (m_currencyPurchaseLock)
            {
                m_currencyPurchaseRequests[request.RequestID] = request;
                SaveCurrencyPurchaseRequestsLocked();
            }

            return request;
        }

        private bool ApproveCurrencyPurchase(string requestID, string note, out string message)
        {
            message = string.Empty;
            UUID agentID;
            string displayName;
            int amount;

            lock (m_currencyPurchaseLock)
            {
                if (!m_currencyPurchaseRequests.TryGetValue(requestID ?? string.Empty, out CurrencyPurchaseRequest request))
                {
                    message = "Currency purchase request not found: " + requestID;
                    return false;
                }

                if (!(request.Status ?? string.Empty).Equals("pending", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Currency purchase request " + request.RequestID + " is already " + request.Status + ".";
                    return false;
                }

                request.Status = "processing";
                request.UpdatedUTC = DateTime.UtcNow;
                request.OperatorName = "console";
                request.Note = note ?? string.Empty;
                agentID = request.AgentID;
                displayName = request.DisplayName;
                amount = request.Amount;
                SaveCurrencyPurchaseRequestsLocked();
            }

            IMoneyModule money = GetCurrencyMoneyModule();
            if (money == null)
            {
                MarkCurrencyPurchasePending(requestID, "Currency module is not active.");
                message = "Currency module is not active. Request left pending.";
                return false;
            }

            if (!InvokeWebBuyCurrency(money, agentID, amount, out string reason))
            {
                string failure = string.IsNullOrWhiteSpace(reason) ? "Token purchase failed." : reason;
                MarkCurrencyPurchasePending(requestID, failure);
                message = failure + " Request left pending.";
                return false;
            }

            lock (m_currencyPurchaseLock)
            {
                if (m_currencyPurchaseRequests.TryGetValue(requestID ?? string.Empty, out CurrencyPurchaseRequest request))
                {
                    request.Status = "approved";
                    request.UpdatedUTC = DateTime.UtcNow;
                    request.OperatorName = "console";
                    request.Note = note ?? string.Empty;
                    SaveCurrencyPurchaseRequestsLocked();
                }
            }

            NotifyCurrencyAvatar(agentID, "Wallet purchase " + requestID + " approved: "
                + amount.ToString(CultureInfo.InvariantCulture) + " tokens credited.");
            message = "Approved " + requestID + " for " + displayName + " and credited "
                + amount.ToString(CultureInfo.InvariantCulture) + " tokens.";
            return true;
        }

        private bool DenyCurrencyPurchase(string requestID, string note, out string message)
        {
            UUID agentID;
            string storedRequestID;
            string displayName;
            string storedNote;

            lock (m_currencyPurchaseLock)
            {
                if (!m_currencyPurchaseRequests.TryGetValue(requestID ?? string.Empty, out CurrencyPurchaseRequest request))
                {
                    message = "Currency purchase request not found: " + requestID;
                    return false;
                }

                string status = request.Status ?? string.Empty;
                if (!status.Equals("pending", StringComparison.OrdinalIgnoreCase)
                    && !status.Equals("processing", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Currency purchase request " + request.RequestID + " is already " + request.Status + ".";
                    return false;
                }

                request.Status = "denied";
                request.UpdatedUTC = DateTime.UtcNow;
                request.OperatorName = "console";
                request.Note = note ?? string.Empty;
                SaveCurrencyPurchaseRequestsLocked();

                agentID = request.AgentID;
                storedRequestID = request.RequestID;
                displayName = request.DisplayName;
                storedNote = request.Note;
            }

            NotifyCurrencyAvatar(agentID, "Wallet purchase " + storedRequestID + " denied."
                + (string.IsNullOrWhiteSpace(storedNote) ? string.Empty : " " + storedNote));
            message = "Denied " + storedRequestID + " for " + displayName + ".";
            return true;
        }

        private void MarkCurrencyPurchasePending(string requestID, string note)
        {
            lock (m_currencyPurchaseLock)
            {
                if (m_currencyPurchaseRequests.TryGetValue(requestID ?? string.Empty, out CurrencyPurchaseRequest request))
                {
                    request.Status = "pending";
                    request.UpdatedUTC = DateTime.UtcNow;
                    request.OperatorName = "console";
                    request.Note = note ?? string.Empty;
                    SaveCurrencyPurchaseRequestsLocked();
                }
            }
        }

        private List<Dictionary<string, string>> GetCurrencyStatement(IMoneyModule money, UUID agentID)
        {
            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
            MethodInfo method = money.GetType().GetMethod("GetCurrencyStatement", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                return rows;

            try
            {
                object result = method.Invoke(money, new object[] { agentID, m_currencyStatementLimit });
                if (result is IEnumerable<Dictionary<string, string>> enumerable)
                    rows.AddRange(enumerable);
            }
            catch
            {
            }

            return rows;
        }

        private List<Dictionary<string, string>> GetCurrencyBalances(IMoneyModule money, int limit)
        {
            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
            MethodInfo method = money.GetType().GetMethod("GetCurrencyBalances", BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                return rows;

            try
            {
                object result = method.Invoke(money, new object[] { limit });
                if (result is IEnumerable<Dictionary<string, string>> enumerable)
                    rows.AddRange(enumerable);
            }
            catch
            {
            }

            return rows;
        }

        private bool IsPayPalConfigured(out string reason)
        {
            reason = string.Empty;
            if (!m_payPalEnabled)
            {
                reason = "PayPal checkout is disabled. Set PayPalEnabled = true in [RegionCurrency].";
                return false;
            }

            if (string.IsNullOrWhiteSpace(m_payPalClientID) || string.IsNullOrWhiteSpace(m_payPalClientSecret))
            {
                reason = "PayPal checkout needs PayPalClientID and PayPalClientSecret in [RegionCurrency].";
                return false;
            }

            if (!IsAbsoluteWebUrl(GetCurrencyPublicBaseUrl()))
            {
                reason = "PayPal checkout needs PayPalReturnBaseUrl set to the public wallet URL, for example https://example.com/currency.";
                return false;
            }

            if (m_payPalPricePerToken <= 0m)
            {
                reason = "PayPalPricePerToken must be greater than zero.";
                return false;
            }

            return true;
        }

        private bool CreatePayPalOrder(CurrencyPayPalOrder order, out string approvalUrl, out string reason)
        {
            approvalUrl = string.Empty;
            reason = string.Empty;

            if (!GetPayPalAccessToken(out string accessToken, out reason))
                return false;

            string publicBase = GetCurrencyPublicBaseUrl().TrimEnd('/');
            string returnUrl = publicBase + "/paypal-return?local=" + Url(order.LocalID);
            string cancelUrl = publicBase + "/paypal-cancel?local=" + Url(order.LocalID);
            string amount = order.FiatAmount.ToString("0.00", CultureInfo.InvariantCulture);
            string description = "Wallet tokens for " + order.DisplayName;
            string customID = order.AgentID + ":" + order.TokenAmount.ToString(CultureInfo.InvariantCulture) + ":" + order.LocalID;

            string body =
                "{"
                + "\"intent\":\"CAPTURE\","
                + "\"purchase_units\":[{"
                + "\"reference_id\":\"" + Json(order.LocalID) + "\","
                + "\"description\":\"" + Json(description) + "\","
                + "\"custom_id\":\"" + Json(customID) + "\","
                + "\"amount\":{\"currency_code\":\"" + Json(order.CurrencyCode) + "\",\"value\":\"" + Json(amount) + "\"}"
                + "}],"
                + "\"application_context\":{"
                + "\"brand_name\":\"Wallet\","
                + "\"landing_page\":\"LOGIN\","
                + "\"user_action\":\"PAY_NOW\","
                + "\"return_url\":\"" + Json(returnUrl) + "\","
                + "\"cancel_url\":\"" + Json(cancelUrl) + "\""
                + "}"
                + "}";

            if (!PayPalPost("/v2/checkout/orders", body, "application/json", accessToken, out string responseText, out reason))
                return false;

            if (!TryParseJsonMap(responseText, out OSDMap map, out reason))
                return false;

            if (!map.TryGetValue("id", out OSD idOSD) || string.IsNullOrWhiteSpace(idOSD.AsString()))
            {
                reason = "PayPal order response did not include an order id.";
                return false;
            }

            order.PayPalOrderID = idOSD.AsString();
            approvalUrl = ExtractPayPalApprovalUrl(map);
            if (string.IsNullOrWhiteSpace(approvalUrl))
            {
                reason = "PayPal order response did not include an approval URL.";
                return false;
            }

            return true;
        }

        private bool CapturePayPalOrder(string paypalOrderID, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(paypalOrderID))
            {
                reason = "PayPal order id is empty.";
                return false;
            }

            if (!GetPayPalAccessToken(out string accessToken, out reason))
                return false;

            string path = "/v2/checkout/orders/" + Url(paypalOrderID) + "/capture";
            if (!PayPalPost(path, "{}", "application/json", accessToken, out string responseText, out reason))
                return false;

            if (!TryParseJsonMap(responseText, out OSDMap map, out reason))
                return false;

            if (map.TryGetValue("status", out OSD statusOSD)
                && statusOSD.AsString().Equals("COMPLETED", StringComparison.OrdinalIgnoreCase))
                return true;

            reason = "PayPal capture did not return COMPLETED status.";
            return false;
        }

        private bool GetPayPalAccessToken(out string accessToken, out string reason)
        {
            accessToken = string.Empty;
            reason = string.Empty;
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(m_payPalClientID + ":" + m_payPalClientSecret));
            if (!PayPalPost("/v1/oauth2/token", "grant_type=client_credentials", "application/x-www-form-urlencoded", "Basic " + credentials, out string responseText, out reason))
                return false;

            if (!TryParseJsonMap(responseText, out OSDMap map, out reason))
                return false;

            if (!map.TryGetValue("access_token", out OSD tokenOSD) || string.IsNullOrWhiteSpace(tokenOSD.AsString()))
            {
                reason = "PayPal OAuth response did not include an access token.";
                return false;
            }

            accessToken = tokenOSD.AsString();
            return true;
        }

        private bool PayPalPost(string path, string body, string contentType, string authorization, out string responseText, out string reason)
        {
            responseText = string.Empty;
            reason = string.Empty;

            try
            {
                string url = GetPayPalBaseUrl().TrimEnd('/') + path;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = contentType;
                request.Accept = "application/json";
                request.Timeout = 20000;
                request.ReadWriteTimeout = 20000;
                if (!string.IsNullOrWhiteSpace(authorization))
                {
                    if (authorization.StartsWith("Basic ", StringComparison.Ordinal) || authorization.StartsWith("Bearer ", StringComparison.Ordinal))
                        request.Headers.Add("Authorization", authorization);
                    else
                        request.Headers.Add("Authorization", "Bearer " + authorization);
                }

                byte[] payload = Encoding.UTF8.GetBytes(body ?? string.Empty);
                request.ContentLength = payload.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(payload, 0, payload.Length);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    responseText = ReadHttpResponse(response);
                    int code = (int)response.StatusCode;
                    if (code >= 200 && code < 300)
                        return true;

                    reason = "PayPal returned HTTP " + code.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }
            }
            catch (WebException e)
            {
                if (e.Response is HttpWebResponse response)
                {
                    responseText = ReadHttpResponse(response);
                    reason = "PayPal returned HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                        + (string.IsNullOrWhiteSpace(responseText) ? string.Empty : ": " + TrimForLog(responseText, 240));
                    return false;
                }

                reason = "PayPal request failed: " + e.Message;
                return false;
            }
            catch (Exception e)
            {
                reason = "PayPal request failed: " + e.Message;
                return false;
            }
        }

        private static string ReadHttpResponse(HttpWebResponse response)
        {
            if (response == null)
                return string.Empty;

            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null)
                    return string.Empty;

                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        private static bool TryParseJsonMap(string json, out OSDMap map, out string reason)
        {
            map = null;
            reason = string.Empty;

            try
            {
                map = OSDParser.DeserializeJson(json ?? string.Empty) as OSDMap;
            }
            catch (Exception e)
            {
                reason = "Could not parse PayPal JSON response: " + e.Message;
                return false;
            }

            if (map == null)
            {
                reason = "PayPal JSON response was not an object.";
                return false;
            }

            return true;
        }

        private static string ExtractPayPalApprovalUrl(OSDMap map)
        {
            if (map == null || !map.TryGetValue("links", out OSD linksOSD) || !(linksOSD is OSDArray links))
                return string.Empty;

            foreach (OSD item in links)
            {
                if (!(item is OSDMap link))
                    continue;

                string rel = link.TryGetValue("rel", out OSD relOSD) ? relOSD.AsString() : string.Empty;
                if (!rel.Equals("approve", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (link.TryGetValue("href", out OSD hrefOSD))
                    return hrefOSD.AsString();
            }

            return string.Empty;
        }

        private string GetPayPalBaseUrl()
        {
            return m_payPalEnvironment.Equals("live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
        }

        private string GetCurrencyPublicBaseUrl()
        {
            string configured = (m_payPalReturnBaseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (IsAbsoluteWebUrl(configured))
                return configured;

            foreach (Scene scene in GetSceneSnapshot())
            {
                string serverURI = (scene.RegionInfo.ServerURI ?? string.Empty).Trim().TrimEnd('/');
                if (IsAbsoluteWebUrl(serverURI))
                    return serverURI + m_basePath;
            }

            return string.Empty;
        }

        private static bool IsAbsoluteWebUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
                && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase));
        }

        private bool TryParsePositiveAmount(string text, int maxAmount, out int amount, out string reason)
        {
            amount = 0;
            reason = string.Empty;
            if (!int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount <= 0)
            {
                reason = "Amount must be a positive whole number.";
                return false;
            }

            if (maxAmount > 0 && amount > maxAmount)
            {
                reason = "Amount cannot exceed " + maxAmount.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            return true;
        }

        private bool TryParseWholeAmount(string text, out int amount, out string reason)
        {
            amount = 0;
            reason = string.Empty;
            if (!int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount < 0)
            {
                reason = "Amount must be zero or a positive whole number.";
                return false;
            }

            return true;
        }

        private bool IsCurrencyAdminSession(CurrencyWebSession session)
        {
            return session != null && session.IsAdmin && IsRegionWebSuperAdmin(session.AgentID);
        }

        private CurrencyWebSession GetCurrencySession(IOSHttpRequest request)
        {
            string token = ReadCookie(request, CurrencySessionCookie);
            if (string.IsNullOrEmpty(token))
                return null;

            lock (m_currencyAuthLock)
            {
                CleanupCurrencyAuthLocked();
                if (m_currencySessions.TryGetValue(token, out CurrencyWebSession session))
                    return session;
            }

            return null;
        }

        private void CleanupCurrencyAuthLocked()
        {
            DateTime now = DateTime.UtcNow;
            List<string> expiredChallenges = new List<string>();
            foreach (KeyValuePair<string, CurrencyLoginChallenge> entry in m_currencyChallenges)
            {
                if (entry.Value.ExpiresUTC <= now)
                    expiredChallenges.Add(entry.Key);
            }
            foreach (string token in expiredChallenges)
                m_currencyChallenges.Remove(token);

            List<string> expiredSessions = new List<string>();
            foreach (KeyValuePair<string, CurrencyWebSession> entry in m_currencySessions)
            {
                if (entry.Value.ExpiresUTC <= now)
                    expiredSessions.Add(entry.Key);
            }
            foreach (string token in expiredSessions)
                m_currencySessions.Remove(token);

            if (m_currencyChallengeCooldownSeconds > 0)
            {
                double keepSeconds = Math.Max(3600, m_currencyChallengeCooldownSeconds * 4);
                List<UUID> oldRequests = new List<UUID>();
                foreach (KeyValuePair<UUID, DateTime> entry in m_currencyLastChallengeUTCByAgent)
                {
                    if ((now - entry.Value).TotalSeconds > keepSeconds)
                        oldRequests.Add(entry.Key);
                }
                foreach (UUID agentID in oldRequests)
                    m_currencyLastChallengeUTCByAgent.Remove(agentID);
            }
        }

        private void SetCurrencySessionCookie(IOSHttpResponse response, string token, DateTime expiresUTC)
        {
            response.AddHeader("Set-Cookie", CurrencySessionCookie + "=" + token
                + "; Path=" + m_basePath + "; Expires=" + expiresUTC.ToString("R", CultureInfo.InvariantCulture)
                + "; HttpOnly; SameSite=Lax");
        }

        private void ClearCurrencySessionCookie(IOSHttpResponse response)
        {
            response.AddHeader("Set-Cookie", CurrencySessionCookie
                + "=; Path=" + m_basePath + "; Expires=Thu, 01 Jan 1970 00:00:00 GMT; HttpOnly; SameSite=Lax");
        }

        private static string GenerateCurrencyChallengeToken()
        {
            return UUID.Random().ToString().Replace("-", string.Empty).Substring(0, 8).ToUpperInvariant();
        }

        private static string GenerateCurrencySessionToken()
        {
            return UUID.Random().ToString().Replace("-", string.Empty) + UUID.Random().ToString().Replace("-", string.Empty);
        }

        private static string GenerateCurrencyPurchaseRequestID()
        {
            return "RW" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                + "-" + UUID.Random().ToString().Replace("-", string.Empty).Substring(0, 6).ToUpperInvariant();
        }

        private static string GenerateCurrencyPayPalOrderID()
        {
            return "PP" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                + "-" + UUID.Random().ToString().Replace("-", string.Empty).Substring(0, 6).ToUpperInvariant();
        }

        private static string NormalizeCurrencyBuyMode(string value)
        {
            string mode = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (mode == "request" || mode == "approval" || mode == "approve")
                return "request";
            if (mode == "paypal" || mode == "pay-pal" || mode == "checkout")
                return "paypal";
            if (mode == "disabled" || mode == "disable" || mode == "off" || mode == "false")
                return "disabled";
            return "grant";
        }

        private static string NormalizePayPalEnvironment(string value)
        {
            string mode = (value ?? string.Empty).Trim().ToLowerInvariant();
            return mode == "live" || mode == "production" ? "live" : "sandbox";
        }

        private static string NormalizePayPalCurrency(string value)
        {
            string currency = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (currency.Length != 3 || currency.Any(c => c < 'A' || c > 'Z'))
                return "EUR";
            return currency;
        }

        private static decimal ParsePositiveDecimal(string value, decimal fallback)
        {
            if (decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
                && parsed > 0m)
                return parsed;

            if (decimal.TryParse((value ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out parsed)
                && parsed > 0m)
                return parsed;

            return fallback;
        }

        private bool IsCurrencyBuyAvailable()
        {
            if (!m_currencyBuyEnabled || m_currencyBuyMode.Equals("disabled", StringComparison.OrdinalIgnoreCase))
                return false;
            if (m_currencyBuyMode.Equals("paypal", StringComparison.OrdinalIgnoreCase))
                return IsPayPalConfigured(out _);
            return true;
        }

        private void NotifyCurrencyAvatar(UUID agentID, string message)
        {
            if (agentID == UUID.Zero || string.IsNullOrWhiteSpace(message))
                return;

            if (!TryFindOnlineClient(agentID, out IClientAPI client))
                return;

            try
            {
                client.SendBlueBoxMessage(UUID.Zero, "Wallet", message);
            }
            catch
            {
                try
                {
                    client.SendAgentAlertMessage(message, false);
                }
                catch
                {
                }
            }
        }

        private static string RowValue(Dictionary<string, string> row, string key)
        {
            if (row != null && row.TryGetValue(key, out string value))
                return value ?? string.Empty;
            return string.Empty;
        }

        private static string GetCurrencyDirection(string agentText, string source, string destination)
        {
            if (destination.Equals(agentText, StringComparison.OrdinalIgnoreCase)
                && !source.Equals(agentText, StringComparison.OrdinalIgnoreCase))
                return "credit";
            if (source.Equals(agentText, StringComparison.OrdinalIgnoreCase)
                && !destination.Equals(agentText, StringComparison.OrdinalIgnoreCase))
                return "debit";
            return "internal";
        }

        private void LoadCurrencyPurchaseRequests()
        {
            lock (m_currencyPurchaseLock)
            {
                m_currencyPurchaseRequests.Clear();

                if (string.IsNullOrWhiteSpace(m_absoluteCurrencyPurchaseStoragePath)
                    || !File.Exists(m_absoluteCurrencyPurchaseStoragePath))
                    return;

                try
                {
                    foreach (string line in File.ReadAllLines(m_absoluteCurrencyPurchaseStoragePath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                            continue;

                        string[] parts = line.Split('\t');
                        if (parts.Length < 9 || !UUID.TryParse(parts[2], out UUID agentID))
                            continue;

                        if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                            continue;

                        DateTime requestedUTC;
                        DateTime updatedUTC;
                        if (!DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out requestedUTC))
                            requestedUTC = DateTime.UtcNow;
                        if (!DateTime.TryParse(parts[6], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out updatedUTC))
                            updatedUTC = requestedUTC;

                        CurrencyPurchaseRequest request = new CurrencyPurchaseRequest
                        {
                            RequestID = parts[0],
                            RequestedUTC = requestedUTC.ToUniversalTime(),
                            AgentID = agentID,
                            DisplayName = parts[3],
                            Amount = amount,
                            Status = string.IsNullOrWhiteSpace(parts[5]) ? "pending" : parts[5],
                            UpdatedUTC = updatedUTC.ToUniversalTime(),
                            OperatorName = parts[7],
                            Note = parts[8]
                        };

                        if (!string.IsNullOrWhiteSpace(request.RequestID))
                            m_currencyPurchaseRequests[request.RequestID] = request;
                    }
                }
                catch (Exception e)
                {
                    m_log.WarnFormat("[REGION WEB]: Could not load currency purchase requests from {0}: {1}", m_absoluteCurrencyPurchaseStoragePath, e.Message);
                }
            }
        }

        private void SaveCurrencyPurchaseRequestsLocked()
        {
            if (string.IsNullOrWhiteSpace(m_absoluteCurrencyPurchaseStoragePath))
                return;

            try
            {
                string directory = Path.GetDirectoryName(m_absoluteCurrencyPurchaseStoragePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                StringBuilder rows = new StringBuilder();
                rows.Append("# request_id\trequested_utc\tagent_id\tdisplay_name\tamount\tstatus\tupdated_utc\toperator\tnote\n");
                foreach (CurrencyPurchaseRequest request in m_currencyPurchaseRequests.Values.OrderBy(r => r.RequestedUTC))
                {
                    rows.Append(Tsv(request.RequestID)).Append('\t')
                        .Append(request.RequestedUTC.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(request.AgentID).Append('\t')
                        .Append(Tsv(request.DisplayName)).Append('\t')
                        .Append(request.Amount.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(Tsv(request.Status)).Append('\t')
                        .Append(request.UpdatedUTC.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(Tsv(request.OperatorName)).Append('\t')
                        .Append(Tsv(request.Note)).Append('\n');
                }

                File.WriteAllText(m_absoluteCurrencyPurchaseStoragePath, rows.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[REGION WEB]: Could not save currency purchase requests to {0}: {1}", m_absoluteCurrencyPurchaseStoragePath, e.Message);
            }
        }

        private void LoadCurrencyPayPalOrders()
        {
            lock (m_currencyPayPalLock)
            {
                m_currencyPayPalOrders.Clear();

                if (string.IsNullOrWhiteSpace(m_absolutePayPalOrderStoragePath)
                    || !File.Exists(m_absolutePayPalOrderStoragePath))
                    return;

                try
                {
                    foreach (string line in File.ReadAllLines(m_absolutePayPalOrderStoragePath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                            continue;

                        string[] parts = line.Split('\t');
                        if (parts.Length < 11 || !UUID.TryParse(parts[2], out UUID agentID))
                            continue;

                        if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tokenAmount))
                            continue;

                        if (!decimal.TryParse(parts[5], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal fiatAmount))
                            fiatAmount = 0m;

                        DateTime createdUTC;
                        DateTime updatedUTC;
                        if (!DateTime.TryParse(parts[8], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out createdUTC))
                            createdUTC = DateTime.UtcNow;
                        if (!DateTime.TryParse(parts[9], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out updatedUTC))
                            updatedUTC = createdUTC;

                        CurrencyPayPalOrder order = new CurrencyPayPalOrder
                        {
                            LocalID = parts[0],
                            PayPalOrderID = parts[1],
                            AgentID = agentID,
                            DisplayName = parts[3],
                            TokenAmount = tokenAmount,
                            FiatAmount = fiatAmount,
                            CurrencyCode = string.IsNullOrWhiteSpace(parts[6]) ? m_payPalCurrencyCode : parts[6],
                            Status = string.IsNullOrWhiteSpace(parts[7]) ? "created" : parts[7],
                            CreatedUTC = createdUTC.ToUniversalTime(),
                            UpdatedUTC = updatedUTC.ToUniversalTime(),
                            Note = parts[10]
                        };

                        if (!string.IsNullOrWhiteSpace(order.LocalID))
                            m_currencyPayPalOrders[order.LocalID] = order;
                    }
                }
                catch (Exception e)
                {
                    m_log.WarnFormat("[REGION WEB]: Could not load PayPal currency orders from {0}: {1}", m_absolutePayPalOrderStoragePath, e.Message);
                }
            }
        }

        private void StoreCurrencyPayPalOrder(CurrencyPayPalOrder order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.LocalID))
                return;

            lock (m_currencyPayPalLock)
            {
                m_currencyPayPalOrders[order.LocalID] = order;
                SaveCurrencyPayPalOrdersLocked();
            }
        }

        private CurrencyPayPalOrder FindCurrencyPayPalOrder(string paypalOrderID, string localID)
        {
            lock (m_currencyPayPalLock)
            {
                if (!string.IsNullOrWhiteSpace(localID)
                    && m_currencyPayPalOrders.TryGetValue(localID, out CurrencyPayPalOrder byLocal))
                    return byLocal;

                if (!string.IsNullOrWhiteSpace(paypalOrderID))
                {
                    foreach (CurrencyPayPalOrder order in m_currencyPayPalOrders.Values)
                    {
                        if ((order.PayPalOrderID ?? string.Empty).Equals(paypalOrderID, StringComparison.OrdinalIgnoreCase))
                            return order;
                    }
                }
            }

            return null;
        }

        private void MarkCurrencyPayPalOrder(string localID, string status, string note)
        {
            if (string.IsNullOrWhiteSpace(localID))
                return;

            lock (m_currencyPayPalLock)
            {
                if (!m_currencyPayPalOrders.TryGetValue(localID, out CurrencyPayPalOrder order))
                    return;

                order.Status = status ?? order.Status;
                order.Note = note ?? string.Empty;
                order.UpdatedUTC = DateTime.UtcNow;
                SaveCurrencyPayPalOrdersLocked();
            }
        }

        private void SaveCurrencyPayPalOrdersLocked()
        {
            if (string.IsNullOrWhiteSpace(m_absolutePayPalOrderStoragePath))
                return;

            try
            {
                string directory = Path.GetDirectoryName(m_absolutePayPalOrderStoragePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                StringBuilder rows = new StringBuilder();
                rows.Append("# local_id\tpaypal_order_id\tagent_id\tdisplay_name\ttokens\tfiat_amount\tcurrency\tstatus\tcreated_utc\tupdated_utc\tnote\n");
                foreach (CurrencyPayPalOrder order in m_currencyPayPalOrders.Values.OrderBy(r => r.CreatedUTC))
                {
                    rows.Append(Tsv(order.LocalID)).Append('\t')
                        .Append(Tsv(order.PayPalOrderID)).Append('\t')
                        .Append(order.AgentID).Append('\t')
                        .Append(Tsv(order.DisplayName)).Append('\t')
                        .Append(order.TokenAmount.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(order.FiatAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(Tsv(order.CurrencyCode)).Append('\t')
                        .Append(Tsv(order.Status)).Append('\t')
                        .Append(order.CreatedUTC.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(order.UpdatedUTC.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(Tsv(order.Note)).Append('\n');
                }

                File.WriteAllText(m_absolutePayPalOrderStoragePath, rows.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[REGION WEB]: Could not save PayPal currency orders to {0}: {1}", m_absolutePayPalOrderStoragePath, e.Message);
            }
        }

        private class CurrencyLoginChallenge
        {
            public UUID AgentID;
            public string DisplayName;
            public string Token;
            public DateTime ExpiresUTC;
            public bool IsAdmin;
        }

        private class CurrencyWebSession
        {
            public UUID AgentID;
            public string DisplayName;
            public string CsrfToken;
            public DateTime ExpiresUTC;
            public bool IsAdmin;
        }

        private class CurrencyPurchaseRequest
        {
            public string RequestID;
            public DateTime RequestedUTC;
            public UUID AgentID;
            public string DisplayName;
            public int Amount;
            public string Status;
            public DateTime UpdatedUTC;
            public string OperatorName;
            public string Note;
        }

        private class CurrencyPayPalOrder
        {
            public string LocalID;
            public string PayPalOrderID;
            public UUID AgentID;
            public string DisplayName;
            public int TokenAmount;
            public decimal FiatAmount;
            public string CurrencyCode;
            public string Status;
            public DateTime CreatedUTC;
            public DateTime UpdatedUTC;
            public string Note;
        }

        private bool IsRegionWebSuperAdmin(UUID agentID)
        {
            if (agentID == UUID.Zero)
                return false;

            foreach (Scene scene in GetSceneSnapshot())
            {
                EstateSettings estate = scene.RegionInfo.EstateSettings;
                if (estate != null && estate.EstateOwner == agentID)
                    return true;
            }

            return false;
        }

        private static string ReadCookie(IOSHttpRequest request, string name)
        {
            string cookies = request.Headers["cookie"] ?? request.Headers["Cookie"];
            if (string.IsNullOrEmpty(cookies))
                return string.Empty;

            string[] parts = cookies.Split(';');
            foreach (string raw in parts)
            {
                string part = raw.Trim();
                int equals = part.IndexOf('=');
                if (equals <= 0)
                    continue;
                if (part.Substring(0, equals).Trim().Equals(name, StringComparison.Ordinal))
                    return part.Substring(equals + 1).Trim();
            }

            return string.Empty;
        }

        private Dictionary<string, string> ReadForm(IOSHttpRequest request)
        {
            Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (request.QueryAsDictionary != null)
            {
                foreach (KeyValuePair<string, string> entry in request.QueryAsDictionary)
                    form[entry.Key] = entry.Value ?? string.Empty;
            }

            if (request.HasEntityBody && request.InputStream != null)
            {
                Encoding encoding = request.ContentEncoding ?? Encoding.UTF8;
                using (StreamReader reader = new StreamReader(request.InputStream, encoding))
                {
                    string body = reader.ReadToEnd();
                    if (!string.IsNullOrEmpty(body))
                    {
                        Dictionary<string, object> parsed = ServerUtils.ParseQueryString(body);
                        foreach (KeyValuePair<string, object> entry in parsed)
                            form[entry.Key] = entry.Value == null ? string.Empty : entry.Value.ToString();
                    }
                }
            }

            return form;
        }

        private static string FormValue(Dictionary<string, string> form, string name)
        {
            if (form != null && form.TryGetValue(name, out string value))
                return value == null ? string.Empty : value.Trim();
            return string.Empty;
        }

        private static string FormRawValue(Dictionary<string, string> form, string name)
        {
            if (form != null && form.TryGetValue(name, out string value))
                return value ?? string.Empty;
            return string.Empty;
        }

        private bool TryResolveAvatar(string value, out UUID agentID, out string displayName)
        {
            agentID = UUID.Zero;
            displayName = string.Empty;
            string name = (value ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name))
                return false;

            if (UUID.TryParse(name, out agentID))
            {
                displayName = LookupAvatarName(agentID);
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = agentID.ToString();
                return true;
            }

            if (TryFindOnlineClientByName(name, out IClientAPI client))
            {
                agentID = client.AgentId;
                displayName = client.Name;
                return true;
            }

            if (!SplitAvatarName(name, out string firstName, out string lastName))
                return false;

            foreach (Scene scene in GetSceneSnapshot())
            {
                IUserAccountService accounts = scene.UserAccountService;
                if (accounts == null)
                    continue;

                UserAccount account = accounts.GetUserAccount(scene.RegionInfo.ScopeID, firstName, lastName)
                    ?? accounts.GetUserAccount(UUID.Zero, firstName, lastName);
                if (account == null)
                    continue;

                agentID = account.PrincipalID;
                displayName = account.Name;
                return true;
            }

            return false;
        }

        private string LookupAvatarName(UUID agentID)
        {
            if (TryFindOnlineClient(agentID, out IClientAPI client))
                return client.Name;

            foreach (Scene scene in GetSceneSnapshot())
            {
                IUserAccountService accounts = scene.UserAccountService;
                if (accounts == null)
                    continue;

                UserAccount account = accounts.GetUserAccount(scene.RegionInfo.ScopeID, agentID)
                    ?? accounts.GetUserAccount(UUID.Zero, agentID);
                if (account != null)
                    return account.Name;
            }

            return string.Empty;
        }

        private bool TryFindOnlineClient(UUID agentID, out IClientAPI client)
        {
            client = null;
            foreach (Scene scene in GetSceneSnapshot())
            {
                if (scene.TryGetScenePresence(agentID, out ScenePresence presence)
                    && TryGetRootClient(presence, out client))
                    return true;
            }

            return false;
        }

        private bool TryFindOnlineClientByName(string name, out IClientAPI client)
        {
            client = null;
            if (!SplitAvatarName(name, out string firstName, out string lastName))
                return false;

            foreach (Scene scene in GetSceneSnapshot())
            {
                ScenePresence presence = scene.GetScenePresence(firstName, lastName);
                if (TryGetRootClient(presence, out client))
                    return true;
            }

            return false;
        }

        private static bool TryGetRootClient(ScenePresence presence, out IClientAPI client)
        {
            client = null;
            if (presence == null || presence.IsDeleted || presence.IsChildAgent || presence.ControllingClient == null)
                return false;

            client = presence.ControllingClient;
            return client.IsActive;
        }

        private List<Scene> GetSceneSnapshot()
        {
            lock (m_sync)
                return new List<Scene>(m_scenesByID.Values);
        }

        private static bool SplitAvatarName(string name, out string firstName, out string lastName)
        {
            firstName = string.Empty;
            lastName = string.Empty;
            string[] parts = (name ?? string.Empty).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            firstName = parts[0];
            if (parts.Length == 1)
                lastName = "Resident";
            else
                lastName = string.Join(" ", parts.Skip(1).ToArray());

            return true;
        }

        private static string FormatUtc(string value)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsed))
                return parsed.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
            return value;
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            string escaped = value.Replace("\"", "\"\"");
            return mustQuote ? "\"" + escaped + "\"" : escaped;
        }

        private static string Json(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            StringBuilder escaped = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '"':
                        escaped.Append("\\\"");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                            escaped.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            escaped.Append(c);
                        break;
                }
            }

            return escaped.ToString();
        }

        private static string TrimForLog(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;
            return value.Substring(0, Math.Max(0, maxLength)) + "...";
        }

        private static string Tsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static void SendHtml(IOSHttpResponse response, string html)
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/html; charset=utf-8";
            response.RawBuffer = Encoding.UTF8.GetBytes(html);
        }

        private static void SendNotFound(IOSHttpResponse response, string message)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.ContentType = "text/plain";
            response.RawBuffer = Encoding.UTF8.GetBytes(message);
        }

        private StringBuilder BeginPage(string title)
        {
            StringBuilder html = new StringBuilder(8192);
            html.Append("<!doctype html><html><head><meta charset=\"utf-8\">")
                .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
                .Append("<title>").Append(Html(title)).Append("</title>")
                .Append("<style>")
                .Append(RegionWebCss())
                .Append("</style></head><body id=\"top\">");
            AppendGlobalNavigation(html);
            return html;
        }

        private void AppendGlobalNavigation(StringBuilder html)
        {
            html.Append("<nav class=\"site-nav\" aria-label=\"Region navigation\"><div class=\"wrap nav-wrap\"><a class=\"brand\" href=\"")
                .Append(Html(m_basePath)).Append("/\"><span class=\"brand-type\">").Append(Html(m_defaultEstateTitle)).Append("</span></a><div class=\"nav-links\">")
                .Append("<a href=\"").Append(Html(m_basePath)).Append("/#regions\">Regions</a>")
                .Append("<a href=\"").Append(Html(m_basePath)).Append("/#features\">Features</a>");

            html.Append("<a href=\"").Append(Html(m_basePath)).Append("/admin\">Admin</a>");

            html.Append("</div></div></nav>");
        }

        private static void AppendPageLinks(StringBuilder html, params string[] labelUrlPairs)
        {
            if (labelUrlPairs == null || labelUrlPairs.Length < 2)
                return;

            html.Append("<nav class=\"page-links\" aria-label=\"Page navigation\">");
            for (int i = 0; i + 1 < labelUrlPairs.Length; i += 2)
            {
                html.Append("<a href=\"").Append(Html(labelUrlPairs[i + 1])).Append("\">")
                    .Append(Html(labelUrlPairs[i])).Append("</a>");
            }
            html.Append("</nav>");
        }

        private static string EndPage()
        {
            return "<a class=\"back-to-top\" href=\"#top\" aria-label=\"Back to top\">Top</a>"
                + "<script>(function(){var groups=document.querySelectorAll('[data-carousel]');for(var g=0;g<groups.length;g++){(function(box){var slides=box.querySelectorAll('.estate-slide');if(slides.length<2)return;var i=0;setInterval(function(){slides[i].classList.remove('is-active');i=(i+1)%slides.length;slides[i].classList.add('is-active');},9500);})(groups[g]);}})();</script>"
                + "</body></html>";
        }

        private static string MakeSlug(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "region";

            StringBuilder slug = new StringBuilder(name.Length);
            bool dash = false;
            foreach (char c in name.Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    slug.Append(c);
                    dash = false;
                }
                else if (!dash)
                {
                    slug.Append('-');
                    dash = true;
                }
            }

            string result = slug.ToString().Trim('-');
            return string.IsNullOrEmpty(result) ? "region" : result;
        }

        private static string Url(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static string Html(string value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly object m_sync = new object();
        private readonly Dictionary<UUID, Scene> m_scenesByID = new Dictionary<UUID, Scene>();
        private bool m_enabled;
        private bool m_handlerRegistered;
        private string m_basePath = "/currency";
        private string m_defaultEstateTitle = "My OpenSim Estate";

        public string Name { get { return "RegionCurrencyModule"; } }

        public Type ReplaceableInterface { get { return null; } }

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["RegionCurrency"];
            if (config == null)
                return;

            m_enabled = config.GetBoolean("Enabled", false);
            m_basePath = CleanPath(config.GetString("PublicPath", "/currency"));
            m_defaultEstateTitle = config.GetString("EstateTitle", "My OpenSim Estate").Trim();
            if (string.IsNullOrEmpty(m_defaultEstateTitle))
                m_defaultEstateTitle = "My OpenSim Estate";
            m_currencyBuyEnabled = config.GetBoolean("CurrencyBuyEnabled", true);
            m_currencyTransferEnabled = config.GetBoolean("CurrencyTransferEnabled", true);
            m_currencyChallengeMinutes = Math.Max(1, config.GetInt("CurrencyChallengeMinutes", 10));
            m_currencyChallengeCooldownSeconds = Math.Max(0, config.GetInt("CurrencyChallengeCooldownSeconds", 20));
            m_currencySessionHours = Math.Max(1, config.GetInt("CurrencySessionHours", 12));
            m_currencyStatementLimit = Math.Max(1, config.GetInt("CurrencyStatementLimit", 30));
            m_currencyBuyLimit = Math.Max(1, config.GetInt("CurrencyBuyLimit", 100000));
            m_currencyBuyMode = NormalizeCurrencyBuyMode(config.GetString("CurrencyBuyMode", "grant"));
            m_currencyPurchaseStoragePath = config.GetString("CurrencyPurchaseStorage", "Currency/regioncurrency-purchases.tsv").Trim();
            m_payPalEnabled = config.GetBoolean("PayPalEnabled", false);
            m_payPalEnvironment = NormalizePayPalEnvironment(config.GetString("PayPalEnvironment", "sandbox"));
            m_payPalClientID = config.GetString("PayPalClientID", string.Empty).Trim();
            m_payPalClientSecret = config.GetString("PayPalClientSecret", string.Empty).Trim();
            m_payPalCurrencyCode = NormalizePayPalCurrency(config.GetString("PayPalCurrencyCode", "EUR"));
            m_payPalPricePerToken = ParsePositiveDecimal(config.GetString("PayPalPricePerToken", "0.01"), 0.01m);
            m_payPalReturnBaseUrl = config.GetString("PayPalReturnBaseUrl", string.Empty).Trim();
            m_payPalOrderStoragePath = config.GetString("PayPalOrderStorage", "Currency/regioncurrency-paypal-orders.tsv").Trim();

            if (string.IsNullOrEmpty(m_currencyPurchaseStoragePath))
                m_currencyPurchaseStoragePath = "Currency/regioncurrency-purchases.tsv";
            if (string.IsNullOrEmpty(m_payPalOrderStoragePath))
                m_payPalOrderStoragePath = "Currency/regioncurrency-paypal-orders.tsv";

            m_absoluteCurrencyPurchaseStoragePath = Path.IsPathRooted(m_currencyPurchaseStoragePath)
                ? m_currencyPurchaseStoragePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, m_currencyPurchaseStoragePath);
            m_absolutePayPalOrderStoragePath = Path.IsPathRooted(m_payPalOrderStoragePath)
                ? m_payPalOrderStoragePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, m_payPalOrderStoragePath);
        }

        public void PostInitialise()
        {
            if (!m_enabled)
                return;

            try
            {
                LoadCurrencyPurchaseRequests();
                LoadCurrencyPayPalOrders();

                IHttpServer server = MainServer.GetHttpServer(0);
                server.AddSimpleStreamHandler(new SimpleStreamHandler(m_basePath, HandleRequest, "RegionCurrency"));
                server.AddSimpleStreamHandler(new SimpleStreamHandler(m_basePath, HandleRequest, "RegionCurrency"), true);
                m_handlerRegistered = true;

                MainConsole.Instance.Commands.AddCommand(
                    "RegionCurrency", false, "regioncurrency pending",
                    "regioncurrency pending",
                    "List pending wallet token purchase requests.",
                    HandleCurrencyCommand);

                MainConsole.Instance.Commands.AddCommand(
                    "RegionCurrency", false, "regioncurrency approve",
                    "regioncurrency approve <request-id> [note]",
                    "Approve a pending wallet token purchase request and credit the avatar.",
                    HandleCurrencyCommand);

                MainConsole.Instance.Commands.AddCommand(
                    "RegionCurrency", false, "regioncurrency deny",
                    "regioncurrency deny <request-id> [note]",
                    "Deny a pending wallet token purchase request.",
                    HandleCurrencyCommand);

                m_log.InfoFormat("[REGION CURRENCY]: Enabled at {0}", m_basePath);
            }
            catch (Exception e)
            {
                m_enabled = false;
                m_log.WarnFormat("[REGION CURRENCY]: Could not enable module: {0}", e.Message);
            }
        }

        public void AddRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            AddOrUpdateScene(scene);
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_enabled)
                return;

            AddOrUpdateScene(scene);
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            lock (m_sync)
                m_scenesByID.Remove(scene.RegionInfo.RegionID);
        }

        public void Close()
        {
            if (m_handlerRegistered)
            {
                MainServer.GetHttpServer(0).RemoveSimpleStreamHandler(m_basePath);
                MainServer.GetHttpServer(0).RemoveSimpleStreamHandler(m_basePath);
                m_handlerRegistered = false;
            }

            lock (m_sync)
                m_scenesByID.Clear();

            lock (m_currencyAuthLock)
            {
                m_currencyChallenges.Clear();
                m_currencySessions.Clear();
                m_currencyLastChallengeUTCByAgent.Clear();
            }

            lock (m_currencyPurchaseLock)
                m_currencyPurchaseRequests.Clear();

            lock (m_currencyPayPalLock)
                m_currencyPayPalOrders.Clear();
        }

        private void AddOrUpdateScene(Scene scene)
        {
            lock (m_sync)
                m_scenesByID[scene.RegionInfo.RegionID] = scene;
        }

        /// <summary>
        /// Top-level HTTP entry point for this module's own base path
        /// (default /currency). RegionCurrency now owns its whole path
        /// rather than living under RegionWeb's /regionweb/currency/ as
        /// it did in the source project - this method strips its own
        /// base path and hands off to the (otherwise unchanged)
        /// SendCurrencyPortal dispatcher below, which still expects a
        /// leading "currency" placeholder in parts[0] for its parts[1]/
        /// parts[2] index checks (admin sub-route, CSV export names,
        /// etc.) - reusing that logic as-is rather than re-indexing every
        /// reference through ~190 lines of routing was judged the lower-
        /// risk option.
        /// </summary>
        private void HandleRequest(IOSHttpRequest request, IOSHttpResponse response)
        {
            try
            {
                string path = request.UriPath ?? string.Empty;
                string relative = path.Length > m_basePath.Length ? path.Substring(m_basePath.Length).Trim('/') : string.Empty;
                string[] rawParts = string.IsNullOrEmpty(relative)
                    ? Array.Empty<string>()
                    : relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                string[] parts = new string[rawParts.Length + 1];
                parts[0] = "currency";
                Array.Copy(rawParts, 0, parts, 1, rawParts.Length);

                SendCurrencyPortal(parts, request, response);
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[REGION CURRENCY]: Request failed: {0}", e);
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.ContentType = "text/plain";
                response.RawBuffer = Encoding.UTF8.GetBytes("RegionCurrency request failed.");
            }
        }

    }
}
