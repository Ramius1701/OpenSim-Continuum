using System;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;

namespace OpenSim.Continuum.Economy
{
    public sealed class MySqlEconomyPurchaseService : IEconomyPurchaseService
    {
        private readonly string m_connectionString;

        public MySqlEconomyPurchaseService(string connectionString)
        {
            if (String.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("An economy database connection string is required", nameof(connectionString));
            m_connectionString = connectionString;
        }

        public LedgerPurchaseResult Authorize(LedgerPurchaseRequest request)
        {
            LedgerPurchaseResult invalid = Validate(request);
            if (invalid != null) return invalid;
            string fingerprint = Fingerprint(request);
            try
            {
                using MySqlConnection connection = new(m_connectionString);
                connection.Open();
                using MySqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                PurchaseRow prior = Read(connection, transaction, request.PurchaseID, true);
                if (prior != null) return Finish(transaction, PriorResult(prior, fingerprint));
                ReserveOperation(connection, transaction, request.PurchaseID, fingerprint);
                EnsureAccount(connection, transaction, request.BuyerID);
                EnsureAccount(connection, transaction, request.SellerID);
                long buyerBalance = LockBalance(connection, transaction, request.BuyerID);
                long sellerBalance = ReadBalance(connection, transaction, request.SellerID);
                long available = checked(buyerBalance - HeldAmount(connection, transaction, request.BuyerID));
                if (available < request.Amount)
                {
                    Insert(connection, transaction, request, fingerprint, LedgerPurchaseState.InsufficientFunds,
                        buyerBalance, sellerBalance, "Insufficient available funds");
                    transaction.Commit();
                    return Result(request.PurchaseID, LedgerResultCode.InsufficientFunds,
                        LedgerPurchaseState.InsufficientFunds, buyerBalance, available, sellerBalance,
                        "Insufficient available funds");
                }
                Insert(connection, transaction, request, fingerprint, LedgerPurchaseState.Authorized,
                    buyerBalance, sellerBalance, String.Empty);
                transaction.Commit();
                return Result(request.PurchaseID, LedgerResultCode.Committed, LedgerPurchaseState.Authorized,
                    buyerBalance, available - request.Amount, sellerBalance, "Purchase authorized");
            }
            catch (MySqlException e) when (e.Number == 1062)
            {
                using MySqlConnection connection = new(m_connectionString);
                connection.Open();
                PurchaseRow prior = Read(connection, null, request.PurchaseID, false);
                return prior == null ? Conflict(request.PurchaseID) : PriorResult(prior, fingerprint);
            }
        }

        public LedgerPurchaseResult Capture(Guid purchaseID)
        {
            if (purchaseID == Guid.Empty) return Invalid(purchaseID, "A non-zero purchase ID is required");
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            PurchaseRow row = Read(connection, transaction, purchaseID, true);
            if (row == null) return Finish(transaction, Invalid(purchaseID, "Purchase not found"));
            if (row.State == LedgerPurchaseState.Captured) return Finish(transaction, PriorResult(row, row.RequestHash));
            if (row.State != LedgerPurchaseState.Authorized)
                return Finish(transaction, Invalid(purchaseID, "Only an authorized purchase can be captured"));
            Guid first = StringComparer.Ordinal.Compare(row.BuyerID.ToString(), row.SellerID.ToString()) < 0 ? row.BuyerID : row.SellerID;
            Guid second = first == row.BuyerID ? row.SellerID : row.BuyerID;
            long firstBalance = LockBalance(connection, transaction, first);
            long secondBalance = LockBalance(connection, transaction, second);
            long buyerBalance = first == row.BuyerID ? firstBalance : secondBalance;
            long sellerBalance = first == row.SellerID ? firstBalance : secondBalance;
            if (buyerBalance < row.Amount)
            {
                transaction.Rollback();
                return Result(purchaseID, LedgerResultCode.TransactionConflict,
                    LedgerPurchaseState.Authorized, buyerBalance, 0, sellerBalance,
                    "Authorized funds are no longer present; capture refused");
            }
            long newBuyer = checked(buyerBalance - row.Amount);
            long newSeller = checked(sellerBalance + row.Amount);
            UpdateBalance(connection, transaction, row.BuyerID, newBuyer);
            UpdateBalance(connection, transaction, row.SellerID, newSeller);
            InsertCapturedTransfer(connection, transaction, row, newBuyer, newSeller);
            SetState(connection, transaction, purchaseID, LedgerPurchaseState.Captured, newBuyer, newSeller);
            transaction.Commit();
            long available = checked(newBuyer - HeldAmount(connection, null, row.BuyerID));
            return Result(purchaseID, LedgerResultCode.Committed, LedgerPurchaseState.Captured,
                newBuyer, available, newSeller, "Purchase captured");
        }

        public LedgerPurchaseResult Cancel(Guid purchaseID)
        {
            if (purchaseID == Guid.Empty) return Invalid(purchaseID, "A non-zero purchase ID is required");
            using MySqlConnection connection = new(m_connectionString);
            connection.Open();
            using MySqlTransaction transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            PurchaseRow row = Read(connection, transaction, purchaseID, true);
            if (row == null) return Finish(transaction, Invalid(purchaseID, "Purchase not found"));
            if (row.State == LedgerPurchaseState.Cancelled) return Finish(transaction, PriorResult(row, row.RequestHash));
            if (row.State != LedgerPurchaseState.Authorized)
                return Finish(transaction, Invalid(purchaseID, "Only an authorized purchase can be cancelled"));
            SetState(connection, transaction, purchaseID, LedgerPurchaseState.Cancelled, row.BuyerBalance, row.SellerBalance);
            long buyerBalance = ReadBalance(connection, transaction, row.BuyerID);
            long sellerBalance = ReadBalance(connection, transaction, row.SellerID);
            long available = checked(buyerBalance - HeldAmount(connection, transaction, row.BuyerID));
            transaction.Commit();
            return Result(purchaseID, LedgerResultCode.Committed, LedgerPurchaseState.Cancelled,
                buyerBalance, available, sellerBalance, "Purchase cancelled");
        }

        private static LedgerPurchaseResult Validate(LedgerPurchaseRequest request)
        {
            if (request == null) return Invalid(Guid.Empty, "A purchase request is required");
            if (request.PurchaseID == Guid.Empty || request.BuyerID == Guid.Empty || request.SellerID == Guid.Empty)
                return Invalid(request.PurchaseID, "Purchase, buyer and seller IDs must be non-zero");
            if (request.BuyerID == request.SellerID || request.Amount <= 0)
                return Invalid(request.PurchaseID, "A positive purchase between different accounts is required");
            if ((request.Description ?? String.Empty).Length > 255)
                return Invalid(request.PurchaseID, "The purchase description exceeds 255 characters");
            return null;
        }

        private static PurchaseRow Read(MySqlConnection connection, MySqlTransaction transaction, Guid purchaseID, bool forUpdate)
        {
            using MySqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"SELECT request_hash,buyer_id,seller_id,amount,transaction_type,region_id,object_id,
                description,state,buyer_balance,seller_balance,failure_reason FROM continuum_economy_purchases
                WHERE purchase_id=?purchase" + (forUpdate ? " FOR UPDATE" : String.Empty);
            command.Parameters.AddWithValue("?purchase", purchaseID.ToString());
            using MySqlDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new PurchaseRow { ID=purchaseID, RequestHash=reader.GetString(0), BuyerID=Guid.Parse(reader.GetString(1)),
                SellerID=Guid.Parse(reader.GetString(2)), Amount=reader.GetInt64(3), TransactionType=reader.GetInt32(4),
                RegionID=Guid.Parse(reader.GetString(5)), ObjectID=Guid.Parse(reader.GetString(6)), Description=reader.GetString(7),
                State=(LedgerPurchaseState)reader.GetInt32(8), BuyerBalance=reader.GetInt64(9), SellerBalance=reader.GetInt64(10),
                FailureReason=reader.GetString(11) };
        }

        private static void ReserveOperation(MySqlConnection c, MySqlTransaction t, Guid id, string hash)
        { using MySqlCommand cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="INSERT INTO continuum_economy_operations (operation_id,request_hash,operation_kind) VALUES (?id,?hash,3)"; cmd.Parameters.AddWithValue("?id",id.ToString()); cmd.Parameters.AddWithValue("?hash",hash); cmd.ExecuteNonQuery(); }
        private static void EnsureAccount(MySqlConnection c, MySqlTransaction t, Guid id)
        { using MySqlCommand cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="INSERT IGNORE INTO continuum_economy_accounts (account_id,balance) VALUES (?id,0)"; cmd.Parameters.AddWithValue("?id",id.ToString()); cmd.ExecuteNonQuery(); }
        private static long LockBalance(MySqlConnection c, MySqlTransaction t, Guid id)
        { using MySqlCommand cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="SELECT balance FROM continuum_economy_accounts WHERE account_id=?id FOR UPDATE"; cmd.Parameters.AddWithValue("?id",id.ToString()); return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture); }
        private static long ReadBalance(MySqlConnection c, MySqlTransaction t, Guid id)
        { using MySqlCommand cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="SELECT balance FROM continuum_economy_accounts WHERE account_id=?id"; cmd.Parameters.AddWithValue("?id",id.ToString()); return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture); }
        private static long HeldAmount(MySqlConnection c, MySqlTransaction t, Guid id)
        { using MySqlCommand cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="SELECT COALESCE(SUM(amount),0) FROM continuum_economy_purchases WHERE buyer_id=?id AND state=1"; cmd.Parameters.AddWithValue("?id",id.ToString()); return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture); }
        private static void UpdateBalance(MySqlConnection c, MySqlTransaction t, Guid id, long balance)
        { using MySqlCommand cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="UPDATE continuum_economy_accounts SET balance=?balance,updated_utc=CURRENT_TIMESTAMP(6) WHERE account_id=?id"; cmd.Parameters.AddWithValue("?balance",balance); cmd.Parameters.AddWithValue("?id",id.ToString()); if(cmd.ExecuteNonQuery()!=1) throw new InvalidOperationException("The locked economy account could not be updated"); }

        private static void Insert(MySqlConnection c, MySqlTransaction t, LedgerPurchaseRequest r, string hash, LedgerPurchaseState state, long buyer, long seller, string failure)
        {
            using MySqlCommand cmd=c.CreateCommand(); cmd.Transaction=t;
            cmd.CommandText=@"INSERT INTO continuum_economy_purchases (purchase_id,request_hash,buyer_id,seller_id,amount,transaction_type,region_id,object_id,description,state,buyer_balance,seller_balance,failure_reason)
                VALUES (?id,?hash,?buyer,?seller,?amount,?type,?region,?object,?description,?state,?buyerBalance,?sellerBalance,?failure)";
            cmd.Parameters.AddWithValue("?id",r.PurchaseID.ToString()); cmd.Parameters.AddWithValue("?hash",hash); cmd.Parameters.AddWithValue("?buyer",r.BuyerID.ToString()); cmd.Parameters.AddWithValue("?seller",r.SellerID.ToString()); cmd.Parameters.AddWithValue("?amount",r.Amount); cmd.Parameters.AddWithValue("?type",r.TransactionType); cmd.Parameters.AddWithValue("?region",r.RegionID.ToString()); cmd.Parameters.AddWithValue("?object",r.ObjectID.ToString()); cmd.Parameters.AddWithValue("?description",r.Description??String.Empty); cmd.Parameters.AddWithValue("?state",(int)state); cmd.Parameters.AddWithValue("?buyerBalance",buyer); cmd.Parameters.AddWithValue("?sellerBalance",seller); cmd.Parameters.AddWithValue("?failure",failure); cmd.ExecuteNonQuery();
        }
        private static void SetState(MySqlConnection c, MySqlTransaction t, Guid id, LedgerPurchaseState state, long buyer, long seller)
        { using MySqlCommand cmd=c.CreateCommand(); cmd.Transaction=t; cmd.CommandText="UPDATE continuum_economy_purchases SET state=?state,buyer_balance=?buyer,seller_balance=?seller,completed_utc=CURRENT_TIMESTAMP(6) WHERE purchase_id=?id AND state=1"; cmd.Parameters.AddWithValue("?state",(int)state); cmd.Parameters.AddWithValue("?buyer",buyer); cmd.Parameters.AddWithValue("?seller",seller); cmd.Parameters.AddWithValue("?id",id.ToString()); if(cmd.ExecuteNonQuery()!=1) throw new InvalidOperationException("Purchase state changed concurrently"); }
        private static void InsertCapturedTransfer(MySqlConnection c, MySqlTransaction t, PurchaseRow r, long buyer, long seller)
        {
            using MySqlCommand cmd=c.CreateCommand(); cmd.Transaction=t;
            cmd.CommandText=@"INSERT INTO continuum_economy_transactions (transaction_id,request_hash,sender_id,receiver_id,amount,transaction_type,region_id,object_id,description,status,sender_balance,receiver_balance,failure_reason)
                VALUES (?id,?hash,?buyer,?seller,?amount,?type,?region,?object,?description,1,?buyerBalance,?sellerBalance,'')";
            cmd.Parameters.AddWithValue("?id",r.ID.ToString()); cmd.Parameters.AddWithValue("?hash",r.RequestHash); cmd.Parameters.AddWithValue("?buyer",r.BuyerID.ToString()); cmd.Parameters.AddWithValue("?seller",r.SellerID.ToString()); cmd.Parameters.AddWithValue("?amount",r.Amount); cmd.Parameters.AddWithValue("?type",r.TransactionType); cmd.Parameters.AddWithValue("?region",r.RegionID.ToString()); cmd.Parameters.AddWithValue("?object",r.ObjectID.ToString()); cmd.Parameters.AddWithValue("?description",r.Description); cmd.Parameters.AddWithValue("?buyerBalance",buyer); cmd.Parameters.AddWithValue("?sellerBalance",seller); cmd.ExecuteNonQuery();
        }

        private static string Fingerprint(LedgerPurchaseRequest r)
        { string canonical=String.Join("|",r.BuyerID,r.SellerID,r.Amount.ToString(CultureInfo.InvariantCulture),r.TransactionType.ToString(CultureInfo.InvariantCulture),r.RegionID,r.ObjectID,r.Description??String.Empty); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant(); }
        private static LedgerPurchaseResult PriorResult(PurchaseRow row,string hash)
        { if(!String.Equals(row.RequestHash,hash,StringComparison.Ordinal)) return Conflict(row.ID); LedgerResultCode code=row.State==LedgerPurchaseState.InsufficientFunds?LedgerResultCode.InsufficientFunds:LedgerResultCode.Replayed; return Result(row.ID,code,row.State,row.BuyerBalance,row.BuyerBalance,row.SellerBalance,row.State==LedgerPurchaseState.InsufficientFunds?row.FailureReason:"Purchase operation already applied"); }
        private static LedgerPurchaseResult Finish(MySqlTransaction t,LedgerPurchaseResult result) { t.Commit(); return result; }
        private static LedgerPurchaseResult Invalid(Guid id,string message)=>Result(id,LedgerResultCode.InvalidRequest,0,0,0,0,message);
        private static LedgerPurchaseResult Conflict(Guid id)=>Result(id,LedgerResultCode.TransactionConflict,0,0,0,0,"The purchase ID is already associated with another operation");
        private static LedgerPurchaseResult Result(Guid id,LedgerResultCode code,LedgerPurchaseState state,long buyer,long available,long seller,string message)
            =>new(){PurchaseID=id,Code=code,State=state,BuyerBalance=buyer,BuyerAvailableBalance=available,SellerBalance=seller,Message=message};

        private sealed class PurchaseRow
        { public Guid ID; public string RequestHash; public Guid BuyerID; public Guid SellerID; public long Amount; public int TransactionType; public Guid RegionID; public Guid ObjectID; public string Description; public LedgerPurchaseState State; public long BuyerBalance; public long SellerBalance; public string FailureReason; }
    }
}
