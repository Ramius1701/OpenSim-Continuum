using System;
using System.Data;
using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text;

namespace OpenSim.Continuum.Economy
{
    public sealed class SQLiteEconomyAccountService : IEconomyAccountService
    {
        private readonly SQLiteEconomyStore m_store;
        public SQLiteEconomyAccountService(string connectionString) { m_store=new(connectionString); }
        internal SQLiteEconomyAccountService(SQLiteEconomyStore store) { m_store=store; }

        public LedgerResultCode Register(LedgerAccountRegistrationRequest request,out string message)
        {
            if(request==null||request.OperationID==Guid.Empty||request.AccountID==Guid.Empty||request.ActorID==Guid.Empty||
                (request.AccountType!=LedgerAccountType.Group&&request.AccountType!=LedgerAccountType.System)||
                String.IsNullOrWhiteSpace(request.DisplayName)||request.DisplayName.Length>255)
            { message="Valid operation, account, actor, non-resident type and display name are required"; return LedgerResultCode.InvalidRequest; }
            string hash=Fingerprint(request);
            lock(m_store.SyncRoot) using(SQLiteConnection c=m_store.Open()) using(SQLiteTransaction t=c.BeginTransaction(IsolationLevel.Serializable))
            {
                using(SQLiteCommand prior=c.CreateCommand())
                {
                    prior.Transaction=t;prior.CommandText="SELECT request_hash FROM continuum_economy_account_registrations WHERE operation_id=@id";
                    SQLiteEconomyStore.Add(prior,"@id",request.OperationID.ToString());object value=prior.ExecuteScalar();
                    if(value!=null){t.Commit();if(!String.Equals(Convert.ToString(value),hash,StringComparison.Ordinal)){message="The operation ID is already associated with different registration data";return LedgerResultCode.TransactionConflict;}message="Economy account already registered";return LedgerResultCode.Replayed;}
                }
                using(SQLiteCommand operation=c.CreateCommand())
                {
                    operation.Transaction=t;operation.CommandText="SELECT 1 FROM continuum_economy_operations WHERE operation_id=@id";
                    SQLiteEconomyStore.Add(operation,"@id",request.OperationID.ToString());
                    if(operation.ExecuteScalar()!=null){t.Commit();message="The operation ID is already associated with another economy operation";return LedgerResultCode.TransactionConflict;}
                }
                int? existing=null;using(SQLiteCommand q=c.CreateCommand()){q.Transaction=t;q.CommandText="SELECT account_type FROM continuum_economy_accounts WHERE account_id=@id";SQLiteEconomyStore.Add(q,"@id",request.AccountID.ToString());object value=q.ExecuteScalar();if(value!=null)existing=Convert.ToInt32(value);}
                int type=(int)request.AccountType;if(existing.HasValue&&existing.Value!=type){t.Rollback();message="The UUID already belongs to a different economy account class";return LedgerResultCode.TransactionConflict;}
                using(SQLiteCommand q=c.CreateCommand())
                {
                    q.Transaction=t;q.CommandText=@"INSERT OR IGNORE INTO continuum_economy_accounts(account_id,balance,account_type)VALUES(@account,0,@type);
                        INSERT INTO continuum_economy_operations(operation_id,request_hash,operation_kind)VALUES(@operation,@hash,4);
                        INSERT INTO continuum_economy_account_registrations(operation_id,request_hash,account_id,actor_id,account_type,display_name)
                        VALUES(@operation,@hash,@account,@actor,@type,@name)";
                    SQLiteEconomyStore.Add(q,"@account",request.AccountID.ToString());SQLiteEconomyStore.Add(q,"@type",type);
                    SQLiteEconomyStore.Add(q,"@operation",request.OperationID.ToString());SQLiteEconomyStore.Add(q,"@hash",hash);
                    SQLiteEconomyStore.Add(q,"@actor",request.ActorID.ToString());SQLiteEconomyStore.Add(q,"@name",request.DisplayName.Trim());q.ExecuteNonQuery();
                }
                t.Commit();message="Economy account registered";return LedgerResultCode.Committed;
            }
        }
        private static string Fingerprint(LedgerAccountRegistrationRequest r)=>Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(String.Join("|",r.AccountID,r.ActorID,(int)r.AccountType,r.DisplayName.Trim())))).ToLowerInvariant();
    }
}
