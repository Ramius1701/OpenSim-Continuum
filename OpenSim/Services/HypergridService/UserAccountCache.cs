using System;
using System.Collections.Generic;
using System.Reflection;

using log4net;
using OpenMetaverse;

using OpenSim.Services.Interfaces;

namespace OpenSim.Services.HypergridService
{
    public class UserAccountCache : IUserAccountService
    {
        private const double ACCOUNT_CACHE_EXPIRATION_SECONDS = 1800.0;
        private const double NEGATIVE_CACHE_EXPIRATION_SECONDS = 60.0;

//        private static readonly ILog m_log =
//                LogManager.GetLogger(
//                MethodBase.GetCurrentMethod().DeclaringType);

        private ExpiringCache<UUID, UserAccount> m_UUIDCache;

        private IUserAccountService m_UserAccountService;

        private static UserAccountCache m_Singleton;

        public static UserAccountCache CreateUserAccountCache(IUserAccountService u)
        {
            if (m_Singleton == null)
                m_Singleton = new UserAccountCache(u);

            return m_Singleton;
        }

        private UserAccountCache(IUserAccountService u)
        {
            m_UUIDCache = new ExpiringCache<UUID, UserAccount>();
            m_UserAccountService = u;
        }

        public void Cache(UUID userID, UserAccount account)
        {
            // Mutable identity data must not remain stale for more than the
            // simulator account cache. Keep negative results much shorter so a
            // newly created/repaired account becomes visible promptly.
            double expiration = account == null
                ? NEGATIVE_CACHE_EXPIRATION_SECONDS
                : ACCOUNT_CACHE_EXPIRATION_SECONDS;
            m_UUIDCache.AddOrUpdate(userID, account, expiration);

            //m_log.DebugFormat("[USER CACHE]: cached user {0}", userID);
        }

        public UserAccount Get(UUID userID, out bool inCache)
        {
            UserAccount account = null;
            inCache = false;
            if (m_UUIDCache.TryGetValue(userID, out account))
            {
                //m_log.DebugFormat("[USER CACHE]: Account {0} {1} found in cache", account.FirstName, account.LastName);
                inCache = true;
                return account;
            }

            return null;
        }

        public UserAccount GetUser(string id)
        {
            UUID uuid = UUID.Zero;
            UUID.TryParse(id, out uuid);
            bool inCache = false;
            UserAccount account = Get(uuid, out inCache);
            if (!inCache)
            {
                account = m_UserAccountService.GetUserAccount(UUID.Zero, uuid);
                Cache(uuid, account);
            }

            return account;
        }

        #region IUserAccountService
        public UserAccount GetUserAccount(UUID scopeID, UUID userID)
        {
            return GetUser(userID.ToString());
        }

        public UserAccount GetUserAccount(UUID scopeID, string FirstName, string LastName)
        {
            return null;
        }

        public UserAccount GetUserAccount(UUID scopeID, string Email)
        {
            return null;
        }

        public List<UserAccount> GetUserAccountsWhere(UUID scopeID, string query)
        {
            return null;
        }

        public List<UserAccount> GetUserAccounts(UUID scopeID, string query)
        {
            return null;
        }

        public List<UserAccount> GetUserAccounts(UUID scopeID, List<string> IDs)
        {
            return null;
        }

        public void InvalidateCache(UUID userID)
        {
            m_UUIDCache.Remove(userID);
        }

        public bool StoreUserAccount(UserAccount data)
        {
            return false;
        }

        public bool SetDisplayName(UUID agentID, string displayName)
        {
            return false;
        }
        #endregion

    }
}
