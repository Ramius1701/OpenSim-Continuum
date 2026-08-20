using System;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Data.SQLite
{
    /// <summary>Standalone provider for the official Groups v2 schema.</summary>
    public sealed class SQLiteGroupsData : IGroupsData
    {
        private readonly Handler<GroupData> m_groups;
        private readonly Handler<MembershipData> m_members;
        private readonly Handler<RoleData> m_roles;
        private readonly Handler<RoleMembershipData> m_roleMembers;
        private readonly Handler<PrincipalData> m_principals;
        private readonly Handler<InvitationData> m_invites;
        private readonly Handler<NoticeData> m_notices;
        private readonly Handler<GroupBanData> m_bans;

        public SQLiteGroupsData(string connectionString, string realm)
        {
            m_groups = new Handler<GroupData>(connectionString, realm + "_groups", realm + "_Store");
            m_members = new Handler<MembershipData>(connectionString, realm + "_membership");
            m_roles = new Handler<RoleData>(connectionString, realm + "_roles");
            m_roleMembers = new Handler<RoleMembershipData>(connectionString, realm + "_rolemembership");
            m_principals = new Handler<PrincipalData>(connectionString, realm + "_principals");
            m_invites = new Handler<InvitationData>(connectionString, realm + "_invites");
            m_notices = new Handler<NoticeData>(connectionString, realm + "_notices");
            m_bans = new Handler<GroupBanData>(connectionString, realm + "_bans");
        }

        public bool StoreGroup(GroupData value) => m_groups.Store(value);
        public GroupData RetrieveGroup(UUID id) => First(m_groups.Get("GroupID", id.ToString()));
        public GroupData RetrieveGroup(string name) => First(m_groups.Get("Name", name));
        public GroupData[] RetrieveGroups(string pattern)
        {
            string escaped = (pattern ?? String.Empty).Replace("'", "''");
            return m_groups.Get(String.IsNullOrEmpty(escaped)
                ? "ShowInList=1" : "ShowInList=1 AND Name LIKE '%" + escaped + "%'");
        }
        public bool DeleteGroup(UUID id) => m_groups.Delete("GroupID", id.ToString());
        public int GroupsCount() => m_groups.Get("Location='' ").Length;

        public MembershipData RetrieveMember(UUID group, string principal) => First(m_members.Get(
            new[] { "GroupID", "PrincipalID" }, new[] { group.ToString(), principal }));
        public MembershipData[] RetrieveMembers(UUID group) => m_members.Get("GroupID", group.ToString());
        public MembershipData[] RetrieveMemberships(string principal) => m_members.Get("PrincipalID", principal);
        public bool StoreMember(MembershipData value) => m_members.Store(value);
        public bool DeleteMember(UUID group, string principal) => m_members.Delete(
            new[] { "GroupID", "PrincipalID" }, new[] { group.ToString(), principal });
        public int MemberCount(UUID group) => m_members.Get("GroupID", group.ToString()).Length;

        public bool StoreRole(RoleData value) => m_roles.Store(value);
        public RoleData RetrieveRole(UUID group, UUID role) => First(m_roles.Get(
            new[] { "GroupID", "RoleID" }, new[] { group.ToString(), role.ToString() }));
        public RoleData[] RetrieveRoles(UUID group) => m_roles.Get("GroupID", group.ToString());
        public bool DeleteRole(UUID group, UUID role) => m_roles.Delete(
            new[] { "GroupID", "RoleID" }, new[] { group.ToString(), role.ToString() });
        public int RoleCount(UUID group) => m_roles.Get("GroupID", group.ToString()).Length;

        public RoleMembershipData[] RetrieveRolesMembers(UUID group) =>
            m_roleMembers.Get("GroupID", group.ToString());
        public RoleMembershipData[] RetrieveRoleMembers(UUID group, UUID role) => m_roleMembers.Get(
            new[] { "GroupID", "RoleID" }, new[] { group.ToString(), role.ToString() });
        public RoleMembershipData[] RetrieveMemberRoles(UUID group, string principal) => m_roleMembers.Get(
            new[] { "GroupID", "PrincipalID" }, new[] { group.ToString(), principal });
        public RoleMembershipData RetrieveRoleMember(UUID group, UUID role, string principal) => First(
            m_roleMembers.Get(new[] { "GroupID", "RoleID", "PrincipalID" },
                new[] { group.ToString(), role.ToString(), principal }));
        public int RoleMemberCount(UUID group, UUID role) => m_roleMembers.Get(
            new[] { "GroupID", "RoleID" }, new[] { group.ToString(), role.ToString() }).Length;
        public bool StoreRoleMember(RoleMembershipData value) => m_roleMembers.Store(value);
        public bool DeleteRoleMember(RoleMembershipData value) => m_roleMembers.Delete(
            new[] { "GroupID", "RoleID", "PrincipalID" },
            new[] { value.GroupID.ToString(), value.RoleID.ToString(), value.PrincipalID });
        public bool DeleteMemberAllRoles(UUID group, string principal) => m_roleMembers.Delete(
            new[] { "GroupID", "PrincipalID" }, new[] { group.ToString(), principal });

        public bool StorePrincipal(PrincipalData value) => m_principals.Store(value);
        public PrincipalData RetrievePrincipal(string principal) => First(m_principals.Get("PrincipalID", principal));
        public bool DeletePrincipal(string principal) => m_principals.Delete("PrincipalID", principal);

        public bool StoreInvitation(InvitationData value) => m_invites.Store(value);
        public InvitationData RetrieveInvitation(UUID invite) => First(m_invites.Get("InviteID", invite.ToString()));
        public InvitationData RetrieveInvitation(UUID group, string principal) => First(m_invites.Get(
            new[] { "GroupID", "PrincipalID" }, new[] { group.ToString(), principal }));
        public bool DeleteInvite(UUID invite) => m_invites.Delete("InviteID", invite.ToString());
        public void DeleteOldInvites()
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-14);
            foreach (InvitationData invite in m_invites.Get("1"))
                if (invite.Data != null && invite.Data.TryGetValue("TMStamp", out string value) &&
                    DateTime.TryParse(value, out DateTime created) && created.ToUniversalTime() < cutoff)
                    DeleteInvite(invite.InviteID);
        }

        public bool StoreNotice(NoticeData value) => m_notices.Store(value);
        public NoticeData RetrieveNotice(UUID notice) => First(m_notices.Get("NoticeID", notice.ToString()));
        public NoticeData[] RetrieveNotices(UUID group) => m_notices.Get("GroupID", group.ToString());
        public bool DeleteNotice(UUID notice) => m_notices.Delete("NoticeID", notice.ToString());
        public void DeleteOldNotices()
        {
            int cutoff = Util.UnixTimeSinceEpoch() - 14 * 86400;
            foreach (NoticeData notice in m_notices.Get("TMStamp < " + cutoff))
                DeleteNotice(notice.NoticeID);
        }

        public bool StoreBan(GroupBanData value) => m_bans.Store(value);
        public GroupBanData RetrieveBan(UUID group, string principal) => First(m_bans.Get(
            new[] { "GroupID", "PrincipalID" }, new[] { group.ToString(), principal }));
        public GroupBanData[] RetrieveBans(UUID group) => m_bans.Get("GroupID", group.ToString());
        public bool DeleteBan(UUID group, string principal) => m_bans.Delete(
            new[] { "GroupID", "PrincipalID" }, new[] { group.ToString(), principal });

        public MembershipData RetrievePrincipalGroupMembership(string principal, UUID group) =>
            RetrieveMember(group, principal);
        public MembershipData[] RetrievePrincipalGroupMemberships(string principal) =>
            RetrieveMemberships(principal);

        private static T First<T>(T[] values) where T : class =>
            values != null && values.Length > 0 ? values[0] : null;

        private class Handler<T> : SQLiteGenericTableHandler<T> where T : class, new()
        {
            internal Handler(string connection, string realm, string store = "") :
                base(connection, realm, store) { }
        }

    }
}
