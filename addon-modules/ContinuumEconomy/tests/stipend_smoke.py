"""Verify scheduled stipends and restart idempotency against the test service."""

import os
import time
import uuid
import xmlrpc.client

url = os.environ.get("CONTINUUM_ECONOMY_TEST_URL", "http://127.0.0.1:18120/")
secret = "CONTINUUM-ECONOMY-STIPEND-SMOKE-TEST-ONLY"
agent = uuid.UUID("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb")
session = uuid.UUID("cccccccc-1111-2222-3333-dddddddddddd")
secure = uuid.UUID("eeeeeeee-1111-2222-3333-ffffffffffff")
region = uuid.UUID("12345678-1111-2222-3333-123456789abc")
service = xmlrpc.client.ServerProxy(url, allow_none=True)

login = service.ClientLogin({"continuumSecret": secret, "clientUUID": str(agent),
    "clientSessionID": str(session), "clientSecureSessionID": str(secure),
    "regionUUID": str(region)})
assert login["success"] is True

deadline = time.monotonic() + 25
balance = login["clientBalance"]
while balance != 7 and time.monotonic() < deadline:
    time.sleep(1)
    balance = service.GetBalance({"continuumSecret": secret, "clientUUID": str(agent),
        "clientSessionID": str(session), "clientSecureSessionID": str(secure)})["clientBalance"]
assert balance == 7, "expected one stipend credit, got %r" % (balance,)
time.sleep(11)
balance = service.GetBalance({"continuumSecret": secret, "clientUUID": str(agent),
    "clientSessionID": str(session), "clientSecureSessionID": str(secure)})["clientBalance"]
assert balance == 7, "stipend operation replay credited the account twice"
print("ContinuumEconomy scheduled stipend smoke test passed")
