/* Copyright (c) OpenSimulator contributors. BSD-3-Clause; see LICENSE.txt. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Servers.HttpServer;

namespace OpenSim.Continuum.WebUI
{
    internal sealed partial class WebUIServiceIntegration
    {
        private readonly WebUIPasswordRecovery _recovery;
        private readonly HashSet<IPAddress> _trustedProxies;
        internal bool RecoveryEnabled => _recovery != null;
        internal bool RequestIsSecure(IOSHttpRequest request) => request.IsSecured
            || (_trustedProxies != null && request.RemoteIPEndPoint?.Address is IPAddress address
                && _trustedProxies.Contains(address.MapToIPv6())
                && String.Equals(request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase));
        internal bool AccountTransportAllowed(IOSHttpRequest request) => (!RecoveryEnabled && (_consoleEndpoints == null || _consoleEndpoints.Count == 0) && _gridConfiguration == null && !_allowPartnerships && !_allowAccountClosure && !_residentContactEnabled && String.IsNullOrEmpty(_searchWriteSecret) && (_auctionEndpoints == null || _auctionEndpoints.Count == 0)) || RequestIsSecure(request)
            || (request.Url?.IsLoopback == true && request.RemoteIPEndPoint?.Address is IPAddress address
                && IPAddress.IsLoopback(address)
                && (_trustedProxies == null || !_trustedProxies.Contains(address.MapToIPv6())));
        private static HashSet<IPAddress> TrustedProxies(string value)
        {
            var result = new HashSet<IPAddress>();
            foreach (string entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!IPAddress.TryParse(entry.Trim(), out var address))
                    throw new InvalidOperationException("TrustedProxyAddresses must contain comma-separated IP addresses");
                result.Add(address.MapToIPv6());
            }
            return result;
        }
        internal string RequestAddress(IOSHttpRequest request)
        {
            IPAddress current = request.RemoteIPEndPoint?.Address;
            string original = current?.ToString() ?? "unknown";
            string forwarded = request.Headers["X-Forwarded-For"];
            if (current == null || _trustedProxies == null || !_trustedProxies.Contains(current.MapToIPv6())
                || String.IsNullOrEmpty(forwarded) || forwarded.Length > 1024) return original;
            var entries = forwarded.Split(',');
            if (entries.Length > 16 || entries.Any(x => !IPAddress.TryParse(x.Trim(), out _))) return original;
            for (int i = entries.Length - 1; i >= 0 && _trustedProxies.Contains(current.MapToIPv6()); --i)
                current = IPAddress.Parse(entries[i].Trim());
            return current.ToString();
        }
        private WebUIPasswordRecovery ConfigureRecovery(IConfig section)
        {
            if (!section.GetBoolean("PasswordRecoveryEnabled", false)) return null;
            if (!Uri.TryCreate(section.GetString("PasswordRecoveryURL", ""), UriKind.Absolute, out Uri url))
                throw new InvalidOperationException("Configure PasswordRecoveryURL with the public HTTPS portal root");
            string host = section.GetString("PasswordRecoverySMTPHost", "").Trim();
            int port = section.GetInt("PasswordRecoverySMTPPort", 587);
            string sender = section.GetString("PasswordRecoveryFrom", "");
            string user = section.GetString("PasswordRecoverySMTPUser", "");
            string password = section.GetString("PasswordRecoverySMTPPassword", "");
            if (host.Length == 0 || host.Length > 255 || port < 1 || port > 65535
                || !MailboxAddress.TryParse(sender, out MailboxAddress from)
                || (user.Length == 0 && password.Length != 0))
                throw new InvalidOperationException("Invalid password recovery mail configuration");
            if (!Enum.TryParse(section.GetString("PasswordRecoverySMTPSecurity", "StartTls"), true, out SecureSocketOptions security)
                || (security != SecureSocketOptions.StartTls && security != SecureSocketOptions.SslOnConnect))
                throw new InvalidOperationException("Recovery SMTP security must be StartTls or SslOnConnect");
            int timeout = Math.Clamp(section.GetInt("PasswordRecoverySMTPTimeoutMilliseconds", 10000), 1000, 30000);
            string path = section.GetString("PasswordRecoveryStorePath", "ContinuumWebUI/recovery.json");
            return new WebUIPasswordRecovery(new PasswordRecoveryStore(path), url, (name, email) =>
            {
                string normalized = name.Replace('.', ' ');
                int split = normalized.IndexOf(' ');
                string first = split > 0 ? normalized.Substring(0, split) : normalized;
                string last = split > 0 ? normalized.Substring(split + 1).Trim() : "Resident";
                var account = _accounts.GetUserAccount(UUID.Zero, first, last);
                if (account == null || !String.Equals(account.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase)) return null;
                var identity = RecoveryIdentityFor(account.PrincipalID);
                return identity != null && String.Equals(identity.Email?.Trim(), email, StringComparison.OrdinalIgnoreCase) ? identity : null;
            }, id => RecoveryIdentityFor(new UUID(id.ToString())),
            (id, newPassword) => _authenticationForPassword.SetPassword(new UUID(id.ToString()), newPassword),
            (recipient, link) =>
            {
                using var message = new MimeMessage();
                message.From.Add(from); message.To.Add(MailboxAddress.Parse(recipient));
                message.Subject = "Your grid account password recovery link";
                message.Body = new TextPart("plain") { Text = "A password reset was requested for your grid account.\n\n"
                    + "Open this link within 30 minutes:\n" + link
                    + "\n\nIf you did not request this, ignore this email. Your password has not changed.\n" };
                using var smtp = new SmtpClient { Timeout = timeout };
                using var deadline = new CancellationTokenSource(timeout);
                smtp.Connect(host, port, security, deadline.Token); // default certificate validation remains enabled
                if (user.Length > 0) smtp.Authenticate(user, password, deadline.Token);
                smtp.Send(message, deadline.Token); smtp.Disconnect(true, deadline.Token);
            }, () => Log.Warn("[CONTINUUM WEBUI]: Password recovery lookup/storage/mail failed; no account details logged"));
        }
        private RecoveryIdentity RecoveryIdentityFor(UUID principal)
        {
            var account = _accounts.GetUserAccount(UUID.Zero, principal);
            if (account == null || account.PrincipalID != principal || account.UserLevel < 0 || !ValidEmail(account.Email)) return null;
            string proof = CredentialProofFor(principal);
            if (proof == null) return null;
            return new RecoveryIdentity { Account = Guid.Parse(principal.ToString()), Email = account.Email, CredentialProof = proof };
        }
        private string CredentialProofFor(UUID principal)
        {
            var auth = _authenticationForPassword?.GetAuthInfo(principal);
            if (auth == null || auth.PrincipalID != principal || String.IsNullOrEmpty(auth.PasswordHash)
                || auth.PasswordSalt == null) return null;
            return PasswordRecoveryStore.Hash(auth.PasswordHash + "|" + auth.PasswordSalt);
        }
        internal string WebSessionStamp(UUID principal, string token)
        {
            if (!RecoveryEnabled) return "";
            string proof = CredentialProofFor(principal);
            if (proof == null) return null;
            return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(proof),
                Encoding.UTF8.GetBytes(principal + "|" + token)));
        }
        internal string WebCredentialVersion(UUID principal) => RecoveryEnabled ? CredentialProofFor(principal) : "";
        internal bool ValidWebSessionStamp(UUID principal, string token, string stamp) => !RecoveryEnabled
            || (stamp?.Length == 64 && PasswordRecoveryStore.Equal(WebSessionStamp(principal, token), stamp));
        private bool RecoverPassword(IReadOnlyDictionary<string, string> form, out string message)
        {
            if (_recovery == null) { message = "Password recovery is not configured; contact your grid administrator"; return true; }
            try
            {
                if (Value(form, "mode") == "reset")
                {
                    bool changed = _recovery.Reset(Value(form, "token"), Value(form, "passwordnew"), Value(form, "passwordconf"));
                    message = changed ? "Password updated" : "The link is invalid/expired or the passwords do not match (8–128 characters). Request a new link if needed";
                }
                else message = _recovery.Request(Value(form, "username"), Value(form, "UserEmail"));
            }
            catch (Exception)
            {
                Log.Warn("[CONTINUUM WEBUI]: Password recovery could not complete; no credentials logged");
                message = "Password recovery could not complete. Request a new link or contact your grid administrator";
            }
            return true;
        }
    }
}
