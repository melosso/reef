#!/usr/bin/env bash
set -euo pipefail

# Requires python3.
# Usage: ./seed.sh [--reset] [admin-username]   Settings: REEF_URL, REEF_USER, REEF_PASSWORD, REEF_APP_DIR

for tool in python3 curl; do
    if ! command -v "$tool" &>/dev/null; then
        echo "ERROR: $tool is not installed or not in PATH" >&2
        exit 1
    fi
done

export REEF_RESET=0
if [ "${1:-}" = "--reset" ]; then
    REEF_RESET=1
    shift
fi

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
export REEF_URL="${REEF_URL:-http://localhost:8085}"
export REEF_APP_DIR="${REEF_APP_DIR:-$SCRIPT_DIR/bin/Debug/net11.0}"
export REEF_USER="${1:-${REEF_USER:-admin@reef.local}}"

if ! curl -sf -o /dev/null "$REEF_URL/health"; then
    echo "ERROR: Reef is not reachable at $REEF_URL. Start it with: dotnet run --project $SCRIPT_DIR" >&2
    exit 1
fi

if [ -z "${REEF_PASSWORD:-}" ]; then
    read -rsp "Password for $REEF_USER: " REEF_PASSWORD
    echo
fi
export REEF_PASSWORD

echo "=== Reef - Demo Seed ($REEF_URL) ==="
python3 -I - <<'PY'
import json, os, random, sqlite3, sys, urllib.request, urllib.error
from datetime import datetime, timedelta

BASE = os.environ["REEF_URL"].rstrip("/")
APP_DIR = os.environ["REEF_APP_DIR"]
USER, PASSWORD = os.environ["REEF_USER"], os.environ["REEF_PASSWORD"]
DEMO = os.path.join(APP_DIR, "demo")
SHOP = os.path.join(DEMO, "shop.db")
INBOX = os.path.join(DEMO, "inbox")


def call(method, path, body=None, token=None):
    req = urllib.request.Request(BASE + path, data=json.dumps(body).encode() if body is not None else None, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            text = r.read().decode()
            return r.status, json.loads(text) if text else None
    except urllib.error.HTTPError as e:
        text = e.read().decode()
        try:
            return e.code, json.loads(text)
        except Exception:
            return e.code, text


def must(result, what):
    status, body = result
    if status not in (200, 201):
        sys.exit(f"{what} failed: {status} {body}")
    print(f"ok  {what}")
    return body


DEMO_TABLES = {"customers", "products", "orders", "order_lines", "price_list"}


def build_shop():
    os.makedirs(INBOX, exist_ok=True)
    if os.path.exists(SHOP):
        with sqlite3.connect(f"file:{SHOP}?mode=ro", uri=True) as existing:
            tables = {r[0] for r in existing.execute("SELECT name FROM sqlite_master WHERE type = 'table'")}
        if not DEMO_TABLES <= tables:
            sys.exit(f"{SHOP} exists and is not a Reef demo database. Nothing was changed.")
        if os.environ.get("REEF_RESET") != "1":
            print(f"skip demo shop database exists ({SHOP}), use --reset to rebuild it")
            write_price_list()
            return
    random.seed(7)
    db = sqlite3.connect(SHOP)
    db.executescript("""
        DROP TABLE IF EXISTS order_lines; DROP TABLE IF EXISTS orders; DROP TABLE IF EXISTS products; DROP TABLE IF EXISTS customers;
        DROP TABLE IF EXISTS price_list;
        CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT, email TEXT, city TEXT, country TEXT, created_at TEXT);
        CREATE TABLE products (sku TEXT PRIMARY KEY, name TEXT, category TEXT, price REAL);
        CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER, order_date TEXT, status TEXT, total REAL);
        CREATE TABLE order_lines (order_id INTEGER, sku TEXT, quantity INTEGER, line_total REAL);
        CREATE TABLE price_list (sku TEXT PRIMARY KEY, price REAL, currency TEXT, valid_from TEXT);
    """)
    customers = [
        ("Noordzee Visgroothandel", "Rotterdam", "NL"), ("Bakkerij De Molen", "Utrecht", "NL"), ("Café Brandstof", "Amsterdam", "NL"),
        ("Smith & Sons Ltd", "Leeds", "GB"), ("Müller Werkzeuge GmbH", "Köln", "DE"), ("Atelier Lumière", "Lyon", "FR"),
        ("Fjord Supplies AS", "Bergen", "NO"), ("Tapas & Vino", "Sevilla", "ES"), ("Green Leaf Market", "Antwerpen", "BE"),
        ("Polder Fietsen", "Groningen", "NL"), ("Hollandse Kaasmakerij", "Gouda", "NL"), ("Alpine Outfitters", "Innsbruck", "AT"),
    ]
    for i, (name, city, country) in enumerate(customers, 1):
        email = "info@" + "".join(c for c in name.lower() if c.isalnum())[:14] + ".example"
        db.execute("INSERT INTO customers VALUES (?,?,?,?,?,?)", (i, name, email, city, country, f"2026-0{1 + i % 9}-1{i % 9}"))
    products = [("SKU-1001", "Espresso beans 1kg", "Coffee", 24.95), ("SKU-1002", "Oat milk 6-pack", "Dairy", 13.50),
                ("SKU-1003", "Stroopwafels tin", "Bakery", 8.75), ("SKU-1004", "Gouda wheel 4kg", "Cheese", 59.00),
                ("SKU-1005", "Herring fillets 2kg", "Fish", 31.20), ("SKU-1006", "Bike bell chrome", "Bikes", 6.40)]
    db.executemany("INSERT INTO products VALUES (?,?,?,?)", products)
    statuses = ["completed"] * 6 + ["processing", "on-hold", "cancelled"]
    start = datetime.now() - timedelta(days=30)
    for order_id in range(1, 61):
        lines = random.sample(products, random.randint(1, 3))
        total = 0
        for sku, _, _, price in lines:
            qty = random.randint(1, 5)
            total += qty * price
            db.execute("INSERT INTO order_lines VALUES (?,?,?,?)", (order_id, sku, qty, round(qty * price, 2)))
        day = start + timedelta(days=order_id / 2, hours=random.randint(8, 18))
        db.execute("INSERT INTO orders VALUES (?,?,?,?,?)",
                   (order_id, random.randint(1, len(customers)), day.strftime("%Y-%m-%d %H:%M"), random.choice(statuses), round(total, 2)))
    db.commit()
    db.close()
    print(f"ok  demo shop database {SHOP}")
    write_price_list()


def write_price_list():
    path = os.path.join(INBOX, "price_list.csv")
    if os.path.exists(path):
        return
    with sqlite3.connect(SHOP) as db:
        products = db.execute("SELECT sku, price FROM products ORDER BY sku").fetchall()
    with open(path, "w") as f:
        f.write("sku,price,currency,valid_from\n")
        for sku, price in products:
            f.write(f"{sku},{round(price * 1.04, 2)},EUR,{datetime.now():%Y-%m-%d}\n")
    print(f"ok  inbox file {path}")


def node(node_id, kind, config, x):
    return {"id": node_id, "type": kind, "x": x, "y": 120, "config": config}


build_shop()
_, login = call("POST", "/api/auth/login", {"username": USER, "password": PASSWORD})
if not login or "token" not in login:
    sys.exit(f"sign-in failed: {login}")
token = login["token"]

existing_groups = {g["name"]: g for g in call("GET", "/api/groups", token=token)[1]}
if "Demo" in existing_groups:
    print("skip group Demo exists")
group = existing_groups.get("Demo") or must(call("POST", "/api/groups", {"name": "Demo", "description": "Demo profiles on the local SQLite shop database"}, token), "group Demo")
group_id = group["id"] if "id" in group else call("GET", "/api/groups", token=token)[1][-1]["id"]

existing_conns = {c["name"]: c for c in call("GET", "/api/connections", token=token)[1]}
conn = existing_conns.get("Demo shop (SQLite)")
if conn:
    # Stored connection strings are encrypted. The connection reports its own database file.
    _, probe = call("POST", "/api/profiles/test-query-preview",
                    {"connectionId": conn["id"], "query": "SELECT file FROM pragma_database_list WHERE name = 'main'"}, token)
    opened = (probe or {}).get("rows", [{}])[0].get("file") if isinstance(probe, dict) and probe.get("success") else None
    if opened and os.path.realpath(opened) != os.path.realpath(SHOP):
        print(f"WARN connection Demo shop (SQLite) opens {opened}. It was kept as is.")
    print("skip connection Demo shop (SQLite) exists")
conn = conn or must(call("POST", "/api/connections", {
    "name": "Demo shop (SQLite)", "type": "Sqlite", "connectionString": f"Data Source={SHOP}", "tags": "demo", "isActive": True, "hash": ""}, token), "connection Demo shop (SQLite)")

local = next(d for d in call("GET", "/api/destinations", token=token)[1] if d["type"] == "Local")

templates = {t["name"]: t for t in call("GET", "/api/templates", token=token)[1]}
xml_template = """<?xml version="1.0" encoding="utf-8"?>
<Orders generated="{{ date.now | date.to_string '%Y-%m-%dT%H:%M:%S' }}">
{{~ for row in rows ~}}
  <Order id="{{ row.order_id | html.escape }}" status="{{ row.status | html.escape }}">
    <Customer>{{ row.customer | html.escape }}</Customer>
    <City>{{ row.city | html.escape }}</City>
    <Date>{{ row.order_date | html.escape }}</Date>
    <Total currency="EUR">{{ row.total | math.format '0.00' }}</Total>
  </Order>
{{~ end ~}}
</Orders>"""
if "Demo orders XML" in templates:
    print("skip template Demo orders XML exists")
template = templates.get("Demo orders XML") or must(call("POST", "/api/templates", {
    "name": "Demo orders XML", "description": "Orders with customer and total", "type": "ScribanTemplate",
    "template": xml_template, "outputFormat": "XML", "isActive": True, "tags": "demo"}, token), "template Demo orders XML")

orders_query = """SELECT o.id AS order_id, c.name AS customer, c.city, c.country, o.order_date, o.status, o.total
FROM orders o JOIN customers c ON c.id = o.customer_id ORDER BY o.order_date DESC"""
exports = [
    ("Demo orders CSV", [node("source", "source", {"ConnectionId": conn["id"], "Query": orders_query}, 40),
                         node("destination", "destination", {"OutputDestinationId": local["id"], "OutputFormat": "CSV", "FilenameTemplate": "demo/orders_{date}.{format}"}, 360)]),
    ("Demo customers JSON (Smart Sync)", [node("source", "source", {"ConnectionId": conn["id"], "Query": "SELECT id, name, email, city, country, created_at FROM customers ORDER BY id"}, 40),
                                          node("deltasync", "deltasync", {"DeltaSyncReefIdColumn": "id", "DeltaSyncHashAlgorithm": "SHA256", "DeltaSyncTrackDeletes": True}, 300),
                                          node("destination", "destination", {"OutputDestinationId": local["id"], "OutputFormat": "JSON", "FilenameTemplate": "demo/customers_{timestamp}.{format}"}, 560)]),
    ("Demo orders XML (template)", [node("source", "source", {"ConnectionId": conn["id"], "Query": orders_query}, 40),
                                    node("template", "template", {"TemplateId": template["id"]}, 300),
                                    node("destination", "destination", {"OutputDestinationId": local["id"], "OutputFormat": "XML", "FilenameTemplate": "demo/orders_{date}.{format}"}, 560)]),
]
profiles = {p["name"]: p for p in call("GET", "/api/profiles", token=token)[1]}
export_ids = []
created_exports = []
for name, nodes in exports:
    if name in profiles:
        export_ids.append(profiles[name]["id"])
        print(f"skip {name} exists")
        continue
    saved = must(call("POST", "/api/profile-documents/export", {"kind": "export", "settings": {"Name": name, "GroupId": group_id, "IsEnabled": True}, "nodes": nodes}, token), f"export profile {name}")
    export_ids.append(saved["id"])
    created_exports.append((name, saved["id"]))

imports = {p["name"]: p for p in call("GET", "/api/profiles?type=import", token=token)[1]}
import_name = "Demo price list import"
import_created = import_name not in imports
if not import_created:
    import_id = imports[import_name]["id"]
    print(f"skip {import_name} exists")
else:
    import_doc = {"kind": "import", "settings": {"Name": import_name, "GroupId": group_id, "IsEnabled": True}, "nodes": [
        node("source", "source", {"SourceType": "Local", "SourceFilePath": INBOX, "SourceFilePattern": "*.csv", "SourceFileSelection": "Latest"}, 40),
        node("parse", "parse", {"SourceFormat": "CSV"}, 260),
        node("mapping", "mapping", {"ColumnMappingsJson": [
            {"sourceColumn": "sku", "targetColumn": "sku", "isKeyColumn": True}, {"sourceColumn": "price", "targetColumn": "price"},
            {"sourceColumn": "currency", "targetColumn": "currency"}, {"sourceColumn": "valid_from", "targetColumn": "valid_from"}]}, 480),
        node("target", "target", {"TargetType": "Database", "TargetConnectionId": conn["id"], "TargetTable": "price_list", "LoadStrategy": "Upsert", "UpsertKeyColumns": "sku"}, 700)]}
    import_id = must(call("POST", "/api/profile-documents/import", import_doc, token), f"import profile {import_name}")["id"]

jobs = {j["name"]: j for j in (lambda r: r if isinstance(r, list) else r.get("data", []))(call("GET", "/api/jobs", token=token)[1])}
if "Demo daily orders export" in jobs:
    print("skip job Demo daily orders export exists")
else:
    must(call("POST", "/api/jobs", {"name": "Demo daily orders export", "description": "Runs the demo orders CSV export every morning",
        "type": "ProfileExecution", "profileId": export_ids[0], "scheduleType": 3, "startTime": "06:00", "priority": 1, "maxRetries": 3,
        "timeoutMinutes": 10, "allowConcurrent": False, "tags": "demo", "isEnabled": True, "autoPauseEnabled": True}, token), "job Demo daily orders export")

# Only new profiles run. Reruns add no executions or output files.
for name, pid in created_exports:
    status, run = call("POST", f"/api/executions/execute/{pid}", {}, token)
    print(f"{'ok ' if status == 200 else 'FAILED'} run {name}" + ("" if status == 200 else f": {status} {run}"))
if import_created:
    status, run = call("POST", f"/api/profiles/{import_id}/execute?type=import", {}, token)
    print(f"{'ok ' if status == 200 else 'FAILED'} run {import_name}" + ("" if status == 200 else f": {status} {run}"))
if not created_exports and not import_created:
    print("skip runs, all demo profiles existed")
print(f"\nDone. Output files are written under the Local destination folder in {APP_DIR}.")
PY
