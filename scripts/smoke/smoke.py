#!/usr/bin/env python3
"""Smoke test for the running API: the business flows end to end, over real HTTP and a real SMTP conversation.

Needs only Python 3.9+ (standard library). It expects:
  * the API running at SMOKE_API (set SMOKE_WAIT_SECONDS to wait for it to come up; start this script BEFORE the API so the SMTP sink is listening
    when the API sends its first emails) (default http://127.0.0.1:5004) on a database seeded with `scripts/dev-setup.sh --seed`;
  * the API configured to send email to this script's built-in SMTP sink (SMOKE_SMTP_PORT, default 2525), with reminders firing at start-up
    (Reminders__InitialDelaySeconds small, Reminders__Hour=0) -- `scripts/smoke/run.sh` sets all of that up.

Every check prints PASS/FAIL; the exit code is 1 if any failed. A section that raises is reported as a failure and the next one still runs.
It only relies on data the seeder creates fresh, so run it against a fresh database, not a shared one.
"""
import asyncio
import datetime
import email
import email.policy
import json
import os
import re
import sys
import threading
import time
import urllib.error
import urllib.request

API = os.environ.get("SMOKE_API", "http://127.0.0.1:5004").rstrip("/") + "/api/v1"
SMTP_PORT = int(os.environ.get("SMOKE_SMTP_PORT", "2525"))
ADMIN = (os.environ.get("SMOKE_ADMIN", "admin"), os.environ.get("SMOKE_ADMIN_PASSWORD", "Admin#123456"))
CLERK = (os.environ.get("SMOKE_CLERK", "apclerk"), os.environ.get("SMOKE_CLERK_PASSWORD", "Clerk#1234567"))
RUN = str(int(time.time()))[-6:]


# ---- a tiny SMTP server that keeps every accepted message in memory --------------------------------------------------------------

MAIL = []          # {"to": [...], "subject": str, "text": str, "html": str}
_mail_lock = threading.Lock()


async def _smtp_client(reader, writer):
    async def say(line):
        writer.write((line + "\r\n").encode())
        await writer.drain()

    await say("220 smoke ESMTP")
    rcpts = []
    while True:
        raw = await reader.readline()
        if not raw:
            break
        line = raw.decode(errors="replace").strip()
        up = line.upper()
        if up.startswith(("EHLO", "HELO")):
            writer.write(b"250-smoke\r\n250 8BITMIME\r\n")
            await writer.drain()
        elif up.startswith("MAIL FROM"):
            rcpts = []
            await say("250 ok")
        elif up.startswith("RCPT TO"):
            address = line[8:].strip().strip("<>")
            if "reject" in address.lower():            # a permanent failure, to prove it never fails the business operation
                await say("550 5.1.1 no such user")
            else:
                rcpts.append(address)
                await say("250 ok")
        elif up == "DATA":
            await say("354 end with <CRLF>.<CRLF>")
            data = []
            while True:
                chunk = await reader.readline()
                if chunk in (b".\r\n", b".\n", b""):
                    break
                data.append(chunk[1:] if chunk.startswith(b"..") else chunk)
            message = email.message_from_bytes(b"".join(data), policy=email.policy.default)
            text = message.get_body(preferencelist=("plain",))
            html = message.get_body(preferencelist=("html",))
            with _mail_lock:
                MAIL.append({"to": [r.lower() for r in rcpts], "subject": message["Subject"] or "", "message_id": message["Message-ID"] or "",
                             "text": text.get_content() if text else "", "html": html.get_content() if html else "", "raw": message.as_string()})
            await say("250 queued")
        elif up == "RSET":
            rcpts = []
            await say("250 ok")
        elif up == "NOOP":
            await say("250 ok")
        elif up == "QUIT":
            await say("221 bye")
            break
        else:
            await say("502 not implemented")
    writer.close()


def start_sink():
    ready = threading.Event()

    def run():
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        server = loop.run_until_complete(asyncio.start_server(_smtp_client, "127.0.0.1", SMTP_PORT))
        ready.set()
        loop.run_forever()
        server.close()

    threading.Thread(target=run, daemon=True).start()
    if not ready.wait(10):
        raise RuntimeError(f"could not start the SMTP sink on port {SMTP_PORT}")


def mails(to=None, subject=None):
    with _mail_lock:
        return [m for m in MAIL if (to is None or to.lower() in m["to"]) and (subject is None or subject in m["subject"])]


# ---- HTTP and checking helpers ----------------------------------------------------------------------------------------------------

def call(method, path, token=None, company=None, body=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    if company:
        headers["X-Company-Id"] = company
    request = urllib.request.Request(API + path, method=method, headers=headers, data=None if body is None else json.dumps(body).encode())
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw, status = response.read().decode(), response.status
    except urllib.error.HTTPError as error:
        raw, status = error.read().decode(), error.code
    try:
        return status, (json.loads(raw) if raw else None)
    except ValueError:
        return status, raw


def detail(response):
    data = response[1]
    return data.get("detail") if isinstance(data, dict) else data


FAILURES = []
PASSED = 0


def check(name, condition, info=""):
    global PASSED
    if condition:
        PASSED += 1
    else:
        FAILURES.append(name)
    print(("  PASS " if condition else "  FAIL ") + name + ((" -- " + str(info)[:300]) if info and not condition else ""), flush=True)


def wait_for(predicate, seconds=30, every=0.5):
    end = time.time() + seconds
    while time.time() < end:
        found = predicate()
        if found:
            return found
        time.sleep(every)
    return predicate()


def settle(seconds=4):
    """Time for the email worker (polling every couple of seconds) to deliver anything it was going to, before checking that nothing came."""
    time.sleep(seconds)


class Session:
    """One signed-in user inside the demo company."""

    def __init__(self, creds):
        status, data = call("POST", "/auth/login", body={"userName": creds[0], "password": creds[1]})
        if status != 200:
            raise RuntimeError(f"cannot sign in as {creds[0]} ({status}); is the database seeded?")
        self.token = data["accessToken"]
        self.company = None

    def __call__(self, method, path, body=None):
        return call(method, path, self.token, self.company, body)


admin = clerk = None


# ---- sections -----------------------------------------------------------------------------------------------------------------------

def section_auth():
    global admin, clerk
    check("an unauthenticated request is refused (401)", call("GET", "/auth/me")[0] == 401)
    check("a wrong password is refused (401)", call("POST", "/auth/login", body={"userName": ADMIN[0], "password": "wrong-password-123"})[0] == 401)
    admin, clerk = Session(ADMIN), Session(CLERK)
    check("admin and the clerk can sign in", bool(admin.token and clerk.token))
    companies = call("GET", "/auth/me/companies", admin.token)[1]
    check("the demo company exists", any(c["code"] == "GTEK-DEMO" for c in companies), [c["code"] for c in companies])
    admin.company = clerk.company = next(c["id"] for c in companies if c["code"] == "GTEK-DEMO")
    check("the clerk is refused what they lack (403)", clerk("POST", "/finance/tax-codes", {"code": "SMK", "name": "x", "ratePercent": 1})[0] == 403)


def section_period():
    today = datetime.date.today()
    periods = admin("GET", "/accounting-periods")[1]
    covering = [p for p in periods if p["status"] == "Open" and p["startDate"] <= today.isoformat() <= p["endDate"]]
    if not covering:
        # The seeder's periods run out at some point; documents dated today need an open one.
        latest = max((datetime.date.fromisoformat(p["endDate"]) for p in periods), default=today - datetime.timedelta(days=1))
        start = max(latest + datetime.timedelta(days=1), today - datetime.timedelta(days=1)) if latest < today else today
        r = admin("POST", "/accounting-periods", {"code": f"SMK-{RUN}", "name": f"Smoke {RUN}", "startDate": start.isoformat(),
                                                    "endDate": (today + datetime.timedelta(days=60)).isoformat()})
        check("an open period covers today (created one)", r[0] == 201, detail(r))
    else:
        check("an open period covers today", True)


def trial_balance():
    return admin("GET", "/finance/trial-balance")[1]


def net(rows, code):
    return round(sum(r["debit"] - r["credit"] for r in rows if r["accountCode"] == code), 2)


def section_order_to_cash():
    items = {i["sku"]: i for i in admin("GET", "/inventory/items")[1]}
    stock = lambda sku: next(s["onHand"] for s in admin("GET", "/inventory/stock")[1] if s["sku"] == sku)
    mouse = items["MOU-001"]
    customer = admin("GET", "/sales/customers")[1][0]["id"]
    before_stock, before = stock("MOU-001"), trial_balance()

    r = admin("POST", "/sales/orders", {"customerId": customer, "lines": [{"itemId": mouse["id"], "quantity": 2}]})
    check("create a sales order", r[0] == 201, detail(r))
    order = r[1]["id"]
    check("open and release it", admin("POST", f"/sales/orders/{order}/open")[0] == 200 and admin("POST", f"/sales/orders/{order}/release")[0] == 200)
    r = admin("POST", f"/sales/orders/{order}/invoice", {})
    check("invoice it", r[0] in (200, 201) and r[1]["total"] > 0, detail(r))
    invoice = r[1]
    after = trial_balance()
    check("receivables rose by the invoice total", abs(net(after["rows"], "1100") - net(before["rows"], "1100") - invoice["total"]) < 0.01,
          (net(before["rows"], "1100"), net(after["rows"], "1100"), invoice["total"]))
    check("stock fell by the quantity sold", stock("MOU-001") == before_stock - 2, (before_stock, stock("MOU-001")))
    check("the ledger is balanced after invoicing", after["isBalanced"])
    r = admin("POST", f"/sales/invoices/{invoice['id']}/payments", {"amount": invoice["total"], "method": "Bank"})
    check("record the payment in full", r[0] in (200, 201), detail(r))
    paid = admin("GET", f"/sales/invoices/{invoice['id']}")[1]
    check("the invoice is Paid with no balance", paid["status"] == "Paid" and paid["balance"] == 0, (paid["status"], paid["balance"]))
    check("the ledger is balanced after payment", trial_balance()["isBalanced"])
    r = admin("POST", f"/sales/invoices/{invoice['id']}/payments", {"amount": 1, "method": "Bank"})
    check("paying a settled invoice is refused", r[0] == 400, detail(r))


def new_vendor_and_item(tag):
    vendor = admin("POST", "/purchasing/vendors", {"code": f"SMV{tag}{RUN}", "name": f"Smoke vendor {tag}"})
    item = admin("POST", "/inventory/items", {"sku": f"SMI{tag}-{RUN}", "name": f"Smoke item {tag}", "unitPrice": 2000, "unitCost": 1000})
    assert vendor[0] == 201 and item[0] == 201, (vendor, item)
    return vendor[1], item[1]


def live_order(vendor, item, quantity=10):
    r = admin("POST", "/purchasing/orders", {"vendorId": vendor["id"], "lines": [{"itemId": item["id"], "quantity": quantity, "unitCost": 1000}]})
    assert r[0] == 201, r
    order = r[1]["id"]
    admin("POST", f"/purchasing/orders/{order}/open")
    admin("POST", f"/purchasing/orders/{order}/release")
    return order


def section_purchasing_and_match():
    vendor, item = new_vendor_and_item("M")
    order = live_order(vendor, item)
    r = admin("POST", f"/purchasing/orders/{order}/receive", {"reference": "SMOKE-DN", "lines": [{"itemId": item["id"], "quantity": 10}]})
    check("receive the goods", r[0] == 200 and r[1]["status"] == "Received", detail(r))
    bill = lambda cost, ref: admin("POST", f"/purchasing/orders/{order}/bill",
                                   {"vendorReference": f"{ref}-{RUN}", "lines": [{"itemId": item["id"], "quantity": 10, "unitCost": cost}]})
    r = bill(1030, "A")
    check("a bill 3% over the order is blocked by the 2% default tolerance", r[0] == 400 and "tolerance" in str(detail(r)).lower(), detail(r))
    r = bill(1020, "B")
    check("a bill exactly 2% over is accepted and marked within tolerance", r[0] == 201 and r[1]["lines"][0]["matchStatus"] == "Within tolerance", detail(r))
    check("the ledger is balanced after billing", trial_balance()["isBalanced"])
    r = admin("POST", f"/purchasing/orders/{order}/receive", {"reference": "SMOKE-DN2", "lines": [{"itemId": item["id"], "quantity": 1}]})
    check("receiving more than was ordered is refused", r[0] == 400, detail(r))


def request_tolerance(session, order, item, price=6, reason="Vendor warned of a price rise"):
    return session("POST", f"/purchasing/orders/{order}/tolerance-requests",
                   {"reason": reason, "lines": [{"itemId": item["id"], "priceTolerancePercent": price, "quantityTolerancePercent": None,
                                                  "underDeliveryTolerancePercent": None}]})


def section_approval_workflow_and_email():
    vendor, item = new_vendor_and_item("A")
    order = live_order(vendor, item)
    admin_before = admin("GET", "/notifications/unread-count")[1]["count"]
    r = request_tolerance(clerk, order, item)
    check("the clerk raises a tolerance request (Pending)", r[0] == 201 and r[1]["status"] == "Pending", detail(r))
    request, number = r[1]["id"], r[1]["number"]
    check("the order is unchanged while it is pending", admin("GET", f"/purchasing/orders/{order}")[1]["lines"][0]["priceTolerancePercent"] == 2)
    check("a second pending request on the order is refused", request_tolerance(clerk, order, item, 7)[0] == 400)
    check("the clerk cannot approve (403)", clerk("POST", f"/purchasing/tolerance-requests/{request}/approve", {})[0] == 403)

    mail = wait_for(lambda: mails("admin@gtek.local", number))
    check("the approver is emailed the request", len(mail) == 1 and "needs a decision" in mail[0]["subject"], [m["subject"] for m in mails("admin@gtek.local")])
    check("the email links back into the app with an absolute URL", bool(mail) and "/tolerance-requests" in mail[0]["text"] and "http" in mail[0]["text"])
    check("the requester is not emailed about their own request", not mails("apclerk@gtek.local", number))
    check("the approver also has an in-app notification", admin("GET", "/notifications/unread-count")[1]["count"] > admin_before)

    r = admin("POST", f"/purchasing/tolerance-requests/{request}/approve", {"note": "Agreed with the buyer"})
    check("the approver approves it", r[0] == 200 and r[1]["status"] == "Approved", detail(r))
    check("the override is applied to the order", admin("GET", f"/purchasing/orders/{order}")[1]["lines"][0]["priceTolerancePercent"] == 6)
    decision = wait_for(lambda: mails("apclerk@gtek.local", number))
    check("the requester is emailed the decision with the note", len(decision) == 1 and "Agreed with the buyer" in decision[0]["text"],
          [m["subject"] for m in mails("apclerk@gtek.local")])

    own = request_tolerance(admin, live_order(vendor, item), item, 5)
    check("an approver can raise a request but cannot approve their own", own[0] == 201 and
          admin("POST", f"/purchasing/tolerance-requests/{own[1]['id']}/approve", {})[0] == 400)
    check("the ledger is still balanced", trial_balance()["isBalanced"])


def section_email_preferences():
    prefs = admin("GET", "/auth/me/preferences")[1]
    categories = {c["category"]: c["emailEnabled"] for c in prefs["categories"]}
    check("email is available and every category is listed",
          prefs["emailAvailable"] and {"approval", "decision", "low-stock", "overdue-invoice", "overdue-bill", "period-close"} <= set(categories) and "account" not in categories, sorted(categories))
    check("admin opts out of approval email", admin("PUT", "/auth/me/preferences/email-categories/approval", {"enabled": False})[0] == 200)
    vendor, item = new_vendor_and_item("O")
    unread = admin("GET", "/notifications/unread-count")[1]["count"]
    r = request_tolerance(clerk, live_order(vendor, item), item)
    number = r[1]["number"]
    settle()
    check("no email once opted out", not mails("admin@gtek.local", number))
    check("the in-app notification still arrives", admin("GET", "/notifications/unread-count")[1]["count"] == unread + 1)
    check("admin opts back in", admin("PUT", "/auth/me/preferences/email-categories/approval", {"enabled": True})[0] == 200)
    check("a bad category name is refused", admin("PUT", "/auth/me/preferences/email-categories/bad%20name", {"enabled": False})[0] == 400)
    admin("POST", "/notifications/read-all")
    check("read-all clears the unread count", admin("GET", "/notifications/unread-count")[1]["count"] == 0)


def section_reminders():
    """The reminders fire once shortly after the API starts (run.sh configures it); the seeded data gives each something to report."""
    expected = {"overdue-invoice": ("invoice", "overdue"), "overdue-bill": ("bill", "overdue"), "low-stock": ("low on stock",), "period-close": ("accounting period",)}
    def categories():
        return {n["category"] for n in admin("GET", "/notifications?take=100")[1]}
    wait_for(lambda: set(expected) <= categories(), 40)
    check("overdue invoices, overdue bills, low stock and open periods were all reported to admin",
          set(expected) <= categories(), sorted(categories()))
    for category, words in expected.items():
        sent = wait_for(lambda: [m for m in mails("admin@gtek.local") if all(w in m["subject"] for w in words)], 20)
        check(f"admin was emailed the {category} summary", bool(sent), [m["subject"] for m in mails("admin@gtek.local")])
    check("the clerk (who pays bills, not collects or manages stock) heard only about bills",
          {n["category"] for n in clerk("GET", "/notifications?take=100")[1] if n["category"].startswith(("overdue", "low", "period"))} <= {"overdue-bill"})
    stock = {s["sku"]: s for s in admin("GET", "/inventory/stock")[1]}
    check("the printer is below its reorder level and flagged", stock["PRN-100"]["isLow"] and stock["PRN-100"]["reorderLevel"] == 10, stock["PRN-100"])


def section_account_notices():
    address = f"smoke-{RUN}@gtek.local"
    password = "Smoke#12345678"
    r = admin("POST", "/users", {"userName": f"smoke{RUN}", "email": address, "password": password})
    check("create a user", r[0] == 201, detail(r))
    user = r[1]["id"]
    welcome = wait_for(lambda: mails(address, "account was created"))
    check("the new user is emailed, without their password", len(welcome) == 1 and f"smoke{RUN}" in welcome[0]["text"] and password not in welcome[0]["raw"])
    check("grant company access", call("POST", f"/users/{user}/companies/{admin.company}", admin.token)[0] == 200)
    check("the user is emailed about the access", bool(wait_for(lambda: mails(address, "You now have access to"))))
    check("revoke it", call("DELETE", f"/users/{user}/companies/{admin.company}", admin.token)[0] == 204)
    check("the user is emailed about the removal", bool(wait_for(lambda: mails(address, "was removed"))))
    check("deactivate the user", call("POST", f"/users/{user}/deactivate", admin.token)[0] == 200)
    check("the user is emailed about the deactivation", bool(wait_for(lambda: mails(address, "was deactivated"))))
    me = call("GET", "/auth/me", admin.token)[1]["id"]
    before = len(mails("admin@gtek.local"))
    role = next(x for x in call("GET", "/roles", admin.token)[1] if not x["isSystem"])
    call("POST", f"/users/{me}/roles/{role['id']}", admin.token)
    call("DELETE", f"/users/{me}/roles/{role['id']}", admin.token)
    settle()
    check("nobody is emailed about their own change", len(mails("admin@gtek.local")) == before)
    check("account notices are mandatory: they cannot be opted out of (400)", admin("PUT", "/auth/me/preferences/email-categories/account", {"enabled": False})[0] == 400)
    off = admin("PUT", "/auth/me/preferences", {"emailNotifications": False})
    other = f"quiet-{RUN}@gtek.local"
    quiet = admin("POST", "/users", {"userName": f"quiet{RUN}", "email": other, "password": password})
    check("a user can be created", quiet[0] == 201, detail(quiet))
    check("the new user is emailed even with the administrator's own email switched off", bool(wait_for(lambda: mails(other, "account was created"))))
    admin("PUT", "/auth/me/preferences", {"emailNotifications": True})
    check("switching email off overall changed only the administrator's own preference", off[0] == 200 and off[1]["emailNotifications"] is False)


def section_email_failure_does_not_fail_the_operation():
    status = lambda: admin("GET", "/notification-settings/email")[1]
    failed_before = status()["failed"]
    r = admin("POST", "/users", {"userName": f"reject{RUN}", "email": f"reject-{RUN}@gtek.local", "password": "Smoke#12345678"})
    check("a user whose address the mail server rejects is still created", r[0] == 201, detail(r))
    check("the permanent refusal is recorded as a failed message, not retried forever",
          bool(wait_for(lambda: status()["failed"] == failed_before + 1, 30)), status())


def section_retention_settings():
    path = "/notification-settings/retention"
    check("the clerk cannot read retention settings (403)", clerk("GET", path)[0] == 403)
    r = admin("PUT", f"{path}/categories/approval", {"readDays": 7, "unreadDays": None})
    approval = lambda: next(c for c in admin("GET", path)[1]["categories"] if c["category"] == "approval")
    check("give the approval category its own read days", r[0] == 200 and approval()["readDaysOverride"] == 7 and approval()["effectiveReadDays"] == 7, detail(r))
    check("the company-wide values are untouched", admin("GET", path)[1]["readDaysOverride"] is None)
    check("an invalid value is refused", admin("PUT", f"{path}/categories/approval", {"readDays": 0, "unreadDays": None})[0] == 400)
    check("a read period longer than the inherited unread period is refused", admin("PUT", f"{path}/categories/approval", {"readDays": 3000, "unreadDays": None})[0] == 400)
    check("clearing the rule restores inheritance",
          admin("PUT", f"{path}/categories/approval", {"readDays": None, "unreadDays": None})[0] == 200 and approval()["readDaysOverride"] is None)


def section_final_integrity():
    check("the ledger is balanced at the end", trial_balance()["isBalanced"])
    status = admin("GET", "/notification-settings/email")[1]
    check("the email outbox has drained", bool(wait_for(lambda: admin("GET", "/notification-settings/email")[1]["pending"] == 0, 30)), status)
    check("only the one deliberately rejected message failed", status["failed"] == 1, status)
    with _mail_lock:
        ids = [m["message_id"] for m in MAIL]
    check("every email carries a stable, unique Message-ID in the sender's domain",
          len(ids) >= 10 and len(set(ids)) == len(ids) and all(re.fullmatch(r"<[0-9a-f]{32}@gtek\.local>", i) for i in ids), ids[:3])
    check("messages were delivered", status["sentLast24Hours"] >= 10, status)


SECTIONS = [
    ("Sign-in and permissions", section_auth),
    ("Accounting period", section_period),
    ("Order to cash", section_order_to_cash),
    ("Purchasing and three-way match", section_purchasing_and_match),
    ("Tolerance approval workflow and email", section_approval_workflow_and_email),
    ("Email preferences", section_email_preferences),
    ("Scheduled reminders", section_reminders),
    ("Account change notices", section_account_notices),
    ("A rejected address does not fail the operation", section_email_failure_does_not_fail_the_operation),
    ("Retention settings", section_retention_settings),
    ("Final integrity", section_final_integrity),
]


def wait_for_api(seconds):
    """The sink is up before the API is, so nothing the API sends at start-up (the reminders) meets a closed port."""
    end = time.time() + seconds
    while time.time() < end:
        try:
            with urllib.request.urlopen(API + "/health", timeout=3):
                return True
        except (urllib.error.URLError, OSError):
            time.sleep(1)
    return False


def main():
    start_sink()
    if not wait_for_api(int(os.environ.get("SMOKE_WAIT_SECONDS", "0"))) and os.environ.get("SMOKE_WAIT_SECONDS", "0") != "0":
        print("the API did not become healthy in time", flush=True)
        return 2
    only = sys.argv[1:]
    for title, function in SECTIONS:
        if only and not any(o in title.lower() for o in only):
            continue
        print(f"\n== {title}", flush=True)
        try:
            function()
        except Exception as error:  # a broken section must not hide the others
            check(f"{title}: ran to completion", False, repr(error))
        if admin is None and function is section_auth:
            print("cannot continue without signing in", flush=True)
            break
    print(f"\n{PASSED} checks passed, {len(FAILURES)} failed", flush=True)
    for name in FAILURES:
        print("  FAILED: " + name)
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
