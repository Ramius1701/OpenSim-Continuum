using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenSim.Continuum.Economy
{
    public sealed class SQLiteEconomyPurchaseService : IEconomyPurchaseService
    {
        private readonly SQLiteEconomyStore m_store;
        public SQLiteEconomyPurchaseService(string connectionString) { m_store=new(connectionString); }
        internal SQLiteEconomyPurchaseService(SQLiteEconomyStore store) { m_store=store; }

        public LedgerPurchaseResult Authorize(LedgerPurchaseRequest r)
        {
            if(r==null||r.PurchaseID==Guid.Empty||r.BuyerID==Guid.Empty||r.SellerID==Guid.Empty||r.BuyerID==r.SellerID||r.Amount<=0||(r.Description?.Length??0)>255)
                return Result(r?.PurchaseID??Guid.Empty,LedgerResultCode.InvalidRequest,0,0,0,"Invalid purchase request");
            string hash=Hash(String.Join("|",r.BuyerID,r.SellerID,r.Amount,r.TransactionType,r.RegionID,r.ObjectID,r.Description??String.Empty));
            lock(m_store.SyncRoot) using(SQLiteConnection c=m_store.Open()) using(SQLiteTransaction t=c.BeginTransaction(IsolationLevel.Serializable))
            {
                Row prior=Read(c,t,r.PurchaseID); if(prior!=null){t.Commit();return Prior(prior,hash);}
                Ensure(c,t,r.BuyerID);Ensure(c,t,r.SellerID);long buyer=Balance(c,t,r.BuyerID),seller=Balance(c,t,r.SellerID),held=Held(c,t,r.BuyerID);
                int state=buyer-held<r.Amount?(int)LedgerPurchaseState.InsufficientFunds:(int)LedgerPurchaseState.Authorized;
                string failure=state==(int)LedgerPurchaseState.Authorized?String.Empty:"Insufficient funds";
                Insert(c,t,r,hash,state,buyer,seller,failure);t.Commit();
                return Result(r.PurchaseID,state==(int)LedgerPurchaseState.Authorized?LedgerResultCode.Committed:LedgerResultCode.InsufficientFunds,state,buyer,buyer-held-(state==1?r.Amount:0),seller,failure.Length==0?"Purchase authorized":failure);
            }
        }
        public LedgerPurchaseResult Capture(Guid id,Guid buyer)=>Complete(id,buyer,true);
        public LedgerPurchaseResult Cancel(Guid id,Guid buyer)=>Complete(id,buyer,false);
        private LedgerPurchaseResult Complete(Guid id,Guid expected,bool capture)
        {
            if(id==Guid.Empty||expected==Guid.Empty)return Result(id,LedgerResultCode.InvalidRequest,0,0,0,"Invalid purchase completion request");
            lock(m_store.SyncRoot)using(SQLiteConnection c=m_store.Open())using(SQLiteTransaction t=c.BeginTransaction(IsolationLevel.Serializable))
            {
                Row r=Read(c,t,id);if(r==null){t.Commit();return Result(id,LedgerResultCode.InvalidRequest,0,0,0,"Purchase not found");}
                if(r.Buyer!=expected){t.Commit();return Result(id,LedgerResultCode.InvalidRequest,r.State,r.BuyerBalance,0,r.SellerBalance,"Purchase buyer mismatch");}
                LedgerPurchaseState target=capture?LedgerPurchaseState.Captured:LedgerPurchaseState.Cancelled;
                if(r.State==(int)target){t.Commit();return Result(id,LedgerResultCode.Replayed,r.State,r.BuyerBalance,r.BuyerBalance,r.SellerBalance,"Purchase already completed");}
                if(r.State!=(int)LedgerPurchaseState.Authorized){t.Commit();return Result(id,LedgerResultCode.InvalidRequest,r.State,r.BuyerBalance,0,r.SellerBalance,"Purchase is not authorized");}
                long bb=Balance(c,t,r.Buyer),sb=Balance(c,t,r.Seller);
                if(capture){if(bb<r.Amount){t.Rollback();return Result(id,LedgerResultCode.TransactionConflict,r.State,bb,0,sb,"Authorized purchase funds are no longer present");}long priorBuyer=bb,priorSeller=sb;try{checked{bb-=r.Amount;sb+=r.Amount;}}catch(OverflowException){t.Rollback();return Result(id,LedgerResultCode.InvalidRequest,r.State,priorBuyer,0,priorSeller,"The purchase exceeds the supported balance range");}Set(c,t,r.Buyer,bb);Set(c,t,r.Seller,sb);InsertTransfer(c,t,r,bb,sb);}
                using(SQLiteCommand q=c.CreateCommand()){q.Transaction=t;q.CommandText="UPDATE continuum_economy_purchases SET state=@state,buyer_balance=@bb,seller_balance=@sb,completed_utc=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE purchase_id=@id AND state=1";Add(q,"@state",(int)target);Add(q,"@bb",bb);Add(q,"@sb",sb);Add(q,"@id",id.ToString());if(q.ExecuteNonQuery()!=1)throw new InvalidOperationException("Purchase state changed concurrently");}
                t.Commit();return Result(id,LedgerResultCode.Committed,(int)target,bb,bb,sb,capture?"Purchase captured":"Purchase cancelled");
            }
        }
        public IReadOnlyList<LedgerPendingPurchase> GetPending(DateTime before,int limit)
        {
            limit=Math.Clamp(limit,1,500);List<LedgerPendingPurchase> list=new();using SQLiteConnection c=m_store.Open();using SQLiteCommand q=c.CreateCommand();q.CommandText="SELECT purchase_id,buyer_id,seller_id,amount,transaction_type,region_id,object_id,description,created_utc FROM continuum_economy_purchases WHERE state=1 AND created_utc<@before ORDER BY created_utc LIMIT @limit";Add(q,"@before",Utc(before));Add(q,"@limit",limit);using SQLiteDataReader r=q.ExecuteReader();while(r.Read())list.Add(new(){PurchaseID=Guid.Parse(r.GetString(0)),BuyerID=Guid.Parse(r.GetString(1)),SellerID=Guid.Parse(r.GetString(2)),Amount=r.GetInt64(3),TransactionType=r.GetInt32(4),RegionID=Guid.Parse(r.GetString(5)),ObjectID=Guid.Parse(r.GetString(6)),Description=r.GetString(7),CreatedUtc=DateTime.Parse(r.GetString(8),CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal)});return list;
        }
        private sealed class Row{public Guid ID,Buyer,Seller,Region,Object;public string Hash,Description;public long Amount,BuyerBalance,SellerBalance;public int Type,State;}
        private static Row Read(SQLiteConnection c,SQLiteTransaction t,Guid id){using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT request_hash,buyer_id,seller_id,amount,transaction_type,region_id,object_id,description,state,buyer_balance,seller_balance FROM continuum_economy_purchases WHERE purchase_id=@id";Add(q,"@id",id.ToString());using SQLiteDataReader r=q.ExecuteReader();return r.Read()?new(){ID=id,Hash=r.GetString(0),Buyer=Guid.Parse(r.GetString(1)),Seller=Guid.Parse(r.GetString(2)),Amount=r.GetInt64(3),Type=r.GetInt32(4),Region=Guid.Parse(r.GetString(5)),Object=Guid.Parse(r.GetString(6)),Description=r.GetString(7),State=r.GetInt32(8),BuyerBalance=r.GetInt64(9),SellerBalance=r.GetInt64(10)}:null;}
        private static LedgerPurchaseResult Prior(Row r,string hash)=>!String.Equals(r.Hash,hash,StringComparison.Ordinal)?Result(r.ID,LedgerResultCode.TransactionConflict,r.State,0,0,"Purchase ID conflict"):Result(r.ID,LedgerResultCode.Replayed,r.State,r.BuyerBalance,r.BuyerBalance,r.SellerBalance,"Purchase already recorded");
        private static void Ensure(SQLiteConnection c,SQLiteTransaction t,Guid id){using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT OR IGNORE INTO continuum_economy_accounts(account_id,balance)VALUES(@id,0)";Add(q,"@id",id.ToString());q.ExecuteNonQuery();}
        private static long Balance(SQLiteConnection c,SQLiteTransaction t,Guid id){using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT balance FROM continuum_economy_accounts WHERE account_id=@id";Add(q,"@id",id.ToString());return Convert.ToInt64(q.ExecuteScalar(),CultureInfo.InvariantCulture);}
        private static long Held(SQLiteConnection c,SQLiteTransaction t,Guid id){using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT COALESCE(SUM(amount),0)FROM continuum_economy_purchases WHERE buyer_id=@id AND state=1";Add(q,"@id",id.ToString());return Convert.ToInt64(q.ExecuteScalar(),CultureInfo.InvariantCulture);}
        private static void Set(SQLiteConnection c,SQLiteTransaction t,Guid id,long b){using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="UPDATE continuum_economy_accounts SET balance=@b,updated_utc=strftime('%Y-%m-%dT%H:%M:%fZ','now')WHERE account_id=@id";Add(q,"@b",b);Add(q,"@id",id.ToString());if(q.ExecuteNonQuery()!=1)throw new InvalidOperationException("Account update failed");}
        private static void Insert(SQLiteConnection c,SQLiteTransaction t,LedgerPurchaseRequest r,string h,int s,long bb,long sb,string f){using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT INTO continuum_economy_operations(operation_id,request_hash,operation_kind)VALUES(@id,@h,3);INSERT INTO continuum_economy_purchases(purchase_id,request_hash,buyer_id,seller_id,amount,transaction_type,region_id,object_id,description,state,buyer_balance,seller_balance,failure_reason)VALUES(@id,@h,@buyer,@seller,@amount,@type,@region,@object,@description,@state,@bb,@sb,@failure)";object[]v={r.PurchaseID.ToString(),h,r.BuyerID.ToString(),r.SellerID.ToString(),r.Amount,r.TransactionType,r.RegionID.ToString(),r.ObjectID.ToString(),r.Description??String.Empty,s,bb,sb,f};string[]n={"@id","@h","@buyer","@seller","@amount","@type","@region","@object","@description","@state","@bb","@sb","@failure"};for(int i=0;i<n.Length;i++)Add(q,n[i],v[i]);q.ExecuteNonQuery();}
        private static void InsertTransfer(SQLiteConnection c,SQLiteTransaction t,Row r,long bb,long sb){using SQLiteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT INTO continuum_economy_transactions(transaction_id,request_hash,sender_id,receiver_id,amount,transaction_type,region_id,object_id,description,status,sender_balance,receiver_balance,failure_reason)VALUES(@id,@h,@buyer,@seller,@amount,@type,@region,@object,@description,1,@bb,@sb,'')";object[]v={r.ID.ToString(),r.Hash,r.Buyer.ToString(),r.Seller.ToString(),r.Amount,r.Type,r.Region.ToString(),r.Object.ToString(),r.Description,bb,sb};string[]n={"@id","@h","@buyer","@seller","@amount","@type","@region","@object","@description","@bb","@sb"};for(int i=0;i<n.Length;i++)Add(q,n[i],v[i]);q.ExecuteNonQuery();}
        private static void Add(SQLiteCommand q,string n,object v)=>SQLiteEconomyStore.Add(q,n,v);
        private static string Hash(string v)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v))).ToLowerInvariant();private static string Utc(DateTime v)=>v.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",CultureInfo.InvariantCulture);
        private static LedgerPurchaseResult Result(Guid id,LedgerResultCode code,int state,long bb,long avail,long sb,string message)=>new(){PurchaseID=id,Code=code,State=(LedgerPurchaseState)state,BuyerBalance=bb,BuyerAvailableBalance=avail,SellerBalance=sb,Message=message};
        private static LedgerPurchaseResult Result(Guid id,LedgerResultCode code,long bb,long sb,long avail,string message)=>Result(id,code,0,bb,avail,sb,message);
    }
}
