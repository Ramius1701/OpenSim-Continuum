using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenSim.Continuum.Economy;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Console;

namespace ContinuumEconomy.Service
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string ini = args.Length > 0 ? args[0] : "ContinuumEconomy.ini";
                IniConfigSource source = new(ini);
                IConfig service = source.Configs["ContinuumEconomyService"] ??
                    throw new InvalidOperationException("[ContinuumEconomyService] is required");
                IConfig database = source.Configs["Database"] ??
                    throw new InvalidOperationException("[Database] is required");
                EconomyBackend backend = EconomyProviderFactory.Create(
                    database.GetString("StorageProvider", String.Empty),
                    database.GetString("ConnectionString", String.Empty));
                backend.Ledger.ValidateSchema();
                MainConsole.Instance = new LocalConsole("ContinuumEconomy ");
                ContinuumEconomyRpc rpc = new(backend, service);
                BaseHttpServer server = new(checked((uint)service.GetInt("Port", 8009)));
                rpc.Register(server);
                server.Start();
                Console.WriteLine("ContinuumEconomy.Service listening on port {0}. Press Ctrl+C to stop.", service.GetInt("Port", 8009));
                using System.Threading.ManualResetEventSlim stop = new(false);
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
                stop.Wait();
                server.Stop();
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ContinuumEconomy.Service failed: {0}", e);
                return 1;
            }
        }
    }

    internal sealed class ContinuumEconomyRpc
    {
        private readonly EconomyBackend m_backend;
        private readonly ConcurrentDictionary<Guid, Session> m_sessions = new();
        private readonly Guid m_systemActor;
        private readonly long m_defaultBalance;
        private readonly string m_sharedSecret;
        private readonly bool m_allowRegionCredits;
        private readonly bool m_currencyPurchaseEnabled;
        private readonly long m_dailyPurchaseLimit;
        private readonly long m_weeklyPurchaseLimit;
        private readonly long m_monthlyPurchaseLimit;
        private readonly long m_maximumBalance;
        private readonly double m_estimatedCostPerUnit;

        internal ContinuumEconomyRpc(EconomyBackend backend, IConfig config)
        {
            m_backend = backend;
            m_defaultBalance = Math.Max(0, config.GetLong("DefaultBalance", 0));
            m_sharedSecret = config.GetString("RegionSharedSecret", String.Empty);
            m_allowRegionCredits = config.GetBoolean("AllowRegionCredits", false);
            m_currencyPurchaseEnabled = config.GetBoolean("CurrencyPurchaseEnabled", false);
            m_dailyPurchaseLimit = Math.Max(0, config.GetLong("DailyPurchaseLimit", 0));
            m_weeklyPurchaseLimit = Math.Max(0, config.GetLong("WeeklyPurchaseLimit", 0));
            m_monthlyPurchaseLimit = Math.Max(0, config.GetLong("MonthlyPurchaseLimit", 0));
            m_maximumBalance = Math.Max(0, config.GetLong("MaximumBalance", 0));
            m_estimatedCostPerUnit = Math.Max(0, config.GetDouble("EstimatedCostPerUnit", 0.01));
            if (m_sharedSecret.Length < 32)
                throw new InvalidOperationException("RegionSharedSecret must contain at least 32 characters");
            if (!Guid.TryParse(config.GetString("SystemActorID", String.Empty), out m_systemActor) || m_systemActor == Guid.Empty)
                throw new InvalidOperationException("SystemActorID must be a non-zero UUID");
        }

        internal void Register(BaseHttpServer server)
        {
            server.AddXmlRPCHandler("ContinuumHealth", Health);
            server.AddXmlRPCHandler("ClientLogin", Login);
            server.AddXmlRPCHandler("ClientLogout", Logout);
            server.AddXmlRPCHandler("GetBalance", Balance);
            server.AddXmlRPCHandler("GetTransaction", Transaction);
            server.AddXmlRPCHandler("TransferMoney", Transfer);
            server.AddXmlRPCHandler("ForceTransferMoney", ForceTransfer);
            server.AddXmlRPCHandler("PayMoneyCharge", Charge);
            server.AddXmlRPCHandler("MoveMoney", ForceTransfer);
            server.AddXmlRPCHandler("SendMoney", Credit);
            server.AddXmlRPCHandler("AddBankerMoney", Credit);
            server.AddXmlRPCHandler("getCurrencyQuote", CurrencyQuote);
            server.AddXmlRPCHandler("buyCurrency", BuyCurrency);
            server.AddXmlRPCHandler("AuthorizePurchase", AuthorizePurchase);
            server.AddXmlRPCHandler("AuthorizeCharge", AuthorizeCharge);
            server.AddXmlRPCHandler("CapturePurchase", CapturePurchase);
            server.AddXmlRPCHandler("CancelPurchase", CancelPurchase);
            server.AddXmlRPCHandler("preflightBuyLandPrep", PreflightLand);
            server.AddXmlRPCHandler("buyLandPrep", BuyLandPrep);
        }

        private XmlRpcResponse Health(XmlRpcRequest request, IPEndPoint remote) => Reply(new Hashtable
        {
            ["success"] = true, ["service"] = "ContinuumEconomy.Service",
            ["provider"] = m_backend.Provider.ToString()
        });

        private XmlRpcResponse Login(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p = Parameters(request);
            if (!Secret(p) || !IDs(p, "clientUUID", "clientSessionID", "clientSecureSessionID",
                out Guid agent, out Guid session, out Guid secure)) return Failure("Invalid login credentials");
            m_backend.Ledger.EnsureAccount(agent);
            if (m_defaultBalance > 0 && m_backend.Ledger.GetBalance(agent) == 0 &&
                m_backend.Ledger.CountHistory(agent, null, null) == 0)
            {
                m_backend.Ledger.Adjust(new LedgerAdjustmentRequest { OperationID=InitialCreditID(agent),
                    AccountID=agent,ActorID=m_systemActor,Amount=m_defaultBalance,Kind=LedgerAdjustmentKind.Credit,
                    TransactionType=900,Reason="Continuum initial account balance" });
            }
            m_sessions[agent] = new Session(session, secure);
            return SuccessBalance(agent);
        }

        private XmlRpcResponse Logout(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p=Parameters(request);
            if(!Authenticated(p,"clientUUID","clientSessionID","clientSecureSessionID",out Guid agent))return Failure("Invalid session");
            m_sessions.TryRemove(agent,out _);return Reply(new Hashtable{{"success",true}});
        }

        private XmlRpcResponse Balance(XmlRpcRequest request,IPEndPoint remote)
        {
            Hashtable p=Parameters(request);
            if(!Authenticated(p,"clientUUID","clientSessionID","clientSecureSessionID",out Guid agent))return Failure("Invalid session");
            return SuccessBalance(agent);
        }

        private XmlRpcResponse Transfer(XmlRpcRequest request,IPEndPoint remote)=>TransferCore(Parameters(request),false);
        private XmlRpcResponse Charge(XmlRpcRequest request,IPEndPoint remote)=>TransferCore(Parameters(request),true);
        private XmlRpcResponse ForceTransfer(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p = Parameters(request);
            if (!Secret(p) || !Guid.TryParse(Text(p, "senderID"), out Guid sender) || sender == Guid.Empty ||
                !Guid.TryParse(Text(p, "receiverID"), out Guid receiver) || receiver == Guid.Empty)
                return Failure("Invalid trusted transfer request");
            return CommitTransfer(p, sender, receiver);
        }

        private XmlRpcResponse Credit(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p = Parameters(request);
            string receiverText = Text(p, "receiverID");
            if (receiverText.Length == 0) receiverText = Text(p, "bankerID");
            if (!Secret(p) || !m_allowRegionCredits || !Guid.TryParse(receiverText, out Guid receiver) || receiver == Guid.Empty ||
                !Guid.TryParse(Text(p, "transactionID"), out Guid operation) || operation == Guid.Empty ||
                !Int64.TryParse(Text(p, "amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount) || amount <= 0)
                return Failure("Trusted region credits are disabled or invalid");
            Int32.TryParse(Text(p, "transactionType"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int type);
            LedgerAdjustmentResult result = m_backend.Ledger.Adjust(new LedgerAdjustmentRequest
            {
                OperationID = operation, AccountID = receiver, ActorID = m_systemActor,
                Amount = amount, Kind = LedgerAdjustmentKind.Credit, TransactionType = type,
                Reason = String.IsNullOrWhiteSpace(Text(p, "description")) ? "Continuum trusted region credit" : Text(p, "description")
            });
            return Reply(new Hashtable { { "success", result.Succeeded }, { "result", result.Code.ToString() },
                { "clientBalance", ViewerBalance(result.Balance) }, { "message", result.Message }, { "banker", true } });
        }

        private XmlRpcResponse CurrencyQuote(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p = Parameters(request);
            if (!CurrencyRequest(p, out Guid agent, out long amount, out string error))
                return Failure(error);
            return Reply(new Hashtable { { "success", true }, { "currency", new Hashtable
                { { "estimatedCost", amount * m_estimatedCostPerUnit }, { "currencyBuy", ViewerBalance(amount) } } },
                { "confirm", Guid.NewGuid().ToString() }, { "agentId", agent.ToString() } });
        }

        private XmlRpcResponse BuyCurrency(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p = Parameters(request);
            if (!CurrencyRequest(p, out Guid agent, out long amount, out string error))
                return Failure(error);
            string operationText = Text(p, "transactionID");
            if (operationText.Length == 0) operationText = Text(p, "confirm");
            if (!Guid.TryParse(operationText, out Guid operation) || operation == Guid.Empty)
                return Failure("A quote confirmation or transaction ID is required");
            LedgerAdjustmentResult result = m_backend.Ledger.Adjust(new LedgerAdjustmentRequest
            {
                OperationID = operation, AccountID = agent, ActorID = m_systemActor, Amount = amount,
                Kind = LedgerAdjustmentKind.Credit, TransactionType = 5010,
                Reason = "Viewer currency purchase", MaximumBalance = m_maximumBalance,
                DailyCreditLimit = m_dailyPurchaseLimit, WeeklyCreditLimit = m_weeklyPurchaseLimit,
                MonthlyCreditLimit = m_monthlyPurchaseLimit
            });
            return Reply(new Hashtable { { "success", result.Succeeded }, { "result", result.Code.ToString() },
                { "message", result.Message }, { "transactionID", operation.ToString() },
                { "clientBalance", ViewerBalance(result.Balance) } });
        }

        private bool CurrencyRequest(Hashtable p, out Guid agent, out long amount, out string error)
        {
            agent = Guid.Empty;
            amount = 0;
            if (!m_currencyPurchaseEnabled) { error = "Currency purchasing is disabled"; return false; }
            string agentText = Text(p, "agentId");
            if (agentText.Length == 0) agentText = Text(p, "clientUUID");
            string secureText = Text(p, "secureSessionId");
            if (secureText.Length == 0) secureText = Text(p, "clientSecureSessionID");
            string amountText = Text(p, "currencyBuy");
            if (!Guid.TryParse(agentText, out agent) || agent == Guid.Empty ||
                !Guid.TryParse(secureText, out Guid secure) || !m_sessions.TryGetValue(agent, out Session known) ||
                known.SecureSessionID != secure || !Int64.TryParse(amountText, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out amount) || amount <= 0)
            { error = "The currency request could not be authenticated or has an invalid amount"; return false; }
            error = String.Empty;
            return true;
        }

        private XmlRpcResponse AuthorizePurchase(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p = Parameters(request);
            if (!Secret(p) || !Guid.TryParse(Text(p, "buyerID"), out Guid buyer) || buyer == Guid.Empty ||
                !Guid.TryParse(Text(p, "buyerSessionID"), out Guid session) ||
                !Guid.TryParse(Text(p, "buyerSecureSessionID"), out Guid secure) ||
                !Guid.TryParse(Text(p, "sellerID"), out Guid seller) || seller == Guid.Empty ||
                !Guid.TryParse(Text(p, "purchaseID"), out Guid purchase) || purchase == Guid.Empty ||
                !Int64.TryParse(Text(p, "amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount) || amount <= 0)
                return Failure("Invalid purchase authorization request");
            if (!m_sessions.TryGetValue(buyer, out Session known) || known.SessionID != session || known.SecureSessionID != secure)
                return Failure("Invalid session");
            Int32.TryParse(Text(p, "transactionType"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int type);
            Guid.TryParse(Text(p, "regionUUID"), out Guid region);
            Guid.TryParse(Text(p, "objectID"), out Guid obj);
            LedgerPurchaseResult result = m_backend.Purchases.Authorize(new LedgerPurchaseRequest
            {
                PurchaseID = purchase, BuyerID = buyer, SellerID = seller, Amount = amount,
                TransactionType = type, RegionID = region, ObjectID = obj, Description = Text(p, "description")
            });
            return PurchaseReply(result);
        }

        private XmlRpcResponse AuthorizeCharge(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p = Parameters(request);
            if (!Secret(p) || !Guid.TryParse(Text(p, "buyerID"), out Guid buyer) || buyer == Guid.Empty ||
                !Guid.TryParse(Text(p, "buyerSessionID"), out Guid session) ||
                !Guid.TryParse(Text(p, "buyerSecureSessionID"), out Guid secure) ||
                !Guid.TryParse(Text(p, "purchaseID"), out Guid purchase) || purchase == Guid.Empty ||
                !Int64.TryParse(Text(p, "amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount) || amount <= 0)
                return Failure("Invalid charge authorization request");
            if (!m_sessions.TryGetValue(buyer, out Session known) || known.SessionID != session || known.SecureSessionID != secure)
                return Failure("Invalid session");

            Int32.TryParse(Text(p, "transactionType"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int type);
            Guid.TryParse(Text(p, "regionUUID"), out Guid region);
            LedgerPurchaseResult result = m_backend.Purchases.Authorize(new LedgerPurchaseRequest
            {
                PurchaseID = purchase, BuyerID = buyer, SellerID = m_systemActor, Amount = amount,
                TransactionType = type, RegionID = region, ObjectID = Guid.Empty,
                Description = Text(p, "description")
            });
            return PurchaseReply(result);
        }

        private XmlRpcResponse CapturePurchase(XmlRpcRequest request, IPEndPoint remote) =>
            CompletePurchase(Parameters(request), true);
        private XmlRpcResponse CancelPurchase(XmlRpcRequest request, IPEndPoint remote) =>
            CompletePurchase(Parameters(request), false);

        private XmlRpcResponse CompletePurchase(Hashtable p, bool capture)
        {
            if (!Secret(p) || !Guid.TryParse(Text(p, "purchaseID"), out Guid purchase) || purchase == Guid.Empty ||
                !Guid.TryParse(Text(p, "buyerID"), out Guid buyer) || buyer == Guid.Empty)
                return Failure("Invalid purchase completion request");
            LedgerPurchaseResult result = capture ? m_backend.Purchases.Capture(purchase, buyer) :
                m_backend.Purchases.Cancel(purchase, buyer);
            return PurchaseReply(result);
        }

        private XmlRpcResponse PreflightLand(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p = Parameters(request);
            if (!LandRequest(p, out Guid agent, out long currencyBuy, out string error)) return Failure(error);
            return Reply(new Hashtable { { "success", m_backend.Ledger.GetAvailableBalance(agent) >= currencyBuy },
                { "billableArea", ParseInt(p, "billableArea") }, { "currencyBuy", ViewerBalance(currencyBuy) } });
        }

        private XmlRpcResponse BuyLandPrep(XmlRpcRequest request, IPEndPoint remote)
        {
            Hashtable p = Parameters(request);
            if (!LandRequest(p, out Guid agent, out long currencyBuy, out string error)) return Failure(error);
            return m_backend.Ledger.GetAvailableBalance(agent) >= currencyBuy ?
                Reply(new Hashtable { { "success", true } }) : Failure("Insufficient funds for land purchase");
        }

        private bool LandRequest(Hashtable p, out Guid agent, out long currencyBuy, out string error)
        {
            agent = Guid.Empty;
            currencyBuy = 0;
            if (!Guid.TryParse(Text(p, "agentId"), out agent) || agent == Guid.Empty ||
                !Guid.TryParse(Text(p, "secureSessionId"), out Guid secure) ||
                !m_sessions.TryGetValue(agent, out Session session) || session.SecureSessionID != secure ||
                !Int64.TryParse(Text(p, "currencyBuy"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out currencyBuy) || currencyBuy < 0 || ParseInt(p, "billableArea") < 0)
            { error = "The land purchase request could not be authenticated or has invalid values"; return false; }
            error = String.Empty;
            return true;
        }

        private static XmlRpcResponse PurchaseReply(LedgerPurchaseResult result) => Reply(new Hashtable
        {
            { "success", result.Succeeded }, { "result", result.Code.ToString() },
            { "state", result.State.ToString() }, { "purchaseID", result.PurchaseID.ToString() },
            { "clientBalance", ViewerBalance(result.BuyerBalance) }, { "message", result.Message }
        });

        private XmlRpcResponse TransferCore(Hashtable p,bool charge)
        {
            if(!Secret(p)||!Guid.TryParse(Text(p,"senderID"),out Guid sender)||sender==Guid.Empty||
                !Guid.TryParse(Text(p,"senderSessionID"),out Guid session)||
                !Guid.TryParse(Text(p,"senderSecureSessionID"),out Guid secure)||
                !m_sessions.TryGetValue(sender,out Session known)||known.SessionID!=session||known.SecureSessionID!=secure)
                return Failure("Invalid sender session");
            Guid receiver=m_systemActor;
            if(!charge&&(!Guid.TryParse(Text(p,"receiverID"),out receiver)||receiver==Guid.Empty))return Failure("Invalid receiver");
            return CommitTransfer(p, sender, receiver);
        }

        private XmlRpcResponse CommitTransfer(Hashtable p, Guid sender, Guid receiver)
        {
            if(!Guid.TryParse(Text(p,"transactionID"),out Guid transaction)||transaction==Guid.Empty)return Failure("A transactionID is required");
            if(!Int64.TryParse(Text(p,"amount"),NumberStyles.Integer,CultureInfo.InvariantCulture,out long amount)||amount<=0)return Failure("Invalid amount");
            Int32.TryParse(Text(p,"transactionType"),NumberStyles.Integer,CultureInfo.InvariantCulture,out int type);
            Guid.TryParse(Text(p,"regionUUID"),out Guid region);Guid.TryParse(Text(p,"objectID"),out Guid obj);
            LedgerTransferResult result=m_backend.Ledger.Transfer(new(){TransactionID=transaction,SenderID=sender,
                ReceiverID=receiver,Amount=amount,TransactionType=type,RegionID=region,ObjectID=obj,
                Description=Text(p,"description")});
            return Reply(new Hashtable{{"success",result.Succeeded},{"result",result.Code.ToString()},
                {"transactionID",result.TransactionID.ToString()},{"clientBalance",ViewerBalance(result.SenderBalance)},
                {"receiverBalance",ViewerBalance(result.ReceiverBalance)},
                {"message",result.Message}});
        }

        private XmlRpcResponse Transaction(XmlRpcRequest request,IPEndPoint remote)
        {
            Hashtable p=Parameters(request);
            if(!Authenticated(p,"clientUUID","clientSessionID","clientSecureSessionID",out Guid agent)||
                !Guid.TryParse(Text(p,"transactionID"),out Guid id))return Failure("Invalid request");
            LedgerHistoryEntry row=m_backend.Ledger.GetOperation(id);
            if(row==null||(row.AccountID!=agent&&row.CounterpartyID!=agent))return Failure("Transaction not found");
            return Reply(new Hashtable{{"success",true},{"amount",ViewerBalance(row.Amount)},
                {"type",row.TransactionType},{"description",row.Description},{"sender",row.IsCredit?row.CounterpartyID.ToString():row.AccountID.ToString()},
                {"receiver",row.IsCredit?row.AccountID.ToString():row.CounterpartyID.ToString()}});
        }

        private bool Authenticated(Hashtable p,string agentKey,string sessionKey,string secureKey,out Guid agent)
        {
            agent=Guid.Empty;return Secret(p)&&Guid.TryParse(Text(p,agentKey),out agent)&&
                Guid.TryParse(Text(p,sessionKey),out Guid session)&&Guid.TryParse(Text(p,secureKey),out Guid secure)&&
                m_sessions.TryGetValue(agent,out Session known)&&known.SessionID==session&&known.SecureSessionID==secure;
        }
        private bool Secret(Hashtable p)=>CryptographicEquals(Text(p,"continuumSecret"),m_sharedSecret);
        private static bool CryptographicEquals(string a,string b)
        {
            byte[] left=System.Text.Encoding.UTF8.GetBytes(a??String.Empty),right=System.Text.Encoding.UTF8.GetBytes(b??String.Empty);
            return left.Length==right.Length&&System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left,right);
        }
        private static bool IDs(Hashtable p, string a, string b, string c, out Guid x, out Guid y, out Guid z)
        {
            x = Guid.Empty;
            y = Guid.Empty;
            z = Guid.Empty;
            return Guid.TryParse(Text(p, a), out x) && x != Guid.Empty &&
                Guid.TryParse(Text(p, b), out y) && y != Guid.Empty &&
                Guid.TryParse(Text(p, c), out z) && z != Guid.Empty;
        }
        private XmlRpcResponse SuccessBalance(Guid id)=>Reply(new Hashtable{{"success",true},{"clientBalance",ViewerBalance(m_backend.Ledger.GetBalance(id))}});
        private static int ViewerBalance(long value)=>checked((int)Math.Clamp(value,0,Int32.MaxValue));
        private static int ParseInt(Hashtable p, string key) => Int32.TryParse(Text(p, key),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : -1;
        private static Guid InitialCreditID(Guid account){byte[] ns=account.ToByteArray(),tag=System.Text.Encoding.ASCII.GetBytes("ContinuumInitialBalance");byte[] all=new byte[ns.Length+tag.Length];Buffer.BlockCopy(ns,0,all,0,ns.Length);Buffer.BlockCopy(tag,0,all,ns.Length,tag.Length);byte[] hash=System.Security.Cryptography.SHA256.HashData(all);byte[] id=new byte[16];Buffer.BlockCopy(hash,0,id,0,16);return new Guid(id);}
        private static Hashtable Parameters(XmlRpcRequest r)=>r?.Params?.Count>0&&r.Params[0] is Hashtable p?p:new Hashtable();
        private static string Text(Hashtable p,string key)=>p.ContainsKey(key)?Convert.ToString(p[key],CultureInfo.InvariantCulture)??String.Empty:String.Empty;
        private static XmlRpcResponse Failure(string message)=>Reply(new Hashtable{{"success",false},{"errorMessage",message},{"errorURI",String.Empty},{"message",message}});
        private static XmlRpcResponse Reply(Hashtable value)=>new(){Value=value};
        private readonly record struct Session(Guid SessionID,Guid SecureSessionID);
    }
}
