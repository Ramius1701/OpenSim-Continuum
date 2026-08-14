using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenSim.Continuum.Economy
{
    public sealed class SQLiteEconomyLedger : IEconomyLedger
    {
        private readonly SQLiteEconomyStore m_store;
        public SQLiteEconomyLedger(string connectionString) { m_store = new(connectionString); }
        internal SQLiteEconomyLedger(SQLiteEconomyStore store) { m_store = store; }
        public void EnsureSchema() => m_store.EnsureSchema();
        public void ValidateSchema() => m_store.ValidateSchema();

        public void EnsureAccount(Guid accountID)
        {
            ValidAccount(accountID);
            lock (m_store.SyncRoot)
            using (SQLiteConnection c = m_store.Open())
            using (SQLiteCommand q = c.CreateCommand())
            {
                q.CommandText = "INSERT OR IGNORE INTO continuum_economy_accounts(account_id,balance) VALUES(@id,0)";
                SQLiteEconomyStore.Add(q, "@id", accountID.ToString()); q.ExecuteNonQuery();
            }
        }

        public bool AccountExists(Guid accountID) => Scalar(accountID,
            "SELECT COUNT(*) FROM continuum_economy_accounts WHERE account_id=@id") != 0;
        public long GetBalance(Guid accountID) => Scalar(accountID,
            "SELECT COALESCE((SELECT balance FROM continuum_economy_accounts WHERE account_id=@id),0)");
        public long GetAvailableBalance(Guid accountID) => Scalar(accountID,
            "SELECT COALESCE((SELECT balance FROM continuum_economy_accounts WHERE account_id=@id),0)-" +
            "COALESCE((SELECT SUM(amount) FROM continuum_economy_purchases WHERE buyer_id=@id AND state=1),0)");

        public LedgerTransferResult Transfer(LedgerTransferRequest request)
        {
            if (request == null || request.TransactionID == Guid.Empty || request.SenderID == Guid.Empty ||
                request.ReceiverID == Guid.Empty || request.SenderID == request.ReceiverID || request.Amount <= 0 ||
                (request.Description?.Length ?? 0) > 255)
                return Result(request?.TransactionID ?? Guid.Empty, LedgerResultCode.InvalidRequest, 0, 0, "Invalid transfer request");
            string hash = Hash(String.Join("|", request.SenderID, request.ReceiverID,
                request.Amount.ToString(CultureInfo.InvariantCulture), request.TransactionType,
                request.RegionID, request.ObjectID, request.Description ?? String.Empty));
            lock (m_store.SyncRoot)
            using (SQLiteConnection c = m_store.Open())
            using (SQLiteTransaction t = c.BeginTransaction(IsolationLevel.Serializable))
            {
                LedgerTransferResult prior = Prior(c, t, request.TransactionID, hash);
                if (prior != null) { t.Commit(); return prior; }
                Ensure(c, t, request.SenderID); Ensure(c, t, request.ReceiverID);
                long sender = Balance(c, t, request.SenderID), receiver = Balance(c, t, request.ReceiverID);
                long held = Held(c, t, request.SenderID);
                if (sender - held < request.Amount)
                {
                    Insert(c, t, request, hash, 2, sender, receiver, "Insufficient funds");
                    t.Commit(); return Result(request.TransactionID, LedgerResultCode.InsufficientFunds, sender, receiver, "Insufficient funds");
                }
                try { checked { sender -= request.Amount; receiver += request.Amount; } }
                catch (OverflowException)
                {
                    t.Rollback();
                    return Result(request.TransactionID, LedgerResultCode.InvalidRequest, sender, receiver,
                        "The transfer exceeds the supported balance range");
                }
                SetBalance(c, t, request.SenderID, sender); SetBalance(c, t, request.ReceiverID, receiver);
                Insert(c, t, request, hash, 1, sender, receiver, String.Empty); t.Commit();
                return Result(request.TransactionID, LedgerResultCode.Committed, sender, receiver, "Transfer committed");
            }
        }

        public LedgerAdjustmentResult Adjust(LedgerAdjustmentRequest request)
        {
            if (request == null || request.OperationID == Guid.Empty || request.AccountID == Guid.Empty ||
                request.ActorID == Guid.Empty || request.Amount <= 0 || String.IsNullOrWhiteSpace(request.Reason) ||
                request.Reason.Length > 255 || !Enum.IsDefined(request.Kind) ||
                request.MaximumBalance < 0 || request.DailyCreditLimit < 0 ||
                request.WeeklyCreditLimit < 0 || request.MonthlyCreditLimit < 0)
                return Adjustment(request?.OperationID ?? Guid.Empty, LedgerResultCode.InvalidRequest, 0, "Invalid adjustment request");
            string hash = Hash(String.Join("|", request.AccountID, request.ActorID, request.Amount,
                (int)request.Kind, request.TransactionType, request.Reason, request.MaximumBalance,
                request.DailyCreditLimit, request.WeeklyCreditLimit, request.MonthlyCreditLimit));
            lock (m_store.SyncRoot)
            using (SQLiteConnection c = m_store.Open())
            using (SQLiteTransaction t = c.BeginTransaction(IsolationLevel.Serializable))
            {
                LedgerAdjustmentResult prior = PriorAdjustment(c, t, request.OperationID, hash);
                if (prior != null) { t.Commit(); return prior; }
                Ensure(c, t, request.AccountID); long balance = Balance(c, t, request.AccountID);
                string failure = String.Empty; int status = 1;
                if (request.Kind == LedgerAdjustmentKind.Debit && balance - Held(c,t,request.AccountID) < request.Amount)
                    failure = "Insufficient funds";
                if (request.Kind == LedgerAdjustmentKind.Credit)
                {
                    if (request.MaximumBalance > 0 &&
                        (balance > request.MaximumBalance || request.Amount > request.MaximumBalance - balance))
                        failure = "Maximum balance exceeded";
                    DateTime now=DateTime.UtcNow, day=now.Date, week=now.Date.AddDays(-(((int)now.DayOfWeek+6)%7)), month=new(now.Year,now.Month,1,0,0,0,DateTimeKind.Utc);
                    if (failure.Length==0 && Exceeds(c,t,request.AccountID,request.TransactionType,day,request.DailyCreditLimit,request.Amount)) failure="Daily credit limit exceeded";
                    if (failure.Length==0 && Exceeds(c,t,request.AccountID,request.TransactionType,week,request.WeeklyCreditLimit,request.Amount)) failure="Weekly credit limit exceeded";
                    if (failure.Length==0 && Exceeds(c,t,request.AccountID,request.TransactionType,month,request.MonthlyCreditLimit,request.Amount)) failure="Monthly credit limit exceeded";
                }
                if (failure.Length != 0) status=2;
                else
                {
                    try { checked { balance += request.Kind==LedgerAdjustmentKind.Credit ? request.Amount : -request.Amount; } }
                    catch (OverflowException)
                    {
                        t.Rollback();
                        return Adjustment(request.OperationID, LedgerResultCode.InvalidRequest, balance,
                            "The adjustment exceeds the supported balance range");
                    }
                    SetBalance(c,t,request.AccountID,balance);
                }
                InsertAdjustment(c,t,request,hash,status,balance,failure); t.Commit();
                return Adjustment(request.OperationID,status==1?LedgerResultCode.Committed:LedgerResultCode.InsufficientFunds,balance,status==1?"Adjustment committed":failure);
            }
        }
        public IReadOnlyList<LedgerHistoryEntry> GetHistory(Guid accountID, DateTime? beforeUtc, int limit)
        {
            ValidAccount(accountID);
            if (limit < 1 || limit > 500)
                throw new ArgumentOutOfRangeException(nameof(limit), "History limit must be between 1 and 500");
            List<LedgerHistoryEntry> entries = new(limit * 2);
            lock (m_store.SyncRoot)
            using (SQLiteConnection c = m_store.Open())
            {
                using (SQLiteCommand q = c.CreateCommand())
                {
                    q.CommandText = @"SELECT transaction_id,sender_id,receiver_id,amount,transaction_type,
                            region_id,object_id,description,status,sender_balance,receiver_balance,
                            failure_reason,created_utc
                        FROM continuum_economy_transactions
                        WHERE (sender_id=@account OR receiver_id=@account)
                          AND (@before IS NULL OR created_utc<@before)
                        ORDER BY created_utc DESC,transaction_id DESC LIMIT @limit";
                    SQLiteEconomyStore.Add(q,"@account",accountID.ToString());
                    SQLiteEconomyStore.Add(q,"@before",beforeUtc.HasValue ? Utc(beforeUtc.Value) : DBNull.Value);
                    SQLiteEconomyStore.Add(q,"@limit",limit);
                    using SQLiteDataReader r=q.ExecuteReader();
                    while(r.Read())
                    {
                        Guid sender=Guid.Parse(r.GetString(1)), receiver=Guid.Parse(r.GetString(2));
                        bool credit=receiver==accountID;
                        entries.Add(new LedgerHistoryEntry { TransactionID=Guid.Parse(r.GetString(0)), AccountID=accountID,
                            CounterpartyID=credit?sender:receiver, Amount=r.GetInt64(3), TransactionType=r.GetInt32(4),
                            RegionID=Guid.Parse(r.GetString(5)), ObjectID=Guid.Parse(r.GetString(6)), Description=r.GetString(7),
                            Succeeded=r.GetInt32(8)==1, ResultingBalance=credit?r.GetInt64(10):r.GetInt64(9),
                            FailureReason=r.GetString(11), CreatedUtc=ReadUtc(r,12), IsCredit=credit, IsAdjustment=false });
                    }
                }
                using (SQLiteCommand q = c.CreateCommand())
                {
                    q.CommandText = @"SELECT operation_id,actor_id,amount,adjustment_kind,transaction_type,
                            reason,status,resulting_balance,failure_reason,created_utc
                        FROM continuum_economy_adjustments
                        WHERE account_id=@account AND (@before IS NULL OR created_utc<@before)
                        ORDER BY created_utc DESC,operation_id DESC LIMIT @limit";
                    SQLiteEconomyStore.Add(q,"@account",accountID.ToString());
                    SQLiteEconomyStore.Add(q,"@before",beforeUtc.HasValue ? Utc(beforeUtc.Value) : DBNull.Value);
                    SQLiteEconomyStore.Add(q,"@limit",limit);
                    using SQLiteDataReader r=q.ExecuteReader();
                    while(r.Read())
                    {
                        Guid actor=Guid.Parse(r.GetString(1));
                        entries.Add(new LedgerHistoryEntry { TransactionID=Guid.Parse(r.GetString(0)), AccountID=accountID,
                            CounterpartyID=actor, ActorID=actor, Amount=r.GetInt64(2), IsCredit=r.GetInt32(3)==1,
                            TransactionType=r.GetInt32(4), Description=r.GetString(5), Succeeded=r.GetInt32(6)==1,
                            ResultingBalance=r.GetInt64(7), FailureReason=r.GetString(8), CreatedUtc=ReadUtc(r,9), IsAdjustment=true });
                    }
                }
            }
            entries.Sort((a,b)=> { int byTime=b.CreatedUtc.CompareTo(a.CreatedUtc); return byTime!=0?byTime:b.TransactionID.CompareTo(a.TransactionID); });
            if(entries.Count>limit) entries.RemoveRange(limit,entries.Count-limit);
            return entries;
        }
        public long GetCreditedTotal(Guid accountID, int transactionType, DateTime sinceUtc)
        {
            ValidAccount(accountID); using SQLiteConnection c=m_store.Open(); using SQLiteCommand q=c.CreateCommand();
            q.CommandText="SELECT COALESCE(SUM(amount),0) FROM continuum_economy_adjustments WHERE account_id=@id AND transaction_type=@type AND adjustment_kind=1 AND status=1 AND created_utc>=@since";
            SQLiteEconomyStore.Add(q,"@id",accountID.ToString());SQLiteEconomyStore.Add(q,"@type",transactionType);SQLiteEconomyStore.Add(q,"@since",Utc(sinceUtc));
            return Convert.ToInt64(q.ExecuteScalar(),CultureInfo.InvariantCulture);
        }
        public long CountHistory(Guid accountID, DateTime? startUtc, DateTime? endUtc)
        {
            ValidAccount(accountID);
            using SQLiteConnection c=m_store.Open(); using SQLiteCommand q=c.CreateCommand();
            q.CommandText=@"SELECT
                (SELECT COUNT(*) FROM continuum_economy_transactions WHERE (sender_id=@account OR receiver_id=@account)
                    AND (@start IS NULL OR created_utc>=@start) AND (@end IS NULL OR created_utc<=@end)) +
                (SELECT COUNT(*) FROM continuum_economy_adjustments WHERE account_id=@account
                    AND (@start IS NULL OR created_utc>=@start) AND (@end IS NULL OR created_utc<=@end))";
            SQLiteEconomyStore.Add(q,"@account",accountID.ToString());
            SQLiteEconomyStore.Add(q,"@start",startUtc.HasValue?Utc(startUtc.Value):DBNull.Value);
            SQLiteEconomyStore.Add(q,"@end",endUtc.HasValue?Utc(endUtc.Value):DBNull.Value);
            return Convert.ToInt64(q.ExecuteScalar(),CultureInfo.InvariantCulture);
        }
        public LedgerHistoryEntry GetOperation(Guid operationID)
        {
            if(operationID==Guid.Empty) throw new ArgumentException("A non-zero operation ID is required",nameof(operationID));
            using SQLiteConnection c=m_store.Open();
            using(SQLiteCommand q=c.CreateCommand())
            {
                q.CommandText=@"SELECT sender_id,receiver_id,amount,transaction_type,region_id,object_id,
                    description,status,sender_balance,failure_reason,created_utc
                    FROM continuum_economy_transactions WHERE transaction_id=@operation";
                SQLiteEconomyStore.Add(q,"@operation",operationID.ToString()); using SQLiteDataReader r=q.ExecuteReader();
                if(r.Read()) { Guid sender=Guid.Parse(r.GetString(0)); return new LedgerHistoryEntry { TransactionID=operationID,
                    AccountID=sender,CounterpartyID=Guid.Parse(r.GetString(1)),Amount=r.GetInt64(2),TransactionType=r.GetInt32(3),
                    RegionID=Guid.Parse(r.GetString(4)),ObjectID=Guid.Parse(r.GetString(5)),Description=r.GetString(6),
                    Succeeded=r.GetInt32(7)==1,ResultingBalance=r.GetInt64(8),FailureReason=r.GetString(9),
                    CreatedUtc=ReadUtc(r,10),IsCredit=false,IsAdjustment=false }; }
            }
            using(SQLiteCommand q=c.CreateCommand())
            {
                q.CommandText=@"SELECT account_id,actor_id,amount,adjustment_kind,transaction_type,reason,
                    status,resulting_balance,failure_reason,created_utc FROM continuum_economy_adjustments WHERE operation_id=@operation";
                SQLiteEconomyStore.Add(q,"@operation",operationID.ToString()); using SQLiteDataReader r=q.ExecuteReader();
                if(!r.Read()) return null; Guid actor=Guid.Parse(r.GetString(1));
                return new LedgerHistoryEntry { TransactionID=operationID,AccountID=Guid.Parse(r.GetString(0)),
                    CounterpartyID=actor,ActorID=actor,Amount=r.GetInt64(2),IsCredit=r.GetInt32(3)==1,
                    TransactionType=r.GetInt32(4),Description=r.GetString(5),Succeeded=r.GetInt32(6)==1,
                    ResultingBalance=r.GetInt64(7),FailureReason=r.GetString(8),CreatedUtc=ReadUtc(r,9),IsAdjustment=true };
            }
        }

        private long Scalar(Guid id, string sql)
        {
            ValidAccount(id); using SQLiteConnection c = m_store.Open(); using SQLiteCommand q = c.CreateCommand();
            q.CommandText = sql; SQLiteEconomyStore.Add(q, "@id", id.ToString());
            return Convert.ToInt64(q.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        private static void ValidAccount(Guid id) { if (id == Guid.Empty) throw new ArgumentException("A non-zero account ID is required"); }
        private static void Ensure(SQLiteConnection c, SQLiteTransaction t, Guid id) { using SQLiteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT OR IGNORE INTO continuum_economy_accounts(account_id,balance) VALUES(@id,0)"; SQLiteEconomyStore.Add(q,"@id",id.ToString()); q.ExecuteNonQuery(); }
        private static long Balance(SQLiteConnection c, SQLiteTransaction t, Guid id) { using SQLiteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="SELECT balance FROM continuum_economy_accounts WHERE account_id=@id"; SQLiteEconomyStore.Add(q,"@id",id.ToString()); return Convert.ToInt64(q.ExecuteScalar(),CultureInfo.InvariantCulture); }
        private static long Held(SQLiteConnection c, SQLiteTransaction t, Guid id) { using SQLiteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="SELECT COALESCE(SUM(amount),0) FROM continuum_economy_purchases WHERE buyer_id=@id AND state=1"; SQLiteEconomyStore.Add(q,"@id",id.ToString()); return Convert.ToInt64(q.ExecuteScalar(),CultureInfo.InvariantCulture); }
        private static void SetBalance(SQLiteConnection c, SQLiteTransaction t, Guid id,long value) { using SQLiteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="UPDATE continuum_economy_accounts SET balance=@balance,updated_utc=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE account_id=@id"; SQLiteEconomyStore.Add(q,"@balance",value); SQLiteEconomyStore.Add(q,"@id",id.ToString()); if(q.ExecuteNonQuery()!=1) throw new InvalidOperationException("Economy account update failed"); }
        private static void Insert(SQLiteConnection c,SQLiteTransaction t,LedgerTransferRequest r,string hash,int status,long sb,long rb,string failure) { using SQLiteCommand q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT INTO continuum_economy_transactions(transaction_id,request_hash,sender_id,receiver_id,amount,transaction_type,region_id,object_id,description,status,sender_balance,receiver_balance,failure_reason) VALUES(@id,@hash,@sender,@receiver,@amount,@type,@region,@object,@description,@status,@sb,@rb,@failure)"; object[] v={r.TransactionID.ToString(),hash,r.SenderID.ToString(),r.ReceiverID.ToString(),r.Amount,r.TransactionType,r.RegionID.ToString(),r.ObjectID.ToString(),r.Description??String.Empty,status,sb,rb,failure}; string[] n={"@id","@hash","@sender","@receiver","@amount","@type","@region","@object","@description","@status","@sb","@rb","@failure"}; for(int i=0;i<n.Length;i++)SQLiteEconomyStore.Add(q,n[i],v[i]); q.ExecuteNonQuery(); }
        private static LedgerTransferResult Prior(SQLiteConnection c,SQLiteTransaction t,Guid id,string hash) { using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT request_hash,status,sender_balance,receiver_balance,failure_reason FROM continuum_economy_transactions WHERE transaction_id=@id";SQLiteEconomyStore.Add(q,"@id",id.ToString());using SQLiteDataReader r=q.ExecuteReader();if(!r.Read())return null;if(!String.Equals(r.GetString(0),hash,StringComparison.Ordinal))return Result(id,LedgerResultCode.TransactionConflict,0,0,"Transaction ID conflict");int s=r.GetInt32(1);return Result(id,s==1?LedgerResultCode.Replayed:LedgerResultCode.InsufficientFunds,r.GetInt64(2),r.GetInt64(3),s==1?"Transfer already committed":r.GetString(4)); }
        private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        private static LedgerTransferResult Result(Guid id,LedgerResultCode code,long sb,long rb,string message)=>new(){TransactionID=id,Code=code,SenderBalance=sb,ReceiverBalance=rb,Message=message};
        private static LedgerAdjustmentResult Adjustment(Guid id,LedgerResultCode code,long balance,string message)=>new(){OperationID=id,Code=code,Balance=balance,Message=message};
        private static string Utc(DateTime value)=>value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",CultureInfo.InvariantCulture);
        private static DateTime ReadUtc(SQLiteDataReader reader,int ordinal) => DateTime.SpecifyKind(
            Convert.ToDateTime(reader.GetValue(ordinal),CultureInfo.InvariantCulture),DateTimeKind.Utc);
        private static bool Exceeds(SQLiteConnection c,SQLiteTransaction t,Guid id,int type,DateTime since,long limit,long amount){if(limit<=0)return false;using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT COALESCE(SUM(amount),0) FROM continuum_economy_adjustments WHERE account_id=@id AND transaction_type=@type AND adjustment_kind=1 AND status=1 AND created_utc>=@since";SQLiteEconomyStore.Add(q,"@id",id.ToString());SQLiteEconomyStore.Add(q,"@type",type);SQLiteEconomyStore.Add(q,"@since",Utc(since));return Convert.ToInt64(q.ExecuteScalar(),CultureInfo.InvariantCulture)+amount>limit;}
        private static void InsertAdjustment(SQLiteConnection c,SQLiteTransaction t,LedgerAdjustmentRequest r,string hash,int status,long balance,string failure){using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT INTO continuum_economy_operations(operation_id,request_hash,operation_kind) VALUES(@id,@hash,2); INSERT INTO continuum_economy_adjustments(operation_id,request_hash,account_id,actor_id,amount,adjustment_kind,transaction_type,reason,status,resulting_balance,failure_reason) VALUES(@id,@hash,@account,@actor,@amount,@kind,@type,@reason,@status,@balance,@failure)";object[]v={r.OperationID.ToString(),hash,r.AccountID.ToString(),r.ActorID.ToString(),r.Amount,(int)r.Kind,r.TransactionType,r.Reason,status,balance,failure};string[]n={"@id","@hash","@account","@actor","@amount","@kind","@type","@reason","@status","@balance","@failure"};for(int i=0;i<n.Length;i++)SQLiteEconomyStore.Add(q,n[i],v[i]);q.ExecuteNonQuery();}
        private static LedgerAdjustmentResult PriorAdjustment(SQLiteConnection c,SQLiteTransaction t,Guid id,string hash){using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT request_hash,status,resulting_balance,failure_reason FROM continuum_economy_adjustments WHERE operation_id=@id";SQLiteEconomyStore.Add(q,"@id",id.ToString());using SQLiteDataReader r=q.ExecuteReader();if(!r.Read())return null;if(!String.Equals(r.GetString(0),hash,StringComparison.Ordinal))return Adjustment(id,LedgerResultCode.TransactionConflict,0,"Operation ID conflict");int s=r.GetInt32(1);return Adjustment(id,s==1?LedgerResultCode.Replayed:LedgerResultCode.InsufficientFunds,r.GetInt64(2),s==1?"Adjustment already committed":r.GetString(3));}
    }
}
