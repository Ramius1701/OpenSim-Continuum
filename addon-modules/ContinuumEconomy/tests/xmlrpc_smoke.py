"""ContinuumEconomy XML-RPC smoke test for a disposable test service."""

import os
import uuid
import xmlrpc.client

url = os.environ.get("CONTINUUM_ECONOMY_TEST_URL", "http://127.0.0.1:18119/")
secret = os.environ.get("CONTINUUM_ECONOMY_TEST_SECRET")
if not secret or len(secret) < 32:
    raise SystemExit("Set CONTINUUM_ECONOMY_TEST_SECRET to the test service's 32+ character shared secret")
service = xmlrpc.client.ServerProxy(url, allow_none=True)
currency_service = xmlrpc.client.ServerProxy(url.rstrip("/") + "/currency.php", allow_none=True)
land_service = xmlrpc.client.ServerProxy(url.rstrip("/") + "/landtool.php", allow_none=True)
buyer, seller = uuid.uuid4(), uuid.uuid4()
buyer_session, buyer_secure = uuid.uuid4(), uuid.uuid4()
seller_session, seller_secure = uuid.uuid4(), uuid.uuid4()
buyer_region, buyer_destination_region, seller_region = uuid.uuid4(), uuid.uuid4(), uuid.uuid4()

def login(agent, session, secure, region):
    return service.ClientLogin({"continuumSecret": secret, "clientUUID": str(agent),
        "clientSessionID": str(session), "clientSecureSessionID": str(secure),
        "regionUUID": str(region)})

def balance(agent, session, secure):
    return service.GetBalance({"continuumSecret": secret, "clientUUID": str(agent),
        "clientSessionID": str(session), "clientSecureSessionID": str(secure)})

assert service.ContinuumHealth({})["service"] == "ContinuumEconomy.Service"
buyer_start = login(buyer, buyer_session, buyer_secure, buyer_region)["clientBalance"]
seller_start = login(seller, seller_session, seller_secure, seller_region)["clientBalance"]
assert buyer_start >= 25 and seller_start >= 0
# A crossing registers the destination before the source logs out. Both region
# sessions must coexist so the source logout cannot invalidate the destination.
assert login(buyer, buyer_session, buyer_secure, buyer_destination_region)["success"] is True
transaction = uuid.uuid4()
transfer = {"continuumSecret": secret, "transactionID": str(transaction),
    "senderID": str(buyer), "receiverID": str(seller),
    "senderSessionID": str(buyer_session), "senderSecureSessionID": str(buyer_secure),
    "amount": 25, "transactionType": 5001, "regionUUID": str(uuid.uuid4()),
    "objectID": str(uuid.UUID(int=0)), "description": "XML-RPC smoke transfer"}
assert service.TransferMoney(transfer)["success"] is True
assert service.TransferMoney(transfer)["result"] == "Replayed"
conflict = dict(transfer)
conflict["amount"] = 26
assert service.TransferMoney(conflict)["success"] is False
assert balance(buyer, buyer_session, buyer_secure)["clientBalance"] == buyer_start - 25
assert balance(seller, seller_session, seller_secure)["clientBalance"] == seller_start + 25
quote = currency_service.getCurrencyQuote({"agentId": str(buyer),
    "secureSessionId": str(buyer_secure), "currencyBuy": 10})
assert quote["success"] is True and quote["currency"]["currencyBuy"] == 10
currency_purchase = {"agentId": str(buyer), "secureSessionId": str(buyer_secure),
    "currencyBuy": 10, "confirm": quote["confirm"]}
assert currency_service.buyCurrency(currency_purchase)["success"] is True
assert currency_service.buyCurrency(currency_purchase)["result"] == "Replayed"
land = {"agentId": str(buyer), "secureSessionId": str(buyer_secure),
    "billableArea": 512, "currencyBuy": 10}
preflight = land_service.preflightBuyLandPrep(land)
assert preflight["success"] is True and preflight["billableArea"] == 512
assert preflight["membership"] == {"upgrade": False, "action": "", "levels": []}
assert preflight["landUse"] == {"upgrade": False, "action": ""}
assert preflight["currency"]["currencyBuy"] == 10
assert preflight["currency"]["estimatedCost"] == 10
uuid.UUID(preflight["confirm"])
assert land_service.buyLandPrep(land)["success"] is True
bad_land = dict(land)
bad_land["secureSessionId"] = str(uuid.uuid4())
assert land_service.preflightBuyLandPrep(bad_land)["success"] is False
purchase_id = uuid.uuid4()
authorization = {"continuumSecret": secret, "purchaseID": str(purchase_id),
    "buyerID": str(buyer), "buyerSessionID": str(buyer_session),
    "buyerSecureSessionID": str(buyer_secure), "sellerID": str(seller),
    "amount": 20, "transactionType": 5000, "regionUUID": str(uuid.uuid4()),
    "objectID": str(uuid.uuid4()), "description": "Object delivery smoke"}
assert service.AuthorizePurchase(authorization)["state"] == "Authorized"
assert service.CapturePurchase({"continuumSecret": secret,
    "purchaseID": str(purchase_id), "buyerID": str(buyer)})["state"] == "Captured"
cancel_id = uuid.uuid4()
cancel_request = dict(authorization)
cancel_request["purchaseID"] = str(cancel_id)
cancel_request["amount"] = 10
assert service.AuthorizePurchase(cancel_request)["state"] == "Authorized"
assert service.CancelPurchase({"continuumSecret": secret,
    "purchaseID": str(cancel_id), "buyerID": str(buyer)})["state"] == "Cancelled"

transaction_info = service.GetTransaction({"continuumSecret": secret,
    "clientUUID": str(buyer), "clientSessionID": str(buyer_session),
    "clientSecureSessionID": str(buyer_secure), "transactionID": str(transaction)})
assert transaction_info["success"] is True
assert transaction_info["amount"] == 25
assert transaction_info["sender"] == str(buyer)
assert transaction_info["receiver"] == str(seller)

fee_reservation = uuid.uuid4()
fee_request = {"continuumSecret": secret, "purchaseID": str(fee_reservation),
    "buyerID": str(buyer), "buyerSessionID": str(buyer_session),
    "buyerSecureSessionID": str(buyer_secure), "amount": 7,
    "transactionType": 1101, "description": "Reserved upload fee smoke"}
invalid_fee_session = dict(fee_request)
invalid_fee_session["purchaseID"] = str(uuid.uuid4())
invalid_fee_session["buyerSessionID"] = str(uuid.uuid4())
invalid_fee_result = service.AuthorizeCharge(invalid_fee_session)
assert invalid_fee_result["success"] is False
assert invalid_fee_result["message"] == "Invalid session"
assert service.AuthorizeCharge(fee_request)["state"] == "Authorized"
assert service.CapturePurchase({"continuumSecret": secret,
    "purchaseID": str(fee_reservation), "buyerID": str(buyer)})["state"] == "Captured"

cancelled_fee = dict(fee_request)
cancelled_fee["purchaseID"] = str(uuid.uuid4())
cancelled_fee["amount"] = 8
assert service.AuthorizeCharge(cancelled_fee)["state"] == "Authorized"
assert service.CancelPurchase({"continuumSecret": secret,
    "purchaseID": cancelled_fee["purchaseID"], "buyerID": str(buyer)})["state"] == "Cancelled"

charge = {"continuumSecret": secret, "transactionID": str(uuid.uuid4()),
    "senderID": str(buyer), "senderSessionID": str(buyer_session),
    "senderSecureSessionID": str(buyer_secure), "amount": 5,
    "transactionType": 1101, "description": "Upload fee smoke"}
assert service.PayMoneyCharge(charge)["success"] is True
assert service.PayMoneyCharge(charge)["result"] == "Replayed"

force = {"continuumSecret": secret, "transactionID": str(uuid.uuid4()),
    "senderID": str(seller), "receiverID": str(buyer), "amount": 3,
    "transactionType": 5011, "description": "Trusted force transfer smoke"}
assert service.ForceTransferMoney(force)["success"] is True
assert service.ForceTransferMoney(force)["result"] == "Replayed"

move = dict(force)
move["transactionID"] = str(uuid.uuid4())
move["amount"] = 2
assert service.MoveMoney(move)["success"] is True

credit = {"continuumSecret": secret, "transactionID": str(uuid.uuid4()),
    "receiverID": str(buyer), "amount": 4, "transactionType": 5012,
    "description": "Trusted credit smoke"}
assert service.SendMoney(credit)["success"] is True
assert service.SendMoney(credit)["result"] == "Replayed"

banker = {"continuumSecret": secret, "transactionID": str(uuid.uuid4()),
    "bankerID": str(seller), "amount": 6, "transactionType": 5010,
    "description": "Trusted banker credit smoke"}
assert service.AddBankerMoney(banker)["success"] is True
assert balance(buyer, buyer_session, buyer_secure)["clientBalance"] == buyer_start - 38
assert balance(seller, seller_session, seller_secure)["clientBalance"] == seller_start + 46

unauthorized = dict(force)
unauthorized["transactionID"] = str(uuid.uuid4())
unauthorized["continuumSecret"] = "not-the-region-secret"
assert service.ForceTransferMoney(unauthorized)["success"] is False
assert service.ClientLogout({"continuumSecret": secret, "clientUUID": str(buyer),
    "clientSessionID": str(buyer_session),
    "clientSecureSessionID": str(buyer_secure),
    "regionUUID": str(buyer_region)})["success"] is True
assert balance(buyer, buyer_session, buyer_secure)["success"] is True
assert service.ClientLogout({"continuumSecret": secret, "clientUUID": str(buyer),
    "clientSessionID": str(buyer_session),
    "clientSecureSessionID": str(buyer_secure),
    "regionUUID": str(buyer_destination_region)})["success"] is True
assert balance(buyer, buyer_session, buyer_secure)["success"] is False
print("ContinuumEconomy XML-RPC smoke test passed against", url)
