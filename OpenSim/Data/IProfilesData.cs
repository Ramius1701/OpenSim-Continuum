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
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;

namespace OpenSim.Data
{

    public interface IUserPartnershipData
    {
        PartnershipProposal[] GetPartnershipProposals(UUID actor);
        string ChangePartnership(UUID actor, UUID other, UUID proposal, string action, string message);
    }

    public interface IProfilesData
    {
        OSDArray GetClassifiedRecords(UUID creatorId);
        bool UpdateClassifiedRecord(UserClassifiedAdd ad, ref string result);
        bool DeleteClassifiedRecord(UUID recordId);
        OSDArray GetAvatarPicks(UUID avatarId);
        UserProfilePick GetPickInfo(UUID avatarId, UUID pickId);
        bool UpdatePicksRecord(UserProfilePick pick);
        bool DeletePicksRecord(UUID pickId);
        bool GetAvatarNotes(ref UserProfileNotes note);
        bool UpdateAvatarNotes(ref UserProfileNotes note, ref string result);
        bool GetAvatarProperties(ref UserProfileProperties props, ref string result);
        bool UpdateAvatarProperties(ref UserProfileProperties props, ref string result);
        bool UpdateAvatarInterests(UserProfileProperties up, ref string result);
        bool GetClassifiedInfo(ref UserClassifiedAdd ad, ref string result);
        bool UpdateUserPreferences(ref UserPreferences pref, ref string result);
        bool GetUserPreferences(ref UserPreferences pref, ref string result);
        bool GetUserAppData(ref UserAppData props, ref string result);
        bool SetUserAppData(UserAppData props, ref string result);
        OSDArray GetUserImageAssets(UUID avatarId);
    }
    public static class PartnershipCommands
    {
        public static PartnershipProposal[] Read(System.Data.Common.DbConnection connection, UUID actor)
        {
            if (actor == UUID.Zero) throw new ArgumentException("A local account is required");
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT proposalid,sender,recipient,message,expires FROM continuum_partnership_proposals "
                + "WHERE (sender=@actor OR recipient=@actor) AND expires>@now ORDER BY expires,proposalid LIMIT 22";
            Parameter(command, "@actor", actor.ToString()); Parameter(command, "@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            using var reader = command.ExecuteReader(); var rows = new System.Collections.Generic.List<PartnershipProposal>();
            while (reader.Read()) rows.Add(new PartnershipProposal { ID = new UUID(Convert.ToString(reader[0])), Sender = new UUID(Convert.ToString(reader[1])),
                Recipient = new UUID(Convert.ToString(reader[2])), Message = Convert.ToString(reader[3]), Expires = Convert.ToInt64(reader[4]) });
            return rows.ToArray();
        }
        private static void Parameter(System.Data.Common.DbCommand command, string name, object value)
        { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
        public static string Change(System.Data.Common.DbConnection connection, string dialect, UUID actor, UUID other, UUID proposal, string action, string message)
        {
            if (actor == UUID.Zero || other == UUID.Zero || actor == other || proposal == UUID.Zero)
                throw new ArgumentException("Choose a different local resident and a valid proposal");
            if (message == null || message.Length > 1000 || System.Linq.Enumerable.Any(message, c => Char.IsControl(c) && c != '\n' && c != '\r'))
                throw new ArgumentException("Proposal messages are limited to 1000 characters");
            if (action is not ("propose" or "accept" or "decline" or "cancel" or "dissolve")) throw new ArgumentException("Unknown partnership operation");
            bool postgres = dialect == "postgres";
            if (!postgres && dialect != "mysql" && dialect != "sqlite") throw new ArgumentException("Unsupported profile provider");
            string partnerColumn = postgres ? "\"profilePartner\"" : "profilePartner";
            object ProfileID(UUID id) => postgres ? (object)Guid.Parse(id.ToString()) : id.ToString();
            using var transaction = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            System.Data.Common.DbCommand Command(string query)
            { var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = query; return command; }
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // SQLite acquires its write lock before reads; other providers lock the two profiles in UUID order.
            using (var expired = Command("DELETE FROM continuum_partnership_proposals WHERE expires<=@now AND (sender=@actor OR recipient=@actor OR sender=@other OR recipient=@other)"))
            { Parameter(expired, "@now", now); Parameter(expired, "@actor", actor.ToString()); Parameter(expired, "@other", other.ToString()); expired.ExecuteNonQuery(); }
            UUID Partner(UUID id)
            {
                using var query = Command("SELECT " + partnerColumn + " FROM userprofile WHERE useruuid=@id" + (dialect == "sqlite" ? "" : " FOR UPDATE"));
                Parameter(query, "@id", ProfileID(id)); object result = query.ExecuteScalar();
                if (result == null || !UUID.TryParse(result.ToString(), out UUID value)) throw new InvalidOperationException("Both residents need native profiles");
                return value;
            }
            UUID actorPartner, otherPartner;
            if (String.CompareOrdinal(actor.ToString(), other.ToString()) < 0) { actorPartner = Partner(actor); otherPartner = Partner(other); }
            else { otherPartner = Partner(other); actorPartner = Partner(actor); }
            int SetPartner(UUID id, UUID expected, UUID updated)
            {
                using var command = Command("UPDATE userprofile SET " + partnerColumn + "=@updated WHERE useruuid=@id AND " + partnerColumn + "=@expected");
                Parameter(command, "@updated", ProfileID(updated)); Parameter(command, "@id", ProfileID(id)); Parameter(command, "@expected", ProfileID(expected)); return command.ExecuteNonQuery();
            }
            if (action == "dissolve")
            {
                if (actorPartner != other || otherPartner != actor) throw new InvalidOperationException("Partnership changed or is inconsistent. Reload and contact the grid operator if needed");
                if (SetPartner(actor, other, UUID.Zero) != 1 || SetPartner(other, actor, UUID.Zero) != 1) throw new InvalidOperationException("Partnership changed. Reload before continuing");
                transaction.Commit(); return "Partnership dissolved";
            }
            if (action == "propose")
            {
                if (actorPartner != UUID.Zero || otherPartner != UUID.Zero) throw new InvalidOperationException("Both residents must be unpartnered");
                using (var existing = Command("SELECT proposalid,recipient,message FROM continuum_partnership_proposals WHERE sender=@actor"))
                {
                    Parameter(existing, "@actor", actor.ToString());
                    using var reader = existing.ExecuteReader();
                    if (reader.Read())
                    {
                        bool same = reader.GetString(0) == proposal.ToString() && reader.GetString(1) == other.ToString() && reader.GetString(2) == message;
                        reader.Close();
                        if (same) { transaction.Commit(); return "Partnership proposal saved"; }
                        throw new InvalidOperationException("Withdraw your existing proposal before sending another");
                    }
                }
                using (var count = Command("SELECT COUNT(*) FROM continuum_partnership_proposals WHERE recipient=@other AND expires>@now"))
                { Parameter(count, "@other", other.ToString()); Parameter(count, "@now", now); if (Convert.ToInt32(count.ExecuteScalar()) >= 20) throw new InvalidOperationException("This resident cannot receive more proposals right now"); }
                using (var insert = Command("INSERT INTO continuum_partnership_proposals(proposalid,sender,recipient,message,expires) VALUES(@id,@sender,@recipient,@message,@expires)"))
                {
                    Parameter(insert, "@id", proposal.ToString()); Parameter(insert, "@sender", actor.ToString()); Parameter(insert, "@recipient", other.ToString());
                    Parameter(insert, "@message", message); Parameter(insert, "@expires", now + 7 * 86400); insert.ExecuteNonQuery();
                }
                transaction.Commit(); return "Partnership proposal saved";
            }
            UUID sender = action == "cancel" ? actor : other, recipient = action == "cancel" ? other : actor;
            using (var query = Command("SELECT COUNT(*) FROM continuum_partnership_proposals WHERE proposalid=@id AND sender=@sender AND recipient=@recipient AND expires>@now"))
            {
                Parameter(query, "@id", proposal.ToString()); Parameter(query, "@sender", sender.ToString()); Parameter(query, "@recipient", recipient.ToString()); Parameter(query, "@now", now);
                if (Convert.ToInt32(query.ExecuteScalar()) != 1) throw new InvalidOperationException("Proposal is unavailable, expired or changed. Reload before continuing");
            }
            if (action == "accept")
            {
                if (actorPartner != UUID.Zero || otherPartner != UUID.Zero) throw new InvalidOperationException("Both residents must be unpartnered");
                if (SetPartner(actor, UUID.Zero, other) != 1 || SetPartner(other, UUID.Zero, actor) != 1) throw new InvalidOperationException("Partnership changed. Reload before continuing");
                using var remove = Command("DELETE FROM continuum_partnership_proposals WHERE sender=@actor OR recipient=@actor OR sender=@other OR recipient=@other");
                Parameter(remove, "@actor", actor.ToString()); Parameter(remove, "@other", other.ToString()); remove.ExecuteNonQuery();
                transaction.Commit(); return "Partnership accepted";
            }
            using (var remove = Command("DELETE FROM continuum_partnership_proposals WHERE proposalid=@id AND sender=@sender AND recipient=@recipient"))
            { Parameter(remove, "@id", proposal.ToString()); Parameter(remove, "@sender", sender.ToString()); Parameter(remove, "@recipient", recipient.ToString()); remove.ExecuteNonQuery(); }
            transaction.Commit(); return action == "cancel" ? "Partnership proposal withdrawn" : "Partnership proposal declined";
        }
    }

}

