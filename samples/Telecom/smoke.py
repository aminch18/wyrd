"""Run against the local Compose stack: python3 samples/Telecom/smoke.py [base-url]."""
import json
import sys
import time
import urllib.error
import urllib.request

base = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5090"
subscription = "smoke-" + str(time.time_ns())
# Allocate a distinct physical profile for each execution; the sample quarantines previous ones.
profile = subscription + "-profile"


def request(path, body=None):
    payload = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(base + path, data=payload, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=10) as response:
        content = response.read()
        return json.loads(content) if content else None


def until(path, condition):
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        try:
            result = request(path)
            if condition(result):
                return result
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise
        except (urllib.error.URLError, OSError):
            pass
        time.sleep(0.2)
    raise AssertionError("Timed out waiting for " + path)


until("/health/queue", lambda _: True)
# Each smoke run uses its own synthetic number; domain retries still reuse its fixed epoch.
epoch = 1
number = "+34" + str(time.time_ns() % 1_000_000_000).zfill(9)
request("/subscriptions", {"subscriptionId": subscription, "customerId": subscription, "msisdn": number,
    "epoch": epoch, "profileId": profile, "initialAmount": 25})
until("/subscriptions/" + subscription, lambda v: v["provisioned"])
request("/webhooks/mnp/confirmed", {"subscriptionId": subscription, "msisdn": number, "epoch": epoch,
    "windowStart": "2026-10-26T02:00:00Z", "windowEnd": "2026-10-26T04:00:00Z"})
request("/webhooks/mnp/completed", {"subscriptionId": subscription, "msisdn": number, "epoch": epoch})
until("/subscriptions/" + subscription, lambda v: v["ported"])
assert not request("/operations/" + subscription)["paymentAndBilling"]["billingStarted"]
request("/webhooks/connectivity", {"subscriptionId": subscription, "profileId": profile, "attempt": 1})
result = until("/operations/" + subscription, lambda v: v["paymentAndBilling"]["billingStarted"])
print(json.dumps(result, indent=2))
print("HTTP smoke passed: billing waited for port completion and current-profile connectivity.")
